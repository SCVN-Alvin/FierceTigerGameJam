#if UNITY_EDITOR
using GameJam.Gameplay.Flow;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GameJam.EditorTools
{
    /// <summary>
    /// Puts the AUTHORED visibility of every screen back to what the game shows on launch: the
    /// main menu and the bottom bar on, everything else off.
    ///
    /// At runtime the flow controller owns these and none of this matters. But screens left
    /// active in the saved scene all draw at once in the edit-mode Game view - a BACK button
    /// over the shop, a stray 0%, two gold chips - which reads as a broken build when it is only
    /// a messy save. The roots are taken from the flow's own serialized fields rather than by
    /// name, so this stays correct when screens are renamed or replaced.
    /// </summary>
    public static class ScreenVisibilityReset
    {
        private static readonly string[] RootsToShow = { "mainMenuRoot", "bottomBarRoot" };

        private static readonly string[] RootsToHide =
        {
            // No ammoPickRoot or backButton: the pick screen and the back button it belonged to
            // are gone, and FindProperty would only answer null for them every run.
            "mapSelectionRoot", "iapShopRoot", "shopRoot",
            "hudRoot", "failRoot", "clearedRoot", "settingsRoot",
        };

        [MenuItem("Tools/Smashdown/Reset Screen Visibility")]
        public static void Reset()
        {
            GameFlowController flow = Object.FindFirstObjectByType<GameFlowController>(FindObjectsInactive.Include);
            if (flow == null)
            {
                Debug.LogWarning("No GameFlowController in this scene, so there is nothing to reset.");
                return;
            }

            SerializedObject serialized = new SerializedObject(flow);
            int changed = 0;

            foreach (string field in RootsToShow)
            {
                changed += SetActive(serialized, field, true);
            }

            foreach (string field in RootsToHide)
            {
                changed += SetActive(serialized, field, false);
            }

            if (changed > 0)
            {
                EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            }

            Debug.Log(changed > 0
                ? $"Screen visibility reset: {changed} object(s) changed. Save the scene to keep it."
                : "The saved screens already match the launch state; nothing to change.");
        }

        private static int SetActive(SerializedObject flow, string fieldName, bool active)
        {
            SerializedProperty property = flow.FindProperty(fieldName);
            if (property == null || property.objectReferenceValue == null)
            {
                return 0;
            }

            GameObject target = property.objectReferenceValue as GameObject;
            if (target == null && property.objectReferenceValue is Component component)
            {
                target = component.gameObject;
            }

            if (target == null || target.activeSelf == active)
            {
                return 0;
            }

            Undo.RecordObject(target, "Reset screen visibility");
            target.SetActive(active);
            return 1;
        }
    }
}
#endif
