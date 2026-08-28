#if UNITY_EDITOR
using GameJam.Config;
using GameJam.Gameplay.Flow;
using GameJam.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace GameJam.EditorTools
{
    /// <summary>
    /// Authors the loading screen - the splash every launch opens on - out of the supplied art and
    /// puts an instance of it in the scene under the Canvas.
    ///
    /// Built the way the garage, the cleared screen and the fail screen are, and for the same
    /// reasons: the prefab is the one description of the screen, the scene holds nothing but an
    /// instance of it, and every number below is written only when the thing it belongs to did not
    /// already exist. These are a starting point; the prefab is where the screen is tuned, and a
    /// builder that rewrote them every run would make re-running it cost the tuning. Deleting the
    /// prefab is how you ask for the numbers below back.
    /// </summary>
    public static class LoadingScreenBuilder
    {
        private const string LoadingTextures = "Assets/GameJam/Textures/UI/Loading";

        private const string SplashSprite = LoadingTextures + "/SplashScreen.png";
        private const string LabelSprite = LoadingTextures + "/img_loading.png";
        private const string BarBaseSprite = LoadingTextures + "/UI_LoadingBar_Base.png";
        private const string BarFillSprite = LoadingTextures + "/UI_LoadingBar_Fill.png";

        private const string LoadingFolder = "Assets/GameJam/Prefabs/UI/Loading";

        public const string ScreenPrefabPath = LoadingFolder + "/LoadingScreen.prefab";
        public const string ScreenName = "LoadingScreen";

        /// <summary>
        /// The splash art's own shape, 1216x2160. The background is fitted to it rather than
        /// stretched to the screen: a phone that is not this aspect should be shown less of the
        /// picture, not a picture pulled out of shape.
        /// </summary>
        private const float SplashAspect = 1216f / 2160f;

        [MenuItem("Tools/Smashdown/Build Loading Screen")]
        public static void BuildLoadingScreen()
        {
            EnsureFolder(LoadingFolder);

            // Before the prefab, because the view is wired to the config and there is nothing to
            // wire until the asset exists.
            GameConfigBuilder.EnsureLoadingConfig();

            GameObject screen = BuildScreenPrefab();
            EnsureSceneInstance(screen);

            AssetDatabase.SaveAssets();
            Debug.Log(
                "Built the loading screen into " + ScreenPrefabPath
                + " and put an instance of it last under the Canvas. Save the scene to keep the wiring.");
        }

        // ------------------------------------------------------------------ screen

        /// <summary>
        /// The whole screen: the splash behind everything, the "Loading..." word art, and the bar
        /// as a tube with a fill inside it.
        /// </summary>
        private static GameObject BuildScreenPrefab()
        {
            return EnsurePrefab(ScreenPrefabPath, ScreenName, (root, created) =>
            {
                RectTransform rect = (RectTransform)root.transform;
                if (created)
                {
                    Place(rect, Vector2.zero, Vector2.one);
                }

                BuildBackground(rect);

                EnsureImage("LoadingLabel", rect, LabelSprite,
                    new Vector2(0.40f, 0.171f), new Vector2(0.60f, 0.201f),
                    Image.Type.Simple, true);

                Image fill = BuildBar(rect);

                LoadingScreenView view = UiBuilder.Ensure<LoadingScreenView>(root);
                SerializedObject serialized = new SerializedObject(view);

                // The config is an asset, so it can be wired here. The flow is a scene object and
                // is wired on the instance instead - a prefab cannot hold a reference into a scene.
                UiBuilder.SetIfEmpty(serialized, "config", UiBuilder.LoadFirstAsset<LoadingConfig>());
                UiBuilder.SetIfEmpty(serialized, "fill", fill);
                serialized.ApplyModifiedPropertiesWithoutUndo();

                if (created)
                {
                    // Inactive in the prefab, not only on the instance: the flow switches screens
                    // on, and a screen that ships active would be an override on every instance of
                    // it. Start() enters Loading before the first frame is drawn, so the splash is
                    // still the first thing the player sees.
                    root.SetActive(false);
                }
            });
        }

        /// <summary>
        /// The splash, cropped rather than stretched. The fitter drives the rect itself once it is
        /// on, so the anchors here only decide what it starts from.
        /// </summary>
        private static void BuildBackground(RectTransform parent)
        {
            // The one image on this screen that takes input: while the splash is up every tap
            // belongs to it, which is what makes "no way to skip" true rather than merely unwired.
            RectTransform background = EnsureImage("Background", parent, SplashSprite,
                Vector2.zero, Vector2.one, Image.Type.Simple, false, true);

            if (background.GetComponent<AspectRatioFitter>() != null)
            {
                return;
            }

            AspectRatioFitter fitter = UiBuilder.Ensure<AspectRatioFitter>(background.gameObject);
            fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fitter.aspectRatio = SplashAspect;
        }

        /// <summary>
        /// The bar: a tube with the fill inside it. Two objects rather than one image with a
        /// background colour, because the fill has to be able to run short of the tube's ends
        /// without the tube moving with it.
        /// </summary>
        private static Image BuildBar(RectTransform parent)
        {
            Transform found = parent.Find("Bar");
            bool created = found == null;

            RectTransform bar = UiBuilder.EnsureRect("Bar", parent,
                new Vector2(0.247f, 0.116f), new Vector2(0.757f, 0.162f));
            if (created)
            {
                Place(bar, new Vector2(0.247f, 0.116f), new Vector2(0.757f, 0.162f));
            }

            EnsureImage("Base", bar, BarBaseSprite, Vector2.zero, Vector2.one,
                Image.Type.Simple, true);

            RectTransform fillRect = EnsureImage("Fill", bar, BarFillSprite, Vector2.zero, Vector2.one,
                Image.Type.Filled, false, false, out bool fillCreated);

            Image fill = fillRect.GetComponent<Image>();
            if (fillCreated && fill != null)
            {
                fill.fillMethod = Image.FillMethod.Horizontal;
                fill.fillOrigin = (int)Image.OriginHorizontal.Left;
                fill.fillAmount = 0f;

                // The brief's inset, straight from the art: the fill is 560x64 inside a 598x98
                // tube, so 19 and 17 pixels on each side. They are applied as UI units, and the
                // bar is drawn at roughly 0.6 of the art's size, so the yellow reads thinner here
                // than in the reference - about 25 units tall where the art wants 38. Left as the
                // brief specifies rather than rescaled, because it errs inwards and the prefab is
                // where the screen is tuned; the scaled equivalent is min (11.4, 10.2).
                fillRect.offsetMin = new Vector2(19f, 17f);
                fillRect.offsetMax = new Vector2(-19f, -17f);
            }

            return fill;
        }

        // ------------------------------------------------------------------ scene

        /// <summary>
        /// Puts the splash in the scene and points the flow at it. Last under the Canvas: it is
        /// the one screen that must have nothing drawn over it, and a screen added by another
        /// builder afterwards would otherwise sit on top of it, so re-running this puts it back at
        /// the end rather than only placing it there the first time.
        /// </summary>
        private static void EnsureSceneInstance(GameObject prefab)
        {
            if (prefab == null)
            {
                return;
            }

            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded)
            {
                Debug.LogWarning("The loading screen prefab was built, but there is no loaded scene to put it in.");
                return;
            }

            Canvas canvas = Object.FindFirstObjectByType<Canvas>(FindObjectsInactive.Include);
            if (canvas == null)
            {
                Debug.LogWarning("The loading screen prefab was built, but the scene has no Canvas to put it under.");
                return;
            }

            Transform existing = canvas.transform.Find(ScreenName);
            GameObject instance;
            if (existing != null)
            {
                instance = existing.gameObject;
            }
            else
            {
                instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, canvas.transform);
                instance.name = ScreenName;

                // Restates what the instance inherited from an already stretched, already inactive
                // prefab root, so it is left with no overrides of its own.
                RectTransform rect = (RectTransform)instance.transform;
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
                instance.SetActive(false);
            }

            if (instance.transform.GetSiblingIndex() != canvas.transform.childCount - 1)
            {
                instance.transform.SetAsLastSibling();
            }

            GameFlowController flow = Object.FindFirstObjectByType<GameFlowController>(FindObjectsInactive.Include);
            if (flow == null)
            {
                Debug.LogWarning(
                    "The loading screen is in the scene but there is no GameFlowController to wire it to, so "
                    + "the splash will never be shown and the bar will never hand the game on.");
                EditorSceneManager.MarkSceneDirty(scene);
                return;
            }

            LoadingScreenView view = instance.GetComponent<LoadingScreenView>();
            if (view != null)
            {
                SerializedObject serializedView = new SerializedObject(view);
                UiBuilder.SetIfEmpty(serializedView, "flow", flow);
                UiBuilder.SetIfEmpty(serializedView, "config", UiBuilder.LoadFirstAsset<LoadingConfig>());
                serializedView.ApplyModifiedPropertiesWithoutUndo();
            }

            SerializedObject serializedFlow = new SerializedObject(flow);
            UiBuilder.SetIfEmpty(serializedFlow, "loadingRoot", instance);
            serializedFlow.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>
        /// Builds a prefab, or runs the same steps over the one that is already there. Editing the
        /// existing asset rather than replacing it is what keeps a reference somebody dragged in by
        /// hand, and what keeps the guid the scene points at.
        /// </summary>
        private static GameObject EnsurePrefab(string path, string rootName, System.Action<GameObject, bool> build)
        {
            bool exists = AssetDatabase.LoadAssetAtPath<GameObject>(path) != null;

            GameObject root = exists
                ? PrefabUtility.LoadPrefabContents(path)
                : new GameObject(rootName, typeof(RectTransform));

            try
            {
                build(root, !exists);
                return PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                if (exists)
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
                else
                {
                    Object.DestroyImmediate(root);
                }
            }
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            int split = path.LastIndexOf('/');
            string parent = path.Substring(0, split);
            string leaf = path.Substring(split + 1);

            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        private static RectTransform EnsureImage(
            string name,
            Transform parent,
            string spritePath,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Image.Type type,
            bool preserveAspect,
            bool raycastTarget = false)
        {
            return EnsureImage(name, parent, spritePath, anchorMin, anchorMax, type, preserveAspect,
                raycastTarget, out bool _);
        }

        /// <summary>
        /// Finds or creates an image and, only if it had to make one, gives it the look this
        /// layout wants. An image somebody re-sliced or nudged is handed straight back.
        /// </summary>
        private static RectTransform EnsureImage(
            string name,
            Transform parent,
            string spritePath,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Image.Type type,
            bool preserveAspect,
            bool raycastTarget,
            out bool created)
        {
            Transform found = parent.Find(name);
            created = found == null || found.GetComponent<Image>() == null;

            RectTransform rect = UiBuilder.EnsureSpriteImage(name, parent, spritePath, anchorMin, anchorMax);
            if (!created)
            {
                return rect;
            }

            Place(rect, anchorMin, anchorMax);

            Image image = rect.GetComponent<Image>();
            if (image != null)
            {
                image.type = type;
                image.preserveAspect = preserveAspect;

                // Off unless the caller says otherwise: only the splash takes input, and it takes
                // all of it.
                image.raycastTarget = raycastTarget;

                // An image with no sprite is a white block over the middle of the screen, which is
                // what a missing texture would look like rather than like a missing texture.
                image.enabled = image.sprite != null;
            }

            return rect;
        }

        /// <summary>Anchors with no offsets: the whole layout is fractions of its parent.</summary>
        private static void Place(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
#endif
