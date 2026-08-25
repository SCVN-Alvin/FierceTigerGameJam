using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using GameJam.Gameplay.Wall;

[CustomEditor(typeof(KnockdownLayoutMapAuthoring))]
public sealed class KnockdownLayoutMapAuthoringEditor : Editor
{
    private const float CellButtonSize = 26f;

    private static readonly Color EmptyCellColor = new Color(0.2f, 0.2f, 0.2f);
    private static readonly Color LooseCellColor = new Color(0.45f, 0.45f, 0.45f);
    private static readonly Color UnknownTypeCellColor = new Color(0.85f, 0.35f, 0.2f);

    private int previewLevel;

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.PropertyField(serializedObject.FindProperty("mapJson"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("mapSelection"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("blockDatabase"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("structureRoot"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("physicsSetup"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("centerGrid"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("buildOnStart"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("createStructureCenter"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("structureSpinner"));

        // The "Wall Grouping" heading comes from the [Header] on the field itself.
        SerializedProperty mode = serializedObject.FindProperty("wallGrouping");
        SerializedProperty minimumWallCells = serializedObject.FindProperty("minimumWallCells");
        EditorGUILayout.PropertyField(mode);

        // Nothing but NamedAndDetected reads the threshold, and a field that is drawn but ignored
        // reads as a dial that still does something.
        if (mode.enumValueIndex == (int)WallGroupingMode.NamedAndDetected)
        {
            EditorGUILayout.PropertyField(minimumWallCells);
        }

        serializedObject.ApplyModifiedProperties();

        KnockdownLayoutMapAuthoring layout = (KnockdownLayoutMapAuthoring)target;
        WallGroupingMode grouping = (WallGroupingMode)mode.enumValueIndex;

        EditorGUILayout.Space(10f);
        KnockdownMapDefinition map = DrawMapSummary(layout, grouping, minimumWallCells.intValue);

        if (map != null)
        {
            EditorGUILayout.Space(10f);
            DrawLayerPreview(layout, map, grouping, minimumWallCells.intValue);
        }

        EditorGUILayout.Space(10f);
        DrawActions(layout, map);
    }

    private KnockdownMapDefinition DrawMapSummary(
        KnockdownLayoutMapAuthoring layout,
        WallGroupingMode grouping,
        int minimumWallCells)
    {
        if (layout.MapJson == null)
        {
            EditorGUILayout.HelpBox("Assign a map JSON asset to preview and build it.", MessageType.Info);
            return null;
        }

        if (!KnockdownMapDefinition.TryParse(layout.MapJson.text, out KnockdownMapDefinition map, out string error))
        {
            EditorGUILayout.HelpBox(error, MessageType.Error);
            return null;
        }

        EditorGUILayout.LabelField("Map", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Id", string.IsNullOrEmpty(map.id) ? "<none>" : map.id);
        EditorGUILayout.LabelField("Grid", $"{map.grid.width} x {map.grid.height} cells, cell {map.grid.cellSize}, layer depth {map.grid.layerDepth}");
        EditorGUILayout.LabelField("Contents", $"{map.layers.Length} layer(s), {map.CountBlocks()} block(s)");

        if (grouping != WallGroupingMode.None && layout.BlockDatabase != null)
        {
            WallPreview preview = ResolveWalls(layout, map, grouping, minimumWallCells);
            int objects = preview.WallCount + (map.CountBlocks() - preview.WallBlockCount);
            EditorGUILayout.LabelField(
                "Grouped",
                $"{preview.WallCount} wall(s) covering {preview.WallBlockCount} block(s) -> {objects} object(s)");
        }

        // Measured off a prefab the map actually uses rather than assumed: a map whose cells are
        // smaller than its blocks still builds, but every block overlaps its neighbours and a
        // merged wall reads as a smear rather than a wall.
        if (TryResolveBlockWidth(layout, map, out float blockWidth)
            && map.grid.cellSize > 0f
            && Mathf.Abs(map.grid.cellSize - blockWidth) > blockWidth * 0.02f)
        {
            float ratio = blockWidth / map.grid.cellSize;
            EditorGUILayout.HelpBox(
                $"Cell size is {map.grid.cellSize} but the blocks are {blockWidth:0.###} across, "
                + $"so each block is {ratio:0.#}x the cell spacing. Blocks will "
                + (ratio > 1f ? "overlap" : "leave gaps")
                + ", and grouped walls will look wrong.",
                MessageType.Warning);
        }

        if (layout.BlockDatabase == null)
        {
            EditorGUILayout.HelpBox("Assign a block database to resolve block types.", MessageType.Warning);
        }

        return map;
    }

    /// <summary>
    /// Face-on view of one layer: columns run along the grid width, rows upward, with the top
    /// row drawn first so the preview reads the same way the slice stands in the scene. Cells
    /// that will be merged share a colour, so a wall is visible before it is built.
    /// </summary>
    private void DrawLayerPreview(
        KnockdownLayoutMapAuthoring layout,
        KnockdownMapDefinition map,
        WallGroupingMode grouping,
        int minimumWallCells)
    {
        EditorGUILayout.LabelField("Layer Preview", EditorStyles.boldLabel);

        int maxLevel = map.MaxLevel();
        previewLevel = EditorGUILayout.IntSlider("Level (Z)", Mathf.Clamp(previewLevel, 0, maxLevel), 0, maxLevel);

        Dictionary<Vector2Int, KnockdownMapBlock> cells = BuildLevelOccupancy(layout, map, previewLevel, out bool hasUnknownType);

        Dictionary<Vector2Int, int> wallByCell = null;
        int wallCount = 0;
        if (grouping != WallGroupingMode.None && layout.BlockDatabase != null)
        {
            WallPreview preview = ResolveWalls(layout, map, grouping, minimumWallCells);
            wallByCell = preview.SliceLevel(previewLevel, out wallCount);
        }

        for (int y = map.grid.height - 1; y >= 0; y--)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            for (int x = 0; x < map.grid.width; x++)
            {
                Vector2Int cell = new Vector2Int(x, y);
                cells.TryGetValue(cell, out KnockdownMapBlock block);

                Color previous = GUI.backgroundColor;
                GUI.backgroundColor = ResolveCellColor(layout, block, cell, wallByCell, out string wallNote);

                string label = block == null ? string.Empty : ShortLabel(block.type);
                string tooltip = block == null
                    ? $"({x}, {y}) empty"
                    : $"({x}, {y}) {block.type} - {block.id}{wallNote}";

                GUILayout.Box(
                    new GUIContent(label, tooltip),
                    GUILayout.Width(CellButtonSize),
                    GUILayout.Height(CellButtonSize));

                GUI.backgroundColor = previous;
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.LabelField($"x 0..{map.grid.width - 1} left to right, y {map.grid.height - 1}..0 top to bottom", EditorStyles.miniLabel);

        if (wallByCell != null)
        {
            EditorGUILayout.LabelField(ResolveLevelWallNote(grouping, wallCount), EditorStyles.miniLabel);
        }

        if (hasUnknownType)
        {
            EditorGUILayout.HelpBox("Cells in red use a block type the database does not map to a prefab.", MessageType.Warning);
        }
    }

    private static string ResolveLevelWallNote(WallGroupingMode grouping, int wallCount)
    {
        if (wallCount > 0)
        {
            return $"{wallCount} wall(s) on this level; each colour is one wall, grey blocks stay separate.";
        }

        return grouping == WallGroupingMode.NamedAndDetected
            ? "No block on this level names a wall, and nothing here is a long enough run to merge."
            : "No block on this level names a wall, so every block on it is built on its own.";
    }

    private static Color ResolveCellColor(
        KnockdownLayoutMapAuthoring layout,
        KnockdownMapBlock block,
        Vector2Int cell,
        Dictionary<Vector2Int, int> wallByCell,
        out string wallNote)
    {
        wallNote = string.Empty;

        if (block == null)
        {
            return EmptyCellColor;
        }

        // An unresolvable type wins over the grouping colour: it is the thing that needs fixing.
        if (!IsTypeKnown(layout, block.type))
        {
            return UnknownTypeCellColor;
        }

        if (wallByCell != null && wallByCell.TryGetValue(cell, out int wallIndex))
        {
            wallNote = $"\nwall #{wallIndex}";
            return WallColor(wallIndex);
        }

        return LooseCellColor;
    }

    /// <summary>Hues stepped by the golden ratio, so neighbouring walls never share a colour.</summary>
    private static Color WallColor(int index)
    {
        return Color.HSVToRGB((index * 0.618034f) % 1f, 0.55f, 0.9f);
    }

    /// <summary>
    /// Every wall the whole map would produce, indexed by the cells it covers. Held for the map
    /// rather than per level because a named wall may span levels: counting one level at a time
    /// would report a wall once per level it reaches into.
    /// </summary>
    private sealed class WallPreview
    {
        /// <summary>Cell (x, y, level) to the index of the wall that covers it.</summary>
        public readonly Dictionary<Vector3Int, int> WallIndexByCell = new Dictionary<Vector3Int, int>();

        public int WallCount;

        /// <summary>Blocks that ended up inside a wall, which is what the summary subtracts.</summary>
        public int WallBlockCount;

        /// <summary>Takes the next wall index and paints the blocks that make it up.</summary>
        public void AddWall(List<PreviewBlock> blocks, List<int> members)
        {
            for (int i = 0; i < members.Count; i++)
            {
                PreviewBlock block = blocks[members[i]];

                // Only the cells on the block's own level: a block deeper than one cell reserves
                // the levels behind it, but it is drawn on the level it was authored in.
                for (int x = 0; x < block.Footprint.x; x++)
                {
                    for (int y = 0; y < block.Footprint.y; y++)
                    {
                        WallIndexByCell[new Vector3Int(
                            block.GridPosition.x + x,
                            block.GridPosition.y + y,
                            block.Level)] = WallCount;
                    }
                }
            }

            WallBlockCount += members.Count;
            WallCount++;
        }

        /// <summary>The walls that show on one level, keyed the way the preview grid is drawn.</summary>
        public Dictionary<Vector2Int, int> SliceLevel(int level, out int wallCount)
        {
            Dictionary<Vector2Int, int> byCell = new Dictionary<Vector2Int, int>();
            HashSet<int> present = new HashSet<int>();

            foreach (KeyValuePair<Vector3Int, int> cell in WallIndexByCell)
            {
                if (cell.Key.z != level)
                {
                    continue;
                }

                byCell[new Vector2Int(cell.Key.x, cell.Key.y)] = cell.Value;
                present.Add(cell.Value);
            }

            wallCount = present.Count;
            return byCell;
        }
    }

    /// <summary>A block the builder would accept, reduced to what grouping actually looks at.</summary>
    private readonly struct PreviewBlock
    {
        public readonly KnockdownMapBlock Source;
        public readonly int Level;
        public readonly Vector3Int GridPosition;
        public readonly Vector3Int Footprint;

        public PreviewBlock(KnockdownMapBlock source, int level, Vector3Int footprint)
        {
            Source = source;
            Level = level;
            GridPosition = new Vector3Int(source.position.x, source.position.y, level);
            Footprint = footprint;
        }

        public bool IsSingleCell => Footprint.x == 1 && Footprint.y == 1 && Footprint.z == 1;
    }

    /// <summary>
    /// Mirrors what the builder does, so the preview never shows a wall the runtime will not
    /// build: named walls first, taken as given but demoted when they hold a single block and
    /// split when their blocks do not touch, then - in NamedAndDetected only - the automatic
    /// same-type rectangles for the blocks the map left unassigned.
    ///
    /// This works straight off the map rather than off accepted placements, so a map with
    /// overlapping or out-of-grid blocks can still preview a wall the builder would end up
    /// trimming.
    /// </summary>
    private static WallPreview ResolveWalls(
        KnockdownLayoutMapAuthoring layout,
        KnockdownMapDefinition map,
        WallGroupingMode grouping,
        int minimumWallCells)
    {
        WallPreview preview = new WallPreview();
        if (grouping == WallGroupingMode.None)
        {
            return preview;
        }

        List<PreviewBlock> blocks = CollectPreviewBlocks(layout, map);
        AddNamedWalls(preview, blocks);

        if (grouping == WallGroupingMode.NamedAndDetected)
        {
            AddDetectedWalls(preview, blocks, minimumWallCells);
        }

        return preview;
    }

    /// <summary>
    /// In the order the builder would place them, so a wall keeps the same index - and so the
    /// same colour - as the map is edited around it.
    /// </summary>
    private static List<PreviewBlock> CollectPreviewBlocks(
        KnockdownLayoutMapAuthoring layout,
        KnockdownMapDefinition map)
    {
        List<PreviewBlock> blocks = new List<PreviewBlock>();

        for (int layerIndex = 0; layerIndex < map.layers.Length; layerIndex++)
        {
            KnockdownMapLayer layer = map.layers[layerIndex];
            if (layer?.blocks == null)
            {
                continue;
            }

            for (int blockIndex = 0; blockIndex < layer.blocks.Length; blockIndex++)
            {
                KnockdownMapBlock block = layer.blocks[blockIndex];

                // A type the database cannot resolve never becomes a placement, so it can never
                // become part of a wall either.
                if (block?.position == null || !IsTypeKnown(layout, block.type))
                {
                    continue;
                }

                blocks.Add(new PreviewBlock(block, layer.level, ResolveFootprint(layout, block)));
            }
        }

        return blocks;
    }

    /// <summary>
    /// Walls the map named. A wall of one block is a block, and a wall whose blocks do not touch
    /// is split, both exactly as the builder does it.
    /// </summary>
    private static void AddNamedWalls(WallPreview preview, List<PreviewBlock> blocks)
    {
        Dictionary<string, List<int>> byWallId = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        List<string> order = new List<string>();

        for (int i = 0; i < blocks.Count; i++)
        {
            string wallId = blocks[i].Source.WallId;
            if (string.IsNullOrEmpty(wallId))
            {
                continue;
            }

            if (!byWallId.TryGetValue(wallId, out List<int> members))
            {
                members = new List<int>();
                byWallId[wallId] = members;
                order.Add(wallId);
            }

            members.Add(i);
        }

        for (int i = 0; i < order.Count; i++)
        {
            List<int> members = byWallId[order[i]];
            if (members.Count < 2)
            {
                continue;
            }

            List<WallGrouping.CellBox> boxes = new List<WallGrouping.CellBox>(members.Count);
            for (int m = 0; m < members.Count; m++)
            {
                boxes.Add(new WallGrouping.CellBox(blocks[members[m]].GridPosition, blocks[members[m]].Footprint));
            }

            List<List<int>> parts = WallGrouping.FindConnectedGroups(boxes);
            for (int p = 0; p < parts.Count; p++)
            {
                if (parts[p].Count < 2)
                {
                    continue;
                }

                List<int> part = new List<int>(parts[p].Count);
                for (int m = 0; m < parts[p].Count; m++)
                {
                    part.Add(members[parts[p][m]]);
                }

                preview.AddWall(blocks, part);
            }
        }
    }

    /// <summary>
    /// The legacy fallback: single-cell blocks of one type inside one level, grouped into
    /// rectangles. Only blocks the map left without a wall id are candidates - a block whose
    /// named wall was demoted stays a block rather than being picked up here, which is what the
    /// builder does too.
    /// </summary>
    private static void AddDetectedWalls(WallPreview preview, List<PreviewBlock> blocks, int minimumWallCells)
    {
        Dictionary<(int level, string type), Dictionary<Vector2Int, int>> groups =
            new Dictionary<(int, string), Dictionary<Vector2Int, int>>();
        List<(int level, string type)> order = new List<(int, string)>();

        for (int i = 0; i < blocks.Count; i++)
        {
            PreviewBlock block = blocks[i];
            if (!string.IsNullOrEmpty(block.Source.WallId) || !block.IsSingleCell)
            {
                continue;
            }

            (int, string) key = (block.Level, block.Source.type);
            if (!groups.TryGetValue(key, out Dictionary<Vector2Int, int> cells))
            {
                cells = new Dictionary<Vector2Int, int>();
                groups[key] = cells;
                order.Add(key);
            }

            cells[new Vector2Int(block.GridPosition.x, block.GridPosition.y)] = i;
        }

        for (int g = 0; g < order.Count; g++)
        {
            Dictionary<Vector2Int, int> cells = groups[order[g]];
            Dictionary<Vector2Int, string> typeByCell = new Dictionary<Vector2Int, string>(cells.Count);
            foreach (KeyValuePair<Vector2Int, int> cell in cells)
            {
                typeByCell[cell.Key] = order[g].type;
            }

            List<WallGrouping.WallRect> rects = WallGrouping.Find(typeByCell);
            for (int i = 0; i < rects.Count; i++)
            {
                if (rects[i].Area < minimumWallCells)
                {
                    continue;
                }

                List<int> members = new List<int>(rects[i].Cells.Count);
                for (int c = 0; c < rects[i].Cells.Count; c++)
                {
                    members.Add(cells[rects[i].Cells[c]]);
                }

                preview.AddWall(blocks, members);
            }
        }
    }

    private static Dictionary<Vector2Int, KnockdownMapBlock> BuildLevelOccupancy(
        KnockdownLayoutMapAuthoring layout,
        KnockdownMapDefinition map,
        int level,
        out bool hasUnknownType)
    {
        Dictionary<Vector2Int, KnockdownMapBlock> cells = new Dictionary<Vector2Int, KnockdownMapBlock>();
        hasUnknownType = false;

        for (int layerIndex = 0; layerIndex < map.layers.Length; layerIndex++)
        {
            KnockdownMapLayer layer = map.layers[layerIndex];
            if (layer == null || layer.level != level || layer.blocks == null)
            {
                continue;
            }

            for (int blockIndex = 0; blockIndex < layer.blocks.Length; blockIndex++)
            {
                KnockdownMapBlock block = layer.blocks[blockIndex];
                if (block?.position == null)
                {
                    continue;
                }

                Vector3Int footprint = ResolveFootprint(layout, block);
                if (!IsTypeKnown(layout, block.type))
                {
                    hasUnknownType = true;
                }

                for (int offsetX = 0; offsetX < footprint.x; offsetX++)
                {
                    for (int offsetY = 0; offsetY < footprint.y; offsetY++)
                    {
                        cells[new Vector2Int(block.position.x + offsetX, block.position.y + offsetY)] = block;
                    }
                }
            }
        }

        return cells;
    }

    /// <summary>
    /// How wide one block actually is, taken from the collider of the first type the map uses.
    /// </summary>
    private static bool TryResolveBlockWidth(
        KnockdownLayoutMapAuthoring layout,
        KnockdownMapDefinition map,
        out float width)
    {
        width = 0f;
        if (layout.BlockDatabase == null)
        {
            return false;
        }

        for (int layerIndex = 0; layerIndex < map.layers.Length; layerIndex++)
        {
            KnockdownMapLayer layer = map.layers[layerIndex];
            if (layer?.blocks == null)
            {
                continue;
            }

            for (int blockIndex = 0; blockIndex < layer.blocks.Length; blockIndex++)
            {
                KnockdownMapBlock block = layer.blocks[blockIndex];
                if (block == null || !layout.BlockDatabase.TryGetPrefab(block.type, out GameObject prefab))
                {
                    continue;
                }

                if (prefab.TryGetComponent(out BoxCollider blockCollider))
                {
                    Vector3Int footprint = KnockdownLayoutMapAuthoring.ResolveFootprint(prefab, 0f);
                    width = blockCollider.size.x / Mathf.Max(1, footprint.x);
                    return width > 0f;
                }
            }
        }

        return false;
    }

    private static Vector3Int ResolveFootprint(KnockdownLayoutMapAuthoring layout, KnockdownMapBlock block)
    {
        if (layout.BlockDatabase != null && layout.BlockDatabase.TryGetPrefab(block.type, out GameObject prefab))
        {
            return KnockdownLayoutMapAuthoring.ResolveFootprint(prefab, block.rotation);
        }

        return Vector3Int.one;
    }

    private static bool IsTypeKnown(KnockdownLayoutMapAuthoring layout, string type)
    {
        return layout.BlockDatabase != null && layout.BlockDatabase.TryGetPrefab(type, out _);
    }

    private static string ShortLabel(string type)
    {
        return string.IsNullOrEmpty(type) ? "?" : type.Substring(0, 1).ToUpperInvariant();
    }

    private void DrawActions(KnockdownLayoutMapAuthoring layout, KnockdownMapDefinition map)
    {
        using (new EditorGUI.DisabledScope(map == null || layout.BlockDatabase == null))
        {
            if (GUILayout.Button("Build Map", GUILayout.Height(34f)))
            {
                layout.BuildMap();
            }
        }

        if (GUILayout.Button("Clear Map", GUILayout.Height(28f)))
        {
            layout.ClearMap();
        }
    }
}
