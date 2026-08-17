using UnityEngine;
using GameJam.Gameplay;
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
        [Tooltip("Hit points taken off the block that was hit directly.")]
        [SerializeField] private float directHitDamage = 3f;

        [Tooltip("Hit points taken off blocks in the blast radius, before distance falloff.")]
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
        /// Damage here is a flat number rather than something derived from the impact speed. The
        /// ball is launched at around a hundred metres per second, so any speed-derived figure
        /// would sail past every material's threshold and shatter the whole blast radius on every
        /// shot; how hard a cannonball hits is a tuning decision, not a physics reading.
        /// </summary>
        private void KnockBlocks(Vector3 impactPoint, Vector3 impulseDirection, KnockdownBlock directlyHitBlock)
        {
            Vector3 forceDirection = (impulseDirection + Vector3.up * upwardForce).normalized;
            HashSet<KnockdownBlock> processedBlocks = new HashSet<KnockdownBlock>();

            if (directlyHitBlock != null)
            {
                processedBlocks.Add(directlyHitBlock);
                directlyHitBlock.Knock(impactPoint, forceDirection * impactForce, ForceMode.Impulse);

                // Knocked before it is damaged: breaking destroys the block, and the shove is
                // what the debris inherits its velocity from.
                Damage(directlyHitBlock, directHitDamage, impactPoint, forceDirection);
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
                float falloff = Mathf.Clamp01(1f - (distance / impactRadius));
                float impulseScale = Mathf.Max(minimumFalloff, falloff) * neighborImpulseMultiplier;
                nearbyBlock.Knock(impactPoint, forceDirection * (impactForce * impulseScale), ForceMode.Impulse);

                Damage(
                    nearbyBlock,
                    splashDamage * Mathf.Max(minimumFalloff, falloff),
                    impactPoint,
                    forceDirection);
            }
        }

        private static void Damage(KnockdownBlock block, float damage, Vector3 impactPoint, Vector3 direction)
        {
            if (block != null && block.TryGetComponent(out BreakableBlock breakable))
            {
                breakable.ApplyDamage(damage, impactPoint, direction);
            }
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
