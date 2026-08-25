using System;
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

        [Header("Knock Clamp")]
        [Tooltip("Fastest a knock may leave a block travelling sideways. Uncapped, one good shot "
                 + "throws blocks off the screen and the chain of destruction stops reading as a "
                 + "chain. Zero disables the cap.")]
        [SerializeField] private float maxKnockHorizontalSpeed = 8.5f;

        [Tooltip("Fastest a knock may throw a block upward. Only the upward half is capped: a "
                 + "block on its way back down is gravity's doing, not the shot's.")]
        [SerializeField] private float maxKnockVerticalSpeed = 1.35f;

        [Tooltip("Reports every knock the clamp had to cut back. For checking the cap is doing "
                 + "its job during a cascade; a full structure logs a lot.")]
        [SerializeField] private bool logClampedKnocks;

        [Tooltip("Cell coordinate as (column x, layer level y, row z).")]
        [SerializeField] private Vector3Int gridPosition;

        private const float HalfCellOffset = 0.5f;

        private Rigidbody blockRigidbody;
        private bool isActivated;

        /// <summary>
        /// Raised the first time the block stops being static, however that happened - struck,
        /// caught in a blast, or released by the block under it. A wall built from many blocks
        /// listens for this to know it has been hit.
        /// </summary>
        public event Action<KnockdownBlock> Activated;

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
            maxKnockHorizontalSpeed = authoring.MaxKnockHorizontalSpeed;
            maxKnockVerticalSpeed = authoring.MaxKnockVerticalSpeed;
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
            ClampKnockSpeed();
        }

        /// <summary>
        /// Caps what one knock can do to a block's speed. An impulse is applied to the velocity
        /// there and then, so the cut can be made here rather than being chased across the next
        /// physics step.
        /// </summary>
        private void ClampKnockSpeed()
        {
            Vector3 velocity = blockRigidbody.linearVelocity;
            bool clamped = false;

            if (maxKnockHorizontalSpeed > 0f)
            {
                float horizontalSqr = (velocity.x * velocity.x) + (velocity.z * velocity.z);
                if (horizontalSqr > maxKnockHorizontalSpeed * maxKnockHorizontalSpeed)
                {
                    float scale = maxKnockHorizontalSpeed / Mathf.Sqrt(horizontalSqr);
                    velocity.x *= scale;
                    velocity.z *= scale;
                    clamped = true;
                }
            }

            if (maxKnockVerticalSpeed > 0f && velocity.y > maxKnockVerticalSpeed)
            {
                velocity.y = maxKnockVerticalSpeed;
                clamped = true;
            }

            if (!clamped)
            {
                return;
            }

            blockRigidbody.linearVelocity = velocity;

#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (logClampedKnocks)
            {
                Debug.Log($"{name} knock clamped to {velocity} m/s.", this);
            }
#endif
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
            Activated?.Invoke(this);
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
            // Deliberately the cheapest pair of settings there is. Interpolation and continuous
            // detection on a few hundred bodies at once is what melts frames on a mid-range
            // phone, and a block only has to fall over convincingly. The projectile is the one
            // thing fast enough to tunnel, so it keeps ContinuousDynamic to itself.
            blockRigidbody.interpolation = RigidbodyInterpolation.None;
            blockRigidbody.collisionDetectionMode = CollisionDetectionMode.Discrete;
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
            Activated?.Invoke(this);
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
