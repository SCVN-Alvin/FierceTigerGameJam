#if UNITY_EDITOR
using GameJam.Economy;
using GameJam.Gameplay.Combat;
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
    /// Authors the garage - the one screen the wrench opens - out of the supplied art, and puts
    /// it into the scene in place of the old shop.
    ///
    /// It builds prefabs rather than scene objects, which is the lesson of the last shop: the
    /// screens in this project are prefab instances, and a builder that appends into one ends up
    /// fighting whoever authored it. Here the prefab is the only description of the garage and
    /// the scene holds nothing but an instance of it, so there is one place to change and one
    /// place to look.
    ///
    /// Everything is positioned by anchor fractions of its parent, read off the frame sprite's
    /// own pixel geometry, so <see cref="FrameSize"/> is the single number that scales the whole
    /// screen at the point the prefab is first written.
    ///
    /// After that, nothing is written twice. Every position, every component setting and every
    /// reference is filled in only when the thing it belongs to did not already exist, because
    /// the numbers below are a starting point and the prefab is where the screen is actually
    /// tuned: an icon nudged in the inspector, a tab switched from Sliced to Simple, a sprite
    /// given a pixels-per-unit multiplier. A builder that rewrote those on every run would make
    /// re-running it cost the tuning, which is the same as not being able to run it again.
    /// Deleting the prefab is how you ask for the numbers below back.
    /// </summary>
    public static class GarageScreenBuilder
    {
        private const string GarageTextures = "Assets/GameJam/Textures/UI/Garage";
        private const string MenuTextures = "Assets/GameJam/Textures/UI/MainMenu";

        private const string FrameSprite = GarageTextures + "/UI_Shop_Frame.png";
        private const string VehicleTabOnSprite = GarageTextures + "/Btn_Vehicle_Selected.png";
        private const string VehicleTabOffSprite = GarageTextures + "/Btn_Vehicle_Unselected.png";
        private const string AmmoTabOnSprite = GarageTextures + "/Btn_Ammo_Selected.png";
        private const string AmmoTabOffSprite = GarageTextures + "/Btn_Ammo_Unselected.png";
        private const string RowFrameSprite = GarageTextures + "/UI_Shop_Ammo_Frame.png";
        private const string LevelFillSprite = GarageTextures + "/UI_Level_Fill.png";
        private const string LevelUnfilledSprite = GarageTextures + "/UI_Level_Unfilled.png";
        private const string BuySprite = GarageTextures + "/Btn_Buy.png";
        private const string LockedSprite = GarageTextures + "/UI_Locked.png";
        private const string CloseSprite = GarageTextures + "/Btn_Esc.png";
        private const string MoneySprite = MenuTextures + "/UI_Money.png";

        private const string GarageFolder = "Assets/GameJam/Prefabs/UI/Garage";
        private const string PipPrefabPath = GarageFolder + "/UpgradeLevelView.prefab";
        private const string BulletRowPrefabPath = GarageFolder + "/BulletTypeViewItem.prefab";
        private const string VehicleRowPrefabPath = GarageFolder + "/VehicleTypeViewItem.prefab";
        internal const string ScreenPrefabPath = GarageFolder + "/GarageScreen.prefab";

        private const string OldShopFolder = "Assets/GameJam/Prefabs/UI/BulletShop";
        private const string OldShopScreenPath = OldShopFolder + "/BulletShopScreen.prefab";
        private const string OldShopRowPath = OldShopFolder + "/BulletTypeUpgradeView.prefab";

        /// <summary>
        /// Where icon art goes when it is drawn. Level 1 is "{id}.png"; a level with a look of
        /// its own is "{id}_L2.png". Nothing here has to exist - a definition whose file is
        /// missing keeps its empty icon, and the row and the preview draw an empty slot.
        /// </summary>
        private const string BulletIconFolder = "Assets/GameJam/Textures/Items/Bullets";
        private const string VehicleIconFolder = "Assets/GameJam/Textures/Items/Vehicles";

        internal const string ScreenName = "GarageScreen";
        private const string OldScreenName = "ShopScreen";

        /// <summary>
        /// The one number to tune, at the frame sprite's own aspect (975:1436). Everything inside
        /// the frame is a fraction of it, so changing this scales the whole garage.
        /// </summary>
        private static readonly Vector2 FrameSize = new Vector2(600f, 884f);

        /// <summary>Hangs the frame off the top of the screen, clear of the gold chip and the X.</summary>
        private static readonly Vector2 FrameOffset = new Vector2(0f, -56f);

        /// <summary>796x148 of row art at the frame's 600/975 scale, so the 9-slice stays near 1:1.</summary>
        private static readonly Vector2 RowSize = new Vector2(490f, 91f);

        /// <summary>81x43 of pip art at the same scale.</summary>
        private static readonly Vector2 PipSize = new Vector2(50f, 26f);

        /// <summary>The screen sits above the bottom bar, which is on screen at the same time.</summary>
        private static readonly Vector2 ScreenAnchorMin = new Vector2(0f, 0.135f);
        private static readonly Vector2 ScreenAnchorMax = new Vector2(1f, 1f);

        [MenuItem("Tools/Smashdown/Build Garage Screen")]
        public static void BuildGarageScreen()
        {
            EnsureFolder(GarageFolder);

            GameObject pip = BuildPipPrefab();
            GameObject bulletRow = BuildRowPrefab(BulletRowPrefabPath, "BulletTypeViewItem", pip, false);
            GameObject vehicleRow = BuildRowPrefab(VehicleRowPrefabPath, "VehicleTypeViewItem", pip, true);
            GameObject screen = BuildScreenPrefab(bulletRow, vehicleRow);

            FillMissingIcons();

            // The scene first and the deletions after: the old prefab may not be thrown away
            // while an instance of it is still standing in the scene.
            bool placed = EnsureSceneInstance(screen, out ShopTabsView _, out Button _);
            if (placed)
            {
                DeleteOldShopPrefabs();
            }

            AssetDatabase.SaveAssets();
            Debug.Log(
                "Built the garage into " + ScreenPrefabPath + " and put an instance of it in the scene. "
                + "Save the scene: the old shop prefab has been deleted, and a scene still holding the "
                + "unsaved reference to it would come back broken.");
        }

        // ------------------------------------------------------------------ pips

        /// <summary>
        /// One level of an item: the empty socket, and the lit half over it. Two images rather
        /// than one whose sprite is swapped, so the lit half can be animated later.
        /// </summary>
        private static GameObject BuildPipPrefab()
        {
            return EnsurePrefab(PipPrefabPath, "UpgradeLevelView", (root, created) =>
            {
                RectTransform rect = (RectTransform)root.transform;
                if (created)
                {
                    PlaceFixed(rect, PipSize);
                }

                RectTransform unfilled = EnsureImage("Unfilled", rect, LevelUnfilledSprite,
                    Vector2.zero, Vector2.one, Image.Type.Simple, false);
                RectTransform fill = EnsureImage("Fill", rect, LevelFillSprite,
                    Vector2.zero, Vector2.one, Image.Type.Simple, false);

                UpgradeLevelView view = UiBuilder.Ensure<UpgradeLevelView>(root);
                SerializedObject serialized = new SerializedObject(view);
                UiBuilder.SetIfEmpty(serialized, "unfilled", unfilled.GetComponent<Image>());
                UiBuilder.SetIfEmpty(serialized, "fill", fill.GetComponent<Image>());
                serialized.ApplyModifiedPropertiesWithoutUndo();
            });
        }

        // ------------------------------------------------------------------ rows

        /// <summary>
        /// One row of either list. Both tabs use the same picture - the row art is the same
        /// sprite, the parts sit in the same places - because the two lists are the same
        /// comparison and a player moving between tabs should not have to re-learn a row.
        ///
        /// The header is a layout group so that switching the lock graphic on pushes the name to
        /// the right of it and switching it off gives the name the whole line; Unity's layout
        /// skips inactive children, so nothing else has to move.
        /// </summary>
        private static GameObject BuildRowPrefab(string path, string rootName, GameObject pipPrefab, bool vehicle)
        {
            return EnsurePrefab(path, rootName, (root, created) =>
            {
                RectTransform rect = (RectTransform)root.transform;
                if (created)
                {
                    PlaceTop(rect, RowSize);
                }

                RectTransform frame = EnsureImage("Frame", rect, RowFrameSprite,
                    Vector2.zero, Vector2.one, Image.Type.Sliced, false, true);
                Image frameImage = frame.GetComponent<Image>();

                // The dark slot the icon sits in is baked into the row art's left border, so the
                // icon is only the picture, centred in a slot that never stretches.
                RectTransform icon = EnsureImage("Icon", rect, null,
                    new Vector2(0.053f, 0.25f), new Vector2(0.143f, 0.74f), Image.Type.Simple, true);

                RectTransform header = EnsureRect("Header", rect,
                    new Vector2(0.214f, 0.45f), new Vector2(0.97f, 0.80f));

                HorizontalLayoutGroup headerLayout = EnsureComponent<HorizontalLayoutGroup>(
                    header.gameObject, out bool headerLayoutCreated);
                if (headerLayoutCreated)
                {
                    headerLayout.spacing = 8f;
                    headerLayout.childAlignment = TextAnchor.MiddleLeft;
                    headerLayout.childControlWidth = true;
                    headerLayout.childControlHeight = true;
                    headerLayout.childForceExpandWidth = false;
                    headerLayout.childForceExpandHeight = false;
                }

                RectTransform locked = EnsureImage("Locked", header, LockedSprite,
                    Vector2.zero, Vector2.one, Image.Type.Simple, true);

                LayoutElement lockedSize = EnsureComponent<LayoutElement>(
                    locked.gameObject, out bool lockedSizeCreated);
                if (lockedSizeCreated)
                {
                    // A minimum as well as a preferred size: a layout group short of room takes
                    // it from every child in proportion, and the graphic that says LOCKED is the
                    // one reading on the row that must not be squeezed. The name gives way.
                    lockedSize.minWidth = 144f;
                    lockedSize.minHeight = 34f;
                    lockedSize.preferredWidth = 144f;
                    lockedSize.preferredHeight = 34f;
                }

                TMP_Text label = EnsureLabel("Label", header, "ITEM", 26,
                    TextAlignmentOptions.Left, Vector2.zero, Vector2.one, out bool labelCreated);
                if (labelCreated)
                {
                    label.fontStyle = FontStyles.Bold;
                    label.raycastTarget = false;

                    // The row is a fixed 490x91 in every state, so a long name has to be cut
                    // rather than allowed to wrap onto a line the row has no height for.
                    label.textWrappingMode = TextWrappingModes.NoWrap;
                    label.overflowMode = TextOverflowModes.Ellipsis;
                }

                RectTransform levels = EnsureRect("Levels", rect,
                    new Vector2(0.214f, 0.06f), new Vector2(0.754f, 0.36f));

                HorizontalLayoutGroup levelsLayout = EnsureComponent<HorizontalLayoutGroup>(
                    levels.gameObject, out bool levelsLayoutCreated);
                if (levelsLayoutCreated)
                {
                    levelsLayout.spacing = 3f;
                    levelsLayout.childAlignment = TextAnchor.MiddleLeft;
                    levelsLayout.childControlWidth = false;
                    levelsLayout.childControlHeight = false;
                    levelsLayout.childForceExpandWidth = false;
                    levelsLayout.childForceExpandHeight = false;
                }

                UpgradeLevelBarView bar = UiBuilder.Ensure<UpgradeLevelBarView>(levels.gameObject);
                SerializedObject serializedBar = new SerializedObject(bar);
                UiBuilder.SetIfEmpty(serializedBar, "pipPrefab",
                    pipPrefab != null ? pipPrefab.GetComponent<UpgradeLevelView>() : null);
                serializedBar.ApplyModifiedPropertiesWithoutUndo();

                bool buyCreated = rect.Find("Buy") == null;
                Button buy = UiBuilder.EnsureSpriteButton("Buy", rect, BuySprite,
                    new Vector2(0.786f, 0.074f), new Vector2(0.969f, 0.412f));
                if (buyCreated)
                {
                    Place((RectTransform)buy.transform, new Vector2(0.786f, 0.074f), new Vector2(0.969f, 0.412f));

                    if (buy.targetGraphic is Image buyImage)
                    {
                        // Sliced, so the price can be as wide as it needs without stretching the
                        // coin baked into the button's left edge.
                        buyImage.type = Image.Type.Sliced;
                    }
                }

                // The price starts to the right of that coin.
                TMP_Text price = EnsureLabel("Price", buy.transform, "0", 16,
                    TextAlignmentOptions.Center, new Vector2(0.30f, 0.05f), new Vector2(0.97f, 0.95f),
                    out bool priceCreated);
                if (priceCreated)
                {
                    Place((RectTransform)price.transform, new Vector2(0.30f, 0.05f), new Vector2(0.97f, 0.95f));
                    price.fontStyle = FontStyles.Bold;

                    // Off, so the caption cannot be the thing a tap lands on. The click would
                    // still reach the button by bubbling, but a text that eats the raycast is
                    // also what makes a disabled button feel pressable.
                    price.raycastTarget = false;
                }

                // On the root: dimming a child would leave the row's own frame lit. interactable
                // is left alone on purpose, so a dimmed row can still be bought from and tapped.
                UiBuilder.Ensure<CanvasGroup>(root);

                // The row is a button. No art of its own - the frame it tints is the row art - so
                // the only thing the player sees is the press, which is the point: the row should
                // read as a thing you tap without looking like a second Buy.
                Button select = EnsureComponent<Button>(root, out bool selectCreated);
                if (selectCreated)
                {
                    select.transition = Selectable.Transition.ColorTint;

                    // Assigned rather than found: Selectable only picks up a Graphic on its own
                    // object, and the row root deliberately has none.
                    select.targetGraphic = frameImage;

                    // The frame is what the tap has to land on, and on a row built before the row
                    // became a button it is still decoration. Turned on here rather than where
                    // the frame is made, so this migrates such a row exactly once and touches
                    // nothing on a row that already had its button.
                    if (frameImage != null)
                    {
                        frameImage.raycastTarget = true;
                    }
                }

                ShopItemView item = vehicle
                    ? (ShopItemView)UiBuilder.Ensure<VehicleTypeViewItem>(root)
                    : UiBuilder.Ensure<BulletTypeViewItem>(root);

                SerializedObject serialized = new SerializedObject(item);
                UiBuilder.SetIfEmpty(serialized, "icon", icon.GetComponent<Image>());
                UiBuilder.SetIfEmpty(serialized, "label", label);
                UiBuilder.SetIfEmpty(serialized, "locked", locked.gameObject);
                UiBuilder.SetIfEmpty(serialized, "levels", bar);
                UiBuilder.SetIfEmpty(serialized, "buyButton", buy);
                UiBuilder.SetIfEmpty(serialized, "buyLabel", price);
                UiBuilder.SetIfEmpty(serialized, "selectButton", select);
                UiBuilder.SetIfEmpty(serialized, "group", root.GetComponent<CanvasGroup>());
                serialized.ApplyModifiedPropertiesWithoutUndo();
            });
        }

        // ------------------------------------------------------------------ screen

        /// <summary>
        /// The whole panel. The title tab, the blue band behind the tabs, the garage table the
        /// preview stands on and the dark inset the list scrolls in are all painted into the one
        /// frame sprite, so what is built here is the empty boxes over them and nothing else.
        /// </summary>
        private static GameObject BuildScreenPrefab(GameObject bulletRow, GameObject vehicleRow)
        {
            return EnsurePrefab(ScreenPrefabPath, ScreenName, (root, created) =>
            {
                RectTransform rect = (RectTransform)root.transform;
                if (created)
                {
                    Place(rect, ScreenAnchorMin, ScreenAnchorMax);
                }

                bool frameCreated = rect.Find("Frame") == null;
                RectTransform frame = EnsureImage("Frame", rect, FrameSprite,
                    Vector2.zero, Vector2.one, Image.Type.Simple, false);

                if (frameCreated)
                {
                    // Pinned to the top edge at a fixed size rather than stretched: the frame art
                    // has one aspect, and a taller phone should leave more room under it, not a
                    // taller frame.
                    frame.anchorMin = new Vector2(0.5f, 1f);
                    frame.anchorMax = new Vector2(0.5f, 1f);
                    frame.pivot = new Vector2(0.5f, 1f);
                    frame.sizeDelta = FrameSize;
                    frame.anchoredPosition = FrameOffset;
                }

                RectTransform tabs = EnsureRect("Tabs", frame,
                    new Vector2(0.048f, 0.847f), new Vector2(0.956f, 0.907f));

                // No label under either tab: the words are painted into the sprites, which is why
                // ShopTabsView swaps them rather than tinting.
                Button vehicleTab = EnsureTabButton("VehicleTypeTab", tabs, VehicleTabOffSprite,
                    new Vector2(0f, 0f), new Vector2(0.48f, 1f));
                Button bulletTab = EnsureTabButton("BulletTypeTab", tabs, AmmoTabOffSprite,
                    new Vector2(0.52f, 0f), new Vector2(1f, 1f));

                GameObject vehiclePanel = BuildPanel(frame, "VehiclePanel");
                GameObject bulletPanel = BuildPanel(frame, "AmmoPanel");

                // Its own gold chip, not the menu's: the main menu root is switched off while the
                // garage is up, and a shop that cannot show what the player is spending is no use.
                RectTransform money = EnsureImage("MoneyChip", rect, MoneySprite,
                    new Vector2(0.046f, 0.926f), new Vector2(0.268f, 0.975f), Image.Type.Simple, false);
                TMP_Text goldLabel = EnsureLabel("GoldLabel", money, "0", 34,
                    TextAlignmentOptions.Center, new Vector2(0.2f, 0.1f), new Vector2(0.82f, 0.9f),
                    out bool goldCreated);
                if (goldCreated)
                {
                    Place((RectTransform)goldLabel.transform, new Vector2(0.2f, 0.1f), new Vector2(0.82f, 0.9f));
                    goldLabel.raycastTarget = false;
                }

                bool closeCreated = rect.Find("CloseButton") == null;
                Button close = UiBuilder.EnsureSpriteButton("CloseButton", rect, CloseSprite,
                    new Vector2(0.867f, 0.925f), new Vector2(0.944f, 0.976f));
                if (closeCreated)
                {
                    Place((RectTransform)close.transform, new Vector2(0.867f, 0.925f), new Vector2(0.944f, 0.976f));
                    if (close.targetGraphic is Image closeImage)
                    {
                        closeImage.preserveAspect = true;
                    }
                }

                WireShopView(vehiclePanel, true, goldLabel, vehicleRow);
                WireShopView(bulletPanel, false, goldLabel, bulletRow);

                WireTabs(root, vehicleTab, vehiclePanel, bulletTab, bulletPanel);

                if (created)
                {
                    // Inactive in the prefab, not only on the instance: the flow switches screens
                    // on, and a screen that ships active would be an override on every instance
                    // of it. Only on the first build - whoever turned it back on to look at the
                    // layout meant to, and the flow hides it on entry either way.
                    root.SetActive(false);
                }
            });
        }

        /// <summary>
        /// A tab's page: the preview over the garage table, and the list in the inset under it.
        /// Both pages are the same two boxes in the same places, so switching tabs moves nothing.
        /// </summary>
        private static GameObject BuildPanel(RectTransform frame, string name)
        {
            RectTransform panel = EnsureRect(name, frame, Vector2.zero, Vector2.one);

            RectTransform preview = EnsureRect("Preview", panel,
                new Vector2(0.047f, 0.508f), new Vector2(0.952f, 0.829f));

            // No background of its own: the light garage window is part of the frame art.
            EnsureImage("PreviewItem", preview, null,
                new Vector2(0.36f, 0.31f), new Vector2(0.64f, 0.84f), Image.Type.Simple, true);

            TMP_Text caption = EnsureLabel("PreviewCaption", preview, string.Empty, 22,
                TextAlignmentOptions.Center, new Vector2(0.05f, 0.03f), new Vector2(0.95f, 0.15f),
                out bool captionCreated);
            if (captionCreated)
            {
                Place((RectTransform)caption.transform, new Vector2(0.05f, 0.03f), new Vector2(0.95f, 0.15f));
                caption.raycastTarget = false;
            }

            RectTransform list = EnsureRect("List", panel,
                new Vector2(0.047f, 0.038f), new Vector2(0.953f, 0.474f));

            RectTransform viewport = EnsureRect("Viewport", list, Vector2.zero, Vector2.one);
            UiBuilder.Ensure<RectMask2D>(viewport.gameObject);

            RectTransform rows = EnsureRect("Rows", viewport,
                new Vector2(0f, 1f), new Vector2(1f, 1f), out bool rowsCreated);
            if (rowsCreated)
            {
                // Hung from the top and growing downward, which is what the fitter below sizes.
                rows.pivot = new Vector2(0.5f, 1f);
                rows.anchoredPosition = Vector2.zero;
                rows.sizeDelta = Vector2.zero;
            }

            VerticalLayoutGroup rowsLayout = EnsureComponent<VerticalLayoutGroup>(
                rows.gameObject, out bool rowsLayoutCreated);
            if (rowsLayoutCreated)
            {
                rowsLayout.padding = new RectOffset(26, 26, 20, 20);
                rowsLayout.spacing = 30f;
                rowsLayout.childAlignment = TextAnchor.UpperCenter;

                // All off: a row keeps the size its prefab defines, which is what makes every row
                // the same shape whatever state it is in.
                rowsLayout.childControlWidth = false;
                rowsLayout.childControlHeight = false;
                rowsLayout.childForceExpandWidth = false;
                rowsLayout.childForceExpandHeight = false;
            }

            ContentSizeFitter fitter = EnsureComponent<ContentSizeFitter>(rows.gameObject, out bool fitterCreated);
            if (fitterCreated)
            {
                fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
                fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            }

            ScrollRect scroll = EnsureComponent<ScrollRect>(list.gameObject, out bool scrollCreated);
            if (scrollCreated)
            {
                scroll.horizontal = false;
                scroll.vertical = true;

                // Clamped rather than elastic: three rows fill the inset exactly, and a list that
                // bounces when there is nothing to scroll reads as broken.
                scroll.movementType = ScrollRect.MovementType.Clamped;
                scroll.viewport = viewport;
                scroll.content = rows;
            }

            return panel.gameObject;
        }

        private static void WireShopView(GameObject panel, bool vehicle, TMP_Text goldLabel, GameObject rowPrefab)
        {
            RectTransform rows = panel.transform.Find("List/Viewport/Rows") as RectTransform;
            Transform previewItem = panel.transform.Find("Preview/PreviewItem");
            Transform previewCaption = panel.transform.Find("Preview/PreviewCaption");

            Component view = vehicle
                ? (Component)UiBuilder.Ensure<VehicleShopView>(panel)
                : UiBuilder.Ensure<BulletShopView>(panel);

            SerializedObject serialized = new SerializedObject(view);
            UiBuilder.SetIfEmpty(serialized, "economy", UiBuilder.LoadFirstAsset<EconomyService>());
            UiBuilder.SetIfEmpty(serialized, "loadout", vehicle
                ? (Object)UiBuilder.LoadFirstAsset<VehicleLoadout>()
                : UiBuilder.LoadFirstAsset<BulletLoadout>());
            UiBuilder.SetIfEmpty(serialized, "container", rows);
            UiBuilder.SetIfEmpty(serialized, "rowPrefab", rowPrefab);
            UiBuilder.SetIfEmpty(serialized, "goldLabel", goldLabel);
            UiBuilder.SetIfEmpty(serialized, "previewImage",
                previewItem != null ? previewItem.GetComponent<Image>() : null);
            UiBuilder.SetIfEmpty(serialized, "previewCaption",
                previewCaption != null ? previewCaption.GetComponent<TMP_Text>() : null);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// Index 0 is vehicles and index 1 is ammunition, left to right, so
        /// <see cref="GameFlowController.EnterShopTab"/> reads the way the strip looks. The
        /// default is ammunition, which is what the shop opened on before the garage replaced it.
        /// </summary>
        private static void WireTabs(GameObject root, Button vehicleTab, GameObject vehiclePanel, Button bulletTab, GameObject bulletPanel)
        {
            ShopTabsView tabs = UiBuilder.Ensure<ShopTabsView>(root);
            SerializedObject serialized = new SerializedObject(tabs);
            SerializedProperty entries = serialized.FindProperty("tabs");

            if (entries.arraySize == 0)
            {
                entries.arraySize = 2;
                FillTab(entries.GetArrayElementAtIndex(0), "Vehicles", vehicleTab, vehiclePanel,
                    VehicleTabOnSprite, VehicleTabOffSprite);
                FillTab(entries.GetArrayElementAtIndex(1), "Ammunition", bulletTab, bulletPanel,
                    AmmoTabOnSprite, AmmoTabOffSprite);
                serialized.FindProperty("defaultTab").intValue = 1;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void FillTab(
            SerializedProperty entry,
            string name,
            Button button,
            GameObject panel,
            string selectedSpritePath,
            string unselectedSpritePath)
        {
            entry.FindPropertyRelative("name").stringValue = name;
            entry.FindPropertyRelative("button").objectReferenceValue = button;
            entry.FindPropertyRelative("panel").objectReferenceValue = panel;
            entry.FindPropertyRelative("selectedSprite").objectReferenceValue = UiBuilder.LoadSprite(selectedSpritePath);
            entry.FindPropertyRelative("unselectedSprite").objectReferenceValue = UiBuilder.LoadSprite(unselectedSpritePath);
        }

        // ------------------------------------------------------------------ icons

        /// <summary>
        /// Fills in icon art that has been drawn since the last run, by the naming convention and
        /// nothing else, so a new sprite never has to be dragged onto a definition by hand. An
        /// icon already set is left alone, and a file that does not exist is not complained about:
        /// most of them do not exist yet, and a warning per level would drown the console.
        /// </summary>
        private static void FillMissingIcons()
        {
            FillMissingIcons<BulletDefinition>(BulletIconFolder);
            FillMissingIcons<VehicleDefinition>(VehicleIconFolder);
        }

        private static void FillMissingIcons<T>(string folder) where T : ScriptableObject
        {
            string[] guids = AssetDatabase.FindAssets($"t:{typeof(T).Name}");
            for (int i = 0; i < guids.Length; i++)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guids[i]);
                T definition = AssetDatabase.LoadAssetAtPath<T>(assetPath);
                if (definition == null)
                {
                    continue;
                }

                SerializedObject serialized = new SerializedObject(definition);
                SerializedProperty id = serialized.FindProperty("id");
                SerializedProperty levels = serialized.FindProperty("levels");
                if (id == null || levels == null || string.IsNullOrEmpty(id.stringValue))
                {
                    continue;
                }

                bool changed = false;
                for (int level = 0; level < levels.arraySize; level++)
                {
                    SerializedProperty icon = levels.GetArrayElementAtIndex(level).FindPropertyRelative("icon");
                    if (icon == null || icon.objectReferenceValue != null)
                    {
                        continue;
                    }

                    string file = level == 0
                        ? $"{folder}/{id.stringValue}.png"
                        : $"{folder}/{id.stringValue}_L{level + 1}.png";

                    Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(file);
                    if (sprite == null)
                    {
                        continue;
                    }

                    icon.objectReferenceValue = sprite;
                    changed = true;
                }

                if (changed)
                {
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(definition);
                }
            }
        }

        // ------------------------------------------------------------------ scene

        /// <summary>
        /// Puts the garage into the scene in place of the old shop and points the flow at it.
        ///
        /// The whole old instance goes, which Unity allows - it is only the children of an
        /// instance that may not be removed from a scene - and taking it out is what leaves the
        /// flow's three references empty for <see cref="UiBuilder.SetIfEmpty"/> to fill.
        /// </summary>
        internal static bool EnsureSceneInstance(GameObject prefab, out ShopTabsView tabs, out Button closeButton)
        {
            tabs = null;
            closeButton = null;

            if (prefab == null)
            {
                return false;
            }

            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded)
            {
                Debug.LogWarning("The garage prefab was built, but there is no loaded scene to put it in.");
                return false;
            }

            Canvas canvas = Object.FindFirstObjectByType<Canvas>(FindObjectsInactive.Include);
            if (canvas == null)
            {
                Debug.LogWarning("The garage prefab was built, but the scene has no Canvas to put it under.");
                return false;
            }

            Transform old = canvas.transform.Find(OldScreenName);
            if (old != null)
            {
                Object.DestroyImmediate(old.gameObject);
            }

            Transform existing = canvas.transform.Find(ScreenName);
            GameObject instance;
            bool placed = existing == null;
            if (existing != null)
            {
                instance = existing.gameObject;
            }
            else
            {
                instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, canvas.transform);
                instance.name = ScreenName;
            }

            if (placed)
            {
                // Only on the instance this run put there. An instance already in the scene is
                // left where it is: it may have been moved on purpose, and the flow switches it
                // on and off by itself whatever state it was saved in.
                RectTransform rect = (RectTransform)instance.transform;
                rect.anchorMin = ScreenAnchorMin;
                rect.anchorMax = ScreenAnchorMax;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
                instance.SetActive(false);
            }

            tabs = instance.GetComponent<ShopTabsView>();
            Transform close = instance.transform.Find("CloseButton");
            closeButton = close != null ? close.GetComponent<Button>() : null;

            GameFlowController flow = Object.FindFirstObjectByType<GameFlowController>(FindObjectsInactive.Include);
            if (flow != null)
            {
                SerializedObject serialized = new SerializedObject(flow);
                UiBuilder.SetIfEmpty(serialized, "shopRoot", instance);
                UiBuilder.SetIfEmpty(serialized, "shopTabs", tabs);
                UiBuilder.SetIfEmpty(serialized, "closeShopButton", closeButton);
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            else
            {
                Debug.LogWarning(
                    "The garage is in the scene but there is no GameFlowController to wire it to, so the "
                    + "wrench will not open it.");
            }

            EditorSceneManager.MarkSceneDirty(scene);
            return true;
        }

        /// <summary>
        /// The old shop screen and its row, gone rather than left behind. Two descriptions of the
        /// same screen is how the last bug happened, and a prefab nobody instantiates is exactly
        /// the kind of thing that gets edited by mistake a month later.
        /// </summary>
        private static void DeleteOldShopPrefabs()
        {
            DeleteIfPresent(OldShopScreenPath);
            DeleteIfPresent(OldShopRowPath);
        }

        private static void DeleteIfPresent(string path)
        {
            if (AssetDatabase.LoadAssetAtPath<Object>(path) != null)
            {
                AssetDatabase.DeleteAsset(path);
            }
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>
        /// Builds a prefab, or runs the same steps over the one that is already there. Editing
        /// the existing asset rather than replacing it is what keeps a reference somebody dragged
        /// in by hand, and what keeps the guid the scene points at.
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

        /// <summary>
        /// Finds or creates a rect, and says which it did. Callers shape what they just made and
        /// leave what was already there alone; that one flag is the whole of this tool's promise
        /// that running it twice is safe.
        /// </summary>
        private static RectTransform EnsureRect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, out bool created)
        {
            created = parent.Find(name) == null;
            return UiBuilder.EnsureRect(name, parent, anchorMin, anchorMax);
        }

        private static RectTransform EnsureRect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax)
        {
            return UiBuilder.EnsureRect(name, parent, anchorMin, anchorMax);
        }

        /// <summary>The same for a component: added and configured, or found and left as it is.</summary>
        private static T EnsureComponent<T>(GameObject target, out bool created) where T : Component
        {
            created = target.GetComponent<T>() == null;
            return UiBuilder.Ensure<T>(target);
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
            bool raycastTarget = false)
        {
            Transform found = parent.Find(name);
            bool created = found == null || found.GetComponent<Image>() == null;

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
                // something behind it. Only the row frame, Buy, the tabs and the close X take input.
                image.raycastTarget = raycastTarget;

                // An image with no sprite is a white block, and every slot it could sit in is
                // already drawn by the art behind it. The views turn it back on when they have
                // something to put there.
                image.enabled = image.sprite != null;
            }

            return rect;
        }

        /// <summary>
        /// A label, configured only when it is new, so a font size or a style set in the
        /// inspector is not undone by the next run.
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
        /// A tab. Its sprite is set here and swapped at runtime by <see cref="ShopTabsView"/>;
        /// Sliced when it is first made, because the strip is a little narrower than the art it
        /// is cut from - and left however it was set after that.
        /// </summary>
        private static Button EnsureTabButton(string name, Transform parent, string spritePath, Vector2 anchorMin, Vector2 anchorMax)
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
                image.type = Image.Type.Sliced;
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

        /// <summary>A fixed size at the parent's centre, for something a layout group places.</summary>
        private static void PlaceFixed(RectTransform rect, Vector2 size)
        {
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = size;
        }

        /// <summary>The same, hung from the top edge, which is how a row sits in a list.</summary>
        private static void PlaceTop(RectTransform rect, Vector2 size)
        {
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = size;
        }
    }
}
#endif
