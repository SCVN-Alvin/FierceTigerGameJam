using UnityEngine;
using GameJam.Gameplay.Wall;
using System.Collections.Generic;

namespace GameJam.Gameplay
{
    [RequireComponent(typeof(Collider))]
    [RequireComponent(typeof(Rigidbody))]
    public class CannonProjectile : MonoBehaviour
    {
        [SerializeField] private float impactForce = 18f;
        [SerializeField] private float impactRadius = 2.25f;
        [SerializeField] private float upwardForce = 0.25f;
        [SerializeField] private LayerMask hittableLayers = ~0;
        [SerializeField] private bool destroyOnImpact = true;

        private Rigidbody projectileRigidbody;
        private Collider projectileCollider;
        private bool hasHit;

        private void Awake()
        {
            projectileRigidbody = GetComponent<Rigidbody>();
            projectileCollider = GetComponent<Collider>();
            projectileRigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
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
                Collider other = overlaps[i];
                if (other == null || other == projectileCollider)
                {
                    continue;
                }

                Physics.IgnoreCollision(projectileCollider, other, true);
            }
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (hasHit)
            {
                return;
            }

            SmashBlock directlyHitBlock = collision.collider.GetComponentInParent<SmashBlock>();
            KnockdownBlock directlyHitLegacyBlock = collision.collider.GetComponentInParent<KnockdownBlock>();
            if (directlyHitBlock == null && directlyHitLegacyBlock == null)
            {
                if (projectileCollider != null && collision.collider != null)
                {
                    Physics.IgnoreCollision(projectileCollider, collision.collider, true);
                }

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

            KnockBlocks(contact.point, impulseDirection, directlyHitBlock);

            if (destroyOnImpact)
            {
                Destroy(gameObject);
            }
        }

        private void KnockBlocks(Vector3 impactPoint, Vector3 impulseDirection, SmashBlock directlyHitBlock)
        {
            Collider[] hits = Physics.OverlapSphere(impactPoint, impactRadius, hittableLayers, QueryTriggerInteraction.Ignore);
            HashSet<SmashBlock> processedSmashBlocks = new HashSet<SmashBlock>();

            for (int i = 0; i < hits.Length; i++)
            {
                SmashBlock smashBlock = hits[i].GetComponentInParent<SmashBlock>();
                if (smashBlock != null)
                {
                    if (!processedSmashBlocks.Add(smashBlock))
                    {
                        continue;
                    }

                    float smashDistance = Vector3.Distance(impactPoint, smashBlock.transform.position);
                    float smashFalloff = Mathf.Clamp01(1f - smashDistance / impactRadius);
                    Vector3 smashForce = (impulseDirection + Vector3.up * upwardForce).normalized * impactForce;
                    bool allowFracture = smashBlock == directlyHitBlock;
                    smashBlock.Knock(impactPoint, smashForce, Mathf.Max(0.2f, smashFalloff), allowFracture);
                    continue;
                }

                KnockdownBlock block = hits[i].GetComponentInParent<KnockdownBlock>();
                if (block == null)
                {
                    continue;
                }

                float distance = Vector3.Distance(impactPoint, block.transform.position);
                float falloff = Mathf.Clamp01(1f - distance / impactRadius);
                Vector3 force = (impulseDirection + Vector3.up * upwardForce).normalized * (impactForce * Mathf.Max(0.2f, falloff));
                block.Knock(impactPoint, force, ForceMode.Impulse);
            }
        }
    }
}
