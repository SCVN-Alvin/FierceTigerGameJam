using UnityEngine;
using GameJam.Gameplay;
using GameJam.Gameplay.Combat;
using GameJam.Gameplay.Wall;
using System.Collections.Generic;

namespace GameJam.Gameplay.Cannon
{
    [RequireComponent(typeof(Collider))]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class GridKnockdownCannonProjectile : MonoBehaviour
    {
        [SerializeField] private float impactForce = 18f;
        [SerializeField] private float impactRadius = 0.65f;
        [SerializeField] private float minimumFalloff = 0.2f;
        [SerializeField] private float neighborImpulseMultiplier = 0.65f;
        [SerializeField] private float upwardForce = 0.25f;
        [SerializeField] private LayerMask hittableLayers = ~0;

        [Tooltip("Seconds the ball keeps flying after its first hit. It carries on bouncing but "
                 + "does no further damage, which is what turns a shot into something the player "
                 + "watches rather than something that ends the instant it lands.")]
        [SerializeField] private float postImpactLifetime = 0.4f;

        [Tooltip("Ceiling on the tumble a launched ball is given, in radians per second. Rolling "
                 + "contact on a ball this small works out at hundreds of radians per second, "
                 + "which is more than a turn per physics step and reads as a strobe, so this "
                 + "clamp is what is actually seen on almost every shot.")]
        [SerializeField] private float maxTumbleAngularSpeed = 20f;

        [Header("Damage")]
        [Tooltip("Where the loaded ammunition and its level are read from. Without one, the flat "
                 + "fallback damage below is used and every material takes the same hit.")]
        [SerializeField] private BulletLoadout loadout;

        [Tooltip("Optional. Fires this ammunition instead of whatever the loadout has, which is "
                 + "how a single shot is tested without touching progression.")]
        [SerializeField] private BulletDefinition bulletOverride;

        [Tooltip("Level used with the override. Ignored when the loadout supplies the ammunition.")]
        [SerializeField] private int bulletLevelOverride = 1;

        [Tooltip("Hit points taken off the block that was hit directly, when no ammunition is set.")]
        [SerializeField] private float directHitDamage = 3f;

        [Tooltip("Hit points taken off blocks in the blast radius, when no ammunition is set.")]
        [SerializeField] private float splashDamage = 1f;

        [Tooltip("Multiplies the ammunition's damage. Set per shot by the fire controller from "
                 + "the selected vehicle; 1 when no vehicle system is wired.")]
        [SerializeField] private float damageMultiplier = 1f;

        /// <summary>
        /// Shared by every shot, and only ever touched between a collision and the end of the
        /// same call. A blast reaches a couple of dozen colliders at most, and overflowing the
        /// buffer costs the outermost blocks of one shot rather than an allocation on every one.
        /// </summary>
        private static readonly Collider[] OverlapBuffer = new Collider[32];

        private static readonly HashSet<KnockdownBlock> ProcessedBlocks = new HashSet<KnockdownBlock>();

        /// <summary>
        /// Below this, the axis the tumble would turn about has collapsed - a shot fired straight
        /// up, where there is no horizontal heading to roll over - and the ball flies unspun.
        /// </summary>
        private const float MinTumbleAxisSqrMagnitude = 0.0001f;

        private Rigidbody projectileRigidbody;
        private Collider projectileCollider;
        private ProjectilePool pool;

        /// <summary>
        /// Colliders this shot was told to pass through. Kept so they can be un-ignored when it
        /// goes back to the pool: an ignore pair outlives the shot that set it, and the next
        /// shot out of the same instance would fly through whatever the last one did.
        /// </summary>
        private readonly List<Collider> ignoredColliders = new List<Collider>();

        /// <summary>
        /// The mesh children that stand for a level, and the level number read off each one's
        /// name. Cached in <see cref="Awake"/> because the look is re-applied on every shot: a
        /// pooled ball is told its ammunition per shot, and re-reading child names there would
        /// allocate a string per child on every tap.
        /// </summary>
        private GameObject[] levelLooks;
        private int[] levelLookNumbers;

        private bool hasHit;
        private float sinceHit;
        private float flightRemaining;

        private void Awake()
        {
            projectileRigidbody = GetComponent<Rigidbody>();
            projectileCollider = GetComponent<Collider>();
            projectileRigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            CacheLevelLooks();

            // Once here as well as per shot, so the states the artist happened to leave the
            // children in stop mattering, and the demo path with no loadout - which never calls
            // SetAmmunition - still shows exactly one look rather than all three at once.
            ResolveBullet(out int level);
            ApplyLevelLook(level);
        }

        /// <summary>Told which pool to go back to. A shot with no pool destroys itself.</summary>
        public void SetPool(ProjectilePool owner)
        {
            pool = owner;
        }

        private void OnValidate()
        {
            impactForce = Mathf.Max(0f, impactForce);
            impactRadius = Mathf.Max(0f, impactRadius);
            minimumFalloff = Mathf.Clamp01(minimumFalloff);
            neighborImpulseMultiplier = Mathf.Max(0f, neighborImpulseMultiplier);
            upwardForce = Mathf.Max(0f, upwardForce);
            postImpactLifetime = Mathf.Max(0f, postImpactLifetime);
            maxTumbleAngularSpeed = Mathf.Max(0f, maxTumbleAngularSpeed);
            directHitDamage = Mathf.Max(0f, directHitDamage);
            splashDamage = Mathf.Max(0f, splashDamage);
            bulletLevelOverride = Mathf.Max(1, bulletLevelOverride);
            damageMultiplier = Mathf.Max(0f, damageMultiplier);
        }

        /// <summary>
        /// Tells the shot what fired it. The player brings a mix of ammunition into a run and
        /// chooses per shot, so which kind this is cannot be baked into the prefab.
        /// </summary>
        public void SetAmmunition(BulletDefinition bullet, int level)
        {
            bulletOverride = bullet;
            bulletLevelOverride = Mathf.Max(1, level);
            ApplyLevelLook(bulletLevelOverride);
        }

        /// <summary>
        /// Enables the mesh child for the level this shot was fired at. Children are matched by
        /// the "LV{n}" suffix the artist uses (Boom01_LV2); the highest n not above the level
        /// wins, so a prefab with fewer looks than the bullet has levels shows its best one
        /// rather than nothing. Children without the suffix (none today) are left alone.
        /// </summary>
        private void ApplyLevelLook(int level)
        {
            if (levelLooks == null || levelLooks.Length == 0)
            {
                return;
            }

            int wanted = Mathf.Max(1, level);
            int bestAtOrBelow = -1;
            int lowest = -1;
            for (int i = 0; i < levelLooks.Length; i++)
            {
                int number = levelLookNumbers[i];
                if (number <= wanted && (bestAtOrBelow < 0 || number > levelLookNumbers[bestAtOrBelow]))
                {
                    bestAtOrBelow = i;
                }

                if (lowest < 0 || number < levelLookNumbers[lowest])
                {
                    lowest = i;
                }
            }

            // Nothing authored at or below the level means a prefab whose looks start higher than
            // level 1; its lowest is still a ball, which beats an invisible shot.
            int chosen = bestAtOrBelow >= 0 ? bestAtOrBelow : lowest;
            for (int i = 0; i < levelLooks.Length; i++)
            {
                if (levelLooks[i] != null)
                {
                    levelLooks[i].SetActive(i == chosen);
                }
            }
        }

        /// <summary>
        /// Counts the level children, then records them, rather than building a list: the two
        /// passes happen once per instance and leave fixed arrays behind, which is what lets the
        /// per-shot swap above run without allocating.
        /// </summary>
        private void CacheLevelLooks()
        {
            Transform self = transform;
            int childCount = self.childCount;

            int found = 0;
            for (int i = 0; i < childCount; i++)
            {
                if (TryParseLevelSuffix(self.GetChild(i).name, out _))
                {
                    found++;
                }
            }

            levelLooks = new GameObject[found];
            levelLookNumbers = new int[found];

            int next = 0;
            for (int i = 0; i < childCount && next < found; i++)
            {
                Transform child = self.GetChild(i);
                if (!TryParseLevelSuffix(child.name, out int level))
                {
                    continue;
                }

                levelLooks[next] = child.gameObject;
                levelLookNumbers[next] = level;
                next++;
            }
        }

        /// <summary>
        /// Reads the trailing "LV{n}" the artist names the level meshes with (Boom01_LV2). Read
        /// character by character rather than with int.Parse on a substring, since even the
        /// once-per-instance scan runs while a run is loading. False for a child named anything
        /// else, which is then left exactly as the prefab authored it.
        /// </summary>
        private static bool TryParseLevelSuffix(string childName, out int level)
        {
            level = 0;
            if (string.IsNullOrEmpty(childName))
            {
                return false;
            }

            int digitsStart = childName.Length;
            while (digitsStart > 0 && childName[digitsStart - 1] >= '0' && childName[digitsStart - 1] <= '9')
            {
                digitsStart--;
            }

            // Needs at least one digit, and the two characters before it to be the LV marker.
            if (digitsStart == childName.Length || digitsStart < 2)
            {
                return false;
            }

            char marker = childName[digitsStart - 2];
            char version = childName[digitsStart - 1];
            if ((marker != 'L' && marker != 'l') || (version != 'V' && version != 'v'))
            {
                return false;
            }

            int value = 0;
            for (int i = digitsStart; i < childName.Length; i++)
            {
                value = (value * 10) + (childName[i] - '0');
            }

            level = value;
            return value > 0;
        }

        /// <summary>
        /// Tells the shot what the cannon is mounted on. Kept apart from the ammunition because
        /// the two progressions are bought separately: a vehicle boosts whatever bullet the
        /// player loaded, so neither knows the other's level.
        /// </summary>
        public void SetDamageMultiplier(float multiplier)
        {
            damageMultiplier = Mathf.Max(0f, multiplier);
        }

        public void Launch(Vector3 direction, float speed, float lifetime)
        {
            if (projectileRigidbody == null)
            {
                projectileRigidbody = GetComponent<Rigidbody>();
            }

            hasHit = false;
            sinceHit = 0f;
            flightRemaining = lifetime;

            // Back to the setting a shot in flight needs. A pooled ball was switched to Discrete
            // when its last shot landed, and would otherwise tunnel straight through thin glass.
            projectileRigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            Vector3 launchVelocity = direction.normalized * speed;
            projectileRigidbody.linearVelocity = launchVelocity;
            ApplyFlightTumble(launchVelocity, speed);
            IgnoreSpawnOverlaps();
        }

        /// <summary>
        /// Spins the ball about the axis it is arcing over, so the slow flight the arc needs reads
        /// as a heavy shot rather than a floating sphere. Set here rather than on the way back to
        /// the pool because this is where the linear velocity is already established for the same
        /// reason - it is the one call every shot makes, pooled or freshly instantiated - and it
        /// unconditionally overwrites whatever the previous flight ended with, which is what stops
        /// a reused ball inheriting spin. Nothing about a hit changes: both the damage and the
        /// knock impulse are read from the linear velocity and the loaded ammunition, never from
        /// the spin, and the ball is already done dealing damage by the time friction on a
        /// spinning sphere could send its bounce anywhere new.
        /// </summary>
        private void ApplyFlightTumble(Vector3 launchVelocity, float speed)
        {
            // First, so every path out of this method leaves a defined spin rather than the last
            // flight's.
            projectileRigidbody.angularVelocity = Vector3.zero;

            Vector3 tumbleAxis = Vector3.Cross(Vector3.up, launchVelocity);
            if (speed <= 0f || maxTumbleAngularSpeed <= 0f || tumbleAxis.sqrMagnitude < MinTumbleAxisSqrMagnitude)
            {
                return;
            }

            float radius = ResolveBallRadius();
            if (radius <= 0f)
            {
                return;
            }

            // Rolling contact would be speed / radius, which on a ball a few centimetres across is
            // hundreds of radians per second - past a full turn per physics step, so it would read
            // as a stutter. The clamp is therefore the rate normally seen; the rolling figure only
            // matters for a ball big or slow enough to fall under it.
            float tumbleRate = Mathf.Min(speed / radius, maxTumbleAngularSpeed);
            projectileRigidbody.angularVelocity = tumbleAxis.normalized * tumbleRate;
        }

        /// <summary>
        /// The ball's radius in world units. Read per shot rather than cached, because a pooled
        /// ball is reparented on every rent and its lossy scale is only settled at that point.
        /// A non-sphere collider falls back to its largest extent, which is the same number for
        /// the sphere case and merely approximate for anything else.
        /// </summary>
        private float ResolveBallRadius()
        {
            if (projectileCollider == null)
            {
                projectileCollider = GetComponent<Collider>();
            }

            if (projectileCollider == null)
            {
                return 0f;
            }

            if (projectileCollider is SphereCollider sphere)
            {
                Vector3 scale = sphere.transform.lossyScale;
                float largestScale = Mathf.Max(
                    Mathf.Abs(scale.x),
                    Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
                return sphere.radius * largestScale;
            }

            Vector3 extents = projectileCollider.bounds.extents;
            return Mathf.Max(extents.x, Mathf.Max(extents.y, extents.z));
        }

        /// <summary>
        /// Two clocks, not one: the flight timeout that catches a shot which never hits anything,
        /// and the short life a shot is given after it does hit, so it can bounce on into a
        /// second block instead of vanishing at the moment of contact.
        /// </summary>
        private void Update()
        {
            if (hasHit)
            {
                sinceHit += Time.deltaTime;
                if (sinceHit >= postImpactLifetime)
                {
                    Despawn();
                }

                return;
            }

            if (flightRemaining <= 0f)
            {
                return;
            }

            flightRemaining -= Time.deltaTime;
            if (flightRemaining <= 0f)
            {
                Despawn();
            }
        }

        /// <summary>
        /// Every path out of a shot ends here - retired by the pool, timed out, or destroyed -
        /// so this is where the ignore pairs it set have to be put back.
        /// </summary>
        private void OnDisable()
        {
            ClearIgnoredCollisions();

            // Per-shot, like the ignore pairs above, and cleared in the same place for the same
            // reason: the next shot out of this instance is told its multiplier before it is
            // launched, and one that is somehow not told must fall back to the bullet's authored
            // damage rather than inherit the last vehicle the player was driving. Resetting in
            // Launch instead would be too late - the fire controller sets it before that call.
            damageMultiplier = 1f;
        }

        private void Despawn()
        {
            if (pool != null)
            {
                pool.Return(this);
                return;
            }

            Destroy(gameObject);
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (hasHit || collision == null)
            {
                return;
            }

            KnockdownBlock block = collision.collider.GetComponentInParent<KnockdownBlock>();
            if (block == null || block.IsActivated)
            {
                IgnoreCollision(collision.collider);
                return;
            }

            int otherLayerMask = 1 << collision.gameObject.layer;
            if ((hittableLayers.value & otherLayerMask) == 0)
            {
                return;
            }

            hasHit = true;
            sinceHit = 0f;

            // Nothing left to tunnel through at the speed it is now going, and continuous
            // detection on a ball rattling around inside a collapsing structure is pure cost.
            projectileRigidbody.collisionDetectionMode = CollisionDetectionMode.Discrete;

            ContactPoint contact = collision.GetContact(0);
            Vector3 impulseDirection = projectileRigidbody.linearVelocity.sqrMagnitude > 0.01f
                ? projectileRigidbody.linearVelocity.normalized
                : transform.forward;
            KnockBlocks(contact.point, impulseDirection, block);

            if (postImpactLifetime <= 0f)
            {
                Despawn();
            }
        }

        /// <summary>
        /// Damage comes from the loaded ammunition, looked up against the material that was hit,
        /// rather than from the projectile's speed. Deliberately independent of it: the launch
        /// speed is a feel setting that has already been changed by a factor of five to make the
        /// arc visible, and damage that moved with it would silently re-tune every material
        /// threshold in the game. How hard a shot lands is a design decision, not a physics
        /// reading.
        /// </summary>
        private void KnockBlocks(Vector3 impactPoint, Vector3 impulseDirection, KnockdownBlock directlyHitBlock)
        {
            Vector3 forceDirection = (impulseDirection + Vector3.up * upwardForce).normalized;

            // Static and cleared per shot rather than allocated per shot. Only one shot is ever
            // resolving a hit at a time: this runs inside OnCollisionEnter and never yields.
            HashSet<KnockdownBlock> processedBlocks = ProcessedBlocks;
            processedBlocks.Clear();

            if (directlyHitBlock != null)
            {
                processedBlocks.Add(directlyHitBlock);
                TryAffect(directlyHitBlock, impactPoint, forceDirection, impactForce, true, 1f);
            }

            if (impactRadius <= 0f)
            {
                return;
            }

            int hitCount = Physics.OverlapSphereNonAlloc(
                impactPoint,
                impactRadius,
                OverlapBuffer,
                hittableLayers,
                QueryTriggerInteraction.Ignore);

            for (int i = 0; i < hitCount; i++)
            {
                KnockdownBlock nearbyBlock = OverlapBuffer[i].GetComponentInParent<KnockdownBlock>();
                if (nearbyBlock == null || !processedBlocks.Add(nearbyBlock))
                {
                    continue;
                }

                float distance = Vector3.Distance(impactPoint, nearbyBlock.transform.position);
                float falloff = Mathf.Max(minimumFalloff, Mathf.Clamp01(1f - (distance / impactRadius)));
                TryAffect(
                    nearbyBlock,
                    impactPoint,
                    forceDirection,
                    impactForce * falloff * neighborImpulseMultiplier,
                    false,
                    falloff);
            }
        }

        /// <summary>
        /// Shoves a target and damages it, but only if the loaded ammunition can hurt what the
        /// target is made of. A rock that cannot scratch concrete does not get to topple it
        /// either - it bounces off and the wall stands, which is the whole point of a material
        /// the player has to unlock different ammunition for.
        /// </summary>
        private void TryAffect(
            KnockdownBlock target,
            Vector3 impactPoint,
            Vector3 forceDirection,
            float force,
            bool direct,
            float falloff)
        {
            ResolveTarget(target, out BreakableWall wall, out BreakableBlock block, out string materialId);

            // Unarmoured walls resolve as bare material: same body, same mesh, but the shot
            // takes blockDamage off it, which is how a level says "this material has no shell".
            float damage = ResolveDamage(materialId, wall != null && wall.IsArmored, direct, falloff);
            if (damage <= 0f)
            {
                return;
            }

            // Knocked before it is damaged: breaking destroys the target, and the shove is what
            // its debris inherits its velocity from.
            target.Knock(impactPoint, forceDirection * force, ForceMode.Impulse);

            if (wall != null)
            {
                wall.ApplyDamage(damage, impactPoint, forceDirection);
            }
            else if (block != null)
            {
                block.ApplyDamage(damage, impactPoint, forceDirection);
            }
        }

        private static void ResolveTarget(
            KnockdownBlock target,
            out BreakableWall wall,
            out BreakableBlock block,
            out string materialId)
        {
            target.TryGetComponent(out wall);
            target.TryGetComponent(out block);

            if (wall != null)
            {
                materialId = wall.MaterialId;
                return;
            }

            materialId = block != null ? block.MaterialId : null;
        }

        /// <summary>
        /// How much this shot takes off that material. Zero means the ammunition cannot hurt it
        /// at all, which is different from hurting it slowly.
        /// </summary>
        private float ResolveDamage(string materialId, bool isWall, bool direct, float falloff)
        {
            BulletDefinition bullet = ResolveBullet(out int level);
            if (bullet == null)
            {
                // Nothing configured yet, so every material takes the same flat hit.
                return direct ? directHitDamage : splashDamage * falloff;
            }

            if (!bullet.TryGetDamage(level, materialId, out BulletDefinition.MaterialDamage damage))
            {
                return 0f;
            }

            float amount = isWall ? damage.wallDamage : damage.blockDamage;

            // Before the early return, so the splash path below is boosted too. A material the
            // ammunition cannot hurt is authored as 0 and stays 0 however good the vehicle is,
            // which is what keeps the vehicle from quietly unlocking matchups the bullet is
            // meant to be bought for. The flat fallback above is deliberately left alone: it
            // only runs when nothing is configured, and boosting it would hide that.
            amount *= damageMultiplier;

            if (amount <= 0f || direct)
            {
                return amount;
            }

            BulletDefinition.Level bulletLevel = bullet.GetLevel(level);
            return amount * (bulletLevel?.splashShare ?? 0f) * falloff;
        }

        private BulletDefinition ResolveBullet(out int level)
        {
            if (bulletOverride != null)
            {
                level = Mathf.Max(1, bulletLevelOverride);
                return bulletOverride;
            }

            if (loadout != null && loadout.Selected != null)
            {
                level = loadout.SelectedLevel;
                return loadout.Selected;
            }

            level = 1;
            return null;
        }

        private void IgnoreSpawnOverlaps()
        {
            if (projectileCollider == null)
            {
                projectileCollider = GetComponent<Collider>();
            }

            if (projectileCollider == null)
            {
                return;
            }

            Bounds bounds = projectileCollider.bounds;
            float overlapRadius = Mathf.Max(bounds.extents.x, bounds.extents.y, bounds.extents.z) * 0.98f;
            int overlapCount = Physics.OverlapSphereNonAlloc(
                bounds.center,
                overlapRadius,
                OverlapBuffer,
                hittableLayers,
                QueryTriggerInteraction.Ignore);

            for (int i = 0; i < overlapCount; i++)
            {
                IgnoreCollision(OverlapBuffer[i]);
            }
        }

        private void IgnoreCollision(Collider other)
        {
            if (projectileCollider == null || other == null || other == projectileCollider)
            {
                return;
            }

            Physics.IgnoreCollision(projectileCollider, other, true);
            ignoredColliders.Add(other);
        }

        /// <summary>
        /// Puts back every pair this shot suppressed. An ignore pair is a property of the two
        /// colliders, not of the shot, so a pooled ball that skipped this would keep flying
        /// through blocks its previous life had spawned inside.
        /// </summary>
        private void ClearIgnoredCollisions()
        {
            for (int i = 0; i < ignoredColliders.Count; i++)
            {
                Collider other = ignoredColliders[i];
                if (other != null && projectileCollider != null)
                {
                    Physics.IgnoreCollision(projectileCollider, other, false);
                }
            }

            ignoredColliders.Clear();
        }
    }
}
