using UnityEngine;

namespace GameJam.Gameplay.Wall
{
    [DisallowMultipleComponent]
    public sealed class KnockdownTableLayout : MonoBehaviour
    {
        public const string DefaultBlocksRootName = "Blocks";

        [SerializeField] private Transform structureCenter;
        [SerializeField] private Transform blockStackFloorPivot;
        [SerializeField] private Transform blocksRoot;

        public Transform StructureCenter
        {
            get
            {
                ResolveReferences();
                return structureCenter;
            }
        }

        public Transform BlockStackFloorPivot
        {
            get
            {
                ResolveReferences();
                return blockStackFloorPivot != null ? blockStackFloorPivot : transform;
            }
        }

        public Transform BlocksRoot
        {
            get
            {
                ResolveReferences();
                return blocksRoot != null ? blocksRoot : BlockStackFloorPivot;
            }
        }

        public bool TryGetSpawnLocalPositionFromCenter(out Vector3 localPosition)
        {
            Transform center = StructureCenter;
            if (center == null)
            {
                localPosition = Vector3.zero;
                return false;
            }

            localPosition = -transform.InverseTransformPoint(center.position);
            return true;
        }

        private void Reset()
        {
            ResolveReferences();
        }

        private void OnValidate()
        {
            ResolveReferences();
        }

        private void ResolveReferences()
        {
            if (structureCenter == null)
            {
                Transform foundCenter = transform.Find(StructureLayout.CenterObjectName);
                if (foundCenter != null)
                {
                    structureCenter = foundCenter;
                }
            }

            if (blockStackFloorPivot == null)
            {
                BlockStackFloorPivot foundPivot = GetComponentInChildren<BlockStackFloorPivot>(true);
                if (foundPivot != null)
                {
                    blockStackFloorPivot = foundPivot.transform;
                }
            }

            if (blocksRoot == null)
            {
                Transform pivot = blockStackFloorPivot != null ? blockStackFloorPivot : transform;
                Transform foundBlocksRoot = pivot.Find(DefaultBlocksRootName);
                blocksRoot = foundBlocksRoot != null ? foundBlocksRoot : pivot;
            }
        }
    }
}
