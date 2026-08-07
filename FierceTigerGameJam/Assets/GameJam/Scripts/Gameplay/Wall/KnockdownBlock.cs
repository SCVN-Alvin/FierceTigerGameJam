using UnityEngine;

namespace GameJam.Gameplay
{
    [RequireComponent(typeof(Collider))]
    [RequireComponent(typeof(Rigidbody))]
    public class KnockdownBlock : MonoBehaviour
    {
        [SerializeField] private bool startAsleep = true;
        [SerializeField] private float mass = 1f;
        [SerializeField] private float angularDrag = 0.05f;
        [SerializeField] private float linearDrag = 0f;
        [SerializeField] private float collisionActivationVelocity = 1.5f;

        private Rigidbody blockRigidbody;
        private bool isActivated;

        public bool IsActivated => isActivated;

        private void Awake()
        {
            blockRigidbody = GetComponent<Rigidbody>();
            blockRigidbody.mass = mass;
            blockRigidbody.angularDamping = angularDrag;
            blockRigidbody.linearDamping = linearDrag;
            blockRigidbody.interpolation = RigidbodyInterpolation.Interpolate;
            blockRigidbody.collisionDetectionMode = CollisionDetectionMode.Continuous;

            if (startAsleep)
            {
                blockRigidbody.isKinematic = true;
                blockRigidbody.useGravity = false;
            }
        }

        public void Knock(Vector3 impactPoint, Vector3 force, ForceMode forceMode)
        {
            Activate();
            blockRigidbody.AddForceAtPosition(force, impactPoint, forceMode);
        }

        public void Activate()
        {
            if (isActivated)
            {
                return;
            }

            isActivated = true;
            blockRigidbody.isKinematic = false;
            blockRigidbody.useGravity = true;
            blockRigidbody.WakeUp();
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (isActivated || collision.relativeVelocity.sqrMagnitude < collisionActivationVelocity * collisionActivationVelocity)
            {
                return;
            }

            KnockdownBlock otherBlock = collision.rigidbody != null
                ? collision.rigidbody.GetComponent<KnockdownBlock>()
                : null;

            if (otherBlock == null || !otherBlock.IsActivated)
            {
                return;
            }

            ContactPoint contact = collision.GetContact(0);
            Vector3 force = collision.relativeVelocity.normalized * otherBlock.blockRigidbody.mass;
            Knock(contact.point, force, ForceMode.Impulse);
        }
    }
}
