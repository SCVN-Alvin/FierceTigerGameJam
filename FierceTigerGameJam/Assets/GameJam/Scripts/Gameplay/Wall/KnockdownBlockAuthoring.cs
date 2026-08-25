using UnityEngine;
using GameJam.Gameplay;

namespace GameJam.Gameplay.Wall
{
    [DisallowMultipleComponent]
    public sealed class KnockdownBlockAuthoring : MonoBehaviour
    {
        [SerializeField] private bool countsTowardKnockdown = true;
        [SerializeField] private float mass = 1f;
        [SerializeField] private bool allowCollisionCascade = true;
        [SerializeField] private float collisionActivationVelocity = 1.75f;
        [SerializeField] private KnockdownBlock.SupportCascadeMode supportCascadeMode = KnockdownBlock.SupportCascadeMode.ColumnAbove;
        [SerializeField] private float supportReleaseImpulse = 0.35f;
        [SerializeField] private Vector3Int logicalSize = Vector3Int.one;

        [Tooltip("Fastest a knock may leave this block travelling sideways. Authored per material "
                 + "because glass wants to scatter further than concrete. Zero disables the cap.")]
        [SerializeField] private float maxKnockHorizontalSpeed = 8.5f;

        [Tooltip("Fastest a knock may throw this block upward.")]
        [SerializeField] private float maxKnockVerticalSpeed = 1.35f;

        [Tooltip("Cell coordinate as (column x, layer level y, row z).")]
        [SerializeField] private Vector3Int gridPosition;

        public bool CountsTowardKnockdown => countsTowardKnockdown;
        public float Mass => mass;
        public bool AllowCollisionCascade => allowCollisionCascade;
        public float CollisionActivationVelocity => collisionActivationVelocity;
        public KnockdownBlock.SupportCascadeMode SupportCascadeMode => supportCascadeMode;
        public float SupportReleaseImpulse => supportReleaseImpulse;
        public float MaxKnockHorizontalSpeed => maxKnockHorizontalSpeed;
        public float MaxKnockVerticalSpeed => maxKnockVerticalSpeed;
        public Vector3Int LogicalSize => logicalSize;
        public Vector3Int GridPosition => gridPosition;

        /// <summary>
        /// Takes the physical character of another block - how it topples, what holds up what -
        /// but with its own mass. A wall built from many blocks uses this so it behaves like the
        /// material it is made of while weighing what all of its blocks weigh together.
        /// </summary>
        public void CopyTuningFrom(KnockdownBlockAuthoring source, float massOverride)
        {
            if (source == null)
            {
                return;
            }

            countsTowardKnockdown = source.countsTowardKnockdown;
            allowCollisionCascade = source.allowCollisionCascade;
            collisionActivationVelocity = source.collisionActivationVelocity;
            supportCascadeMode = source.supportCascadeMode;
            supportReleaseImpulse = source.supportReleaseImpulse;
            maxKnockHorizontalSpeed = source.maxKnockHorizontalSpeed;
            maxKnockVerticalSpeed = source.maxKnockVerticalSpeed;
            mass = Mathf.Max(0.01f, massOverride);
        }

        public void SetGridPosition(Vector3Int value)
        {
            gridPosition = value;
        }

        /// <summary>
        /// Set by the map builder so a rotated block reports the cells it actually covers;
        /// the support cascade reads this to decide what a block is holding up.
        /// </summary>
        public void SetLogicalSize(Vector3Int value)
        {
            logicalSize = new Vector3Int(
                Mathf.Max(1, value.x),
                Mathf.Max(1, value.y),
                Mathf.Max(1, value.z));
        }

        private void OnValidate()
        {
            if (mass < 0.01f)
            {
                mass = 0.01f;
            }

            collisionActivationVelocity = Mathf.Max(0f, collisionActivationVelocity);
            supportReleaseImpulse = Mathf.Max(0f, supportReleaseImpulse);
            maxKnockHorizontalSpeed = Mathf.Max(0f, maxKnockHorizontalSpeed);
            maxKnockVerticalSpeed = Mathf.Max(0f, maxKnockVerticalSpeed);
            logicalSize.x = Mathf.Max(1, logicalSize.x);
            logicalSize.y = Mathf.Max(1, logicalSize.y);
            logicalSize.z = Mathf.Max(1, logicalSize.z);
        }
    }
}
