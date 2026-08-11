using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(StructureCenter))]
public class StructureCenterEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        DrawDefaultInspector();
        serializedObject.ApplyModifiedProperties();

        EditorGUILayout.Space(10);

        if (GUILayout.Button(
                "Create / Update Structure Center",
                GUILayout.Height(32)
            ))
        {
            StructureCenter structureCenter =
                (StructureCenter)target;

            structureCenter.CreateOrUpdateCenter();
        }
    }
}