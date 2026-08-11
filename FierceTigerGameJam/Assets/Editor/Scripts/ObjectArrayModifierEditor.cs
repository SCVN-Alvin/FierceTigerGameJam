using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(ObjectArrayModifier))]
[CanEditMultipleObjects]
public class ObjectArrayModifierEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        DrawDefaultInspector();
        serializedObject.ApplyModifiedProperties();

        EditorGUILayout.Space(10);

        if (GUILayout.Button("Spawn / Rebuild Array", GUILayout.Height(32)))
        {
            foreach (Object targetObject in targets)
            {
                ObjectArrayModifier array =
                    (ObjectArrayModifier)targetObject;

                array.SpawnArray();
            }
        }

        if (GUILayout.Button("Clear Array"))
        {
            foreach (Object targetObject in targets)
            {
                ObjectArrayModifier array =
                    (ObjectArrayModifier)targetObject;

                array.ClearArray();
            }
        }
    }
}