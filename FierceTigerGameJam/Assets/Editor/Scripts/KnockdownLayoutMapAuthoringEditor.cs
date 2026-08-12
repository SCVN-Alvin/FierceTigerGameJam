using UnityEditor;
using UnityEngine;
using GameJam.Gameplay.Wall;

[CustomEditor(typeof(KnockdownLayoutMapAuthoring))]
public sealed class KnockdownLayoutMapAuthoringEditor : Editor
{
    private const float CellButtonSize = 26f;

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        KnockdownLayoutMapAuthoring layout = (KnockdownLayoutMapAuthoring)target;

        DrawDimensions(layout);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("blockPrefab"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("blocksRoot"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("cellSize"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("centerGrid"));

        EditorGUILayout.Space(10f);
        DrawGrid(layout);

        EditorGUILayout.Space(10f);
        DrawActions(layout);

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawDimensions(KnockdownLayoutMapAuthoring layout)
    {
        SerializedProperty widthProperty = serializedObject.FindProperty("width");
        SerializedProperty heightProperty = serializedObject.FindProperty("height");

        EditorGUI.BeginChangeCheck();
        int nextWidth = EditorGUILayout.IntField("Width", widthProperty.intValue);
        int nextHeight = EditorGUILayout.IntField("Height", heightProperty.intValue);
        if (!EditorGUI.EndChangeCheck())
        {
            return;
        }

        Undo.RecordObject(layout, "Resize Knockdown Layout Grid");
        layout.ResizeGrid(nextWidth, nextHeight);
        EditorUtility.SetDirty(layout);
        serializedObject.Update();
    }

    private void DrawGrid(KnockdownLayoutMapAuthoring layout)
    {
        EditorGUILayout.LabelField("Layout Grid", EditorStyles.boldLabel);

        for (int y = layout.Height - 1; y >= 0; y--)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            for (int x = 0; x < layout.Width; x++)
            {
                bool occupied = layout.IsCellOccupied(x, y);
                Color previous = GUI.backgroundColor;
                GUI.backgroundColor = occupied ? new Color(0.25f, 0.8f, 0.35f) : new Color(0.2f, 0.2f, 0.2f);

                if (GUILayout.Button(occupied ? "X" : string.Empty, GUILayout.Width(CellButtonSize), GUILayout.Height(CellButtonSize)))
                {
                    Undo.RecordObject(layout, "Toggle Knockdown Layout Cell");
                    layout.ToggleCell(x, y);
                    EditorUtility.SetDirty(layout);
                }

                GUI.backgroundColor = previous;
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }
    }

    private void DrawActions(KnockdownLayoutMapAuthoring layout)
    {
        if (GUILayout.Button("Clear Layout", GUILayout.Height(28f)))
        {
            Undo.RecordObject(layout, "Clear Knockdown Layout");
            layout.ClearLayout();
            EditorUtility.SetDirty(layout);
        }

        GUI.enabled = layout.BlockPrefab != null;
        if (GUILayout.Button("Generate", GUILayout.Height(34f)))
        {
            layout.GenerateBlocks();
        }

        GUI.enabled = true;
    }
}
