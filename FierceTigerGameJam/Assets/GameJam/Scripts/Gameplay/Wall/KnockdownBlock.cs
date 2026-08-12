using UnityEngine;
using GameJam.Gameplay.Wall;

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
        [SerializeField] private bool allowCollisionCascade;
        [SerializeField] private bool allowSupportCascade = true;
        [SerializeField] private bool countsTowardKnockdown = true;
        [SerializeField] private Vector3Int logicalSize = Vector3Int.one;
        [SerializeField] private Vector2Int gridPosition;

        private Rigidbody blockRigidbody;
        private bool isActivated;

        public bool IsActivated => isActivated;
        public bool CountsTowardKnockdown => countsTowardKnockdown;
        public Vector3Int LogicalSize => logicalSize;
        public Vector2Int GridPosition => gridPosition;

        private void Awake()
        {
            blockRigidbody = GetComponent<Rigidbody>();
            ApplyRuntimeBodySettings();

            if (startAsleep)
            {
                blockRigidbody.isKinematic = true;
                blockRigidbody.useGravity = false;
            }
        }

        public void ApplyAuthoring(KnockdownBlockAuthoring authoring)
        {
            if (authoring == null)
            {
                return;
            }

            countsTowardKnockdown = authoring.CountsTowardKnockdown;
            logicalSize = authoring.LogicalSize;
            mass = authoring.Mass;
            gridPosition = authoring.GridPosition;

            if (blockRigidbody == null)
            {
                blockRigidbody = GetComponent<Rigidbody>();
            }

            if (blockRigidbody != null)
            {
                ApplyRuntimeBodySettings();
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
            ReleaseSupportedBlockAbove();
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (!allowCollisionCascade)
            {
                return;
            }

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

        private void ApplyRuntimeBodySettings()
        {
            blockRigidbody.mass = mass;
            blockRigidbody.angularDamping = angularDrag;
            blockRigidbody.linearDamping = linearDrag;
            blockRigidbody.interpolation = RigidbodyInterpolation.Interpolate;
            blockRigidbody.collisionDetectionMode = CollisionDetectionMode.Continuous;
        }

        private void ReleaseSupportedBlockAbove()
        {
            if (!allowSupportCascade)
            {
                return;
            }

            Transform parent = transform.parent;
            if (parent == null)
            {
                return;
            }

            KnockdownBlock nextBlock = null;
            int nextGridY = int.MaxValue;
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                if (child == transform)
                {
                    continue;
                }

                KnockdownBlock candidate = child.GetComponent<KnockdownBlock>();
                if (candidate == null || candidate.IsActivated)
                {
                    continue;
                }

                if (candidate.GridPosition.x != gridPosition.x)
                {
                    continue;
                }

                if (candidate.GridPosition.y <= gridPosition.y)
                {
                    continue;
                }

                if (candidate.GridPosition.y >= nextGridY)
                {
                    continue;
                }

                nextGridY = candidate.GridPosition.y;
                nextBlock = candidate;
            }

            if (nextBlock != null)
            {
                nextBlock.Activate();
            }
        }
    }
}
