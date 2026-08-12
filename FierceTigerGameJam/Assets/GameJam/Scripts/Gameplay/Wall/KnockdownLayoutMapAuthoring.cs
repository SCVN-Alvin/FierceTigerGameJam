using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace GameJam.Gameplay.Wall
{
    [DisallowMultipleComponent]
    public sealed class KnockdownLayoutMapAuthoring : MonoBehaviour
    {
        public const string GeneratedBlocksRootName = "GeneratedLayoutBlocks";

        [SerializeField] private int width = 5;
        [SerializeField] private int height = 5;
        [SerializeField] private GameObject blockPrefab;
        [SerializeField] private Transform blocksRoot;
        [SerializeField] private Vector2 cellSize = Vector2.one;
        [SerializeField] private bool centerGrid = true;
        [SerializeField, HideInInspector] private int serializedWidth = 5;
        [SerializeField, HideInInspector] private int serializedHeight = 5;
        [SerializeField, HideInInspector] private bool[] occupiedCells = new bool[25];

        public int Width => width;
        public int Height => height;
        public GameObject BlockPrefab => blockPrefab;
        public Transform BlocksRoot => ResolveBlocksRoot();
        public Vector2 CellSize => cellSize;
        public bool CenterGrid => centerGrid;

        public bool IsCellOccupied(int x, int y)
        {
            if (!IsInside(x, y) || occupiedCells == null)
            {
                return false;
            }

            int index = GetIndex(x, y);
            return index >= 0 && index < occupiedCells.Length && occupiedCells[index];
        }

        public void SetCellOccupied(int x, int y, bool occupied)
        {
            EnsureGridCapacity();
            if (!IsInside(x, y))
            {
                return;
            }

            occupiedCells[GetIndex(x, y)] = occupied;
        }

        public void ToggleCell(int x, int y)
        {
            SetCellOccupied(x, y, !IsCellOccupied(x, y));
        }

        public void ResizeGrid(int newWidth, int newHeight)
        {
            newWidth = Mathf.Max(1, newWidth);
            newHeight = Mathf.Max(1, newHeight);

            bool[] previous = occupiedCells;
            int previousWidth = width;
            int previousHeight = height;

            width = newWidth;
            height = newHeight;
            occupiedCells = new bool[width * height];

            if (previous == null)
            {
                return;
            }

            int copyWidth = Mathf.Min(previousWidth, width);
            int copyHeight = Mathf.Min(previousHeight, height);
            for (int y = 0; y < copyHeight; y++)
            {
                for (int x = 0; x < copyWidth; x++)
                {
                    occupiedCells[GetIndex(x, y)] = previous[(y * previousWidth) + x];
                }
            }
        }

        public void ClearLayout()
        {
            EnsureGridCapacity();
            for (int i = 0; i < occupiedCells.Length; i++)
            {
                occupiedCells[i] = false;
            }
        }

        public void GenerateBlocks()
        {
#if UNITY_EDITOR
            Transform parent = ResolveBlocksRoot();
            if (blockPrefab == null || parent == null)
            {
                return;
            }

            EnsureGridCapacity();

            Transform generatedRoot = EnsureGeneratedBlocksRoot(parent);
            ClearGeneratedBlocks(generatedRoot);

            Vector3 originOffset = Vector3.zero;
            if (centerGrid)
            {
                originOffset.x = -((width - 1) * cellSize.x) * 0.5f;
                originOffset.y = -((height - 1) * cellSize.y) * 0.5f;
            }

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (!IsCellOccupied(x, y))
                    {
                        continue;
                    }

                    GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(blockPrefab, generatedRoot);
                    if (instance == null)
                    {
                        continue;
                    }

                    instance.name = $"{blockPrefab.name}_{x}_{y}";
                    Transform instanceTransform = instance.transform;
                    instanceTransform.localPosition = new Vector3(
                        originOffset.x + (x * cellSize.x),
                        originOffset.y + (y * cellSize.y),
                        0f);
                    instanceTransform.localRotation = Quaternion.identity;

                    if (!instance.TryGetComponent(out KnockdownBlockAuthoring authoring))
                    {
                        authoring = instance.AddComponent<KnockdownBlockAuthoring>();
                    }

                    authoring.SetGridPosition(new Vector2Int(x, y));
                    EditorUtility.SetDirty(instance);
                }
            }

            EditorUtility.SetDirty(generatedRoot.gameObject);
            EditorUtility.SetDirty(gameObject);
#endif
        }

        private void Reset()
        {
            ResolveBlocksRoot();
            serializedWidth = width;
            serializedHeight = height;
            EnsureGridCapacity();
        }

        private void OnValidate()
        {
            width = Mathf.Max(1, width);
            height = Mathf.Max(1, height);
            cellSize.x = Mathf.Max(0.01f, cellSize.x);
            cellSize.y = Mathf.Max(0.01f, cellSize.y);
            ResolveBlocksRoot();
            if (serializedWidth != width || serializedHeight != height)
            {
                ResizeGrid(width, height);
            }

            serializedWidth = width;
            serializedHeight = height;
            EnsureGridCapacity();
        }

        private void EnsureGridCapacity()
        {
            int targetLength = Mathf.Max(1, width * height);
            if (occupiedCells != null && occupiedCells.Length == targetLength)
            {
                return;
            }

            occupiedCells = new bool[targetLength];
        }

        private Transform ResolveBlocksRoot()
        {
            if (blocksRoot != null)
            {
                return blocksRoot;
            }

            KnockdownTableLayout tableLayout = GetComponent<KnockdownTableLayout>();
            if (tableLayout == null)
            {
                tableLayout = GetComponentInParent<KnockdownTableLayout>();
            }

            if (tableLayout != null)
            {
                blocksRoot = tableLayout.BlocksRoot;
            }
            else
            {
                blocksRoot = transform;
            }

            return blocksRoot;
        }

        private Transform EnsureGeneratedBlocksRoot(Transform parent)
        {
            Transform existing = parent.Find(GeneratedBlocksRootName);
            if (existing != null)
            {
                return existing;
            }

            GameObject rootObject = new GameObject(GeneratedBlocksRootName);
            Transform rootTransform = rootObject.transform;
            rootTransform.SetParent(parent, false);
            rootTransform.localPosition = Vector3.zero;
            rootTransform.localRotation = Quaternion.identity;
            rootTransform.localScale = Vector3.one;
            return rootTransform;
        }

        private void ClearGeneratedBlocks(Transform generatedRoot)
        {
            for (int i = generatedRoot.childCount - 1; i >= 0; i--)
            {
                Transform child = generatedRoot.GetChild(i);
                if (Application.isPlaying)
                {
                    Destroy(child.gameObject);
                }
                else
                {
                    DestroyImmediate(child.gameObject);
                }
            }
        }

        private bool IsInside(int x, int y)
        {
            return x >= 0 && x < width && y >= 0 && y < height;
        }

        private int GetIndex(int x, int y)
        {
            return (y * width) + x;
        }
    }
}
