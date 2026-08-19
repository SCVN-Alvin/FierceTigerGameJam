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

        SerializedProperty grouping = serializedObject.FindProperty("groupBlocksIntoWalls");
        SerializedProperty minimumWallCells = serializedObject.FindProperty("minimumWallCells");

        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("Wall Grouping", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(grouping);
        using (new EditorGUI.DisabledScope(!grouping.boolValue))
        {
            EditorGUILayout.PropertyField(minimumWallCells);
        }

        serializedObject.ApplyModifiedProperties();

        KnockdownLayoutMapAuthoring layout = (KnockdownLayoutMapAuthoring)target;

        EditorGUILayout.Space(10f);
        KnockdownMapDefinition map = DrawMapSummary(layout, grouping.boolValue, minimumWallCells.intValue);

        if (map != null)
        {
            EditorGUILayout.Space(10f);
            DrawLayerPreview(layout, map, grouping.boolValue, minimumWallCells.intValue);
        }

        EditorGUILayout.Space(10f);
        DrawActions(layout, map);
    }

    private KnockdownMapDefinition DrawMapSummary(KnockdownLayoutMapAuthoring layout, bool grouping, int minimumWallCells)
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

        if (grouping && layout.BlockDatabase != null)
        {
            CountGrouping(layout, map, minimumWallCells, out int walls, out int wallCells);
            int objects = walls + (map.CountBlocks() - wallCells);
            EditorGUILayout.LabelField("Grouped", $"{walls} wall(s) covering {wallCells} block(s) -> {objects} object(s)");
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
    /// row drawn first so the preview reads the same way the slice stands in the scene. With
    /// grouping on, cells that will be merged share a colour, so a wall is visible before it is
    /// built.
    /// </summary>
    private void DrawLayerPreview(
        KnockdownLayoutMapAuthoring layout,
        KnockdownMapDefinition map,
        bool grouping,
        int minimumWallCells)
    {
        EditorGUILayout.LabelField("Layer Preview", EditorStyles.boldLabel);

        int maxLevel = map.MaxLevel();
        previewLevel = EditorGUILayout.IntSlider("Level (Z)", Mathf.Clamp(previewLevel, 0, maxLevel), 0, maxLevel);

        Dictionary<Vector2Int, KnockdownMapBlock> cells = BuildLevelOccupancy(layout, map, previewLevel, out bool hasUnknownType);

        Dictionary<Vector2Int, int> wallByCell = null;
        int wallCount = 0;
        if (grouping && layout.BlockDatabase != null)
        {
            wallByCell = ResolveWalls(layout, map, previewLevel, minimumWallCells, out wallCount);
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
            EditorGUILayout.LabelField(
                wallCount > 0
                    ? $"{wallCount} wall(s) on this level; each colour is one wall, grey blocks stay separate."
                    : "Nothing on this level is a long enough run to merge.",
                EditorStyles.miniLabel);
        }

        if (hasUnknownType)
        {
            EditorGUILayout.HelpBox("Cells in red use a block type the database does not map to a prefab.", MessageType.Warning);
        }
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
    /// Mirrors what the builder does: only single-cell blocks group, by type, within one level.
    /// This works straight off the map rather than off accepted placements, so a map with
    /// overlapping blocks can preview a wall the builder would end up trimming.
    /// </summary>
    private static Dictionary<Vector2Int, int> ResolveWalls(
        KnockdownLayoutMapAuthoring layout,
        KnockdownMapDefinition map,
        int level,
        int minimumWallCells,
        out int wallCount)
    {
        Dictionary<Vector2Int, int> wallByCell = new Dictionary<Vector2Int, int>();
        wallCount = 0;

        // Walls the map named come first and are taken as given, exactly as the builder does.
        HashSet<Vector2Int> named = new HashSet<Vector2Int>();
        Dictionary<string, int> indexByWallId = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (KnockdownMapBlock block in BlocksOnLevel(map, level))
        {
            string wallId = block.WallId;
            if (string.IsNullOrEmpty(wallId) || block.position == null)
            {
                continue;
            }

            if (!indexByWallId.TryGetValue(wallId, out int index))
            {
                index = wallCount++;
                indexByWallId[wallId] = index;
            }

            Vector2Int cell = new Vector2Int(block.position.x, block.position.y);
            wallByCell[cell] = index;
            named.Add(cell);
        }

        foreach (KeyValuePair<string, Dictionary<Vector2Int, string>> group in BuildSingleCellGroups(layout, map, level))
        {
            Dictionary<Vector2Int, string> free = new Dictionary<Vector2Int, string>(group.Value.Count);
            foreach (KeyValuePair<Vector2Int, string> cell in group.Value)
            {
                if (!named.Contains(cell.Key))
                {
                    free[cell.Key] = cell.Value;
                }
            }

            List<WallGrouping.WallRect> rects = WallGrouping.Find(free);
            for (int i = 0; i < rects.Count; i++)
            {
                if (rects[i].Area < minimumWallCells)
                {
                    continue;
                }

                for (int c = 0; c < rects[i].Cells.Count; c++)
                {
                    wallByCell[rects[i].Cells[c]] = wallCount;
                }

                wallCount++;
            }
        }

        return wallByCell;
    }

    private static IEnumerable<KnockdownMapBlock> BlocksOnLevel(KnockdownMapDefinition map, int level)
    {
        for (int layerIndex = 0; layerIndex < map.layers.Length; layerIndex++)
        {
            KnockdownMapLayer layer = map.layers[layerIndex];
            if (layer == null || layer.level != level || layer.blocks == null)
            {
                continue;
            }

            for (int blockIndex = 0; blockIndex < layer.blocks.Length; blockIndex++)
            {
                if (layer.blocks[blockIndex] != null)
                {
                    yield return layer.blocks[blockIndex];
                }
            }
        }
    }

    private static Dictionary<string, Dictionary<Vector2Int, string>> BuildSingleCellGroups(
        KnockdownLayoutMapAuthoring layout,
        KnockdownMapDefinition map,
        int level)
    {
        Dictionary<string, Dictionary<Vector2Int, string>> groups = new Dictionary<string, Dictionary<Vector2Int, string>>();

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
                if (block?.position == null || !IsTypeKnown(layout, block.type))
                {
                    continue;
                }

                Vector3Int footprint = ResolveFootprint(layout, block);
                if (footprint.x != 1 || footprint.y != 1 || footprint.z != 1)
                {
                    continue;
                }

                if (!groups.TryGetValue(block.type, out Dictionary<Vector2Int, string> cells))
                {
                    cells = new Dictionary<Vector2Int, string>();
                    groups[block.type] = cells;
                }

                cells[new Vector2Int(block.position.x, block.position.y)] = block.type;
            }
        }

        return groups;
    }

    private static void CountGrouping(
        KnockdownLayoutMapAuthoring layout,
        KnockdownMapDefinition map,
        int minimumWallCells,
        out int walls,
        out int wallCells)
    {
        walls = 0;
        wallCells = 0;

        for (int level = 0; level <= map.MaxLevel(); level++)
        {
            Dictionary<Vector2Int, int> wallByCell = ResolveWalls(layout, map, level, minimumWallCells, out int levelWalls);
            walls += levelWalls;
            wallCells += wallByCell.Count;
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
