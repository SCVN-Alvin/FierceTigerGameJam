#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace GameJam.EditorTools
{
    /// <summary>
    /// Converts the corner widgets - gold chip, mission chip, close X, settings gear and their
    /// kin - from fractional anchors to fixed-size corner anchors.
    ///
    /// The screens were authored as fractions of a 9:16 canvas, which is right for panels but
    /// wrong for chrome: on a wide screen a chip spanning "4.6% to 26.8% of the width" is twice
    /// as wide as its art and smears, taking its label with it. Pinning gives each widget the
    /// exact pixel box it has at the reference aspect, anchored to its nearest corner, so at any
    /// other aspect it keeps that size and simply stays by its corner. Labels inside a widget
    /// are fractions of the widget, not of the screen, so they follow along without being
    /// touched.
    ///
    /// At 9:16 the pinned box is identical to the fractional one, so the reference layout does
    /// not move. Runs over the mission and garage prefabs and over the plain-scene screens, and
    /// skips anything already pinned, so re-running is harmless.
    /// </summary>
    public static class CornerWidgetPinner
    {
        private static readonly string[] PrefabPaths =
        {
            "Assets/GameJam/Prefabs/UI/Mission/MissionScreen.prefab",
            "Assets/GameJam/Prefabs/UI/Garage/GarageScreen.prefab",
        };

        private static readonly HashSet<string> WidgetNames = new HashSet<string>
        {
            // Only the gold chip for now - it is the one piece of chrome whose stretching was
            // signed off as a problem. Add a name here to pin more.
            "MoneyChip",
        };

        [MenuItem("Tools/Smashdown/Pin Corner Widgets")]
        public static void Pin()
        {
            Vector2 reference = ResolveReferenceResolution();
            int pinned = 0;

            foreach (string path in PrefabPaths)
            {
                GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (root == null)
                {
                    continue;
                }

                GameObject contents = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    int changed = PinChildren((RectTransform)contents.transform, reference);
                    if (changed > 0)
                    {
                        PrefabUtility.SaveAsPrefabAsset(contents, path);
                        pinned += changed;
                    }
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(contents);
                }
            }

            pinned += PinSceneScreens(reference);

            Debug.Log(pinned > 0
                ? $"Pinned {pinned} corner widget(s) to fixed sizes. Save the scene to keep the scene-side ones."
                : "Every corner widget is already pinned; nothing to change.");
        }

        /// <summary>Scene screens that are not instances of the prefabs handled above.</summary>
        private static int PinSceneScreens(Vector2 reference)
        {
            Canvas canvas = Object.FindFirstObjectByType<Canvas>(FindObjectsInactive.Include);
            if (canvas == null)
            {
                return 0;
            }

            int pinned = 0;
            foreach (Transform child in canvas.transform)
            {
                if (!(child is RectTransform root))
                {
                    continue;
                }

                // Prefab instances inherit the fix from their asset; converting the instance too
                // would only pile overrides on top of it.
                if (PrefabUtility.IsPartOfPrefabInstance(root))
                {
                    continue;
                }

                int changed = PinChildren(root, reference);
                if (changed > 0)
                {
                    pinned += changed;
                    EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
                }
            }

            return pinned;
        }

        /// <summary>
        /// Pins every direct child of the root whose name marks it as chrome. The root's own
        /// designed size comes from its anchors against the reference canvas, which is how the
        /// builders laid everything out.
        /// </summary>
        private static int PinChildren(RectTransform root, Vector2 reference)
        {
            Vector2 rootSize = new Vector2(
                (root.anchorMax.x - root.anchorMin.x) * reference.x + root.sizeDelta.x,
                (root.anchorMax.y - root.anchorMin.y) * reference.y + root.sizeDelta.y);

            if (rootSize.x <= 0f || rootSize.y <= 0f)
            {
                // The root itself is fixed-size (a prefab root pinned by hand); its own rect is
                // the design space.
                rootSize = root.rect.size;
            }

            int pinned = 0;
            foreach (Transform child in root.transform)
            {
                if (!(child is RectTransform rect) || !WidgetNames.Contains(child.name))
                {
                    continue;
                }

                if (PinWidget(rect, rootSize))
                {
                    pinned++;
                }
            }

            return pinned;
        }

        private static bool PinWidget(RectTransform rect, Vector2 rootSize)
        {
            // Equal min and max anchors means it is already fixed-size: pinned, or placed by
            // hand. Either way it is not stretched, which is all this tool is for.
            if (Mathf.Approximately(rect.anchorMin.x, rect.anchorMax.x)
                && Mathf.Approximately(rect.anchorMin.y, rect.anchorMax.y))
            {
                return false;
            }

            // The widget's designed pixel box, exactly as it renders at the reference aspect.
            float x0 = rect.anchorMin.x * rootSize.x + rect.offsetMin.x;
            float x1 = rect.anchorMax.x * rootSize.x + rect.offsetMax.x;
            float y0 = rect.anchorMin.y * rootSize.y + rect.offsetMin.y;
            float y1 = rect.anchorMax.y * rootSize.y + rect.offsetMax.y;

            Vector2 size = new Vector2(x1 - x0, y1 - y0);
            Vector2 center = new Vector2((x0 + x1) * 0.5f, (y0 + y1) * 0.5f);

            // Nearest corner (or edge middle): a widget in the left third belongs to the left
            // edge, right third to the right, middle stays centred; same for height.
            float anchorX = Snap(center.x / rootSize.x);
            float anchorY = Snap(center.y / rootSize.y);

            Undo.RecordObject(rect, "Pin corner widget");
            rect.anchorMin = new Vector2(anchorX, anchorY);
            rect.anchorMax = new Vector2(anchorX, anchorY);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = new Vector2(
                center.x - (anchorX * rootSize.x),
                center.y - (anchorY * rootSize.y));
            return true;
        }

        private static float Snap(float fraction)
        {
            if (fraction < 1f / 3f)
            {
                return 0f;
            }

            return fraction > 2f / 3f ? 1f : 0.5f;
        }

        private static Vector2 ResolveReferenceResolution()
        {
            CanvasScaler scaler = Object.FindFirstObjectByType<CanvasScaler>(FindObjectsInactive.Include);
            return scaler != null && scaler.referenceResolution.x > 0f
                ? scaler.referenceResolution
                : new Vector2(720f, 1280f);
        }
    }
}
#endif
