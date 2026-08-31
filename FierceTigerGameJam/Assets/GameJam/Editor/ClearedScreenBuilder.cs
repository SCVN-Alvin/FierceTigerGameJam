#if UNITY_EDITOR
using GameJam.Gameplay.Flow;
using GameJam.Gameplay.Wall;
using GameJam.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace GameJam.EditorTools
{
    /// <summary>
    /// Authors the cleared screen - what a passed run comes to - out of the supplied art, and puts
    /// an instance of it in the scene beside the fail screen, which is the other half of the same
    /// question.
    ///
    /// Built the way the garage is, and for the same reasons: a prefab is the one description of
    /// the screen, the scene holds nothing but an instance of it, and nothing is written twice.
    /// Every position, component setting and reference below is filled in only when the thing it
    /// belongs to did not already exist, because these numbers are a starting point and the prefab
    /// is where the screen is actually tuned. A builder that rewrote them every run would make
    /// re-running it cost the tuning, which is the same as not being able to run it again.
    /// Deleting the prefab is how you ask for the numbers below back.
    ///
    /// The create-only helpers at the bottom are this builder's own rather than shared with
    /// <see cref="GarageScreenBuilder"/>: each screen builder owns its idempotence, and the
    /// pieces worth sharing - the plain ensure-and-return helpers - already live on
    /// <see cref="UiBuilder"/> and are used from there.
    /// </summary>
    public static class ClearedScreenBuilder
    {
        private const string UiTextures = "Assets/GameJam/Textures/UI";
        private const string WinTextures = UiTextures + "/WinScreen";

        private const string BadgeSprite = WinTextures + "/UI_Clear_Badge.png";
        private const string CoinSprite = UiTextures + "/Common/UI_Coin.png";
        private const string ReplaySprite = WinTextures + "/Btn_Retry_Long.png";
        private const string ContinueSprite = WinTextures + "/Btn_Continue_Long.png";
        private const string CloseSprite = UiTextures + "/Garage/Btn_Esc.png";

        private const string ResultFolder = "Assets/GameJam/Prefabs/UI/Result";
        internal const string ScreenPrefabPath = ResultFolder + "/ClearedScreen.prefab";
        internal const string ScreenName = "ClearedScreen";

        /// <summary>
        /// Where a map's picture goes when it is drawn, by id. Nothing here has to exist: no map
        /// has art yet, and a map whose file is missing keeps its empty slot and shows no picture.
        /// </summary>
        private const string MapPictureFolder = "Assets/GameJam/Textures/Maps";

        /// <summary>
        /// The badge at its sprite's own aspect (814x610), hung under the top edge. Fixed rather
        /// than stretched: the art has one shape, and a taller phone should leave more room under
        /// the banner, not a taller banner.
        /// </summary>
        private static readonly Vector2 BadgeSize = new Vector2(482f, 361f);
        private static readonly Vector2 BadgeOffset = new Vector2(0f, -80f);

        /// <summary>84x89 of coin art, at the size the digits beside it are set in.</summary>
        private static readonly Vector2 CoinSize = new Vector2(50f, 53f);

        /// <summary>The gold the reward is written in, taken off the mock-up.</summary>
        private static readonly Color RewardColor = new Color32(0xFF, 0xC6, 0x1A, 0xFF);

        [MenuItem("Tools/Smashdown/Build Cleared Screen")]
        public static void BuildClearedScreen()
        {
            EnsureFolder(ResultFolder);

            GameObject screen = BuildScreenPrefab();

            FillMissingMapPictures();
            EnsureSceneInstance(screen);

            AssetDatabase.SaveAssets();
            Debug.Log(
                "Built the cleared screen into " + ScreenPrefabPath
                + " and put an instance of it in the scene. Save the scene to keep the wiring.");
        }

        // ------------------------------------------------------------------ screen

        /// <summary>
        /// The whole screen. The words on the banner and on both buttons are painted into the art,
        /// so what is built here is the dim behind it, the reward row, the map's picture and the
        /// three things that can be tapped.
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

                // The dark ground, and the one image here that takes input: the structure is still
                // standing behind the screen and the cannon still listens for a drag, so without
                // something over it a tap meant for REPLAY would also be a shot.
                UiBuilder.EnsureBackdrop(rect);

                RemoveRetiredDim(rect);

                RectTransform badge = EnsureImage("Badge", rect, BadgeSprite,
                    new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    Image.Type.Simple, true, false, out bool badgeCreated);
                if (badgeCreated)
                {
                    // The sprite carries big transparent margins above and below the ribbon, which
                    // is why the badge is placed by its own top edge and given the art's size
                    // rather than being fitted to a box.
                    badge.pivot = new Vector2(0.5f, 1f);
                    badge.sizeDelta = BadgeSize;
                    badge.anchoredPosition = BadgeOffset;
                }

                BuildReward(rect, out GameObject rewardRoot, out TMP_Text rewardLabel);

                RectTransform mapImage = EnsureImage("MapImage", rect, null,
                    new Vector2(0.226f, 0.229f), new Vector2(0.777f, 0.635f),
                    Image.Type.Simple, true);

                EnsureSpriteButton("ReplayButton", rect, ReplaySprite,
                    new Vector2(0.132f, 0.085f), new Vector2(0.473f, 0.168f));
                EnsureSpriteButton("ContinueButton", rect, ContinueSprite,
                    new Vector2(0.527f, 0.085f), new Vector2(0.868f, 0.168f));
                EnsureSpriteButton("CloseButton", rect, CloseSprite,
                    new Vector2(0.789f, 0.906f), new Vector2(0.865f, 0.953f));

                // The flow is a scene object, so it is wired on the instance and not here.
                ClearedScreenView view = UiBuilder.Ensure<ClearedScreenView>(root);
                SerializedObject serialized = new SerializedObject(view);
                UiBuilder.SetIfEmpty(serialized, "mapConfig", UiBuilder.LoadFirstAsset<MapConfig>());
                UiBuilder.SetIfEmpty(serialized, "mapImage", mapImage.GetComponent<Image>());
                UiBuilder.SetIfEmpty(serialized, "rewardRoot", rewardRoot);
                UiBuilder.SetIfEmpty(serialized, "rewardLabel", rewardLabel);
                serialized.ApplyModifiedPropertiesWithoutUndo();

                if (created)
                {
                    // Inactive in the prefab, not only on the instance: the flow switches screens
                    // on, and a screen that ships active would be an override on every instance of
                    // it. Only on the first build - whoever turned it back on to look at the
                    // layout meant to, and the flow hides it on entry either way.
                    root.SetActive(false);
                }
            });
        }

        /// <summary>
        /// Coin and amount on one line. A layout group rather than two placed boxes, so the row
        /// stays centred whether the reward is two digits or four.
        /// </summary>
        private static void BuildReward(RectTransform parent, out GameObject rewardRoot, out TMP_Text amount)
        {
            RectTransform reward = UiBuilder.EnsureRect("Reward", parent,
                new Vector2(0.30f, 0.696f), new Vector2(0.70f, 0.743f));
            rewardRoot = reward.gameObject;

            HorizontalLayoutGroup layout = EnsureComponent<HorizontalLayoutGroup>(rewardRoot, out bool layoutCreated);
            if (layoutCreated)
            {
                layout.spacing = 12f;
                layout.childAlignment = TextAnchor.MiddleCenter;
                layout.childControlWidth = true;
                layout.childControlHeight = true;

                // Off, so the coin keeps its size and the row is only as wide as its contents,
                // which is what lets the alignment above centre it.
                layout.childForceExpandWidth = false;
                layout.childForceExpandHeight = false;
            }

            RectTransform coin = EnsureImage("Coin", reward, CoinSprite, Vector2.zero, Vector2.one,
                Image.Type.Simple, false);

            LayoutElement coinSize = EnsureComponent<LayoutElement>(coin.gameObject, out bool coinSizeCreated);
            if (coinSizeCreated)
            {
                coinSize.preferredWidth = CoinSize.x;
                coinSize.preferredHeight = CoinSize.y;
            }

            amount = EnsureLabel("Amount", reward, "+200", 44, TextAlignmentOptions.Left,
                Vector2.zero, Vector2.one, out bool amountCreated);
            if (amountCreated)
            {
                amount.fontStyle = FontStyles.Bold;
                amount.color = RewardColor;
                amount.raycastTarget = false;
            }
        }

        // ------------------------------------------------------------------ map pictures

        /// <summary>
        /// Fills in map art that has been drawn since the last run, by the naming convention and
        /// nothing else, so a new picture never has to be dragged onto a map by hand. A picture
        /// already set is left alone, and a file that does not exist is not complained about: none
        /// of them exist yet, and a warning per map would say nothing useful.
        /// </summary>
        private static void FillMissingMapPictures()
        {
            string[] guids = AssetDatabase.FindAssets("t:MapConfig");
            for (int i = 0; i < guids.Length; i++)
            {
                MapConfig config = AssetDatabase.LoadAssetAtPath<MapConfig>(
                    AssetDatabase.GUIDToAssetPath(guids[i]));
                if (config == null)
                {
                    continue;
                }

                SerializedObject serialized = new SerializedObject(config);
                SerializedProperty maps = serialized.FindProperty("maps");
                if (maps == null)
                {
                    continue;
                }

                bool changed = false;
                for (int map = 0; map < maps.arraySize; map++)
                {
                    SerializedProperty entry = maps.GetArrayElementAtIndex(map);
                    SerializedProperty id = entry.FindPropertyRelative("id");
                    SerializedProperty picture = entry.FindPropertyRelative("clearedImage");
                    if (id == null || picture == null
                        || picture.objectReferenceValue != null
                        || string.IsNullOrEmpty(id.stringValue))
                    {
                        continue;
                    }

                    Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(
                        $"{MapPictureFolder}/{id.stringValue}.png");
                    if (sprite == null)
                    {
                        continue;
                    }

                    picture.objectReferenceValue = sprite;
                    changed = true;
                }

                if (changed)
                {
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(config);
                }
            }
        }

        // ------------------------------------------------------------------ scene

        /// <summary>
        /// Puts the cleared screen into the scene alongside the fail screen and points the flow
        /// at it. The fail screen is left exactly where it is: the two are switched between by
        /// HandleRunFinished, and neither builder has any business moving the other's instance.
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
                Debug.LogWarning("The cleared screen prefab was built, but there is no loaded scene to put it in.");
                return;
            }

            Canvas canvas = Object.FindFirstObjectByType<Canvas>(FindObjectsInactive.Include);
            if (canvas == null)
            {
                Debug.LogWarning("The cleared screen prefab was built, but the scene has no Canvas to put it under.");
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

                // The prefab root is already stretched and already inactive, so this only restates
                // what the instance inherited and leaves it with no overrides of its own. An
                // instance that was already in the scene is not touched at all: it may have been
                // moved on purpose, and the flow switches it on and off whatever state it was
                // saved in.
                RectTransform rect = (RectTransform)instance.transform;
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
                instance.SetActive(false);
            }

            Button replay = FindButton(instance.transform, "ReplayButton");
            Button continueButton = FindButton(instance.transform, "ContinueButton");
            Button close = FindButton(instance.transform, "CloseButton");

            GameFlowController flow = Object.FindFirstObjectByType<GameFlowController>(FindObjectsInactive.Include);
            if (flow == null)
            {
                Debug.LogWarning(
                    "The cleared screen is in the scene but there is no GameFlowController to wire it to, so "
                    + "a passed run will not show it.");
                EditorSceneManager.MarkSceneDirty(scene);
                return;
            }

            SerializedObject serializedFlow = new SerializedObject(flow);
            UiBuilder.SetIfEmpty(serializedFlow, "clearedRoot", instance);
            UiBuilder.SetIfEmpty(serializedFlow, "clearedReplayButton", replay);
            UiBuilder.SetIfEmpty(serializedFlow, "clearedContinueButton", continueButton);
            UiBuilder.SetIfEmpty(serializedFlow, "clearedCloseButton", close);
            serializedFlow.ApplyModifiedPropertiesWithoutUndo();

            ClearedScreenView view = instance.GetComponent<ClearedScreenView>();
            if (view != null)
            {
                SerializedObject serializedView = new SerializedObject(view);
                UiBuilder.SetIfEmpty(serializedView, "flow", flow);
                serializedView.ApplyModifiedPropertiesWithoutUndo();
            }

            EditorSceneManager.MarkSceneDirty(scene);
        }

        private static Button FindButton(Transform parent, string name)
        {
            Transform found = parent.Find(name);
            return found != null ? found.GetComponent<Button>() : null;
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

        /// <summary>A component: added and configured, or found and left as it is.</summary>
        private static T EnsureComponent<T>(GameObject target, out bool created) where T : Component
        {
            created = target.GetComponent<T>() == null;
            return UiBuilder.Ensure<T>(target);
        }

        /// <summary>
        /// Clears the old Dim. It was the tutorial's Filter.png, which carries a transparent
        /// ellipse punched through the middle for the spotlight - never what this screen wanted -
        /// and it has been sitting here disabled, blocking nothing, because the importer left that
        /// file unloadable as a sprite. The backdrop above is what it was always meant to be.
        /// </summary>
        private static void RemoveRetiredDim(RectTransform rect)
        {
            Transform dim = rect.Find("Dim");
            if (dim != null)
            {
                Object.DestroyImmediate(dim.gameObject);
            }
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

                // Off unless the caller says otherwise: decoration must never eat a tap meant for
                // something behind it. Only the dim and the three buttons take input.
                image.raycastTarget = raycastTarget;

                // An image with no sprite is a white block, which is what the map picture would be
                // on every map until the art lands. The view turns it back on when it has
                // something to put there.
                image.enabled = image.sprite != null;
            }

            return rect;
        }

        /// <summary>
        /// A label, configured only when it is new, so a font size or a colour set in the inspector
        /// is not undone by the next run.
        /// </summary>
        private static TMP_Text EnsureLabel(
            string name,
            Transform parent,
            string text,
            int size,
            TextAlignmentOptions alignment,
            Vector2 anchorMin,
            Vector2 anchorMax,
            out bool created)
        {
            Transform found = parent.Find(name);
            created = found == null || found.GetComponent<TMP_Text>() == null;
            return UiBuilder.EnsureLabel(name, parent, text, size, alignment, anchorMin, anchorMax);
        }

        /// <summary>
        /// A button whose caption is painted into its sprite. preserveAspect on creation only, so
        /// the long buttons keep the shape they were drawn at whatever box they are given.
        /// </summary>
        private static Button EnsureSpriteButton(
            string name,
            Transform parent,
            string spritePath,
            Vector2 anchorMin,
            Vector2 anchorMax)
        {
            Transform found = parent.Find(name);
            bool created = found == null || found.GetComponent<Button>() == null;

            Button button = UiBuilder.EnsureSpriteButton(name, parent, spritePath, anchorMin, anchorMax);
            if (!created)
            {
                return button;
            }

            Place((RectTransform)button.transform, anchorMin, anchorMax);

            if (button.targetGraphic is Image image)
            {
                image.preserveAspect = true;
            }

            return button;
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
