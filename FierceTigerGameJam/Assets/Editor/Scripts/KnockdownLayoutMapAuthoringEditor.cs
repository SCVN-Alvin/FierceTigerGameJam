using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using GameJam.Gameplay.Wall;

[CustomEditor(typeof(KnockdownLayoutMapAuthoring))]
public sealed class KnockdownLayoutMapAuthoringEditor : Editor
{
    private const float CellButtonSize = 26f;

    private static readonly Color OccupiedCellColor = new Color(0.25f, 0.8f, 0.35f);
    private static readonly Color EmptyCellColor = new Color(0.2f, 0.2f, 0.2f);
    private static readonly Color UnknownTypeCellColor = new Color(0.85f, 0.35f, 0.2f);

    private int previewLevel;

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.PropertyField(serializedObject.FindProperty("mapJson"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("blockDatabase"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("blocksRoot"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("physicsSetup"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("centerGrid"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("buildOnStart"));

        serializedObject.ApplyModifiedProperties();

        KnockdownLayoutMapAuthoring layout = (KnockdownLayoutMapAuthoring)target;

        EditorGUILayout.Space(10f);
        KnockdownMapDefinition map = DrawMapSummary(layout);

        if (map != null)
        {
            EditorGUILayout.Space(10f);
            DrawLayerPreview(layout, map);
        }

        EditorGUILayout.Space(10f);
        DrawActions(layout, map);
    }

    private KnockdownMapDefinition DrawMapSummary(KnockdownLayoutMapAuthoring layout)
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

        if (layout.BlockDatabase == null)
        {
            EditorGUILayout.HelpBox("Assign a block database to resolve block types.", MessageType.Warning);
        }

        return map;
    }

    /// <summary>
    /// Face-on view of one layer: columns run along the grid width, rows upward, with the top
    /// row drawn first so the preview reads the same way the slice stands in the scene.
    /// </summary>
    private void DrawLayerPreview(KnockdownLayoutMapAuthoring layout, KnockdownMapDefinition map)
    {
        EditorGUILayout.LabelField("Layer Preview", EditorStyles.boldLabel);

        int maxLevel = map.MaxLevel();
        previewLevel = EditorGUILayout.IntSlider("Level (Z)", Mathf.Clamp(previewLevel, 0, maxLevel), 0, maxLevel);

        Dictionary<Vector2Int, KnockdownMapBlock> cells = BuildLevelOccupancy(layout, map, previewLevel, out bool hasUnknownType);

        for (int y = map.grid.height - 1; y >= 0; y--)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            for (int x = 0; x < map.grid.width; x++)
            {
                cells.TryGetValue(new Vector2Int(x, y), out KnockdownMapBlock block);

                Color previous = GUI.backgroundColor;
                GUI.backgroundColor = block == null
                    ? EmptyCellColor
                    : (IsTypeKnown(layout, block.type) ? OccupiedCellColor : UnknownTypeCellColor);

                string label = block == null ? string.Empty : ShortLabel(block.type);
                GUILayout.Box(
                    new GUIContent(label, block == null ? $"({x}, {y}) empty" : $"({x}, {y}) {block.type} — {block.id}"),
                    GUILayout.Width(CellButtonSize),
                    GUILayout.Height(CellButtonSize));

                GUI.backgroundColor = previous;
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.LabelField($"x 0..{map.grid.width - 1} left to right, y {map.grid.height - 1}..0 top to bottom", EditorStyles.miniLabel);

        if (hasUnknownType)
        {
            EditorGUILayout.HelpBox("Cells in red use a block type the database does not map to a prefab.", MessageType.Warning);
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
