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

        [Tooltip("Cell coordinate as (column x, layer level y, row z).")]
        [SerializeField] private Vector3Int gridPosition;

        public bool CountsTowardKnockdown => countsTowardKnockdown;
        public float Mass => mass;
        public bool AllowCollisionCascade => allowCollisionCascade;
        public float CollisionActivationVelocity => collisionActivationVelocity;
        public KnockdownBlock.SupportCascadeMode SupportCascadeMode => supportCascadeMode;
        public float SupportReleaseImpulse => supportReleaseImpulse;
        public Vector3Int LogicalSize => logicalSize;
        public Vector3Int GridPosition => gridPosition;

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
            logicalSize.x = Mathf.Max(1, logicalSize.x);
            logicalSize.y = Mathf.Max(1, logicalSize.y);
            logicalSize.z = Mathf.Max(1, logicalSize.z);
        }
    }
}
