#if UNITY_EDITOR
using System.Collections.Generic;
using GameJam.Gameplay.Flow;
using GameJam.Gameplay.Wall;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace GameJam.EditorTools
{
    /// <summary>
    /// Sorts the open scene's root objects under labelled section headers. Re-running only touches
    /// objects still sitting at the root, so anything filed by hand afterwards stays where it is.
    /// </summary>
    public static class SceneHierarchyOrganizer
    {
        public const string StaticHeader = "=====STATIC=====";
        public const string GameplayHeader = "=====GAMEPLAY=====";
        public const string UiHeader = "=====UI=====";
        public const string SystemHeader = "=====SYSTEM=====";

        /// <summary>Declared in the order they should appear in the hierarchy.</summary>
        private static readonly string[] SectionOrder =
        {
            StaticHeader,
            GameplayHeader,
            UiHeader,
            SystemHeader,
        };

        [MenuItem("Tools/Smashdown/Organize Scene Hierarchy")]
        public static void OrganizeActiveScene()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded)
            {
                Debug.LogWarning($"{nameof(SceneHierarchyOrganizer)} needs a loaded scene.");
                return;
            }

            Dictionary<string, Transform> sections = new Dictionary<string, Transform>();
            for (int i = 0; i < SectionOrder.Length; i++)
            {
                sections[SectionOrder[i]] = EnsureSection(scene, SectionOrder[i]);
            }

            int moved = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (sections.ContainsKey(root.name))
                {
                    continue;
                }

                Transform section = sections[Classify(root)];
                Undo.SetTransformParent(root.transform, section, "Organize Scene Hierarchy");
                moved++;
            }

            // Done after the moves so the headers are not shuffled while objects are reparented.
            for (int i = 0; i < SectionOrder.Length; i++)
            {
                sections[SectionOrder[i]].SetSiblingIndex(i);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log($"{nameof(SceneHierarchyOrganizer)} filed {moved} root object(s) into {SectionOrder.Length} sections.");
        }

        /// <summary>
        /// Looks at the whole subtree, because the telling component is often on a child: a UI
        /// root holds its Canvas directly, but a rig may keep its light or camera one level down.
        /// </summary>
        private static string Classify(GameObject root)
        {
            if (root.GetComponentInChildren<Canvas>(true) != null
                || root.GetComponentInChildren<EventSystem>(true) != null)
            {
                return UiHeader;
            }

            if (root.GetComponentInChildren<MapSelector>(true) != null
                || root.GetComponentInChildren<GameFlowController>(true) != null)
            {
                return SystemHeader;
            }

            if (root.GetComponentInChildren<Light>(true) != null
                || root.GetComponentInChildren<ReflectionProbe>(true) != null
                || root.GetComponentInChildren<LightProbeGroup>(true) != null)
            {
                return StaticHeader;
            }

            return GameplayHeader;
        }

        private static Transform EnsureSection(Scene scene, string sectionName)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == sectionName)
                {
                    return root.transform;
                }
            }

            GameObject sectionObject = new GameObject(sectionName);
            Undo.RegisterCreatedObjectUndo(sectionObject, "Create Scene Section");
            SceneManager.MoveGameObjectToScene(sectionObject, scene);
            sectionObject.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            sectionObject.transform.localScale = Vector3.one;
            return sectionObject.transform;
        }
    }
}
#endif
