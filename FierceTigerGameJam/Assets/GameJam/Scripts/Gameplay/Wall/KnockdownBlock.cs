using System.Collections.Generic;
using UnityEngine;
using GameJam.Gameplay.Wall;

namespace GameJam.Gameplay
{
    [RequireComponent(typeof(Collider))]
    [RequireComponent(typeof(Rigidbody))]
    public class KnockdownBlock : MonoBehaviour
    {
        public enum SupportCascadeMode
        {
            Disabled,
            OneLevel,
            ColumnAbove
        }

        [SerializeField] private bool startAsleep = true;
        [SerializeField] private float mass = 1f;
        [SerializeField] private float angularDrag = 0.05f;
        [SerializeField] private float linearDrag = 0f;
        [SerializeField] private float collisionActivationVelocity = 1.5f;
        [SerializeField] private bool allowCollisionCascade;
        [SerializeField] private SupportCascadeMode supportCascadeMode = SupportCascadeMode.ColumnAbove;
        [SerializeField] private float supportReleaseImpulse = 0.35f;
        [SerializeField] private bool countsTowardKnockdown = true;
        [SerializeField] private Vector3Int logicalSize = Vector3Int.one;

        [Tooltip("Cell coordinate as (column x, layer level y, row z).")]
        [SerializeField] private Vector3Int gridPosition;

        private const float HalfCellOffset = 0.5f;

        private Rigidbody blockRigidbody;
        private bool isActivated;

        public bool IsActivated => isActivated;
        public bool CountsTowardKnockdown => countsTowardKnockdown;
        public Vector3Int LogicalSize => logicalSize;
        public Vector3Int GridPosition => gridPosition;

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
            allowCollisionCascade = authoring.AllowCollisionCascade;
            collisionActivationVelocity = authoring.CollisionActivationVelocity;
            supportCascadeMode = authoring.SupportCascadeMode;
            supportReleaseImpulse = authoring.SupportReleaseImpulse;
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
            ReleaseSupportedBlocksAbove();
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

        private void ReleaseSupportedBlocksAbove()
        {
            if (supportCascadeMode == SupportCascadeMode.Disabled)
            {
                return;
            }

            Transform parent = transform.parent;
            if (parent == null)
            {
                return;
            }

            List<KnockdownBlock> supportedBlocks = new List<KnockdownBlock>();
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

                if (!IsSupportedAbove(candidate))
                {
                    continue;
                }

                supportedBlocks.Add(candidate);
            }

            supportedBlocks.Sort((left, right) => left.GridPosition.y.CompareTo(right.GridPosition.y));

            if (supportCascadeMode == SupportCascadeMode.OneLevel)
            {
                if (supportedBlocks.Count > 0)
                {
                    supportedBlocks[0].ActivateFromSupportRelease(this);
                }

                return;
            }

            for (int i = 0; i < supportedBlocks.Count; i++)
            {
                supportedBlocks[i].ActivateFromSupportRelease(this);
            }
        }

        private bool IsSupportedAbove(KnockdownBlock candidate)
        {
            if (candidate.GridPosition.y <= gridPosition.y)
            {
                return false;
            }

            // A block only holds up what sits directly over its footprint, so both the column
            // and the depth row have to overlap.
            return RangesOverlap(
                       gridPosition.x,
                       Mathf.Max(1, logicalSize.x),
                       candidate.GridPosition.x,
                       Mathf.Max(1, candidate.LogicalSize.x))
                   && RangesOverlap(
                       gridPosition.z,
                       Mathf.Max(1, logicalSize.z),
                       candidate.GridPosition.z,
                       Mathf.Max(1, candidate.LogicalSize.z));
        }

        private static bool RangesOverlap(int leftStart, int leftSize, int rightStart, int rightSize)
        {
            float leftMin = leftStart - HalfCellOffset;
            float leftMax = leftStart + leftSize - HalfCellOffset;
            float rightMin = rightStart - HalfCellOffset;
            float rightMax = rightStart + rightSize - HalfCellOffset;
            return leftMin < rightMax && rightMin < leftMax;
        }

        private void ActivateFromSupportRelease(KnockdownBlock releasedBy)
        {
            if (isActivated)
            {
                return;
            }

            isActivated = true;
            blockRigidbody.isKinematic = false;
            blockRigidbody.useGravity = true;
            blockRigidbody.WakeUp();
            ApplySupportReleaseImpulse(releasedBy);
        }

        private void ApplySupportReleaseImpulse(KnockdownBlock releasedBy)
        {
            if (blockRigidbody == null || supportReleaseImpulse <= 0f)
            {
                return;
            }

            Vector3 direction = transform.position - (releasedBy != null ? releasedBy.transform.position : transform.position);
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f)
            {
                direction = transform.right;
            }

            Vector3 impulse = (direction.normalized + Vector3.up * 0.25f).normalized * supportReleaseImpulse;
            blockRigidbody.AddForce(impulse, ForceMode.Impulse);
        }
    }
}
