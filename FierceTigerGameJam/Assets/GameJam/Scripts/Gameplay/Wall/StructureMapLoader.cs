using UnityEngine;
using GameJam.Gameplay;

namespace GameJam.Gameplay.Wall
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(WallBlockPhysicsSetup))]
    public sealed class StructureMapLoader : MonoBehaviour
    {
        [SerializeField] private GameObject structurePrefab;
        [SerializeField] private Transform structureRoot;
        [SerializeField] private WallBlockPhysicsSetup physicsSetup;
        [SerializeField] private bool clearStructureRootOnLoad = true;

        private GameObject structureInstance;

        private void Awake()
        {
            if (physicsSetup == null)
            {
                physicsSetup = GetComponent<WallBlockPhysicsSetup>();
            }
        }

        private void Start()
        {
            LoadMap();
        }

        [ContextMenu("Load Map")]
        public void LoadMap()
        {
            if (structurePrefab == null)
            {
#if UNITY_EDITOR
                Debug.LogWarning($"{nameof(StructureMapLoader)} needs a structure prefab reference.", this);
#endif
                return;
            }

            if (structureInstance != null)
            {
                DestroyStructureObject(structureInstance);
                structureInstance = null;
            }

            Transform parent = structureRoot != null ? structureRoot : transform;
            if (clearStructureRootOnLoad)
            {
                ClearStructureRoot(parent);
            }

            structureInstance = Instantiate(structurePrefab, parent);
            structureInstance.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            structureInstance.transform.localScale = Vector3.one;

            if (structureInstance.TryGetComponent(out KnockdownTableLayout tableLayout))
            {
                SetupLoadedTable(parent, tableLayout);
            }
            else if (structureInstance.TryGetComponent(out StructureLayout structureLayout))
            {
                SetupLegacyStructure(parent, structureLayout);
            }

            if (physicsSetup != null && structureInstance != null)
            {
                Transform physicsRoot = structureInstance.transform;
                if (structureInstance.TryGetComponent(out KnockdownTableLayout resolvedTableLayout))
                {
                    physicsRoot = resolvedTableLayout.BlocksRoot;
                }

                physicsSetup.PrepareBlocks(physicsRoot);
            }
        }

        private void SetupLoadedTable(Transform parent, KnockdownTableLayout tableLayout)
        {
            if (tableLayout.TryGetSpawnLocalPositionFromCenter(out Vector3 spawnLocalPosition))
            {
                structureInstance.transform.localPosition = spawnLocalPosition;
            }

            SpinOnAxis spinner = ResolveSpinner(parent);
            if (spinner != null)
            {
                spinner.SetRotationCenter(tableLayout.StructureCenter);
            }
        }

        private void SetupLegacyStructure(Transform parent, StructureLayout structureLayout)
        {
            if (structureLayout.TryGetSpawnLocalPositionFromCenter(out Vector3 spawnLocalPosition))
            {
                structureInstance.transform.localPosition = spawnLocalPosition;
            }

            SpinOnAxis spinner = ResolveSpinner(parent);
            if (spinner != null)
            {
                spinner.SetRotationCenter(structureLayout.StructureCenter);
            }
        }

        private SpinOnAxis ResolveSpinner(Transform parent)
        {
            if (parent != null && parent.TryGetComponent(out SpinOnAxis parentSpinner))
            {
                return parentSpinner;
            }

            if (structureRoot != null && structureRoot.TryGetComponent(out SpinOnAxis rootSpinner))
            {
                return rootSpinner;
            }

            return GetComponent<SpinOnAxis>();
        }

        private void ClearStructureRoot(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                Transform child = parent.GetChild(i);
                DestroyStructureObject(child.gameObject);
            }
        }

        private void DestroyStructureObject(GameObject structureObject)
        {
            if (Application.isPlaying)
            {
                Destroy(structureObject);
            }
            else
            {
                DestroyImmediate(structureObject);
            }
        }
    }
}
