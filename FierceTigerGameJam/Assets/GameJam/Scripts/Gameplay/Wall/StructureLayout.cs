using UnityEngine;

namespace GameJam.Gameplay.Wall
{
    [DisallowMultipleComponent]
    public sealed class StructureLayout : MonoBehaviour
    {
        public const string CenterObjectName = "Structure Center";

        [SerializeField] private Transform structureCenter;

        public Transform StructureCenter
        {
            get
            {
                ResolveStructureCenter();
                return structureCenter;
            }
        }

        public bool TryGetStructureCenterLocalPosition(out Vector3 localPosition)
        {
            Transform center = StructureCenter;
            if (center == null)
            {
                localPosition = Vector3.zero;
                return false;
            }

            localPosition = transform.InverseTransformPoint(center.position);
            return true;
        }

        public bool TryGetSpawnLocalPositionFromCenter(out Vector3 localPosition)
        {
            if (!TryGetStructureCenterLocalPosition(out Vector3 centerLocalPosition))
            {
                localPosition = Vector3.zero;
                return false;
            }

            localPosition = -centerLocalPosition;
            return true;
        }

        private void Reset()
        {
            ResolveStructureCenter();
        }

        private void OnValidate()
        {
            ResolveStructureCenter();
        }

        private void ResolveStructureCenter()
        {
            if (structureCenter != null)
            {
                return;
            }

            Transform foundCenter = transform.Find(CenterObjectName);
            if (foundCenter != null)
            {
                structureCenter = foundCenter;
            }
        }
    }
}
