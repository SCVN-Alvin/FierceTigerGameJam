using UnityEngine;
using GameJam.Gameplay;

namespace GameJam.Gameplay.Wall
{
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

            if (structureInstance.TryGetComponent(out StructureLayout structureLayout))
            {
                if (structureLayout.TryGetSpawnLocalPositionFromCenter(out Vector3 spawnLocalPosition))
                {
                    structureInstance.transform.localPosition = spawnLocalPosition;
                }

                if (parent.TryGetComponent(out SpinOnAxis spinner))
                {
                    spinner.SetRotationCenter(structureLayout.StructureCenter);
                }
            }

            if (physicsSetup != null)
            {
                physicsSetup.PrepareBlocks(structureInstance.transform);
            }
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
