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
        [SerializeField] private bool destroyOnImpact = true;

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

        private Rigidbody projectileRigidbody;
        private Collider projectileCollider;
        private bool hasHit;

        private void Awake()
        {
            projectileRigidbody = GetComponent<Rigidbody>();
            projectileCollider = GetComponent<Collider>();
            projectileRigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        }

        private void OnValidate()
        {
            impactForce = Mathf.Max(0f, impactForce);
            impactRadius = Mathf.Max(0f, impactRadius);
            minimumFalloff = Mathf.Clamp01(minimumFalloff);
            neighborImpulseMultiplier = Mathf.Max(0f, neighborImpulseMultiplier);
            upwardForce = Mathf.Max(0f, upwardForce);
            directHitDamage = Mathf.Max(0f, directHitDamage);
            splashDamage = Mathf.Max(0f, splashDamage);
            bulletLevelOverride = Mathf.Max(1, bulletLevelOverride);
        }

        /// <summary>
        /// Tells the shot what fired it. The player brings a mix of ammunition into a run and
        /// chooses per shot, so which kind this is cannot be baked into the prefab.
        /// </summary>
        public void SetAmmunition(BulletDefinition bullet, int level)
        {
            bulletOverride = bullet;
            bulletLevelOverride = Mathf.Max(1, level);
        }

        public void Launch(Vector3 direction, float speed, float lifetime)
        {
            if (projectileRigidbody == null)
            {
                projectileRigidbody = GetComponent<Rigidbody>();
            }

            projectileRigidbody.linearVelocity = direction.normalized * speed;
            IgnoreSpawnOverlaps();

            if (lifetime > 0f)
            {
                Destroy(gameObject, lifetime);
            }
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

            ContactPoint contact = collision.GetContact(0);
            Vector3 impulseDirection = projectileRigidbody.linearVelocity.sqrMagnitude > 0.01f
                ? projectileRigidbody.linearVelocity.normalized
                : transform.forward;
            KnockBlocks(contact.point, impulseDirection, block);

            if (destroyOnImpact)
            {
                Destroy(gameObject);
            }
        }

        /// <summary>
        /// Damage comes from the loaded ammunition, looked up against the material that was hit,
        /// rather than from the projectile's speed. The ball is launched at around a hundred
        /// metres per second, so any speed-derived figure would clear every material's threshold
        /// and shatter the whole blast radius on every shot; how hard a shot lands is a tuning
        /// decision, not a physics reading.
        /// </summary>
        private void KnockBlocks(Vector3 impactPoint, Vector3 impulseDirection, KnockdownBlock directlyHitBlock)
        {
            Vector3 forceDirection = (impulseDirection + Vector3.up * upwardForce).normalized;
            HashSet<KnockdownBlock> processedBlocks = new HashSet<KnockdownBlock>();

            if (directlyHitBlock != null)
            {
                processedBlocks.Add(directlyHitBlock);
                TryAffect(directlyHitBlock, impactPoint, forceDirection, impactForce, true, 1f);
            }

            if (impactRadius <= 0f)
            {
                return;
            }

            Collider[] hits = Physics.OverlapSphere(
                impactPoint,
                impactRadius,
                hittableLayers,
                QueryTriggerInteraction.Ignore);

            for (int i = 0; i < hits.Length; i++)
            {
                KnockdownBlock nearbyBlock = hits[i].GetComponentInParent<KnockdownBlock>();
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

            float damage = ResolveDamage(materialId, wall != null, direct, falloff);
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
            Collider[] overlaps = Physics.OverlapSphere(
                bounds.center,
                overlapRadius,
                hittableLayers,
                QueryTriggerInteraction.Ignore);

            for (int i = 0; i < overlaps.Length; i++)
            {
                IgnoreCollision(overlaps[i]);
            }
        }

        private void IgnoreCollision(Collider other)
        {
            if (projectileCollider == null || other == null || other == projectileCollider)
            {
                return;
            }

            Physics.IgnoreCollision(projectileCollider, other, true);
        }
    }
}
