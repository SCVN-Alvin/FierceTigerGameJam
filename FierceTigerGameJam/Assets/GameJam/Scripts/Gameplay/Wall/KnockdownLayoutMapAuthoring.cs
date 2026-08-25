using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace GameJam.Gameplay.Wall
{
    /// <summary>
    /// How much of the map's block list is allowed to become a wall. Merging is a decision the
    /// map makes, so the default only honours walls the map named; the automatic detection that
    /// came before it is kept for maps written when there was nothing else.
    /// </summary>
    public enum WallGroupingMode
    {
        /// <summary>Never merge. Every block is its own body.</summary>
        None,

        /// <summary>
        /// Merge only blocks that name a wall in the JSON ("wall": { "wall_id": ... }).
        /// Blocks without a wall id are single blocks. Default.
        /// </summary>
        NamedOnly,

        /// <summary>
        /// NamedOnly, plus the legacy fallback: same-type single-cell blocks in one layer are
        /// grouped into rectangles automatically when no wall id was given.
        /// </summary>
        NamedAndDetected,
    }

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

        [Header("Wall Grouping")]
        [Tooltip("NamedOnly: a block is merged into a wall only when the map gives it a wall_id. "
                 + "NamedAndDetected adds the old automatic rectangle grouping for blocks without one.")]
        [SerializeField] private WallGroupingMode wallGrouping = WallGroupingMode.NamedOnly;

        [Tooltip("Smallest automatically detected run worth merging. Only used by NamedAndDetected. "
                 + "Below this a wall costs more than the blocks it replaces, and single blocks "
                 + "still need to behave like single blocks.")]
        [SerializeField] private int minimumWallCells = 3;

        // Legacy field kept for migration. Read once in OnValidate and never written again.
        [SerializeField, HideInInspector] private bool groupBlocksIntoWalls = true;
        [SerializeField, HideInInspector] private bool wallGroupingMigrated;

        [Tooltip("Draw walls with the wall art from the block database instead of welding the "
                 + "block meshes. A panel is a couple of hundred vertices whatever the run's "
                 + "length; welding costs the same vertices as the blocks it replaced.")]
        [SerializeField] private bool useWallPanels = true;

        [Tooltip("How many times the wall texture repeats across one cell. This is the dial for "
                 + "how big the bricks look: lower means larger bricks.")]
        [SerializeField] private float wallTextureTilesPerCell = 1f;

        /// <summary>
        /// A block that passed validation and reserved its cells, held until every block in the
        /// map has been placed so runs of the same type can be found before anything is built.
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
            public int Level;

            /// <summary>The wall the map assigned this block to, or null.</summary>
            public string WallId;

            public bool IsSingleCell => Footprint.x == 1 && Footprint.y == 1 && Footprint.z == 1;
        }

        /// <summary>
        /// A set of blocks that will be built as one wall, however they were grouped.
        /// </summary>
        private sealed class WallBuild
        {
            public string Name;

            /// <summary>The shared block type, or null when the wall mixes materials.</summary>
            public string Type;

            public readonly List<PlacedBlock> Blocks = new List<PlacedBlock>();

            /// <summary>
            /// True only for a solid, single-material rectangle inside one layer. A wall panel
            /// can be stretched over that and nothing else, because a panel has no holes.
            /// </summary>
            public bool IsRectangle;

            public Vector3Int GridPosition;
            public Vector3Int LogicalSize;
        }

        private const float RightAngleDegrees = 90f;
        private const float RotationEpsilon = 0.01f;

        /// <summary>
        /// How many blocks the last build placed, counting the blocks a wall stands for rather
        /// than the wall. This is the denominator clear progress is measured against, so it has
        /// to be the count the player sees, not the count of objects in the scene.
        /// </summary>
        public int PlacedBlockCount { get; private set; }

        public BlockDatabase BlockDatabase => blockDatabase;
        public WallGroupingMode WallGrouping => wallGrouping;
        public int MinimumWallCells => minimumWallCells;
        public TextAsset MapJson => ResolveMapJson();
        public Transform StructureRoot => ResolveStructureRoot();
        public SpinOnAxis StructureSpinner => ResolveSpinner();

        private void OnValidate()
        {
            if (!wallGroupingMigrated)
            {
                // Scenes saved before the enum existed: a disabled bool meant "never group".
                // An enabled bool is mapped to NamedOnly, which is the new intended default.
                wallGrouping = groupBlocksIntoWalls ? WallGroupingMode.NamedOnly : WallGroupingMode.None;
                wallGroupingMigrated = true;
            }

            minimumWallCells = Mathf.Max(2, minimumWallCells);
        }

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

            Vector3 origin = ResolveGridOrigin(map);
            HashSet<Vector3Int> occupiedCells = new HashSet<Vector3Int>();
            List<PlacedBlock> placed = new List<PlacedBlock>();

            // Placement first, building second. Validation and cell reservation are unchanged and
            // still run per block; grouping then only ever sees blocks that were actually
            // accepted, so a rejected block can never end up inside a wall.
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
            int walls = BuildPlacedBlocks(placed, generatedRoot, out int panelWalls);

            if (physicsSetup != null)
            {
                physicsSetup.PrepareBlocks(generatedRoot);
            }

            // Only now do the walls have a KnockdownBlock to listen to: the physics setup adds it
            // after everything is built, so the subscription cannot happen while the wall is made.
            SubscribeWalls(generatedRoot);

            if (createStructureCenter)
            {
                SetupStructureCenter(parent, generatedRoot, map);
            }

            // The panel/welded split is the number that says whether merging actually bought
            // anything: a panel is a couple of hundred vertices however long the wall is, while a
            // welded wall still costs every vertex of the blocks it replaced.
            Debug.Log(
                $"Built map \"{map.id}\" with {spawned} block(s) under {generatedRoot.name} "
                + $"in {wallGrouping} mode"
                + (walls > 0
                    ? $", grouped into {walls} wall(s): {panelWalls} panel, {walls - panelWalls} welded."
                    : "."),
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
        private void SetupStructureCenter(Transform parent, Transform generatedRoot, KnockdownMapDefinition map)
        {
            Transform center = EnsureGeneratedChild(parent, StructureLayout.CenterObjectName);
            center.SetLocalPositionAndRotation(
                ResolveStructureCenterLocalPosition(parent, generatedRoot, map),
                Quaternion.identity);
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
            Transform parent,
            Transform generatedRoot,
            KnockdownMapDefinition map)
        {
            Renderer[] renderers = generatedRoot.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length > 0)
            {
                Bounds bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++)
                {
                    bounds.Encapsulate(renderers[i].bounds);
                }

                return parent.InverseTransformPoint(bounds.center);
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
                Level = layer.level,
                WallId = block.WallId,
            };

            return true;
        }

        /// <summary>
        /// Turns accepted placements into objects. Which blocks may be merged is the mode's
        /// decision: blocks that name a wall are built as that wall whatever their type or layer,
        /// and only NamedAndDetected still looks for runs of the same type to merge on its own.
        /// </summary>
        /// <param name="panelWalls">How many of the walls were drawn with a stretched panel.</param>
        /// <returns>How many walls were built.</returns>
        private int BuildPlacedBlocks(List<PlacedBlock> placed, Transform generatedRoot, out int panelWalls)
        {
            panelWalls = 0;

            if (wallGrouping == WallGroupingMode.None)
            {
                for (int i = 0; i < placed.Count; i++)
                {
                    SpawnPlacedBlock(placed[i], generatedRoot);
                }

                return 0;
            }

            List<WallBuild> walls = new List<WallBuild>();
            List<PlacedBlock> loose = new List<PlacedBlock>();
            List<PlacedBlock> unassigned = new List<PlacedBlock>();

            CollectNamedWalls(placed, walls, loose, unassigned);

            if (wallGrouping == WallGroupingMode.NamedAndDetected)
            {
                CollectDetectedWalls(unassigned, walls, loose);
            }
            else
            {
                // NamedOnly: a block the map did not put in a wall is a block.
                loose.AddRange(unassigned);
            }

            for (int i = 0; i < walls.Count; i++)
            {
                if (!TryBuildWall(walls[i], generatedRoot, out bool usedPanel))
                {
                    loose.AddRange(walls[i].Blocks);
                    walls.RemoveAt(i--);
                    continue;
                }

                if (usedPanel)
                {
                    panelWalls++;
                }
            }

            for (int i = 0; i < loose.Count; i++)
            {
                SpawnPlacedBlock(loose[i], generatedRoot);
            }

            return walls.Count;
        }

        /// <summary>
        /// Walls the map named explicitly. An author who put a wall id on a block meant it, so
        /// these are taken whatever their types, layers or shape. Only two things are refused: a
        /// wall holding a single block, which is just a block, and a wall whose blocks do not
        /// touch, which is split into one wall per cluster.
        /// </summary>
        private void CollectNamedWalls(
            List<PlacedBlock> placed,
            List<WallBuild> walls,
            List<PlacedBlock> loose,
            List<PlacedBlock> unassigned)
        {
            Dictionary<string, List<PlacedBlock>> named = new Dictionary<string, List<PlacedBlock>>(StringComparer.Ordinal);
            List<string> order = new List<string>();

            for (int i = 0; i < placed.Count; i++)
            {
                string wallId = placed[i].WallId;
                if (string.IsNullOrEmpty(wallId))
                {
                    unassigned.Add(placed[i]);
                    continue;
                }

                if (!named.TryGetValue(wallId, out List<PlacedBlock> members))
                {
                    members = new List<PlacedBlock>();
                    named[wallId] = members;
                    order.Add(wallId);
                }

                members.Add(placed[i]);
            }

            for (int i = 0; i < order.Count; i++)
            {
                List<PlacedBlock> members = named[order[i]];
                if (members.Count < 2)
                {
                    Debug.LogWarning(
                        $"Wall \"{order[i]}\" has only {members.Count} block(s), so it is built as a plain block.",
                        this);
                    loose.AddRange(members);
                    continue;
                }

                AddNamedWall(order[i], members, walls, loose);
            }
        }

        /// <summary>
        /// One wall per set of blocks that actually touch. A named wall whose blocks sit in two
        /// separate clusters would get a single box collider stretched across the gap between
        /// them, which stops shots in empty air, so it is split into a wall per cluster and the
        /// map is told about it.
        /// </summary>
        private void AddNamedWall(
            string wallId,
            List<PlacedBlock> members,
            List<WallBuild> walls,
            List<PlacedBlock> loose)
        {
            List<WallGrouping.CellBox> boxes = new List<WallGrouping.CellBox>(members.Count);
            for (int i = 0; i < members.Count; i++)
            {
                boxes.Add(new WallGrouping.CellBox(members[i].GridPosition, members[i].Footprint));
            }

            // Qualified because this class exposes a WallGrouping property, which would otherwise
            // hide the helper of the same name.
            List<List<int>> parts = GameJam.Gameplay.Wall.WallGrouping.FindConnectedGroups(boxes);
            if (parts.Count <= 1)
            {
                walls.Add(CreateWallBuild($"Wall_{wallId}", members));
                return;
            }

            Debug.LogWarning(
                $"Wall \"{wallId}\" is {parts.Count} groups of blocks that do not touch each other, "
                + "so it is built as one wall per group rather than one body spanning the gaps.",
                this);

            for (int i = 0; i < parts.Count; i++)
            {
                List<PlacedBlock> part = new List<PlacedBlock>(parts[i].Count);
                for (int m = 0; m < parts[i].Count; m++)
                {
                    part.Add(members[parts[i][m]]);
                }

                // A cluster of one is a block, the same as a named wall of one.
                if (part.Count < 2)
                {
                    loose.AddRange(part);
                    continue;
                }

                walls.Add(CreateWallBuild($"Wall_{wallId}_part{i + 1}", part));
            }
        }

        /// <summary>
        /// The automatic fallback for blocks with no wall id: runs of the same type inside one
        /// layer, which is all that can be inferred safely without the map saying so.
        /// </summary>
        private void CollectDetectedWalls(List<PlacedBlock> unassigned, List<WallBuild> walls, List<PlacedBlock> loose)
        {
            // Only single-cell blocks group. A 2x1 already covers more than one cell, and folding
            // one into a run would lose the footprint the cascade reads.
            Dictionary<(int level, string type), Dictionary<Vector2Int, PlacedBlock>> groups =
                new Dictionary<(int, string), Dictionary<Vector2Int, PlacedBlock>>();

            for (int i = 0; i < unassigned.Count; i++)
            {
                PlacedBlock candidate = unassigned[i];
                if (!candidate.IsSingleCell)
                {
                    loose.Add(candidate);
                    continue;
                }

                var key = (candidate.Level, candidate.Source.type);
                if (!groups.TryGetValue(key, out Dictionary<Vector2Int, PlacedBlock> cells))
                {
                    cells = new Dictionary<Vector2Int, PlacedBlock>();
                    groups[key] = cells;
                }

                cells[new Vector2Int(candidate.GridPosition.x, candidate.GridPosition.y)] = candidate;
            }

            foreach (KeyValuePair<(int level, string type), Dictionary<Vector2Int, PlacedBlock>> group in groups)
            {
                Dictionary<Vector2Int, string> typeByCell = new Dictionary<Vector2Int, string>(group.Value.Count);
                foreach (KeyValuePair<Vector2Int, PlacedBlock> cell in group.Value)
                {
                    typeByCell[cell.Key] = group.Key.type;
                }

                List<WallGrouping.WallRect> rects = GameJam.Gameplay.Wall.WallGrouping.Find(typeByCell);
                for (int i = 0; i < rects.Count; i++)
                {
                    WallGrouping.WallRect rect = rects[i];
                    List<PlacedBlock> members = new List<PlacedBlock>(rect.Cells.Count);
                    for (int c = 0; c < rect.Cells.Count; c++)
                    {
                        members.Add(group.Value[rect.Cells[c]]);
                    }

                    if (rect.Area < minimumWallCells)
                    {
                        loose.AddRange(members);
                        continue;
                    }

                    WallBuild build = CreateWallBuild(
                        $"Wall_{rect.Type}_{rect.Width}x{rect.Height}_L{group.Key.level}",
                        members);
                    walls.Add(build);
                }
            }
        }

        /// <summary>
        /// Works out what a set of blocks amounts to: the cells it spans, whether it is one
        /// material, and whether it is a solid rectangle in a single layer. Only that last shape
        /// can have a wall panel stretched over it; anything else is welded from its blocks, so a
        /// wall with a hole in it keeps the hole.
        /// </summary>
        private static WallBuild CreateWallBuild(string name, List<PlacedBlock> members)
        {
            WallBuild build = new WallBuild { Name = name };
            build.Blocks.AddRange(members);

            Vector3Int min = members[0].GridPosition;
            Vector3Int max = min + members[0].Footprint - Vector3Int.one;
            string type = members[0].Source.type;
            bool singleType = true;
            bool singleCells = members[0].IsSingleCell;

            for (int i = 1; i < members.Count; i++)
            {
                Vector3Int position = members[i].GridPosition;
                min = Vector3Int.Min(min, position);
                max = Vector3Int.Max(max, position + members[i].Footprint - Vector3Int.one);

                singleType &= string.Equals(members[i].Source.type, type, StringComparison.Ordinal);
                singleCells &= members[i].IsSingleCell;
            }

            Vector3Int size = max - min + Vector3Int.one;
            build.GridPosition = min;
            build.LogicalSize = size;
            build.Type = singleType ? type : null;
            build.IsRectangle = singleType && singleCells && size.z == 1 && members.Count == size.x * size.y;
            return build;
        }

        /// <summary>
        /// Hands every wall the body it should watch. A wall comes apart when it is knocked, and
        /// what does the knocking is the KnockdownBlock the physics setup just added to it.
        /// </summary>
        private static void SubscribeWalls(Transform generatedRoot)
        {
            BreakableWall[] walls = generatedRoot.GetComponentsInChildren<BreakableWall>(true);
            for (int i = 0; i < walls.Length; i++)
            {
                walls[i].Listen(walls[i].GetComponent<KnockdownBlock>());
            }
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

        /// <summary>
        /// Builds one wall: its blocks drawn as a single mesh, with a single collider and a
        /// single body, plus the manifest it needs to put those blocks back when it is knocked.
        /// </summary>
        private bool TryBuildWall(WallBuild build, Transform generatedRoot, out bool usedPanel)
        {
            usedPanel = false;
            ResolveWallBounds(build.Blocks, out Vector3 center, out Vector3 span);

            List<BreakableWall.Cell> manifest = new List<BreakableWall.Cell>(build.Blocks.Count);
            for (int i = 0; i < build.Blocks.Count; i++)
            {
                PlacedBlock cell = build.Blocks[i];
                manifest.Add(new BreakableWall.Cell
                {
                    Prefab = cell.Prefab,
                    Name = cell.Name,
                    PositionInWall = cell.LocalPosition - center,
                    RotationInWall = cell.LocalRotation,
                    GridPosition = cell.GridPosition,
                    LogicalSize = cell.Footprint,
                });
            }

            Mesh wallMesh = null;
            Material[] materials = null;

            if (build.IsRectangle
                && useWallPanels
                && blockDatabase != null
                && blockDatabase.TryGetWallPanel(build.Type, out GameObject panel))
            {
                wallMesh = BuildPanelMesh(panel, build, span, out Material panelMaterial);
                if (wallMesh != null)
                {
                    materials = new[] { panelMaterial };
                    usedPanel = true;
                }
            }

            if (wallMesh == null)
            {
                wallMesh = BuildWeldedMesh(build.Blocks, center, out materials);
            }

            if (wallMesh == null)
            {
                return false;
            }

            GameObject wall = new GameObject(build.Name);
            wall.transform.SetParent(generatedRoot, false);
            wall.transform.SetLocalPositionAndRotation(center, Quaternion.identity);
            wall.transform.localScale = Vector3.one;

            wall.AddComponent<MeshFilter>().sharedMesh = wallMesh;
            wall.AddComponent<MeshRenderer>().sharedMaterials = materials;

            BoxCollider wallCollider = wall.AddComponent<BoxCollider>();
            wallCollider.center = wallMesh.bounds.center;
            wallCollider.size = wallMesh.bounds.size;

            KnockdownBlockAuthoring wallAuthoring = wall.AddComponent<KnockdownBlockAuthoring>();
            if (build.Blocks[0].Prefab != null
                && build.Blocks[0].Prefab.TryGetComponent(out KnockdownBlockAuthoring source))
            {
                // The wall behaves like the material it is made of, but weighs what all of its
                // blocks weigh together.
                wallAuthoring.CopyTuningFrom(source, source.Mass * build.Blocks.Count);
            }

            wallAuthoring.SetGridPosition(build.GridPosition);
            wallAuthoring.SetLogicalSize(build.LogicalSize);

            // Durability is summed from the blocks the wall stands for, so a long wall is harder
            // to bring down than a short one and much harder than a lone block.
            ResolveWallDurability(build.Blocks, out string wallMaterialId, out float wallHitPoints);
            wall.AddComponent<BreakableWall>().Initialize(manifest, physicsSetup, wallMaterialId, wallHitPoints);
            return true;
        }

        /// <summary>
        /// What the wall is made of and how much punishment it takes, read off the blocks it
        /// replaces. A wall of mixed materials answers to the first block in it, since a shot has
        /// to hit something definite.
        /// </summary>
        private static void ResolveWallDurability(List<PlacedBlock> blocks, out string materialId, out float hitPoints)
        {
            materialId = null;
            hitPoints = 0f;

            for (int i = 0; i < blocks.Count; i++)
            {
                if (blocks[i].Prefab == null || !blocks[i].Prefab.TryGetComponent(out BreakableBlock breakable))
                {
                    continue;
                }

                hitPoints += breakable.MaxHitPoints;
                if (string.IsNullOrEmpty(materialId))
                {
                    materialId = breakable.MaterialId;
                }
            }

            if (hitPoints <= 0f)
            {
                hitPoints = blocks.Count;
            }
        }

        /// <summary>
        /// Stretches the one-cell wall panel across the run. The stretch is baked into a copy of
        /// the mesh rather than applied as a transform scale, so the panel's normals stay correct
        /// under a very non-uniform stretch, and the UVs are scaled with it so the bricks keep
        /// their size instead of smearing along the wall.
        /// </summary>
        private Mesh BuildPanelMesh(GameObject panel, WallBuild build, Vector3 cellSpan, out Material material)
        {
            material = null;

            MeshFilter panelFilter = panel.GetComponentInChildren<MeshFilter>(true);
            if (panelFilter == null || panelFilter.sharedMesh == null)
            {
                return null;
            }

            if (panelFilter.TryGetComponent(out MeshRenderer panelRenderer))
            {
                material = panelRenderer.sharedMaterial;
            }

            Mesh source = panelFilter.sharedMesh;
            Vector3 panelSize = source.bounds.size;
            Vector3 stretch = new Vector3(
                panelSize.x > 0.0001f ? cellSpan.x / panelSize.x : 1f,
                panelSize.y > 0.0001f ? cellSpan.y / panelSize.y : 1f,
                1f);

            Mesh wallMesh = Instantiate(source);
            wallMesh.name = $"{build.Name}_Mesh";

            Vector3[] vertices = wallMesh.vertices;
            for (int i = 0; i < vertices.Length; i++)
            {
                vertices[i] = Vector3.Scale(vertices[i], stretch);
            }

            Vector3[] normals = wallMesh.normals;
            Vector3 normalScale = new Vector3(1f / stretch.x, 1f / stretch.y, 1f / stretch.z);
            for (int i = 0; i < normals.Length; i++)
            {
                normals[i] = Vector3.Scale(normals[i], normalScale).normalized;
            }

            // Tiled by how far the panel was stretched, not by the cell count, so a map whose
            // cells are not the same size as its blocks still gets even brick sizes.
            Vector2 tiling = new Vector2(
                stretch.x * Mathf.Max(0.001f, wallTextureTilesPerCell),
                stretch.y * Mathf.Max(0.001f, wallTextureTilesPerCell));

            Vector2[] uv = wallMesh.uv;
            for (int i = 0; i < uv.Length; i++)
            {
                uv[i] = Vector2.Scale(uv[i], tiling);
            }

            wallMesh.vertices = vertices;
            if (normals.Length > 0)
            {
                wallMesh.normals = normals;
            }

            if (uv.Length > 0)
            {
                wallMesh.uv = uv;
            }

            wallMesh.RecalculateBounds();
            return wallMesh;
        }

        /// <summary>
        /// The fallback when a wall cannot use a panel - it mixes materials, spans layers, or is
        /// not a solid rectangle: the block meshes welded where they stand. Costs the same
        /// vertices as the blocks it replaces, but still collapses them into one renderer and one
        /// body, and keeps the wall's real shape.
        /// </summary>
        private static Mesh BuildWeldedMesh(List<PlacedBlock> blocks, Vector3 center, out Material[] materials)
        {
            materials = null;

            // Grouped by material so a wall of mixed types becomes one submesh per material
            // rather than one per block.
            List<Material> materialOrder = new List<Material>();
            Dictionary<Material, List<CombineInstance>> byMaterial = new Dictionary<Material, List<CombineInstance>>();

            for (int i = 0; i < blocks.Count; i++)
            {
                PlacedBlock cell = blocks[i];
                MeshFilter visual = cell.Prefab != null ? cell.Prefab.GetComponentInChildren<MeshFilter>(true) : null;
                if (visual == null || visual.sharedMesh == null)
                {
                    return null;
                }

                Material material = visual.TryGetComponent(out MeshRenderer visualRenderer)
                    ? visualRenderer.sharedMaterial
                    : null;

                if (!byMaterial.TryGetValue(material, out List<CombineInstance> instances))
                {
                    instances = new List<CombineInstance>();
                    byMaterial[material] = instances;
                    materialOrder.Add(material);
                }

                Matrix4x4 cellMatrix = Matrix4x4.TRS(cell.LocalPosition - center, cell.LocalRotation, Vector3.one);
                Matrix4x4 visualMatrix = cell.Prefab.transform.worldToLocalMatrix * visual.transform.localToWorldMatrix;
                Matrix4x4 combined = cellMatrix * visualMatrix;

                for (int subMesh = 0; subMesh < visual.sharedMesh.subMeshCount; subMesh++)
                {
                    instances.Add(new CombineInstance
                    {
                        mesh = visual.sharedMesh,
                        subMeshIndex = subMesh,
                        transform = combined,
                    });
                }
            }

            if (materialOrder.Count == 0)
            {
                return null;
            }

            materials = materialOrder.ToArray();

            if (materialOrder.Count == 1)
            {
                return CombineToMesh(byMaterial[materialOrder[0]], true, true);
            }

            // Two passes: each material's blocks welded into one mesh, then those welded together
            // without merging, which leaves exactly one submesh per material.
            List<CombineInstance> parts = new List<CombineInstance>(materialOrder.Count);
            List<Mesh> temporaries = new List<Mesh>(materialOrder.Count);
            for (int i = 0; i < materialOrder.Count; i++)
            {
                Mesh part = CombineToMesh(byMaterial[materialOrder[i]], true, true);
                temporaries.Add(part);
                parts.Add(new CombineInstance { mesh = part, subMeshIndex = 0 });
            }

            Mesh wallMesh = CombineToMesh(parts, false, false);

            for (int i = 0; i < temporaries.Count; i++)
            {
                DestroyMesh(temporaries[i]);
            }

            return wallMesh;
        }

        private static Mesh CombineToMesh(List<CombineInstance> instances, bool mergeSubMeshes, bool useMatrices)
        {
            Mesh mesh = new Mesh { indexFormat = IndexFormat.UInt32 };
            mesh.CombineMeshes(instances.ToArray(), mergeSubMeshes, useMatrices);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void DestroyMesh(Mesh mesh)
        {
            if (mesh == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(mesh);
            }
            else
            {
                DestroyImmediate(mesh);
            }
        }

        /// <summary>
        /// Middle of the blocks the wall covers, and how far it has to reach. The span is measured
        /// from the blocks themselves plus one block, rather than assumed from the cell count, so
        /// it stays right when a map's cell size and its block size disagree.
        /// </summary>
        private static void ResolveWallBounds(List<PlacedBlock> blocks, out Vector3 center, out Vector3 span)
        {
            Vector3 min = blocks[0].LocalPosition;
            Vector3 max = min;
            for (int i = 1; i < blocks.Count; i++)
            {
                min = Vector3.Min(min, blocks[i].LocalPosition);
                max = Vector3.Max(max, blocks[i].LocalPosition);
            }

            center = (min + max) * 0.5f;
            span = (max - min) + ResolveBlockSize(blocks[0].Prefab);
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
