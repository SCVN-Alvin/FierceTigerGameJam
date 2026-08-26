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
            GameObject shop = BuildShop(canvas, economy, out ShopTabsView shopTabs);
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
        /// </summary>
        private static GameObject BuildBottomBar(Transform canvas, out Button iapButton, out Button homeButton, out Button wrenchButton)
        {
            RectTransform root = EnsureRect("BottomBar", canvas, new Vector2(0f, 0f), new Vector2(1f, 0.135f));
            EnsureSpriteImage("Panel", root, $"{MenuTextures}/UI_MainMenu_BottomPanel.png", Vector2.zero, Vector2.one);

            iapButton = EnsureSpriteButton("IapShopButton", root, null, new Vector2(0.10f, 0.12f), new Vector2(0.27f, 0.78f));
            homeButton = EnsureSpriteButton("HomeButton", root, null, new Vector2(0.38f, 0.05f), new Vector2(0.62f, 0.95f));
            // The wrench opens the shop. Its sprite is named for vehicles, which is a fair
            // clue about what the slot was always meant to be; now it reaches both.
            RenameIfPresent(root, "BulletShopButton", "ShopButton");
            wrenchButton = EnsureSpriteButton("ShopButton", root, $"{MenuTextures}/Btn_Setting_Vehicle.png",
                new Vector2(0.77f, 0.12f), new Vector2(0.92f, 0.78f));

            // The bar art already draws the three slots, so the left and middle buttons are
            // invisible hit areas over it rather than a second set of icons on top.
            MakeInvisibleHitArea(iapButton);
            MakeInvisibleHitArea(homeButton);
            return root.gameObject;
        }

        private static GameObject BuildIapShop(Transform canvas, EconomyService economy)
        {
            RectTransform root = EnsureShopPanel("IapShopScreen", canvas, "GET GOLD", out RectTransform rows, out TMP_Text goldLabel);

            IapShopView view = Ensure<IapShopView>(root.gameObject);
            SerializedObject serialized = new SerializedObject(view);
            SetIfEmpty(serialized, "economy", economy);
            SetIfEmpty(serialized, "container", rows);
            SetIfEmpty(serialized, "goldLabel", goldLabel);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return root.gameObject;
        }

        // Read off RefAI/Garage_Vehicle.png and Garage_Ammo.png. Placeholder colours stand in
        // for panel art that does not exist yet; the shapes and proportions are the mock's.
        private static readonly Color GaragePanelColor = new Color(0.106f, 0.341f, 0.839f, 1f);
        private static readonly Color GarageInsetColor = new Color(0.078f, 0.259f, 0.682f, 1f);
        private static readonly Color GarageTabOnColor = new Color(1f, 0.75f, 0.16f, 1f);
        private static readonly Color GarageTabOffColor = new Color(0.09f, 0.28f, 0.72f, 1f);
        private static readonly Color GaragePreviewColor = new Color(0.85f, 0.88f, 0.92f, 1f);

        /// <summary>
        /// One shop, a tab per thing sold. The title, the gold chip and the panel behind them
        /// are shared; only the preview and the list change, which is the point - the player is
        /// spending the same gold whichever tab they are on, and should be able to see it move.
        ///
        /// Laid out from RefAI/Garage_Vehicle.png and RefAI/Garage_Ammo.png: a tab strip across
        /// the top, a preview of whatever is currently equipped under it, and the list below
        /// that. Vehicles are a grid of cards because they are chosen by looking at them; ammo
        /// is a list of rows because a row has to carry a level and its progress.
        /// </summary>
        private static GameObject BuildShop(Transform canvas, EconomyService economy, out ShopTabsView tabs)
        {
            // Was two screens. Rename rather than build a third, so an existing scene keeps the
            // object the flow controller is already pointing at.
            RenameIfPresent(canvas, "BulletShopScreen", "ShopScreen");
            DestroyIfPresent(canvas, "VehicleShopScreen");

            RectTransform root = EnsureRect("ShopScreen", canvas, new Vector2(0f, 0.135f), new Vector2(1f, 1f));
            EnsureColorImage("Background", root, GaragePanelColor, new Vector2(0.03f, 0.02f), new Vector2(0.97f, 0.9f));

            EnsureLabel("Title", root, "GARAGE", 46, TextAlignmentOptions.Center,
                new Vector2(0.34f, 0.905f), new Vector2(0.78f, 0.975f));

            // Its own gold chip, not the menu's: the main menu root is switched off while the
            // shop is up, and a shop that cannot show what the player is spending is no use.
            RectTransform money = EnsureSpriteImage("MoneyChip", root, $"{MenuTextures}/UI_Money.png",
                new Vector2(0.04f, 0.905f), new Vector2(0.30f, 0.972f));
            TMP_Text goldLabel = EnsureLabel("GoldLabel", money, "0", 34, TextAlignmentOptions.Center,
                new Vector2(0.2f, 0.1f), new Vector2(0.82f, 0.9f));

            // The list left over from the single-shop layout would otherwise sit behind both
            // tabs catching taps.
            DestroyIfPresent(root, "Rows");

            RectTransform tabStrip = EnsureRect("Tabs", root, new Vector2(0.06f, 0.825f), new Vector2(0.94f, 0.888f));
            Button vehicleTab = EnsureButton("VehicleTypeTab", tabStrip, "VEHICLES", new Vector2(0f, 0f), new Vector2(0.485f, 1f));
            Button bulletTab = EnsureButton("BulletTypeTab", tabStrip, "AMMO", new Vector2(0.515f, 0f), new Vector2(1f, 1f));

            GameObject vehiclePanel = BuildVehicleSection(root, economy, goldLabel);
            GameObject bulletPanel = BuildBulletSection(root, economy, goldLabel);

            tabs = Ensure<ShopTabsView>(root.gameObject);
            SerializedObject serializedTabs = new SerializedObject(tabs);
            SerializedProperty entries = serializedTabs.FindProperty("tabs");
            if (entries.arraySize == 0)
            {
                entries.arraySize = 2;
                FillTab(entries.GetArrayElementAtIndex(0), "Vehicles", vehicleTab, vehiclePanel);
                FillTab(entries.GetArrayElementAtIndex(1), "Ammunition", bulletTab, bulletPanel);
                serializedTabs.FindProperty("selectedTint").colorValue = GarageTabOnColor;
                serializedTabs.FindProperty("unselectedTint").colorValue = GarageTabOffColor;
            }

            serializedTabs.ApplyModifiedPropertiesWithoutUndo();
            return root.gameObject;
        }

        private static void FillTab(SerializedProperty entry, string name, Button button, GameObject panel)
        {
            entry.FindPropertyRelative("name").stringValue = name;
            entry.FindPropertyRelative("button").objectReferenceValue = button;
            entry.FindPropertyRelative("panel").objectReferenceValue = panel;
        }

        /// <summary>
        /// A tab's page: a preview of what is equipped, and the list underneath. Both tabs are
        /// the same two boxes in the same places, so switching between them moves nothing.
        /// </summary>
        private static RectTransform EnsureGaragePage(
            RectTransform shop,
            string name,
            out RectTransform preview,
            out RectTransform rows)
        {
            RectTransform panel = EnsureRect(name, shop, new Vector2(0.06f, 0.04f), new Vector2(0.94f, 0.81f));

            preview = EnsureColorImage("Preview", panel, GaragePreviewColor, new Vector2(0f, 0.6f), new Vector2(1f, 1f));
            EnsureColorImage("ListBackground", panel, GarageInsetColor, new Vector2(0f, 0f), new Vector2(1f, 0.56f));
            rows = EnsureRect("Rows", panel, new Vector2(0.04f, 0.03f), new Vector2(0.96f, 0.53f));
            return panel;
        }

        /// <summary>
        /// Vehicles are picked by looking at them, so they are a grid of cards rather than a
        /// list of rows: three across, which is what the mock shows and what fits 720 wide.
        /// </summary>
        private static GameObject BuildVehicleSection(RectTransform shop, EconomyService economy, TMP_Text goldLabel)
        {
            RectTransform panel = EnsureGaragePage(shop, "VehiclePanel", out RectTransform preview, out RectTransform rows);
            EnsureLabel("PreviewCaption", preview, "EQUIPPED VEHICLE", 24, TextAlignmentOptions.Center,
                new Vector2(0.05f, 0.02f), new Vector2(0.95f, 0.14f));

            // The view parents its cards here and does not impose a layout of its own, so the
            // grid is the container's business.
            GridLayoutGroup grid = Ensure<GridLayoutGroup>(rows.gameObject);
            grid.cellSize = new Vector2(196f, 190f);
            grid.spacing = new Vector2(14f, 14f);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 3;
            grid.childAlignment = TextAnchor.UpperCenter;

            VehicleShopView view = Ensure<VehicleShopView>(panel.gameObject);
            SerializedObject serialized = new SerializedObject(view);
            SetIfEmpty(serialized, "economy", economy);
            SetIfEmpty(serialized, "loadout", LoadFirstAsset<GameJam.Gameplay.Combat.VehicleLoadout>());
            SetIfEmpty(serialized, "container", rows);
            SetIfEmpty(serialized, "goldLabel", goldLabel);

            // rowPrefab is deliberately left empty until VehicleShopRow.prefab is authored: the
            // view generates a plain two-button card, so the tab is usable in the meantime.
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return panel.gameObject;
        }

        /// <summary>
        /// Ammunition is a list, because a row has to carry the level it is at and the button
        /// that raises it - things a card cannot show at this size.
        /// </summary>
        private static GameObject BuildBulletSection(RectTransform shop, EconomyService economy, TMP_Text goldLabel)
        {
            RectTransform panel = EnsureGaragePage(shop, "AmmoPanel", out RectTransform preview, out RectTransform rows);
            EnsureLabel("PreviewCaption", preview, "LOADED AMMO", 24, TextAlignmentOptions.Center,
                new Vector2(0.05f, 0.02f), new Vector2(0.95f, 0.14f));

            BulletShopView view = Ensure<BulletShopView>(panel.gameObject);
            SerializedObject serialized = new SerializedObject(view);
            SetIfEmpty(serialized, "economy", economy);
            SetIfEmpty(serialized, "loadout", LoadFirstAsset<GameJam.Gameplay.Combat.BulletLoadout>());
            SetIfEmpty(serialized, "container", rows);
            SetIfEmpty(serialized, "goldLabel", goldLabel);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return panel.gameObject;
        }

        /// <summary>Renames a child that an earlier layout left behind, keeping its references.</summary>
        private static void RenameIfPresent(Transform parent, string from, string to)
        {
            if (parent.Find(to) != null)
            {
                return;
            }

            Transform existing = parent.Find(from);
            if (existing != null)
            {
                existing.name = to;
            }
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

        private static RectTransform EnsureSpriteImage(string name, Transform parent, string spritePath, Vector2 anchorMin, Vector2 anchorMax)
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

        private static RectTransform EnsureColorImage(string name, Transform parent, Color color, Vector2 anchorMin, Vector2 anchorMax)
        {
            RectTransform rect = EnsureRect(name, parent, anchorMin, anchorMax);
            if (rect.GetComponent<Image>() == null)
            {
                Image image = Undo.AddComponent<Image>(rect.gameObject);
                image.color = color;
            }

            return rect;
        }

        private static Button EnsureSpriteButton(string name, Transform parent, string spritePath, Vector2 anchorMin, Vector2 anchorMax)
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

            return button;
        }

        /// <summary>
        /// A hit area over art that already shows the button. The image has to stay enabled to
        /// receive taps, so it is made fully transparent rather than switched off.
        /// </summary>
        private static void MakeInvisibleHitArea(Button button)
        {
            if (button != null && button.targetGraphic is Image image && image.sprite == null)
            {
                image.color = new Color(1f, 1f, 1f, 0f);
            }
        }

        private static Sprite LoadSprite(string path)
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

        private static T LoadFirstAsset<T>() where T : Object
        {
            string[] guids = AssetDatabase.FindAssets($"t:{typeof(T).Name}");
            return guids.Length == 0 ? null : AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }
    }
}
#endif
