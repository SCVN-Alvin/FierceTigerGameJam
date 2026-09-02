using UnityEngine;
using GameJam.Audio;
using GameJam.Gameplay;
using GameJam.Gameplay.Combat;
using GameJam.Gameplay.Playfield;
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

        [Tooltip("How much harder than the world this shot falls while it is flying. 1 is world "
                 + "gravity, which makes the fixed-apex arc take about 1.6 seconds whatever the "
                 + "distance and reads as floaty; the default pulls that under a second without "
                 + "changing where the shot lands. The launch is solved against this same number, "
                 + "so raising it steepens the arc rather than moving the impact point.")]
        [SerializeField] private float gravityMultiplier = DefaultGravityMultiplier;

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
        /// What a shot falls at when nobody has said otherwise. Public so the fire controller can
        /// solve an arc for a projectile it has not built yet - the built-in debugging sphere -
        /// against the very number that instance will end up flying at.
        /// </summary>
        public const float DefaultGravityMultiplier = 2.5f;

        /// <summary>
        /// Floor under the multiplier. At zero the shot would hang in the air and the launch solve
        /// would answer with a near-infinite flight time, so an inspector typo cannot switch
        /// gravity off.
        /// </summary>
        private const float MinGravityMultiplier = 0.1f;

        /// <summary>
        /// Below this speed the heading is noise - a ball rolling to a stop, or the instant at the
        /// crest - and turning the nose to it would jitter, so the last heading is kept.
        /// </summary>
        private const float MinNoseAlignSpeedSqrMagnitude = 0.25f;

        /// <summary>
        /// How nearly vertical a heading may be before <see cref="Quaternion.LookRotation"/> is
        /// given a different up axis: it is undefined when the two are parallel, and a shot fired
        /// straight up is the one aim that gets there.
        /// </summary>
        private const float VerticalHeadingDotSqr = 0.998f;

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

        /// <summary>
        /// True from launch until the shot hits something or goes home. Gates the two things this
        /// shot does to itself in the air - the extra gravity that makes the arc snappy, and the
        /// nose alignment - so that neither outlives the flight: after a hit the physics engine
        /// owns the body, and a pooled ball that came back still boosted would fire visibly
        /// differently the second time.
        /// </summary>
        private bool isFlying;

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
            gravityMultiplier = Mathf.Max(MinGravityMultiplier, gravityMultiplier);
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
            EnableLevelLook(levelLooks, levelLookNumbers, level);
        }

        /// <summary>
        /// The same rule applied to a projectile nobody is firing - the garage's preview copy.
        ///
        /// Static and taking the root rather than the component, because that copy is a mannequin:
        /// its scripts, colliders and rigidbody are stripped off before it is ever shown, so there
        /// is no instance left to ask. Reusing the rule rather than the object is what keeps the
        /// garage and the shot agreeing about which mesh level 2 is - a second copy of the suffix
        /// spelling is exactly how the two would drift apart the next time the artist renames one.
        ///
        /// This one allocates, unlike the per-shot path, which is why that path keeps its cache:
        /// this runs when the player taps a row, never in flight.
        /// </summary>
        public static void ApplyLevelLook(Transform root, int level)
        {
            if (root == null)
            {
                return;
            }

            int childCount = root.childCount;
            int found = 0;
            for (int i = 0; i < childCount; i++)
            {
                if (TryParseLevelSuffix(root.GetChild(i).name, out _))
                {
                    found++;
                }
            }

            if (found == 0)
            {
                // A model with no LV children at all - every vehicle, and any ball authored as one
                // mesh. Left exactly as the prefab has it.
                return;
            }

            GameObject[] looks = new GameObject[found];
            int[] numbers = new int[found];

            int next = 0;
            for (int i = 0; i < childCount && next < found; i++)
            {
                Transform child = root.GetChild(i);
                if (!TryParseLevelSuffix(child.name, out int number))
                {
                    continue;
                }

                looks[next] = child.gameObject;
                numbers[next] = number;
                next++;
            }

            EnableLevelLook(looks, numbers, level);
        }

        /// <summary>
        /// Switches on the one look a level should show and switches the rest off. The whole of
        /// the choice, in one place, so the in-flight ball and the garage's mannequin cannot
        /// disagree: the highest n not above the level wins, and a prefab whose looks start above
        /// level 1 falls back to its lowest.
        /// </summary>
        private static void EnableLevelLook(GameObject[] looks, int[] numbers, int level)
        {
            if (looks == null || looks.Length == 0)
            {
                return;
            }

            int wanted = Mathf.Max(1, level);
            int bestAtOrBelow = -1;
            int lowest = -1;
            for (int i = 0; i < looks.Length; i++)
            {
                int number = numbers[i];
                if (number <= wanted && (bestAtOrBelow < 0 || number > numbers[bestAtOrBelow]))
                {
                    bestAtOrBelow = i;
                }

                if (lowest < 0 || number < numbers[lowest])
                {
                    lowest = i;
                }
            }

            // Nothing authored at or below the level means a prefab whose looks start higher than
            // level 1; its lowest is still a ball, which beats an invisible shot.
            int chosen = bestAtOrBelow >= 0 ? bestAtOrBelow : lowest;
            for (int i = 0; i < looks.Length; i++)
            {
                if (looks[i] != null)
                {
                    looks[i].SetActive(i == chosen);
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

        /// <summary>
        /// How much harder than the world this shot falls. Read by the fire controller off the
        /// prefab, because the arc has to be solved before there is an instance to ask, and the
        /// solve is only right if it uses the very number the shot will fly at.
        /// </summary>
        public float GravityMultiplier => Mathf.Max(MinGravityMultiplier, gravityMultiplier);

        /// <summary>
        /// The legacy entry point, kept for callers that only know a heading and a speed - the
        /// cannon's own no-aim fallback. Delegates, so there is one launch path however a shot was
        /// aimed.
        /// </summary>
        public void Launch(Vector3 direction, float speed, float lifetime)
        {
            Launch(direction.normalized * speed, lifetime);
        }

        /// <summary>
        /// Fires the shot along a velocity that was solved for where it should land, rather than a
        /// heading and a speed. The whole arc is decided by this vector plus the gravity below, so
        /// nothing after this point may push the ball around before it hits.
        /// </summary>
        public void Launch(Vector3 velocity, float lifetime)
        {
            if (projectileRigidbody == null)
            {
                projectileRigidbody = GetComponent<Rigidbody>();
            }

            hasHit = false;
            sinceHit = 0f;
            flightRemaining = lifetime;
            isFlying = true;

            // Back to the setting a shot in flight needs. A pooled ball was switched to Discrete
            // when its last shot landed, and would otherwise tunnel straight through thin glass.
            projectileRigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            projectileRigidbody.linearVelocity = velocity;

            // Where the tumble used to be set, and for the same reason it was set here: this is
            // the one call every shot makes, pooled or freshly instantiated, so it is the only
            // place that can guarantee a reused ball does not inherit the spin its last landing
            // left it rolling with. Shots fly unspun now - the nose is pointed by Update instead.
            projectileRigidbody.angularVelocity = Vector3.zero;

            // Straight away rather than waiting for the first Update, so the shot is never drawn
            // for a frame still wearing the rotation the pool handed it.
            AlignNoseToHeading(velocity);
            IgnoreSpawnOverlaps();
        }

        /// <summary>
        /// The extra pull that makes the arc snappy. World gravity alone would give the fixed-apex
        /// flight the same lazy 1.6 seconds whatever the distance; the rest of the multiplier is
        /// added here, as a velocity step ahead of the solver's own, so the total acceleration is
        /// exactly the gravity the launch was solved against and the shot lands on the tap.
        ///
        /// Only while flying: after a hit the ball is the physics engine's, and a pooled ball must
        /// not come back out still boosted.
        /// </summary>
        private void FixedUpdate()
        {
            if (!isFlying || projectileRigidbody == null)
            {
                return;
            }

            // Whatever the solver is not already applying. A body with gravity switched off in the
            // prefab gets the whole of it here, so the two can never quietly disagree.
            float extraMultiplier = GravityMultiplier - (projectileRigidbody.useGravity ? 1f : 0f);
            if (Mathf.Approximately(extraMultiplier, 0f))
            {
                return;
            }

            projectileRigidbody.linearVelocity +=
                Physics.gravity * (extraMultiplier * Time.fixedDeltaTime);
        }

        /// <summary>
        /// Points the shot the way it is going, so a rocket flies nose-first up the arc and tips
        /// over the crest. Written to the transform rather than spun into the rigidbody because
        /// this is a look, not a force: the ball carries no angular velocity at all now, so
        /// nothing is being fought. Balls are radially symmetric and simply read as still.
        /// </summary>
        private void AlignNoseToHeading(Vector3 heading)
        {
            float sqrSpeed = heading.sqrMagnitude;
            if (sqrSpeed < MinNoseAlignSpeedSqrMagnitude)
            {
                return;
            }

            // LookRotation is undefined when the heading and the up axis are parallel, which a
            // shot fired straight up would hit; anything else keeps world up so the model does not
            // roll along the arc.
            Vector3 up = (heading.y * heading.y) > (VerticalHeadingDotSqr * sqrSpeed)
                ? Vector3.forward
                : Vector3.up;
            transform.rotation = Quaternion.LookRotation(heading, up);
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

            // Ahead of the timeout, so the last frame of a flight is still pointed the right way.
            // Only while flying: the moment something is hit this stops, and the tumble the
            // collision gives the ball is left to the physics engine to draw.
            if (isFlying && projectileRigidbody != null)
            {
                AlignNoseToHeading(projectileRigidbody.linearVelocity);
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

            // Per-shot like everything else here. A ball retired mid-flight - timed out, or called
            // home when the run ended - goes back to the pool still marked as flying, and would be
            // re-enabled that way. Launch does set it again on the very next shot, and the rent and
            // the launch happen in the same frame with no physics step between them, so this is
            // belt and braces rather than a bug being fixed; it is here because "reset everything
            // per shot on the way out" is the rule this class is easy to reason about by.
            isFlying = false;

            // Per-shot, like the ignore pairs above, and cleared in the same place for the same
            // reason: the next shot out of this instance is told its multiplier before it is
            // launched, and one that is somehow not told must fall back to the bullet's authored
            // damage rather than inherit the last vehicle the player was driving. Resetting in
            // Launch instead would be too late - the fire controller sets it before that call.
            damageMultiplier = 1f;
        }

        /// <summary>
        /// Sends a live shot home early, for anything outside the ball that decides its flight is
        /// over. Exists so an out-of-bounds <see cref="FallBreakZone"/> in Despawn mode has a way
        /// to clear a ball that escaped the floor: destroying it there would take a pooled
        /// instance out of circulation permanently, shrinking the pool every time a shot went
        /// wide.
        /// </summary>
        public void ReturnToPool()
        {
            Despawn();
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

            // The floor ends a flight instead of being ignored: the ball lands, rolls out its
            // post-impact beat on the floor's friction, and goes home to the pool. Ignoring it was
            // harmless when shots were flat and died off-screen; under the Brief 18 arc it reads
            // as the world having no ground. Deliberately ahead of the block lookup, so the floor
            // never reaches the miss branch below and never gets an IgnoreCollision pair - those
            // persist for the life of a pooled instance.
            //
            // No `if (!hasHit)` guard around this, unlike the brief's sketch: the method has
            // already returned above when hasHit is true, so a ball touching the floor after
            // hitting a block keeps its running timer without any further test here.
            if (collision.collider.GetComponentInParent<FallBreakZone>() != null)
            {
                hasHit = true;
                sinceHit = 0f;

                // The arc is over: the ball rolls out its beat under plain world gravity, and
                // nothing keeps writing its rotation while it does.
                isFlying = false;

                // The first floor contact of the flight, and only ever the first: the method has
                // already returned above once hasHit is set, so a ball that rolls and touches the
                // floor again is silent.
                AudioService.Play(AudioSlot.BallFall);

                // Same reason as the block branch below: nothing left to tunnel through.
                projectileRigidbody.collisionDetectionMode = CollisionDetectionMode.Discrete;

                // No damage and no ignore - physics is left to keep the ball on the surface.
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

            // Same as the floor branch: the flight is done, so the extra pull and the nose
            // alignment both stop and the bounce is entirely the physics engine's.
            isFlying = false;

            // The hit is accepted here, so this is where the ball is heard landing on the block.
            // Separate from the material hit/break sounds, which BreakableBlock raises from the
            // damage it actually took: this one is the ball, those are the block.
            AudioService.Play(AudioSlot.BallImpact);

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
        /// rather than from the projectile's speed. Deliberately independent of it, and more so
        /// now the arc is solved for a fixed crest: the launch speed is whatever it takes to reach
        /// the tap, so a far shot leaves the muzzle half again as fast as a near one. Damage that
        /// moved with it would quietly make distant blocks easier than close ones and re-tune every
        /// material threshold in the game. How hard a shot lands is a design decision, not a
        /// physics reading.
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
        /// either - it bounces off and the block stands, which is the whole point of a material
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
            ResolveTarget(target, out BreakableBlock block, out string materialId);

            float damage = ResolveDamage(materialId, direct, falloff);
            if (damage <= 0f)
            {
                return;
            }

            // Knocked before it is damaged: breaking destroys the target, and the shove is what
            // its debris inherits its velocity from.
            target.Knock(impactPoint, forceDirection * force, ForceMode.Impulse);

            if (block != null)
            {
                block.ApplyDamage(damage, impactPoint, forceDirection);
            }
        }

        private static void ResolveTarget(
            KnockdownBlock target,
            out BreakableBlock block,
            out string materialId)
        {
            target.TryGetComponent(out block);
            materialId = block != null ? block.MaterialId : null;
        }

        /// <summary>
        /// How much this shot takes off that material. Zero means the ammunition cannot hurt it
        /// at all, which is different from hurting it slowly.
        /// </summary>
        private float ResolveDamage(string materialId, bool direct, float falloff)
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

            // Every target is a block now that walls are gone, so there is one damage number.
            float amount = damage.blockDamage;

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
