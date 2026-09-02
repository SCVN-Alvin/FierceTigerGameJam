#if UNITY_EDITOR
using GameJam.Economy;
using GameJam.Gameplay.Flow;
using GameJam.Gameplay.Wall;
using GameJam.UI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace GameJam.EditorTools
{
    /// <summary>
    /// The parts of the interface that are made of the supplied art: the main menu, the bottom
    /// tab bar, the two shops, the settings overlay, and the in-run chrome.
    ///
    /// Positions are anchors read off the reference mock-ups rather than pixel offsets, so the
    /// layout holds its shape on a screen that is not the exact aspect the mock-ups were drawn at.
    /// </summary>
    public static partial class UiBuilder
    {
        private const string UiTextures = "Assets/GameJam/Textures/UI";
        private const string MenuTextures = UiTextures + "/MainMenu";

        private static readonly Color DimColor = new Color(0f, 0f, 0f, 0.6f);

        private static void BuildSpriteScreens(Transform canvas, GameFlowController flow, GameObject hud, EconomyService economy)
        {
            GameObject mainMenu = BuildMainMenu(
                canvas, out Button playButton, out Button settingsButton);
            GameObject bottomBar = BuildBottomBar(canvas, out Button iapButton, out Button homeButton, out Button wrenchButton);
            GameObject iapShop = BuildIapShop(canvas, economy);
            GameObject shop = BuildShop(canvas, out ShopTabsView shopTabs);
            GameObject settings = BuildSettings(canvas, out Button closeSettings, out Button settingsMainMenu);
            Button runSettingsButton = BuildRunChrome(hud);

            if (flow == null)
            {
                Debug.LogWarning($"{nameof(UiBuilder)} found no {nameof(GameFlowController)}, so the sprite screens are built but not wired.");
                return;
            }

            SerializedObject serialized = new SerializedObject(flow);
            SetIfEmpty(serialized, "mainMenuRoot", mainMenu);
            SetIfEmpty(serialized, "bottomBarRoot", bottomBar);
            SetIfEmpty(serialized, "iapShopRoot", iapShop);
            SetIfEmpty(serialized, "shopRoot", shop);
            SetIfEmpty(serialized, "shopTabs", shopTabs);
            SetIfEmpty(serialized, "settingsRoot", settings);
            SetIfEmpty(serialized, "playButton", playButton);
            SetIfEmpty(serialized, "iapShopButton", iapButton);
            SetIfEmpty(serialized, "homeButton", homeButton);
            SetIfEmpty(serialized, "shopButton", wrenchButton);
            SetIfEmpty(serialized, "openSettingsButton", settingsButton);
            SetIfEmpty(serialized, "openSettingsInRunButton", runSettingsButton);
            SetIfEmpty(serialized, "closeSettingsButton", closeSettings);
            SetIfEmpty(serialized, "settingsMainMenuButton", settingsMainMenu);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// Background, the two chips along the top, the gear, and PLAY. The chips sit at the top
        /// corners and the button near the bottom, matching the mock-up.
        ///
        /// The vehicle shop button is the one thing here the mock-ups do not draw: the tab bar's
        /// three slots are already spoken for, so it is a plain button above PLAY rather than a
        /// fourth slot invented on top of the supplied art. It is a way in, not a design.
        /// </summary>
        private static GameObject BuildMainMenu(
            Transform canvas,
            out Button playButton,
            out Button settingsButton)
        {
            RectTransform root = EnsureRect("MainMenuScreen", canvas, Vector2.zero, Vector2.one);
            EnsureSpriteImage("Background", root, $"{MenuTextures}/UI_MainMenu_BG.png", Vector2.zero, Vector2.one);

            RectTransform mission = EnsureSpriteImage("MissionChip", root, $"{MenuTextures}/UI_Mission.png",
                new Vector2(0.13f, 0.903f), new Vector2(0.35f, 0.957f));
            TMP_Text missionLabel = EnsureLabel("MissionLabel", mission, "0/0", 40, TextAlignmentOptions.Right,
                new Vector2(0.42f, 0.1f), new Vector2(0.92f, 0.9f));

            RectTransform money = EnsureSpriteImage("MoneyChip", root, $"{MenuTextures}/UI_Money.png",
                new Vector2(0.54f, 0.903f), new Vector2(0.77f, 0.957f));
            TMP_Text goldLabel = EnsureLabel("GoldLabel", money, "0", 40, TextAlignmentOptions.Center,
                new Vector2(0.2f, 0.1f), new Vector2(0.82f, 0.9f));

            settingsButton = EnsureSpriteButton("SettingsButton", root, $"{MenuTextures}/Btn_Setting.png",
                new Vector2(0.785f, 0.903f), new Vector2(0.87f, 0.957f));
            playButton = EnsureSpriteButton("PlayButton", root, $"{MenuTextures}/Btn_Play.png",
                new Vector2(0.30f, 0.196f), new Vector2(0.70f, 0.272f));
            // The stand-in VEHICLES button is gone: vehicles are a tab inside the one shop, so
            // the main menu is back to the three slots its art actually draws. Removed rather
            // than left hidden, so a rebuild does not resurrect it.
            DestroyIfPresent(root, "VehicleShopButton");

            MapProgressView progress = Ensure<MapProgressView>(mission.gameObject);
            SerializedObject progressObject = new SerializedObject(progress);
            SetIfEmpty(progressObject, "mapConfig", LoadFirstAsset<MapConfig>());
            SetIfEmpty(progressObject, "label", missionLabel);
            progressObject.ApplyModifiedPropertiesWithoutUndo();

            GoldView gold = Ensure<GoldView>(money.gameObject);
            SerializedObject goldObject = new SerializedObject(gold);
            SetIfEmpty(goldObject, "economy", LoadFirstAsset<EconomyService>());
            SetIfEmpty(goldObject, "goldLabel", goldLabel);
            goldObject.ApplyModifiedPropertiesWithoutUndo();

            return root.gameObject;
        }

        /// <summary>
        /// The tab bar. Its own root rather than a child of the menu, because it is also shown
        /// over the shops, and the flow switches it independently.
        ///
        /// There is nothing to lay out here any more. The bar is authored in
        /// Prefabs/UI/MainMenu/BottomBar.prefab by <see cref="BottomBarBuilder"/> - the flat
        /// strip, the three slots and the plate that raises the one the player is on - and all
        /// this does is make sure the scene holds an instance of it. The same move the garage
        /// made, for the same reason: two descriptions of one screen is how the last bug happened.
        /// </summary>
        private static GameObject BuildBottomBar(Transform canvas, out Button iapButton, out Button homeButton, out Button wrenchButton)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BottomBarBuilder.PrefabPath);

            if (prefab == null)
            {
                Debug.LogWarning(
                    "There is no BottomBar prefab yet, so the tab bar was left as it is. Run "
                    + "Tools > Smashdown > Build Bottom Bar, which authors it.");

                Transform authored = canvas.Find(BottomBarBuilder.RootName);
                iapButton = null;
                homeButton = null;
                wrenchButton = null;
                return authored != null ? authored.gameObject : null;
            }

            return BottomBarBuilder.EnsureSceneInstance(prefab, out iapButton, out homeButton, out wrenchButton);
        }

        private static GameObject BuildIapShop(Transform canvas, EconomyService economy)
        {
            RectTransform root = EnsureShopPanel("IapShopScreen", canvas, "GET GOLD", out RectTransform rows, out TMP_Text goldLabel);

            EnsureBackdrop(root);

            IapShopView view = Ensure<IapShopView>(root.gameObject);
            SerializedObject serialized = new SerializedObject(view);
            SetIfEmpty(serialized, "economy", economy);
            SetIfEmpty(serialized, "container", rows);
            SetIfEmpty(serialized, "goldLabel", goldLabel);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return root.gameObject;
        }

        /// <summary>
        /// The garage, which is the one shop gold is spent in. There is nothing to lay out here:
        /// the screen is authored in Prefabs/UI/Garage/GarageScreen.prefab by
        /// <see cref="GarageScreenBuilder"/>, and all this does is make sure the scene holds an
        /// instance of it.
        ///
        /// It used to build the whole thing into the scene, and that is what went wrong: the
        /// screens in this project are prefab instances, EnsureRect hands back an existing object
        /// without re-anchoring it, and everything the builder added then landed inside the
        /// prefab's own layout - an opaque background added last covering the screen it was meant
        /// to sit behind. Two descriptions of one screen is how that happened, so now there is
        /// one, and it is the prefab.
        /// </summary>
        private static GameObject BuildShop(Transform canvas, out ShopTabsView tabs)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(GarageScreenBuilder.ScreenPrefabPath);

            if (prefab == null)
            {
                Debug.LogWarning(
                    "There is no GarageScreen prefab yet, so the shop was left as it is. Run "
                    + "Tools > Smashdown > Build Garage Screen, which authors it.");

                Transform authored = canvas.Find(GarageScreenBuilder.ScreenName);
                tabs = authored != null ? authored.GetComponent<ShopTabsView>() : null;
                return authored != null ? authored.gameObject : null;
            }

            GarageScreenBuilder.EnsureSceneInstance(prefab, out tabs, out Button _);

            Transform garage = canvas.Find(GarageScreenBuilder.ScreenName);
            return garage != null ? garage.gameObject : null;
        }

        /// <summary>Removes a child an earlier layout created and this one no longer wants.</summary>
        private static void DestroyIfPresent(Transform parent, string name)
        {
            Transform existing = parent.Find(name);
            if (existing != null)
            {
                UnityEngine.Object.DestroyImmediate(existing.gameObject);
            }
        }

        /// <summary>
        /// A shop is a titled panel with a scrolling list and the player's gold in the corner, so
        /// both are built the same way and differ only in what fills the list. The bottom of the
        /// panel stops above the tab bar, which is on screen at the same time.
        /// </summary>
        /// <summary>
        /// The mission board's frame, reused as the panel behind the settings and the gold shop so
        /// the three read as one game rather than one authored screen and two placeholders. It is
        /// nine-sliced now, so it takes any panel shape without the corners stretching.
        /// </summary>
        private const string PanelFrameSprite = UiTextures + "/SelectMission/UI_Mission_Frame.png";

        /// <summary>
        /// Layer Lab's sky button, nine-sliced by its author. Chosen over the plain colour block
        /// the settings buttons used because MAIN MENU and CLOSE are the only two things on that
        /// panel a player can act on, and they were the least visible things on it.
        /// </summary>
        /// <summary>
        /// Shrinks the button art's borders so they fit the rect we give it.
        ///
        /// The Layer Lab button is a 64x225 strip whose borders consume the whole sprite - the
        /// stretchable middle is a single column and a single row, so it reproduces the button
        /// body by smearing one line of pixels. Their own prefab uses it at 374x225: stretched
        /// wide, never squashed short. Our settings button is about 383x92 in canvas units, so
        /// 225px of vertical border does not fit and the art would fold over itself. Raising the
        /// pixels-per-unit multiplier scales the borders down uniformly, which is the supported
        /// way to reuse a chunky nine-slice at a smaller size: at 3 the border needs 75 units and
        /// the button keeps roughly the reference proportions.
        /// </summary>
        private const float PanelButtonPixelsPerUnit = 3f;

        private const string PanelButtonSprite =
            "Assets/Layer Lab/GUI Pro-CasualGame/ResourcesData/Sprites/Components/Button/Button01_225_Sky.png";

        private static RectTransform EnsureShopPanel(
            string name,
            Transform canvas,
            string title,
            out RectTransform rows,
            out TMP_Text goldLabel)
        {
            RectTransform root = EnsureRect(name, canvas, new Vector2(0f, 0.135f), new Vector2(1f, 1f));

            // The menu's own ground, then the frame on top of it - the same two layers the garage
            // and the mission board stand on, so the gold shop stops being the one screen you can
            // see the playfield through.
            EnsureSpriteImage("Background", root, $"{MenuTextures}/UI_MainMenu_BG.png", Vector2.zero, Vector2.one);

            RectTransform frame = EnsureSlicedImage("Frame", root, PanelFrameSprite,
                new Vector2(0.06f, 0.10f), new Vector2(0.94f, 0.92f));

            EnsureLabel("Title", frame, title, 52, TextAlignmentOptions.Center,
                new Vector2(0.1f, 0.86f), new Vector2(0.9f, 0.96f));

            EnsureLabel("ComingSoon", frame, "COMING SOON", 44, TextAlignmentOptions.Center,
                new Vector2(0.1f, 0.44f), new Vector2(0.9f, 0.60f));

            RectTransform money = EnsureSpriteImage("MoneyChip", frame, $"{MenuTextures}/UI_Money.png",
                new Vector2(0.55f, 0.865f), new Vector2(0.78f, 0.94f));
            goldLabel = EnsureLabel("GoldLabel", money, "0", 36, TextAlignmentOptions.Center,
                new Vector2(0.2f, 0.1f), new Vector2(0.82f, 0.9f));

            rows = EnsureRect("Rows", frame, new Vector2(0.08f, 0.08f), new Vector2(0.92f, 0.40f));
            return root;
        }

        /// <summary>
        /// The settings overlay: a dim over whatever is behind, a panel, and the way out of a run.
        /// The dim is a button too, so tapping outside the panel closes it.
        /// </summary>
        private static GameObject BuildSettings(Transform canvas, out Button closeButton, out Button mainMenuButton)
        {
            RectTransform root = EnsureRect("SettingsOverlay", canvas, Vector2.zero, Vector2.one);

            // The same black the garage and the mission board dim to.
            EnsureBackdrop("Dim", root);

            // Taller and a touch wider than it was: the frame's decorated top rim is 163px of a
            // 1436px sprite and nine-slicing keeps it at that thickness, so a short panel would be
            // a third banner. This gives it something to be a rim around.
            RectTransform panel = EnsureRect("Panel", root, new Vector2(0.10f, 0.24f), new Vector2(0.90f, 0.76f));
            EnsureSlicedImage("PanelBackground", panel, PanelFrameSprite, Vector2.zero, Vector2.one);
            // Everything below lives inside the frame's flat interior. Nine-slicing holds the
            // rim at its authored thickness, so on a 665-unit-tall panel the decorated top eats
            // the upper ~25% and the base ~10%; the interior is roughly 0.10 to 0.75. Anchoring
            // the title where it used to sit would have printed it across the decoration.
            EnsureLabel("Title", panel, "SETTINGS", 48, TextAlignmentOptions.Center,
                new Vector2(0.05f, 0.62f), new Vector2(0.95f, 0.74f));
            EnsureLabel("Note", panel, "Nothing to configure yet.", 26, TextAlignmentOptions.Center,
                new Vector2(0.05f, 0.51f), new Vector2(0.95f, 0.60f));

            mainMenuButton = EnsureSlicedButton("MainMenuButton", panel, "MAIN MENU",
                new Vector2(0.15f, 0.31f), new Vector2(0.85f, 0.48f));
            closeButton = EnsureSlicedButton("CloseButton", panel, "CLOSE",
                new Vector2(0.15f, 0.12f), new Vector2(0.85f, 0.29f));

            SettingsPanelView view = Ensure<SettingsPanelView>(root.gameObject);
            SerializedObject serialized = new SerializedObject(view);
            SetIfEmpty(serialized, "closeButton", closeButton);
            SetIfEmpty(serialized, "mainMenuButton", mainMenuButton);
            SetIfEmpty(serialized, "mainMenuButtonRoot", mainMenuButton.gameObject);
            serialized.ApplyModifiedPropertiesWithoutUndo();

            root.gameObject.SetActive(false);
            return root.gameObject;
        }

        /// <summary>
        /// The in-run chrome: the bullet counter at the top left and the gear at the top right,
        /// as in the mock-up. The counter's number is handed to the HUD as its remaining-bullets
        /// readout, so the art and the value are the same object.
        /// </summary>
        private static Button BuildRunChrome(GameObject hud)
        {
            if (hud == null)
            {
                return null;
            }

            RectTransform hudRect = (RectTransform)hud.transform;

            RectTransform counter = EnsureSpriteImage("BallCounter", hudRect, $"{UiTextures}/Ingame/UI_BallCounter.png",
                new Vector2(0.12f, 0.885f), new Vector2(0.28f, 0.962f));
            TMP_Text counterLabel = EnsureLabel("CounterLabel", counter, "0", 48, TextAlignmentOptions.Center,
                new Vector2(0.1f, 0.2f), new Vector2(0.9f, 0.85f));

            Button settingsButton = EnsureSpriteButton("RunSettingsButton", hudRect, $"{MenuTextures}/Btn_Setting.png",
                new Vector2(0.785f, 0.885f), new Vector2(0.87f, 0.962f));

            // The counter owns its own number rather than the HUD reaching across to it, so the
            // readout cannot end up on a different object from the art it belongs to.
            BulletCounterView counterView = Ensure<BulletCounterView>(counter.gameObject);
            SerializedObject serialized = new SerializedObject(counterView);
            SetIfEmpty(serialized, "inventory", LoadFirstAsset<GameJam.Gameplay.Combat.BulletInventory>());
            SetIfEmpty(serialized, "countLabel", counterLabel);
            serialized.ApplyModifiedPropertiesWithoutUndo();

            return settingsButton;
        }

        internal static RectTransform EnsureSpriteImage(string name, Transform parent, string spritePath, Vector2 anchorMin, Vector2 anchorMax)
        {
            RectTransform rect = EnsureRect(name, parent, anchorMin, anchorMax);
            Image image = rect.GetComponent<Image>();
            if (image == null)
            {
                image = Undo.AddComponent<Image>(rect.gameObject);
                image.sprite = LoadSprite(spritePath);
                image.raycastTarget = false;
                image.type = Image.Type.Simple;
                image.preserveAspect = false;
            }

            return rect;
        }

        /// <summary>
        /// A sprite drawn nine-sliced, so a panel frame keeps its corners and its rim at whatever
        /// size the rect is. Sliced rather than Simple is the whole reason the frame art was given
        /// a border - stretched Simple is what makes a reused frame look melted.
        ///
        /// Unlike the create-only helpers around it, this one re-asserts the look every run. The
        /// screens it dresses already existed as prefabs, so a create-only version silently did
        /// nothing to them - which is exactly how the settings panel kept its old flat background
        /// after being told to wear the frame. Layout is deliberately not re-asserted: anchors get
        /// nudged by hand in the editor and that work is worth keeping, but the sprite is named
        /// right here in the call, so it is ours to enforce.
        /// </summary>
        internal static RectTransform EnsureSlicedImage(
            string name,
            Transform parent,
            string spritePath,
            Vector2 anchorMin,
            Vector2 anchorMax)
        {
            RectTransform rect = EnsureSpriteImage(name, parent, spritePath, anchorMin, anchorMax);
            ApplySlicedSprite(rect != null ? rect.GetComponent<Image>() : null, spritePath, true);
            return rect;
        }

        /// <summary>
        /// A button wearing sliced art with a caption on top, for the two controls the settings
        /// panel actually offers. Built from the sprite button helper so the click sound and the
        /// transition match every other authored button in the game, then restyled on every run
        /// for the same reason <see cref="EnsureSlicedImage"/> is.
        /// </summary>
        internal static Button EnsureSlicedButton(
            string name,
            Transform parent,
            string caption,
            Vector2 anchorMin,
            Vector2 anchorMax)
        {
            Button button = EnsureSpriteButton(name, parent, PanelButtonSprite, anchorMin, anchorMax);
            if (button == null)
            {
                return null;
            }

            ApplySlicedSprite(button.targetGraphic as Image, PanelButtonSprite, true, PanelButtonPixelsPerUnit);
            EnsureLabel("Label", (RectTransform)button.transform, caption, 36, TextAlignmentOptions.Center,
                new Vector2(0.05f, 0.15f), new Vector2(0.95f, 0.85f));

            return button;
        }

        /// <summary>
        /// Puts the named sprite on an image and draws it nine-sliced, in white.
        ///
        /// White matters: an image keeps whatever tint it was built with, and these two were built
        /// as flat coloured rectangles - a dark slab and a sky-blue one. Left alone, that tint
        /// multiplies the new art and the frame arrives muddy or blue. The colour was standing in
        /// for art that now exists, so it goes.
        /// </summary>
        private static void ApplySlicedSprite(
            Image image,
            string spritePath,
            bool raycastTarget,
            float pixelsPerUnitMultiplier = 1f)
        {
            if (image == null)
            {
                return;
            }

            Sprite sprite = LoadSprite(spritePath);
            if (sprite == null)
            {
                // LoadSprite already warned. Keep whatever is there rather than blanking the
                // screen to an untextured white box.
                return;
            }

            if (image.sprite == sprite
                && image.type == Image.Type.Sliced
                && image.color == Color.white
                && Mathf.Approximately(image.pixelsPerUnitMultiplier, pixelsPerUnitMultiplier)
                && image.raycastTarget == raycastTarget)
            {
                // Already right. Bailing matters because these screens are prefab instances:
                // writing a value that equals the prefab's still records a scene override, and a
                // pile of those is what makes a prefab stop meaning anything.
                return;
            }

            Undo.RecordObject(image, "Style panel sprite");
            image.sprite = sprite;
            image.type = Image.Type.Sliced;
            image.color = Color.white;
            image.preserveAspect = false;
            image.raycastTarget = raycastTarget;
            image.pixelsPerUnitMultiplier = pixelsPerUnitMultiplier;
            EditorUtility.SetDirty(image);
        }

        /// <summary>
        /// The full-screen dim behind a modal. Re-asserted like the sliced art above, and it drops
        /// any sprite it finds: this one used to wear the tutorial spotlight, which has a hole
        /// punched through its middle, so the panel sat in a gap in its own backdrop.
        ///
        /// It takes raycasts on purpose - a tap on the dim is a tap on the screen that owns it,
        /// not a shot through it at the playfield.
        /// </summary>
        internal static RectTransform EnsureBackdrop(string name, Transform parent)
        {
            RectTransform rect = EnsureColorImage(name, parent, BackdropColor, Vector2.zero, Vector2.one);
            Image image = rect != null ? rect.GetComponent<Image>() : null;
            if (image != null
                && !(image.sprite == null && image.color == BackdropColor && image.raycastTarget))
            {
                Undo.RecordObject(image, "Style backdrop");
                image.sprite = null;
                image.type = Image.Type.Simple;
                image.color = BackdropColor;
                image.raycastTarget = true;
                EditorUtility.SetDirty(image);
            }

            return rect;
        }

        internal static RectTransform EnsureColorImage(string name, Transform parent, Color color, Vector2 anchorMin, Vector2 anchorMax)
        {
            RectTransform rect = EnsureRect(name, parent, anchorMin, anchorMax);
            if (rect.GetComponent<Image>() == null)
            {
                Image image = Undo.AddComponent<Image>(rect.gameObject);
                image.color = color;
            }

            return rect;
        }

        internal static Button EnsureSpriteButton(string name, Transform parent, string spritePath, Vector2 anchorMin, Vector2 anchorMax)
        {
            RectTransform rect = EnsureRect(name, parent, anchorMin, anchorMax);
            Button button = rect.GetComponent<Button>();
            if (button == null)
            {
                Image image = Undo.AddComponent<Image>(rect.gameObject);
                Sprite sprite = LoadSprite(spritePath);
                if (sprite != null)
                {
                    image.sprite = sprite;
                }

                button = Undo.AddComponent<Button>(rect.gameObject);
                button.targetGraphic = image;
            }

            EnsureClickSound(button);
            return button;
        }

        internal static Sprite LoadSprite(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null)
            {
                Debug.LogWarning(
                    $"{nameof(UiBuilder)} could not load a sprite at {path}. "
                    + "Check the texture is imported as Sprite (2D and UI).");
            }

            return sprite;
        }

        internal static T LoadFirstAsset<T>() where T : Object
        {
            string[] guids = AssetDatabase.FindAssets($"t:{typeof(T).Name}");
            return guids.Length == 0 ? null : AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }
    }
}
#endif
