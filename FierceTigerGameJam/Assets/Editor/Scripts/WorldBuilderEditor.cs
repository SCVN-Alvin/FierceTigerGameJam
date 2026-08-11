using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(WorldBuilder))]
public class WorldBuilderEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        DrawDefaultInspector();
        serializedObject.ApplyModifiedProperties();

        EditorGUILayout.Space(12);

        WorldBuilder worldBuilder = (WorldBuilder)target;

        if (GUILayout.Button(
                "Create / Update Structure Center",
                GUILayout.Height(32)
            ))
        {
            worldBuilder.CreateOrUpdateCenter();
        }

        EditorGUILayout.Space(4);

        GUI.backgroundColor = new Color(0.4f, 0.9f, 0.5f);

        if (GUILayout.Button(
                "Save Structure Prefab",
                GUILayout.Height(38)
            ))
        {
            worldBuilder.SaveAsPrefab();
        }

        GUI.backgroundColor = Color.white;
    }
}