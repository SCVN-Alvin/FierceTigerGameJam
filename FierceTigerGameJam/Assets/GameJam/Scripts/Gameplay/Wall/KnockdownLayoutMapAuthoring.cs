using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

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

        [Tooltip("Fallback used when no map selection is assigned or nothing is selected yet, so "
                 + "the scene can still be opened and played on its own.")]
        [SerializeField] private TextAsset mapJson;

        [Tooltip("When set, the selected map wins over the fallback and choosing a map rebuilds.")]
        [SerializeField] private MapSelection mapSelection;
        [SerializeField] private BlockDatabase blockDatabase;
        [Tooltip("Everything generated is parented here. Left empty, the spinner's transform is "
                 + "used so the map sits inside whatever rotates it.")]
        [FormerlySerializedAs("blocksRoot")]
        [SerializeField] private Transform structureRoot;
        [SerializeField] private WallBlockPhysicsSetup physicsSetup;
        [SerializeField] private bool centerGrid = true;
        [SerializeField] private bool buildOnStart = true;

        [Tooltip("Creates a \"Structure Center\" marker at the middle of the built map and points "
                 + "the spinner at it. Left empty, the spinner is looked up from the parents.")]
        [SerializeField] private bool createStructureCenter = true;
        [SerializeField] private SpinOnAxis structureSpinner;

        /// <summary>
        /// A block that passed validation and reserved its cells. Placement is still collected
        /// before anything is spawned, so the structure centre can be measured from what was
        /// actually accepted rather than from what the map asked for.
        /// </summary>
        private struct PlacedBlock
        {
            public KnockdownMapBlock Source;
            public GameObject Prefab;
            public string Name;
            public Vector3 LocalPosition;
            public Quaternion LocalRotation;
            public Vector3Int GridPosition;
            public Vector3Int Footprint;
        }

        private const float RightAngleDegrees = 90f;
        private const float RotationEpsilon = 0.01f;

        /// <summary>
        /// How many blocks the last build placed. This is the denominator clear progress is
        /// measured against, and every placement is one object in the scene, so the two agree.
        /// </summary>
        public int PlacedBlockCount { get; private set; }

        public BlockDatabase BlockDatabase => blockDatabase;
        public TextAsset MapJson => ResolveMapJson();
        public Transform StructureRoot => ResolveStructureRoot();
        public SpinOnAxis StructureSpinner => ResolveSpinner();

        private void Start()
        {
            if (buildOnStart)
            {
                BuildMap();
            }
        }

        private void OnEnable()
        {
            if (mapSelection != null)
            {
                mapSelection.SelectionChanged += HandleSelectionChanged;
            }
        }

        private void OnDisable()
        {
            if (mapSelection != null)
            {
                mapSelection.SelectionChanged -= HandleSelectionChanged;
            }
        }

        private void HandleSelectionChanged(MapInfo map)
        {
            BuildMap();
        }

        /// <summary>
        /// The selection wins when it has a map, otherwise the serialized asset stands in so the
        /// scene still builds something when opened directly.
        /// </summary>
        private TextAsset ResolveMapJson()
        {
            MapInfo selected = mapSelection != null ? mapSelection.Selected : null;
            return selected != null && selected.MapJson != null ? selected.MapJson : mapJson;
        }

        [ContextMenu("Build Map")]
        public void BuildMap()
        {
            GameJam.Diagnostics.RuntimeProfileLogger.Count("build_map_calls");
            GameJam.Diagnostics.RuntimeProfileLogger.BeginPhase("build_map");
            try
            {
                BuildMapInternal();
            }
            finally
            {
                GameJam.Diagnostics.RuntimeProfileLogger.EndPhase();
            }
        }

        /// <summary>
        /// Loads a structure that Tools/Smashdown/Bake Map Prefabs built ahead of time. The
        /// prefab is the generated root as the JSON build left it, so what remains is exactly the
        /// run-time half of a normal build: physics on every block, and the structure centre.
        /// </summary>
        private void BuildFromPrefab(GameObject mapPrefab)
        {
            Transform parent = ResolveStructureRoot();
            if (parent == null)
            {
                Debug.LogError($"{nameof(KnockdownLayoutMapAuthoring)} could not resolve a blocks root.", this);
                return;
            }

            Transform generatedRoot = EnsureGeneratedChild(parent, GeneratedBlocksRootName);
            ClearGeneratedBlocks(generatedRoot);

            // The progress tracker counts the generated root's DIRECT children, so the baked
            // blocks are moved out of their wrapper and onto the root itself - the same shape a
            // JSON build leaves behind. The wrapper's registry moves with them onto the root.
            if (!generatedRoot.TryGetComponent(out StructureRegistry _))
            {
                generatedRoot.gameObject.AddComponent<StructureRegistry>();
            }

            GameObject instance = Instantiate(mapPrefab, generatedRoot);
            instance.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            instance.transform.localScale = Vector3.one;

            while (instance.transform.childCount > 0)
            {
                instance.transform.GetChild(0).SetParent(generatedRoot, false);
            }

            if (Application.isPlaying)
            {
                Destroy(instance);
            }
            else
            {
                DestroyImmediate(instance);
            }

            // The same count a JSON build reports: one per authored block.
            int placedCount = generatedRoot.GetComponentsInChildren<KnockdownBlockAuthoring>(true).Length;
            PlacedBlockCount = placedCount;
            GameJam.Diagnostics.RuntimeProfileLogger.Count("blocks_placed", placedCount);

            if (physicsSetup != null)
            {
                physicsSetup.PrepareBlocks(generatedRoot);
            }

            if (createStructureCenter)
            {
                SetupStructureCenter(parent, ResolvePrefabCenterLocalPosition(parent, generatedRoot.gameObject));
            }

            Debug.Log(
                $"Loaded baked map \"{mapPrefab.name}\" with {placedCount} block(s) under {generatedRoot.name}.",
                this);
        }

        /// <summary>
        /// The spinner pivot for a baked structure, read off its renderers since the block list
        /// that a JSON build measures never existed here.
        /// </summary>
        private static Vector3 ResolvePrefabCenterLocalPosition(Transform parent, GameObject instance)
        {
            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                return Vector3.zero;
            }

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            return parent.InverseTransformPoint(bounds.center);
        }

        private void BuildMapInternal()
        {
            // A map baked into a prefab skips parsing and per-block spawning entirely: the
            // structure arrives in one instantiation.
            MapInfo selectedMap = mapSelection != null ? mapSelection.Selected : null;
            if (selectedMap != null && selectedMap.MapPrefab != null)
            {
                BuildFromPrefab(selectedMap.MapPrefab);
                return;
            }

            if (!TryParseMap(out KnockdownMapDefinition map))
            {
                return;
            }

            if (blockDatabase == null)
            {
                Debug.LogError($"{nameof(KnockdownLayoutMapAuthoring)} needs a block database.", this);
                return;
            }

            Transform parent = ResolveStructureRoot();
            if (parent == null)
            {
                Debug.LogError($"{nameof(KnockdownLayoutMapAuthoring)} could not resolve a blocks root.", this);
                return;
            }

            Transform generatedRoot = EnsureGeneratedChild(parent, GeneratedBlocksRootName);
            ClearGeneratedBlocks(generatedRoot);

            // Sits on the root the blocks are parented to, so each one finds it with a single
            // walk up the hierarchy and never has to search its siblings again.
            if (!generatedRoot.TryGetComponent(out StructureRegistry _))
            {
                generatedRoot.gameObject.AddComponent<StructureRegistry>();
            }

            Vector3 origin = ResolveGridOrigin(map);
            HashSet<Vector3Int> occupiedCells = new HashSet<Vector3Int>();
            List<PlacedBlock> placed = new List<PlacedBlock>();

            // Placement first, spawning second, so the structure centre is measured from the
            // blocks that were actually accepted rather than from every block the map listed.
            for (int layerIndex = 0; layerIndex < map.layers.Length; layerIndex++)
            {
                KnockdownMapLayer layer = map.layers[layerIndex];
                if (layer?.blocks == null)
                {
                    continue;
                }

                for (int blockIndex = 0; blockIndex < layer.blocks.Length; blockIndex++)
                {
                    if (TryPlaceBlock(map.grid, layer, layer.blocks[blockIndex], origin, occupiedCells, out PlacedBlock placement))
                    {
                        placed.Add(placement);
                    }
                }
            }

            int spawned = placed.Count;
            PlacedBlockCount = spawned;
            GameJam.Diagnostics.RuntimeProfileLogger.Count("blocks_placed", spawned);

            for (int i = 0; i < placed.Count; i++)
            {
                SpawnPlacedBlock(placed[i], generatedRoot);
            }

            if (physicsSetup != null)
            {
                physicsSetup.PrepareBlocks(generatedRoot);
            }

            if (createStructureCenter)
            {
                SetupStructureCenter(parent, ResolveStructureCenterLocalPosition(placed, map));
            }

            Debug.Log(
                $"Built map \"{map.id}\" with {spawned} block(s) under {generatedRoot.name}.",
                this);
        }

        [ContextMenu("Clear Map")]
        public void ClearMap()
        {
            Transform parent = ResolveStructureRoot();
            if (parent == null)
            {
                return;
            }

            Transform generatedRoot = parent.Find(GeneratedBlocksRootName);
            if (generatedRoot != null)
            {
                ClearGeneratedBlocks(generatedRoot);
            }

            PlacedBlockCount = 0;
        }

        /// <summary>
        /// Places a "Structure Center" marker at the middle of what was actually built and hands
        /// it to the spinner, which rotates around a vertical axis through that point.
        /// </summary>
        private void SetupStructureCenter(Transform parent, Vector3 centerLocalPosition)
        {
            Transform center = EnsureGeneratedChild(parent, StructureLayout.CenterObjectName);
            center.SetLocalPositionAndRotation(centerLocalPosition, Quaternion.identity);
            center.localScale = Vector3.one;

            SpinOnAxis spinner = ResolveSpinner();
            if (spinner != null)
            {
                spinner.SetRotationCenter(center);
                return;
            }

            Debug.LogWarning(
                $"{nameof(KnockdownLayoutMapAuthoring)} built a structure center but found no "
                + $"{nameof(SpinOnAxis)} to drive, so the map will not rotate.",
                this);
        }

        /// <summary>
        /// Measured from the blocks that actually landed, so a map that only fills part of its
        /// grid still spins about its own middle rather than the grid's.
        /// </summary>
        private Vector3 ResolveStructureCenterLocalPosition(
            List<PlacedBlock> placed,
            KnockdownMapDefinition map)
        {
            // Measured from the placements rather than from a walk of every Renderer under the
            // generated root. The blocks are already in hand and their sizes come off the
            // prefabs, so this costs a loop over a list instead of a hierarchy search plus a
            // world-space bounds encapsulation per renderer. The generated root sits at its
            // parent's origin, so a placement's local position is already in parent space.
            if (placed.Count > 0)
            {
                Vector3 half = ResolveBlockSize(placed[0].Prefab) * 0.5f;
                Vector3 min = placed[0].LocalPosition - half;
                Vector3 max = placed[0].LocalPosition + half;

                for (int i = 1; i < placed.Count; i++)
                {
                    half = ResolveBlockSize(placed[i].Prefab) * 0.5f;
                    min = Vector3.Min(min, placed[i].LocalPosition - half);
                    max = Vector3.Max(max, placed[i].LocalPosition + half);
                }

                return (min + max) * 0.5f;
            }

            // Nothing was placed, so fall back to the middle of the declared grid.
            Vector3 origin = ResolveGridOrigin(map);
            return new Vector3(
                origin.x + ((map.grid.width - 1) * 0.5f * map.grid.cellSize),
                map.grid.height * 0.5f * map.grid.cellSize,
                origin.z + (map.MaxLevel() * 0.5f * map.grid.layerDepth));
        }

        private SpinOnAxis ResolveSpinner()
        {
            if (structureSpinner != null)
            {
                return structureSpinner;
            }

            // The spinner normally sits on a child root, so look down before looking up.
            structureSpinner = GetComponentInChildren<SpinOnAxis>(true);
            if (structureSpinner == null)
            {
                structureSpinner = GetComponentInParent<SpinOnAxis>();
            }

            return structureSpinner;
        }

        public bool TryParseMap(out KnockdownMapDefinition map)
        {
            map = null;
            TextAsset source = ResolveMapJson();
            if (source == null)
            {
                Debug.LogError($"{nameof(KnockdownLayoutMapAuthoring)} needs a map JSON asset.", this);
                return false;
            }

            if (KnockdownMapDefinition.TryParse(source.text, out map, out string error))
            {
                return true;
            }

            Debug.LogError($"{source.name}: {error}", this);
            return false;
        }

        private bool TryPlaceBlock(
            KnockdownMapGrid grid,
            KnockdownMapLayer layer,
            KnockdownMapBlock block,
            Vector3 origin,
            HashSet<Vector3Int> occupiedCells,
            out PlacedBlock placement)
        {
            placement = default;

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

            placement = new PlacedBlock
            {
                Source = block,
                Prefab = prefab,
                Name = $"{block.type}_{blockId}",
                LocalPosition = ResolveLocalPosition(grid, layer.level, block.position, footprint, origin),
                LocalRotation = ResolveVisualRotation(block.rotation),
                // The cascade reads (column, height, depth), which is exactly the cell address.
                GridPosition = new Vector3Int(block.position.x, block.position.y, layer.level),
                Footprint = footprint,
            };

            return true;
        }

        private void SpawnPlacedBlock(PlacedBlock placement, Transform generatedRoot)
        {
            GameObject instance = InstantiateBlock(placement.Prefab, generatedRoot);
            if (instance == null)
            {
                return;
            }

            instance.name = placement.Name;
            instance.transform.SetLocalPositionAndRotation(placement.LocalPosition, placement.LocalRotation);
            instance.transform.localScale = Vector3.one;

            if (instance.TryGetComponent(out KnockdownBlockAuthoring authoring))
            {
                authoring.SetGridPosition(placement.GridPosition);
                authoring.SetLogicalSize(placement.Footprint);
            }
            else
            {
                Debug.LogWarning(
                    $"Prefab for type \"{placement.Source.type}\" has no {nameof(KnockdownBlockAuthoring)}, "
                    + $"so block {placement.Name} will not cascade.",
                    this);
            }
        }

        private static Vector3 ResolveBlockSize(GameObject prefab)
        {
            if (prefab != null && prefab.TryGetComponent(out BoxCollider blockCollider))
            {
                return blockCollider.size;
            }

            return Vector3.one * 0.25f;
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

        /// <summary>
        /// Prefers the spinner's transform when nothing is assigned: SpinOnAxis rotates itself,
        /// so anything generated outside it would sit still while the rest of the map turns.
        /// </summary>
        private Transform ResolveStructureRoot()
        {
            if (structureRoot != null)
            {
                return structureRoot;
            }

            KnockdownTableLayout tableLayout = GetComponent<KnockdownTableLayout>();
            if (tableLayout == null)
            {
                tableLayout = GetComponentInParent<KnockdownTableLayout>();
            }

            if (tableLayout != null)
            {
                structureRoot = tableLayout.BlocksRoot;
                return structureRoot;
            }

            SpinOnAxis spinner = ResolveSpinner();
            structureRoot = spinner != null ? spinner.transform : transform;
            return structureRoot;
        }

        /// <summary>
        /// Returns the named child of the root, adopting one that an earlier build left elsewhere
        /// under this component. Without that, changing the root would strand the previous
        /// GeneratedLayoutBlocks and Structure Center outside whatever now rotates.
        /// </summary>
        private Transform EnsureGeneratedChild(Transform parent, string childName)
        {
            Transform existing = parent.Find(childName) ?? FindStrayChild(parent, childName);
            if (existing == null)
            {
                existing = new GameObject(childName).transform;
            }

            existing.SetParent(parent, false);
            existing.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            existing.localScale = Vector3.one;
            return existing;
        }

        private Transform FindStrayChild(Transform parent, string childName)
        {
            for (int i = 0; i < transform.childCount; i++)
            {
                Transform child = transform.GetChild(i);
                if (child != parent && child.name == childName)
                {
                    return child;
                }
            }

            return null;
        }

        private static void ClearGeneratedBlocks(Transform generatedRoot)
        {
            // Debris from the last attempt is parented here so it spins with the structure, but
            // it belongs to a pool. Handing it back first is what stops the clear from destroying
            // instances the pool still counts as its own.
            ShatteredBlockPool.ReturnAll();

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
