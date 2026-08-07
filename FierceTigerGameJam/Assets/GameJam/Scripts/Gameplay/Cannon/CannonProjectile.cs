using UnityEngine;

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
        private bool hasHit;

        private void Awake()
        {
            projectileRigidbody = GetComponent<Rigidbody>();
            projectileRigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        }

        public void Launch(Vector3 direction, float speed, float lifetime)
        {
            if (projectileRigidbody == null)
            {
                projectileRigidbody = GetComponent<Rigidbody>();
            }

            projectileRigidbody.linearVelocity = direction.normalized * speed;

            if (lifetime > 0f)
            {
                Destroy(gameObject, lifetime);
            }
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (hasHit)
            {
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

            KnockBlocks(contact.point, impulseDirection);

            if (destroyOnImpact)
            {
                Destroy(gameObject);
            }
        }

        private void KnockBlocks(Vector3 impactPoint, Vector3 impulseDirection)
        {
            Collider[] hits = Physics.OverlapSphere(impactPoint, impactRadius, hittableLayers, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < hits.Length; i++)
            {
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
