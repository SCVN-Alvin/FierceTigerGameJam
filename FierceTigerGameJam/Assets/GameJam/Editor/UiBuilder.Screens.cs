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
        private static RectTransform EnsureShopPanel(
            string name,
            Transform canvas,
            string title,
            out RectTransform rows,
            out TMP_Text goldLabel)
        {
            RectTransform root = EnsureRect(name, canvas, new Vector2(0f, 0.135f), new Vector2(1f, 1f));
            EnsureColorImage("Background", root, PanelColor, Vector2.zero, Vector2.one);

            EnsureLabel("Title", root, title, 52, TextAlignmentOptions.Center,
                new Vector2(0.1f, 0.88f), new Vector2(0.9f, 0.96f));

            RectTransform money = EnsureSpriteImage("MoneyChip", root, $"{MenuTextures}/UI_Money.png",
                new Vector2(0.55f, 0.885f), new Vector2(0.78f, 0.95f));
            goldLabel = EnsureLabel("GoldLabel", money, "0", 36, TextAlignmentOptions.Center,
                new Vector2(0.2f, 0.1f), new Vector2(0.82f, 0.9f));

            rows = EnsureRect("Rows", root, new Vector2(0.06f, 0.06f), new Vector2(0.94f, 0.85f));
            return root;
        }

        /// <summary>
        /// The settings overlay: a dim over whatever is behind, a panel, and the way out of a run.
        /// The dim is a button too, so tapping outside the panel closes it.
        /// </summary>
        private static GameObject BuildSettings(Transform canvas, out Button closeButton, out Button mainMenuButton)
        {
            RectTransform root = EnsureRect("SettingsOverlay", canvas, Vector2.zero, Vector2.one);
            EnsureSpriteImage("Dim", root, $"{UiTextures}/Tutorial/Filter.png", Vector2.zero, Vector2.one);

            RectTransform panel = EnsureRect("Panel", root, new Vector2(0.12f, 0.32f), new Vector2(0.88f, 0.68f));
            EnsureColorImage("PanelBackground", panel, PanelColor, Vector2.zero, Vector2.one);
            EnsureLabel("Title", panel, "SETTINGS", 48, TextAlignmentOptions.Center,
                new Vector2(0.05f, 0.78f), new Vector2(0.95f, 0.95f));
            EnsureLabel("Note", panel, "Nothing to configure yet.", 26, TextAlignmentOptions.Center,
                new Vector2(0.05f, 0.6f), new Vector2(0.95f, 0.75f));

            mainMenuButton = EnsureButton("MainMenuButton", panel, "MAIN MENU", new Vector2(0.15f, 0.34f), new Vector2(0.85f, 0.5f));
            closeButton = EnsureButton("CloseButton", panel, "CLOSE", new Vector2(0.15f, 0.1f), new Vector2(0.85f, 0.26f));

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
