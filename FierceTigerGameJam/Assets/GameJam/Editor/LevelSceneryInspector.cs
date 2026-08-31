using UnityEditor;
using UnityEngine;

namespace GameJam.Gameplay.Playfield
{
    /// <summary>
    /// Shows the two fields this component is actually used through and hides the rest.
    ///
    /// Everything here was wired once and never touched again; left on screen it read as a wall
    /// of settings to understand before anything could be dragged.
    /// </summary>
    [CustomEditor(typeof(LevelScenery))]
    public sealed class LevelSceneryInspector : Editor
    {
        private static bool showAdvanced;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.HelpBox(
                "1. Chon BG cho mission trong Mission Editor.\n"
                + "2. Go so mission vao Preview Mission.\n"
                + "3. Chon Backdrop trong Hierarchy, keo/scale thoai mai - tu luu ngay.\n"
                + "4. Go so mission khac: no tra ve dung thong so da luu cua mission do, "
                + "hoac thong so goc neu chua tung chinh.\n"
                + "5. Go 0 khi xong.",
                MessageType.Info);

            EditorGUILayout.PropertyField(serializedObject.FindProperty("previewMission"),
                new GUIContent("Preview Mission", "1, 2, 3. 0 = tat preview."));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("previewLevel"),
                new GUIContent("Preview Level", "0 = ca mission. 1..9 = rieng level do."));

            LevelScenery scenery = (LevelScenery)target;

            EditorGUILayout.Space(4f);
            if (GUILayout.Button("Xoa thong so cua mission/level dang chon", GUILayout.Height(22f)))
            {
                Undo.RecordObject(scenery, "Clear backdrop placement");
                scenery.ClearPlacement();
            }

            EditorGUILayout.Space(6f);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("showCameraFrame"),
                new GUIContent("Show Camera Frame", "Khung vang = vung camera nhin thay."));

            EditorGUILayout.Space(6f);
            showAdvanced = EditorGUILayout.Foldout(showAdvanced, "Nang cao", true);
            if (showAdvanced)
            {
                EditorGUI.indentLevel++;
                foreach (string name in new[]
                         {
                             "mapSelection", "missionSource", "backdropRoot", "backdrops",
                             "ground", "fit", "backdropHeight", "rescaleBackdrops",
                             "frameCamera", "frameAspect", "authoredBackdropSize",
                         })
                {
                    SerializedProperty property = serializedObject.FindProperty(name);
                    if (property != null)
                    {
                        EditorGUILayout.PropertyField(property, true);
                    }
                }

                EditorGUILayout.Space(4f);
                if (GUILayout.Button("Lay dang hien tai lam goc", GUILayout.Height(22f)))
                {
                    Undo.RecordObject(scenery, "Rebaseline backdrops");
                    scenery.RebaselineBackdrops();
                }

                if (GUILayout.Button("Tim backdrop va ground", GUILayout.Height(22f)))
                {
                    Undo.RecordObject(scenery, "Find scenery pieces");
                    scenery.FindPieces();
                }

                EditorGUI.indentLevel--;
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
