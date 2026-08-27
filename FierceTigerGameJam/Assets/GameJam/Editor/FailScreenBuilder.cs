#if UNITY_EDITOR
using GameJam.Economy;
using GameJam.Gameplay.Flow;
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
    /// Authors the fail screen - the one question a lost run asks - out of the supplied art, puts
    /// an instance of it in the scene in place of the old ResultScreen, and takes that screen out
    /// of the project behind it.
    ///
    /// Built the way the garage and the cleared screen are, and for the same reasons: a prefab is
    /// the one description of the screen, the scene holds nothing but an instance of it, and
    /// nothing is written twice. Every position, component setting and reference below is filled
    /// in only when the thing it belongs to did not already exist, because these numbers are a
    /// starting point and the prefab is where the screen is actually tuned. A builder that rewrote
    /// them every run would make re-running it cost the tuning, which is the same as not being
    /// able to run it again. Deleting the prefab is how you ask for the numbers below back.
    ///
    /// The create-only helpers at the bottom are this builder's own rather than shared with
    /// <see cref="ClearedScreenBuilder"/> or <see cref="GarageScreenBuilder"/>: each screen builder
    /// owns its idempotence, and the pieces worth sharing - the plain ensure-and-return helpers -
    /// already live on <see cref="UiBuilder"/> and are used from there.
    /// </summary>
    public static class FailScreenBuilder
    {
        private const string UiTextures = "Assets/GameJam/Textures/UI";
        private const string LoseTextures = UiTextures + "/LoseScreen";

        private const string DimSprite = UiTextures + "/Tutorial/Filter.png";
        private const string TitleSprite = LoseTextures + "/UI_Continue_img.png";
        private const string BannerSprite = LoseTextures + "/UI_Banner_PlusAmmo.png";
        private const string CoinSprite = UiTextures + "/Common/UI_Coin.png";
        private const string CloseSprite = UiTextures + "/Garage/Btn_Esc.png";

        /// <summary>
        /// The long blank price button the mock-up is drawn around. It has not been supplied yet,
        /// so the builder falls back to the garage's buy button, which is the same green in a
        /// smaller box and carries its own coin. The two are interchangeable in the layout: the
        /// button's rect is the same either way, and the only difference is the sprite, how it is
        /// drawn and whether the separate coin child is needed at all.
        /// </summary>
        private const string PriceButtonSprite = LoseTextures + "/Btn_Price_Long.png";
        private const string PriceButtonFallbackSprite = UiTextures + "/Garage/Btn_Buy.png";

        private const string ResultFolder = "Assets/GameJam/Prefabs/UI/Result";
        private const string ScreenPrefabPath = ResultFolder + "/FailScreen.prefab";
        private const string ScreenName = "FailScreen";

        /// <summary>
        /// The screen this one replaces. Both the instance under the Canvas and the asset go: two
        /// descriptions of "what a run came to" is exactly the confusion Brief 06 was written to
        /// stop, and a prefab nobody instantiates is the kind of thing that gets edited by mistake
        /// a month later.
        /// </summary>
        private const string OldScreenName = "ResultScreen";
        private const string OldScreenPrefabPath = "Assets/GameJam/Prefabs/UI/ResultScreen/ResultScreen.prefab";

        [MenuItem("Tools/Smashdown/Build Fail Screen")]
        public static void BuildFailScreen()
        {
            EnsureFolder(ResultFolder);

            // Before the prefab, because the view is wired to the economy and the economy is only
            // worth wiring once it knows what a continue costs.
            GameConfigBuilder.EnsureLoseConfig();

            GameObject screen = BuildScreenPrefab();

            EnsureSceneInstance(screen);

            // Last, and only once the scene has stopped naming it: deleting an asset the open
            // scene still holds an instance of leaves that instance as a broken prefab link.
            DeleteOldScreen();

            AssetDatabase.SaveAssets();
            Debug.Log(
                "Built the fail screen into " + ScreenPrefabPath
                + " and put an instance of it in the scene in place of " + OldScreenName
                + ". Save the scene to keep the wiring.");
        }

        // ------------------------------------------------------------------ screen

        /// <summary>
        /// The whole screen. "Continue?" and the "+5 - Add 5 ammo to continue!" banner are painted
        /// into the art, so what is built here is the dim behind them, the two pictures, the
        /// priced button and the way out.
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

                // The one image here that takes input. The structure is still standing behind the
                // screen and the cannon still listens for a drag, so without something over it a
                // tap meant for the price button would also be a shot at nothing.
                EnsureImage("Dim", rect, DimSprite, Vector2.zero, Vector2.one,
                    Image.Type.Simple, false, true);

                EnsureImage("Title", rect, TitleSprite,
                    new Vector2(0.12f, 0.74f), new Vector2(0.88f, 0.88f),
                    Image.Type.Simple, true);

                EnsureImage("Banner", rect, BannerSprite,
                    new Vector2(0.171f, 0.365f), new Vector2(0.837f, 0.72f),
                    Image.Type.Simple, true);

                Button continueButton = BuildContinueButton(rect, out TMP_Text priceLabel);

                EnsureSpriteButton("CloseButton", rect, CloseSprite,
                    new Vector2(0.789f, 0.906f), new Vector2(0.865f, 0.953f));

                // The economy is an asset, so unlike the cleared screen's flow reference it can be
                // wired here rather than on the instance.
                FailScreenView view = UiBuilder.Ensure<FailScreenView>(root);
                SerializedObject serialized = new SerializedObject(view);
                UiBuilder.SetIfEmpty(serialized, "economy", UiBuilder.LoadFirstAsset<EconomyService>());
                UiBuilder.SetIfEmpty(serialized, "continueButton", continueButton);
                UiBuilder.SetIfEmpty(serialized, "priceLabel", priceLabel);
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
        /// The priced button, with room for a coin beside the digits whichever sprite it gets. The
        /// coin child is built in both cases so that dropping the long blank button in later is a
        /// sprite swap and a checkbox rather than a re-layout; the fallback simply switches it off,
        /// because that art already has a coin painted into it and two would read as a bug.
        /// </summary>
        private static Button BuildContinueButton(RectTransform parent, out TMP_Text priceLabel)
        {
            bool hasLongSprite = AssetDatabase.LoadAssetAtPath<Sprite>(PriceButtonSprite) != null;
            string spritePath = hasLongSprite ? PriceButtonSprite : PriceButtonFallbackSprite;

            Transform found = parent.Find("ContinueButton");
            bool created = found == null || found.GetComponent<Button>() == null;

            Button button = UiBuilder.EnsureSpriteButton("ContinueButton", parent, spritePath,
                new Vector2(0.303f, 0.219f), new Vector2(0.693f, 0.304f));
            RectTransform rect = (RectTransform)button.transform;

            if (created)
            {
                Place(rect, new Vector2(0.303f, 0.219f), new Vector2(0.693f, 0.304f));

                if (button.targetGraphic is Image image)
                {
                    // Sliced only for the long button, which is drawn to be stretched. The buy
                    // button is 145x51 with its coin baked in at a fixed spot, so stretching it
                    // would pull that coin out of shape; it is drawn at its own aspect instead.
                    image.type = hasLongSprite ? Image.Type.Sliced : Image.Type.Simple;
                    image.preserveAspect = !hasLongSprite;
                }

                button.transition = Selectable.Transition.ColorTint;
            }

            RectTransform coin = EnsureImage("Coin", rect, CoinSprite,
                new Vector2(0.08f, 0.15f), new Vector2(0.32f, 0.85f),
                Image.Type.Simple, true, false, out bool coinCreated);
            if (coinCreated)
            {
                coin.gameObject.SetActive(hasLongSprite);
            }

            priceLabel = EnsureLabel("Price", rect, "4,000", 40, TextAlignmentOptions.Center,
                new Vector2(0.34f, 0.05f), new Vector2(0.96f, 0.95f), out bool priceCreated);
            if (priceCreated)
            {
                priceLabel.fontStyle = FontStyles.Bold;
                priceLabel.color = Color.white;
                priceLabel.raycastTarget = false;
            }

            return button;
        }

        // ------------------------------------------------------------------ scene

        /// <summary>
        /// Puts the fail screen into the scene where the old result panel was and points the flow
        /// at it. The old instance is destroyed first, so that the flow's failRoot is genuinely
        /// empty by the time it is filled in and there is no moment where both screens exist.
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
                Debug.LogWarning("The fail screen prefab was built, but there is no loaded scene to put it in.");
                return;
            }

            Canvas canvas = Object.FindFirstObjectByType<Canvas>(FindObjectsInactive.Include);
            if (canvas == null)
            {
                Debug.LogWarning("The fail screen prefab was built, but the scene has no Canvas to put it under.");
                return;
            }

            DestroyOldSceneInstance(canvas.transform);

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

            Button continueButton = FindButton(instance.transform, "ContinueButton");
            Button close = FindButton(instance.transform, "CloseButton");

            GameFlowController flow = Object.FindFirstObjectByType<GameFlowController>(FindObjectsInactive.Include);
            if (flow == null)
            {
                Debug.LogWarning(
                    "The fail screen is in the scene but there is no GameFlowController to wire it to, so "
                    + "a failed run will not show it.");
                EditorSceneManager.MarkSceneDirty(scene);
                return;
            }

            SerializedObject serializedFlow = new SerializedObject(flow);
            UiBuilder.SetIfEmpty(serializedFlow, "failRoot", instance);
            UiBuilder.SetIfEmpty(serializedFlow, "failContinueButton", continueButton);
            UiBuilder.SetIfEmpty(serializedFlow, "failCloseButton", close);
            UiBuilder.SetIfEmpty(serializedFlow, "economy", UiBuilder.LoadFirstAsset<EconomyService>());
            serializedFlow.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
        }

        /// <summary>
        /// Takes the old result panel out of the scene, instance and all. Destroyed rather than
        /// left switched off: the flow no longer has a field that names it, so anything left
        /// behind is a screen nothing can ever show and nothing can ever hide.
        /// </summary>
        private static void DestroyOldSceneInstance(Transform canvas)
        {
            Transform old = canvas.Find(OldScreenName);
            if (old == null)
            {
                return;
            }

            Object.DestroyImmediate(old.gameObject);
        }

        /// <summary>
        /// Deletes the old result prefab, after the scene has stopped naming it. Its view script is
        /// deleted from source in the same change, so what is removed here is the last thing in the
        /// project that still described the screen.
        /// </summary>
        private static void DeleteOldScreen()
        {
            if (AssetDatabase.LoadAssetAtPath<Object>(OldScreenPrefabPath) != null)
            {
                AssetDatabase.DeleteAsset(OldScreenPrefabPath);
            }
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
                // something behind it. Only the dim and the two buttons take input.
                image.raycastTarget = raycastTarget;

                // An image with no sprite is a white block over the middle of the screen, which is
                // what a missing texture would look like rather than like a missing texture.
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
        /// A button whose whole look is its sprite. preserveAspect on creation only, so the X keeps
        /// the shape it was drawn at whatever box it is given.
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
