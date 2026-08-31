#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GameJam.EditorTools
{
    /// <summary>
    /// Takes the retired ammunition pick screen out of the scene and then out of the project.
    ///
    /// This is a menu item rather than a deletion in the repository because the two halves have to
    /// happen in that order and only Unity can do the first half safely. The scene holds the pick
    /// screen as a prefab INSTANCE - stripped transforms, cross-references from the flow - and a
    /// prefab asset deleted while the scene still instantiates it leaves a missing-prefab instance
    /// behind that nothing can identify afterwards. Hand-editing the scene YAML to remove the
    /// instance is the other way to get it wrong. So: instance first, then the asset, in one
    /// action, from the editor that understands both.
    ///
    /// Idempotent like the builders it sits beside. A second run finds nothing and says so, which
    /// is also what a fresh clone that never had the screen will see.
    /// </summary>
    public static class AmmoPickScreenRemover
    {
        private const string ScreenFolder = "Assets/GameJam/Prefabs/UI/AmmoPickScreen";
        private const string ScreenPrefabPath = ScreenFolder + "/AmmoPickScreen.prefab";
        private const string ScreenName = "AmmoPickScreen";

        /// <summary>
        /// The back button's object. Named rather than taken from the flow's serialized field
        /// because that field is already gone: the pick screen was its last home, so it went with
        /// it. There is exactly one object by this name in the scene and none in any prefab, and
        /// the search below is narrowed to the Canvas's own children on top of that.
        /// </summary>
        private const string BackButtonName = "BackBtn";

        [MenuItem("Tools/Smashdown/Retire Ammo Pick Screen")]
        public static void RetireAmmoPickScreen()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded)
            {
                Debug.LogWarning($"{nameof(AmmoPickScreenRemover)} needs a loaded scene.");
                return;
            }

            Canvas canvas = Object.FindFirstObjectByType<Canvas>(FindObjectsInactive.Include);
            if (canvas == null)
            {
                Debug.LogWarning(
                    $"{nameof(AmmoPickScreenRemover)} found no Canvas, so there is nothing in this "
                    + "scene to clean up. The prefab is left alone rather than deleted out from "
                    + "under a scene that was simply not open.");
                return;
            }

            int removed = RemoveFromScene(canvas.transform);
            if (removed > 0)
            {
                EditorSceneManager.MarkSceneDirty(scene);
            }

            // Only after the scene no longer points at it. The other order is the one that leaves
            // a broken instance behind.
            bool deletedAsset = DeleteScreenAsset();

            if (removed == 0 && !deletedAsset)
            {
                Debug.Log("The ammunition pick screen is already gone; nothing to do.");
                return;
            }

            Debug.Log(
                $"Retired the ammunition pick screen: {removed} object(s) removed from the scene"
                + (deletedAsset ? " and the prefab deleted" : " (the prefab was already gone)")
                + ". Save the scene to keep it. The asset deletion is not undoable.");
        }

        /// <summary>
        /// The screen instance and the back button, both direct children of the Canvas. Collected
        /// before anything is destroyed, because destroying while walking the children re-indexes
        /// the ones not yet looked at.
        /// </summary>
        private static int RemoveFromScene(Transform canvas)
        {
            List<GameObject> doomed = new List<GameObject>();

            for (int i = 0; i < canvas.childCount; i++)
            {
                GameObject child = canvas.GetChild(i).gameObject;

                if (child.name == BackButtonName || IsPickScreen(child))
                {
                    doomed.Add(child);
                }
            }

            for (int i = 0; i < doomed.Count; i++)
            {
                Undo.DestroyObjectImmediate(doomed[i]);
            }

            return doomed.Count;
        }

        /// <summary>
        /// True for the pick screen's instance. The prefab path is the reliable answer while the
        /// asset still exists; the name is what is left once it does not, which is the state a
        /// half-finished cleanup leaves behind.
        /// </summary>
        private static bool IsPickScreen(GameObject candidate)
        {
            if (candidate.name == ScreenName)
            {
                return true;
            }

            return PrefabUtility.IsAnyPrefabInstanceRoot(candidate)
                   && PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(candidate) == ScreenPrefabPath;
        }

        /// <summary>
        /// The prefab and the folder that held only it. Returns whether there was anything left to
        /// delete, so a re-run can say "already gone" rather than claiming work it did not do.
        /// </summary>
        private static bool DeleteScreenAsset()
        {
            bool deleted = false;

            if (AssetDatabase.LoadAssetAtPath<GameObject>(ScreenPrefabPath) != null)
            {
                deleted = AssetDatabase.DeleteAsset(ScreenPrefabPath);
            }

            // The folder exists for this one prefab, so it goes too - but only once it is empty,
            // in case something else was put there in the meantime.
            if (AssetDatabase.IsValidFolder(ScreenFolder)
                && AssetDatabase.FindAssets(string.Empty, new[] { ScreenFolder }).Length == 0)
            {
                deleted |= AssetDatabase.DeleteAsset(ScreenFolder);
            }

            if (deleted)
            {
                AssetDatabase.SaveAssets();
            }

            return deleted;
        }
    }
}
#endif
