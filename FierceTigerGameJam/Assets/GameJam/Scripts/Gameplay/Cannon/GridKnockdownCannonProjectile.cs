using UnityEngine;
using GameJam.Gameplay;

namespace GameJam.Gameplay.Cannon
{
    [RequireComponent(typeof(Collider))]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class GridKnockdownCannonProjectile : MonoBehaviour
    {
        [SerializeField] private float impactForce = 18f;
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
            Vector3 force = (impulseDirection + Vector3.up * upwardForce).normalized * impactForce;
            block.Knock(contact.point, force, ForceMode.Impulse);

            if (destroyOnImpact)
            {
                Destroy(gameObject);
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
