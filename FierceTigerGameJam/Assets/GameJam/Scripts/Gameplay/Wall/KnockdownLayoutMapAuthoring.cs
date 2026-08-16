using System;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace GameJam.Gameplay.Wall
{
    /// <summary>
    /// Builds a knockdown structure from a map JSON file. Each layer is a vertical XY slice:
    /// position.x runs along the grid width and position.y upward from the floor, while the
    /// layer's level is its Z index, one layerDepth further back per step.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class KnockdownLayoutMapAuthoring : MonoBehaviour
    {
        public const string GeneratedBlocksRootName = "GeneratedLayoutBlocks";

        [SerializeField] private TextAsset mapJson;
        [SerializeField] private BlockDatabase blockDatabase;
        [SerializeField] private Transform blocksRoot;
        [SerializeField] private WallBlockPhysicsSetup physicsSetup;
        [SerializeField] private bool centerGrid = true;
        [SerializeField] private bool buildOnStart = true;

        private const float RightAngleDegrees = 90f;
        private const float RotationEpsilon = 0.01f;

        public BlockDatabase BlockDatabase => blockDatabase;
        public TextAsset MapJson => mapJson;
        public Transform BlocksRoot => ResolveBlocksRoot();

        private void Start()
        {
            if (buildOnStart)
            {
                BuildMap();
            }
        }

        [ContextMenu("Build Map")]
        public void BuildMap()
        {
            if (!TryParseMap(out KnockdownMapDefinition map))
            {
                return;
            }

            if (blockDatabase == null)
            {
                Debug.LogError($"{nameof(KnockdownLayoutMapAuthoring)} needs a block database.", this);
                return;
            }

            Transform parent = ResolveBlocksRoot();
            if (parent == null)
            {
                Debug.LogError($"{nameof(KnockdownLayoutMapAuthoring)} could not resolve a blocks root.", this);
                return;
            }

            Transform generatedRoot = EnsureGeneratedBlocksRoot(parent);
            ClearGeneratedBlocks(generatedRoot);

            Vector3 origin = ResolveGridOrigin(map);
            HashSet<Vector3Int> occupiedCells = new HashSet<Vector3Int>();
            int spawned = 0;

            for (int layerIndex = 0; layerIndex < map.layers.Length; layerIndex++)
            {
                KnockdownMapLayer layer = map.layers[layerIndex];
                if (layer?.blocks == null)
                {
                    continue;
                }

                for (int blockIndex = 0; blockIndex < layer.blocks.Length; blockIndex++)
                {
                    if (TrySpawnBlock(map.grid, layer, layer.blocks[blockIndex], generatedRoot, origin, occupiedCells))
                    {
                        spawned++;
                    }
                }
            }

            if (physicsSetup != null)
            {
                physicsSetup.PrepareBlocks(generatedRoot);
            }

            Debug.Log($"Built map \"{map.id}\" with {spawned} block(s) under {generatedRoot.name}.", this);
        }

        [ContextMenu("Clear Map")]
        public void ClearMap()
        {
            Transform parent = ResolveBlocksRoot();
            if (parent == null)
            {
                return;
            }

            Transform generatedRoot = parent.Find(GeneratedBlocksRootName);
            if (generatedRoot != null)
            {
                ClearGeneratedBlocks(generatedRoot);
            }
        }

        public bool TryParseMap(out KnockdownMapDefinition map)
        {
            map = null;
            if (mapJson == null)
            {
                Debug.LogError($"{nameof(KnockdownLayoutMapAuthoring)} needs a map JSON asset.", this);
                return false;
            }

            if (KnockdownMapDefinition.TryParse(mapJson.text, out map, out string error))
            {
                return true;
            }

            Debug.LogError($"{mapJson.name}: {error}", this);
            return false;
        }

        private bool TrySpawnBlock(
            KnockdownMapGrid grid,
            KnockdownMapLayer layer,
            KnockdownMapBlock block,
            Transform generatedRoot,
            Vector3 origin,
            HashSet<Vector3Int> occupiedCells)
        {
            if (block?.position == null)
            {
                Debug.LogWarning($"A block on level {layer.level} has no position and was skipped.", this);
                return false;
            }

            string blockId = string.IsNullOrEmpty(block.id) ? "<no id>" : block.id;
            if (!blockDatabase.TryGetPrefab(block.type, out GameObject prefab))
            {
                Debug.LogError($"Block {blockId} uses type \"{block.type}\", which the block database does not map to a prefab.", this);
                return false;
            }

            if (!IsQuarterTurnMultiple(block.rotation))
            {
                Debug.LogError(
                    $"Block {blockId} has rotation {block.rotation}, which is not a multiple of 90. "
                    + "Rotation selects a footprint orientation, so only 0, 90, 180 and 270 are meaningful.",
                    this);
                return false;
            }

            Vector3Int footprint = ResolveFootprint(prefab, block.rotation);
            if (!TryReserveCells(grid, layer.level, block, blockId, footprint, occupiedCells))
            {
                return false;
            }

            GameObject instance = InstantiateBlock(prefab, generatedRoot);
            if (instance == null)
            {
                return false;
            }

            instance.name = $"{block.type}_{blockId}";
            instance.transform.SetLocalPositionAndRotation(
                ResolveLocalPosition(grid, layer.level, block.position, footprint, origin),
                ResolveVisualRotation(block.rotation));
            instance.transform.localScale = Vector3.one;

            if (instance.TryGetComponent(out KnockdownBlockAuthoring authoring))
            {
                // The cascade reads (column, height, depth), which is exactly the cell address.
                authoring.SetGridPosition(new Vector3Int(block.position.x, block.position.y, layer.level));
                authoring.SetLogicalSize(footprint);
            }
            else
            {
                Debug.LogWarning($"Prefab for type \"{block.type}\" has no {nameof(KnockdownBlockAuthoring)}, so block {blockId} will not cascade.", this);
            }

            return true;
        }

        /// <summary>
        /// Cells the block covers, in grid space. The block turns within its own XY slice, so
        /// a 90 or 270 degree turn swaps width and height and leaves depth alone.
        /// </summary>
        public static Vector3Int ResolveFootprint(GameObject prefab, float rotation)
        {
            Vector3Int size = Vector3Int.one;
            if (prefab.TryGetComponent(out KnockdownBlockAuthoring authoring))
            {
                size = authoring.LogicalSize;
            }

            size.x = Mathf.Max(1, size.x);
            size.y = Mathf.Max(1, size.y);
            size.z = Mathf.Max(1, size.z);

            return IsQuarterTurned(rotation) ? new Vector3Int(size.y, size.x, size.z) : size;
        }

        private static bool IsQuarterTurned(float rotation)
        {
            float wrapped = Mathf.Repeat(rotation, 2f * RightAngleDegrees);
            return Mathf.Abs(wrapped - RightAngleDegrees) < RotationEpsilon;
        }

        private static bool IsQuarterTurnMultiple(float rotation)
        {
            return Mathf.Abs(Mathf.Repeat(rotation, RightAngleDegrees)) < RotationEpsilon;
        }

        /// <summary>
        /// Turns the mesh within its layer so it covers the same cells the footprint claims.
        /// Blocks that occupy a single cell look the same at every rotation.
        /// </summary>
        private static Quaternion ResolveVisualRotation(float rotation)
        {
            return Quaternion.Euler(0f, 0f, rotation);
        }

        private bool TryReserveCells(
            KnockdownMapGrid grid,
            int level,
            KnockdownMapBlock block,
            string blockId,
            Vector3Int footprint,
            HashSet<Vector3Int> occupiedCells)
        {
            // Every cell is checked before any is claimed, so a block that gets rejected leaves
            // no partial reservation behind to block whatever comes next.
            List<Vector3Int> wanted = new List<Vector3Int>(footprint.x * footprint.y * footprint.z);

            for (int offsetX = 0; offsetX < footprint.x; offsetX++)
            {
                for (int offsetY = 0; offsetY < footprint.y; offsetY++)
                {
                    int cellX = block.position.x + offsetX;
                    int cellY = block.position.y + offsetY;

                    if (cellX < 0 || cellX >= grid.width || cellY < 0 || cellY >= grid.height)
                    {
                        Debug.LogError(
                            $"Block {blockId} covers cell ({cellX}, {cellY}) on level {level}, "
                            + $"which is outside the {grid.width}x{grid.height} grid. Skipped.",
                            this);
                        return false;
                    }

                    // A block deeper than one cell also claims the layers behind it.
                    for (int offsetZ = 0; offsetZ < footprint.z; offsetZ++)
                    {
                        Vector3Int cell = new Vector3Int(cellX, cellY, level + offsetZ);
                        if (occupiedCells.Contains(cell))
                        {
                            Debug.LogWarning(
                                $"Block {blockId} wants cell ({cellX}, {cellY}) on level {cell.z}, "
                                + "which is already taken. Skipped.",
                                this);
                            return false;
                        }

                        wanted.Add(cell);
                    }
                }
            }

            for (int i = 0; i < wanted.Count; i++)
            {
                occupiedCells.Add(wanted[i]);
            }

            return true;
        }

        /// <summary>
        /// Centres the slice across X and the layer stack across Z. Y is left alone so the
        /// structure always stands on the floor rather than straddling it.
        /// </summary>
        private Vector3 ResolveGridOrigin(KnockdownMapDefinition map)
        {
            if (!centerGrid)
            {
                return Vector3.zero;
            }

            return new Vector3(
                -((map.grid.width - 1) * map.grid.cellSize) * 0.5f,
                0f,
                -(map.MaxLevel() * map.grid.layerDepth) * 0.5f);
        }

        /// <summary>
        /// Block pivots are centred, so a block is offset by half its own footprint. Row 0 of
        /// every layer sits with its base on the floor.
        /// </summary>
        private static Vector3 ResolveLocalPosition(
            KnockdownMapGrid grid,
            int level,
            KnockdownMapCell position,
            Vector3Int footprint,
            Vector3 origin)
        {
            return new Vector3(
                origin.x + (position.x + ((footprint.x - 1) * 0.5f)) * grid.cellSize,
                (position.y + (footprint.y * 0.5f)) * grid.cellSize,
                origin.z + (level + ((footprint.z - 1) * 0.5f)) * grid.layerDepth);
        }

        private static GameObject InstantiateBlock(GameObject prefab, Transform parent)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                return (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            }
#endif
            return Instantiate(prefab, parent);
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

            blocksRoot = tableLayout != null ? tableLayout.BlocksRoot : transform;
            return blocksRoot;
        }

        private static Transform EnsureGeneratedBlocksRoot(Transform parent)
        {
            Transform existing = parent.Find(GeneratedBlocksRootName);
            if (existing != null)
            {
                return existing;
            }

            GameObject rootObject = new GameObject(GeneratedBlocksRootName);
            Transform rootTransform = rootObject.transform;
            rootTransform.SetParent(parent, false);
            rootTransform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            rootTransform.localScale = Vector3.one;
            return rootTransform;
        }

        private static void ClearGeneratedBlocks(Transform generatedRoot)
        {
            for (int i = generatedRoot.childCount - 1; i >= 0; i--)
            {
                GameObject child = generatedRoot.GetChild(i).gameObject;
                if (Application.isPlaying)
                {
                    Destroy(child);
                }
                else
                {
                    DestroyImmediate(child);
                }
            }
        }
    }
}
