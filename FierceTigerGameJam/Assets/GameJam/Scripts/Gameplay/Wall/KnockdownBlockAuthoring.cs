using UnityEngine;

namespace GameJam.Gameplay.Wall
{
    [DisallowMultipleComponent]
    public sealed class KnockdownBlockAuthoring : MonoBehaviour
    {
        [SerializeField] private bool countsTowardKnockdown = true;
        [SerializeField] private float mass = 1f;
        [SerializeField] private Vector3Int logicalSize = Vector3Int.one;
        [SerializeField] private Vector2Int gridPosition;

        public bool CountsTowardKnockdown => countsTowardKnockdown;
        public float Mass => mass;
        public Vector3Int LogicalSize => logicalSize;
        public Vector2Int GridPosition => gridPosition;

        public void SetGridPosition(Vector2Int value)
        {
            gridPosition = value;
        }

        private void OnValidate()
        {
            if (mass < 0.01f)
            {
                mass = 0.01f;
            }

            logicalSize.x = Mathf.Max(1, logicalSize.x);
            logicalSize.y = Mathf.Max(1, logicalSize.y);
            logicalSize.z = Mathf.Max(1, logicalSize.z);
        }
    }
}
