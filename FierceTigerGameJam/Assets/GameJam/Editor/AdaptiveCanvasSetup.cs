#if UNITY_EDITOR
using GameJam.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace GameJam.EditorTools
{
    /// <summary>
    /// Puts an <see cref="AdaptiveCanvasScaler"/> on every canvas in the open scene that scales
    /// with screen size, and takes the column limiter experiment back off: squeezing the whole
    /// interface into a 9:16 column crammed the corner chips into the panel, so wide screens are
    /// left full-stretch instead - the chips keep their authored fractional margins from the
    /// outer edges, which scale with the screen.
    ///
    /// One menu item rather than a hand edit, and re-running it is harmless: what is already
    /// right is left exactly as it is.
    /// </summary>
    public static class AdaptiveCanvasSetup
    {
        [MenuItem("Tools/Smashdown/Setup Adaptive Canvas")]
        public static void Setup()
        {
            int changed = 0;

            CanvasScaler[] scalers = Object.FindObjectsByType<CanvasScaler>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            foreach (CanvasScaler scaler in scalers)
            {
                if (scaler.uiScaleMode != CanvasScaler.ScaleMode.ScaleWithScreenSize)
                {
                    continue;
                }

                if (scaler.GetComponent<AdaptiveCanvasScaler>() == null)
                {
                    Undo.AddComponent<AdaptiveCanvasScaler>(scaler.gameObject);
                    changed++;
                }

                CanvasColumnLimiter limiter = scaler.GetComponent<CanvasColumnLimiter>();
                if (limiter != null)
                {
                    Undo.DestroyObjectImmediate(limiter);
                    changed += ClearColumnMargins(scaler.transform);
                    changed++;
                }
            }

            if (changed > 0)
            {
                EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            }

            Debug.Log(changed > 0
                ? $"Adaptive canvas setup: {changed} change(s) made. Save the scene to keep them."
                : "Every scaling canvas in this scene is already set up; nothing to change.");
        }

        /// <summary>
        /// The limiter worked by writing side margins into the screens' offsets, so a scene
        /// saved while it was active keeps those margins after the component is gone. Zeroing
        /// them here restores the authored full-stretch layout.
        /// </summary>
        private static int ClearColumnMargins(Transform canvas)
        {
            int cleared = 0;
            for (int i = 0; i < canvas.childCount; i++)
            {
                if (!(canvas.GetChild(i) is RectTransform child))
                {
                    continue;
                }

                if (!Mathf.Approximately(child.anchorMin.x, 0f) || !Mathf.Approximately(child.anchorMax.x, 1f))
                {
                    continue;
                }

                if (child.offsetMin.x == 0f && child.offsetMax.x == 0f)
                {
                    continue;
                }

                Undo.RecordObject(child, "Clear column margins");
                child.offsetMin = new Vector2(0f, child.offsetMin.y);
                child.offsetMax = new Vector2(0f, child.offsetMax.y);
                cleared++;
            }

            return cleared;
        }
    }
}
#endif
