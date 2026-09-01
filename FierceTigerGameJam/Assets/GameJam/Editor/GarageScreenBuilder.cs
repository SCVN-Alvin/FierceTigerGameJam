#if UNITY_EDITOR
using GameJam.Economy;
using GameJam.Gameplay.Combat;
using GameJam.Gameplay.Flow;
using GameJam.Gameplay.Tool;
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
    ///
    /// The exceptions are meant to be read as exceptions, and there are three:
    ///
    ///   - <see cref="MoveRootSelectToChild"/>, because a row whose equip target is still the row
    ///     root would hide itself the moment its item was equipped;
    ///   - <see cref="ClearBakedIcon"/>, because a sprite baked into a row prefab is a stand-in
    ///     rather than a choice - every row's picture comes from its bind;
    ///   - the preview rig's culling mask, render target and starting state, rewritten on every
    ///     run in <see cref="EnsurePreviewRig"/>, because those three are the whole of the rig's
    ///     containment and a drifted one draws the playfield into the garage.
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
        private const string SelectSprite = GarageTextures + "/Btn_Select.png";
        private const string EquippedSprite = GarageTextures + "/UI_Equipped.png";
        private const string LockedSprite = GarageTextures + "/UI_Locked.png";
        private const string CloseSprite = GarageTextures + "/Btn_Esc.png";
        private const string MoneySprite = MenuTextures + "/UI_Money.png";

        /// <summary>
        /// The same full-screen art the main menu and the mission board stand on. The garage had a
        /// dim and nothing else, so the world showed through it - it is a menu, not an overlay on
        /// the run, and it gets the menu's ground.
        /// </summary>
        private const string BackgroundSprite = MenuTextures + "/UI_MainMenu_BG.png";

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
        private static readonly Vector2 FrameOffset = new Vector2(0f, -96f);

        /// <summary>796x148 of row art at the frame's 600/975 scale, so the 9-slice stays near 1:1.</summary>
        private static readonly Vector2 RowSize = new Vector2(490f, 91f);

        /// <summary>81x43 of pip art at the same scale.</summary>
        private static readonly Vector2 PipSize = new Vector2(50f, 26f);

        /// <summary>
        /// Buy's column, in the band above it: SELECT and the EQUIPPED chip share this one rect,
        /// so whichever of them the row's state calls for, nothing on the row moves. Read off the
        /// row art at 148px tall - 15px clear of the top edge, 15px of gap down to Buy.
        /// </summary>
        private static readonly Vector2 EquipAnchorMin = new Vector2(0.786f, 0.514f);
        private static readonly Vector2 EquipAnchorMax = new Vector2(0.969f, 0.851f);

        /// <summary>The screen sits above the bottom bar, which is on screen at the same time.</summary>
        private static readonly Vector2 ScreenAnchorMin = new Vector2(0f, 0.135f);
        private static readonly Vector2 ScreenAnchorMax = new Vector2(1f, 1f);

        // ---------------------------------------------------------------- 3D preview

        /// <summary>
        /// The layer the preview rig lives on, and the only layer its camera draws. Nothing else
        /// in the project may be put on it: the rig's whole safety is that the main camera cannot
        /// see the layer and the preview camera cannot see anything else.
        /// </summary>
        private const string PreviewLayerName = "Preview";

        /// <summary>
        /// Where the search for a free slot starts. Layers 0-7 are Unity's own; three of them are
        /// blank and editable, and this project has already put "Debris" in one - which is exactly
        /// why they are left alone here. A builtin slot is somebody else's to spend.
        /// </summary>
        private const int FirstUserLayer = 8;

        private const string TagManagerPath = "ProjectSettings/TagManager.asset";

        private const string TexturesFolder = "Assets/GameJam/Textures";

        private const string PreviewTexturePath = TexturesFolder + "/RT_Preview.renderTexture";

        /// <summary>Square, so the fit does not have to care which way the window is longer.</summary>
        private const int PreviewTextureSize = 512;

        /// <summary>The artist's marker for the preview window, one under each panel's Preview.</summary>
        private const string Preview3DName = "Tank_Preview_3D";

        private const string RigObjectName = "PreviewRig";
        private const string RigPivotName = "Pivot";
        private const string RigCameraName = "PreviewCamera";
        private const string RigLightName = "PreviewLight";

        /// <summary>
        /// Far below the playfield. Not a hiding place - the mannequin has no colliders and the
        /// main camera cannot see its layer - but distance costs nothing and means a mistake in
        /// either of those two shows up as a model in the middle of nowhere rather than one in
        /// the middle of the level.
        /// </summary>
        private static readonly Vector3 RigPosition = new Vector3(0f, -50f, 0f);

        private static readonly Vector3 RigCameraLocalPosition = new Vector3(0f, 0f, -4f);

        /// <summary>Roughly a portrait lens: enough perspective to read as 3D, not enough to bow.</summary>
        private const float RigCameraFieldOfView = 30f;

        /// <summary>
        /// The window inside the artist's Preview rect: everything above the caption, with a
        /// hair of margin. Written only when the slot is first turned into a rect.
        /// </summary>
        private static readonly Vector2 Preview3DAnchorMin = new Vector2(0.04f, 0.17f);
        private static readonly Vector2 Preview3DAnchorMax = new Vector2(0.96f, 0.98f);

        [MenuItem("Tools/Smashdown/Build Garage Screen")]
        public static void BuildGarageScreen()
        {
            EnsureFolder(GarageFolder);

            // Before the prefab, because the screen's two preview windows are wired to the
            // texture as they are built. A layer that could not be made takes the whole rig with
            // it - see EnsurePreviewLayer.
            int previewLayer = EnsurePreviewLayer();
            RenderTexture previewTexture = previewLayer >= 0 ? EnsurePreviewTexture() : null;

            GameObject pip = BuildPipPrefab();
            GameObject bulletRow = BuildRowPrefab(BulletRowPrefabPath, "BulletTypeViewItem", pip, false);
            GameObject vehicleRow = BuildRowPrefab(VehicleRowPrefabPath, "VehicleTypeViewItem", pip, true);
            GameObject screen = BuildScreenPrefab(bulletRow, vehicleRow, previewTexture);

            FillMissingIcons();

            // The scene first and the deletions after: the old prefab may not be thrown away
            // while an instance of it is still standing in the scene.
            bool placed = EnsureSceneInstance(screen, out ShopTabsView _, out Button _);
            if (placed)
            {
                DeleteOldShopPrefabs();
            }

            if (previewLayer >= 0)
            {
                EnsurePreviewRig(previewLayer, previewTexture);
            }

            AssetDatabase.SaveAssets();
            Debug.Log(
                "Built the garage into " + ScreenPrefabPath + " and put an instance of it in the scene, "
                + "with the 3D preview rig under the scene's SYSTEM section. Save the scene: the old shop "
                + "prefab has been deleted and the rig lives in the scene, so a scene left unsaved would "
                + "come back both broken and without a preview.");
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

                // A raycast target, though nothing on the row root acts on a click any more: it is
                // what a drag starting over a row has to land on before the list's ScrollRect can
                // pick it up.
                EnsureImage("Frame", rect, RowFrameSprite,
                    Vector2.zero, Vector2.one, Image.Type.Sliced, false, true);

                // The dark slot the icon sits in is baked into the row art's left border, so the
                // icon is only the picture, centred in a slot that never stretches.
                RectTransform icon = EnsureImage("Icon", rect, null,
                    new Vector2(0.053f, 0.25f), new Vector2(0.143f, 0.74f), Image.Type.Simple, true);
                ClearBakedIcon(icon);

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

                // Directly above Buy, in Buy's column. The equip control the shops have always
                // wired but never had a picture for.
                Transform foundSelect = rect.Find("Select");
                bool selectCreated = foundSelect == null || foundSelect.GetComponent<Button>() == null;
                Button select = UiBuilder.EnsureSpriteButton("Select", rect, SelectSprite,
                    EquipAnchorMin, EquipAnchorMax);
                if (selectCreated)
                {
                    Place((RectTransform)select.transform, EquipAnchorMin, EquipAnchorMax);
                    select.transition = Selectable.Transition.ColorTint;

                    if (select.targetGraphic is Image selectImage)
                    {
                        // Simple, not Sliced like Buy: the word is baked into the art and there
                        // are no borders to cut, so the aspect is kept and the band it sits in is
                        // a hair wider than the sprite rather than the sprite being stretched.
                        selectImage.type = Image.Type.Simple;
                        selectImage.preserveAspect = true;
                    }
                }

                // The same rect as Select, and only one of the two is ever up. A chip and not a
                // button: there is nothing to do to the thing already mounted, so it takes no
                // raycast either - EnsureImage leaves that off.
                Transform foundEquipped = rect.Find("Equipped");
                bool equippedCreated = foundEquipped == null || foundEquipped.GetComponent<Image>() == null;
                RectTransform equipped = EnsureImage("Equipped", rect, EquippedSprite,
                    EquipAnchorMin, EquipAnchorMax, Image.Type.Simple, true);
                if (equippedCreated)
                {
                    // Down in the prefab. Every row is spawned before the shop's first Refresh,
                    // and a chip that shipped up would say every item is equipped until then.
                    equipped.gameObject.SetActive(false);
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

                // A migration, not a SetIfEmpty, and the one place this builder overwrites a
                // reference that is already set. Rows authored before this run point selectButton
                // at a Button on the row root - back then the whole row was the equip target -
                // and ShopItemView.Bind now hides whatever that reference names while an item is
                // equipped. Left alone, the equipped row would hide itself. Only a reference to
                // the root is moved; one pointed anywhere else by hand is still left as it is.
                MoveRootSelectToChild(serialized, root, select);

                UiBuilder.SetIfEmpty(serialized, "selectButton", select);
                UiBuilder.SetIfEmpty(serialized, "equippedBadge", equipped.gameObject);
                serialized.ApplyModifiedPropertiesWithoutUndo();

                // And the Button that reference named goes with it. It carried no art of its own
                // - it tinted the frame - and existed only to make the row the equip target. With
                // SELECT doing that, a press anywhere on the row would tint it and do nothing,
                // and a locked row has to be inert. The frame keeps its raycast target, so a drag
                // that starts over a row still reaches the list's ScrollRect.
                Button rowButton = root.GetComponent<Button>();
                if (rowButton != null)
                {
                    Object.DestroyImmediate(rowButton);
                }

                // The dim goes the same way. Being equipped was said twice - by the chip and by
                // every other row being knocked back - and the alpha was the weaker half: it is
                // a comparison across rows, and a row at half strength that can still be bought
                // from reads as disabled. Removed rather than left at alpha 1, so there is no
                // component sitting on the row inviting the cue back, and so a project built
                // before this run is cleaned by re-running the tool. Nothing else in the project
                // reads a CanvasGroup on these rows; the list does not fade them.
                CanvasGroup rowGroup = root.GetComponent<CanvasGroup>();
                if (rowGroup != null)
                {
                    Object.DestroyImmediate(rowGroup, true);
                }
            });
        }

        /// <summary>
        /// Empties the row's icon slot, every run.
        ///
        /// The second place this builder overwrites something already set, and the same kind of
        /// case as <see cref="MoveRootSelectToChild"/>: a sprite baked into the row prefab is not
        /// a different opinion about the art, it is a stand-in the artist left behind while the
        /// icons were being drawn. Every row's picture comes from <c>ShopItemView.Bind</c>, so a
        /// baked one can only ever be one item's icon shown on all of them for the frames before
        /// the first bind - which is the flash a34d7277 removed and the reason blank-until-bound
        /// is the rule here.
        ///
        /// Disabled as well as emptied, which is what <see cref="EnsureImage"/> does for a
        /// sprite-less image: the slot it sits in is already painted into the row art, and an
        /// Image with no sprite is a white block over it.
        /// </summary>
        private static void ClearBakedIcon(RectTransform icon)
        {
            Image image = icon != null ? icon.GetComponent<Image>() : null;
            if (image == null || image.sprite == null)
            {
                return;
            }

            image.sprite = null;
            image.enabled = false;
        }

        /// <summary>
        /// Repoints a row's <c>selectButton</c> from the row root to the SELECT child, and only
        /// from there. The one exception to this builder's rule that a reference already set is
        /// never touched: the old shape - the row root as the equip target - is now actively
        /// wrong rather than merely different, because Bind hides the object selectButton names.
        /// Checked against the root rather than assumed, so a reference somebody pointed at a
        /// button of their own survives the run.
        /// </summary>
        private static void MoveRootSelectToChild(SerializedObject serialized, GameObject root, Button select)
        {
            if (select == null)
            {
                return;
            }

            SerializedProperty property = serialized.FindProperty("selectButton");
            if (property == null)
            {
                return;
            }

            if (property.objectReferenceValue is Button current && current.gameObject == root)
            {
                property.objectReferenceValue = select;
            }
        }

        // ------------------------------------------------------------------ screen

        /// <summary>
        /// The whole panel. The title tab, the blue band behind the tabs, the garage table the
        /// preview stands on and the dark inset the list scrolls in are all painted into the one
        /// frame sprite, so what is built here is the empty boxes over them and nothing else.
        /// </summary>
        private static GameObject BuildScreenPrefab(
            GameObject bulletRow,
            GameObject vehicleRow,
            RenderTexture previewTexture)
        {
            return EnsurePrefab(ScreenPrefabPath, ScreenName, (root, created) =>
            {
                RectTransform rect = (RectTransform)root.transform;
                if (created)
                {
                    Place(rect, ScreenAnchorMin, ScreenAnchorMax);
                }

                UiBuilder.EnsureBackdrop(rect);
                EnsureBackground(rect);

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

                WireShopView(vehiclePanel, true, goldLabel, vehicleRow, previewTexture);
                WireShopView(bulletPanel, false, goldLabel, bulletRow, previewTexture);

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

        private static void WireShopView(
            GameObject panel,
            bool vehicle,
            TMP_Text goldLabel,
            GameObject rowPrefab,
            RenderTexture previewTexture)
        {
            RectTransform rows = panel.transform.Find("List/Viewport/Rows") as RectTransform;
            Transform previewItem = panel.transform.Find("Preview/PreviewItem");
            Transform previewCaption = panel.transform.Find("Preview/PreviewCaption");
            ModelPreviewView preview3D = EnsurePreview3D(
                panel.transform.Find("Preview") as RectTransform, previewTexture);

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
            UiBuilder.SetIfEmpty(serialized, "preview3D", preview3D);
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

        // ------------------------------------------------------------------ 3D preview

        /// <summary>
        /// The layer the rig draws on, added to the project if it is not there yet.
        ///
        /// A free slot is searched for rather than assumed. The obvious shortcut - take 31,
        /// nobody uses it - is how two tools end up sharing a layer and the second one silently
        /// starts rendering the first one's objects; and this project has already spent one of
        /// the blank builtin slots on "Debris", which is the same lesson from the other side.
        ///
        /// Failure is loud and stops the rig being built at all. There is no safe fallback:
        /// putting the preview on a gameplay layer would draw the playfield into the garage
        /// window and hang the mannequin in front of the player, and both of those are worse than
        /// a garage whose preview window is empty.
        /// </summary>
        /// <returns>The layer's index, or -1 when there is none to be had.</returns>
        private static int EnsurePreviewLayer()
        {
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath(TagManagerPath);
            if (assets == null || assets.Length == 0 || assets[0] == null)
            {
                Debug.LogError(
                    $"{nameof(GarageScreenBuilder)} could not open {TagManagerPath}, so it cannot add the "
                    + $"\"{PreviewLayerName}\" layer and the preview rig was not built.");
                return -1;
            }

            SerializedObject tagManager = new SerializedObject(assets[0]);
            SerializedProperty layers = tagManager.FindProperty("layers");
            if (layers == null || !layers.isArray)
            {
                Debug.LogError(
                    $"{nameof(GarageScreenBuilder)} found no layer list in {TagManagerPath}, so the preview "
                    + "rig was not built.");
                return -1;
            }

            for (int i = 0; i < layers.arraySize; i++)
            {
                if (layers.GetArrayElementAtIndex(i).stringValue == PreviewLayerName)
                {
                    return i;
                }
            }

            for (int i = FirstUserLayer; i < layers.arraySize; i++)
            {
                SerializedProperty slot = layers.GetArrayElementAtIndex(i);
                if (!string.IsNullOrEmpty(slot.stringValue))
                {
                    continue;
                }

                slot.stringValue = PreviewLayerName;
                tagManager.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.SaveAssets();
                Debug.Log(
                    $"{nameof(GarageScreenBuilder)} added the \"{PreviewLayerName}\" layer at index {i}. It "
                    + "belongs to the garage's preview rig; nothing else should be put on it.");
                return i;
            }

            Debug.LogError(
                $"{nameof(GarageScreenBuilder)} found no free user layer for \"{PreviewLayerName}\", so the "
                + "preview rig was not built. Sharing a gameplay layer would put the playfield in the "
                + "garage window and the preview model in front of the player. Free one of layers "
                + $"{FirstUserLayer}-31 and run this again.");
            return -1;
        }

        /// <summary>
        /// The texture the rig renders into and the garage draws.
        ///
        /// Built once and never rewritten: the size, the depth and the format are a starting
        /// point somebody may want to change, and a builder that reset them on every run would
        /// make the change impossible to keep. Deleting the asset is how you ask for these
        /// numbers back - the same rule the prefabs here follow.
        ///
        /// Its format carries alpha on purpose. The camera clears to nothing rather than to a
        /// colour, so the garage's own frame art stays the backdrop behind the model instead of a
        /// grey square being pasted over it.
        /// </summary>
        private static RenderTexture EnsurePreviewTexture()
        {
            RenderTexture existing = AssetDatabase.LoadAssetAtPath<RenderTexture>(PreviewTexturePath);
            if (existing != null)
            {
                return existing;
            }

            EnsureFolder(TexturesFolder);

            RenderTexture texture =
                new RenderTexture(PreviewTextureSize, PreviewTextureSize, 16, RenderTextureFormat.ARGB32)
                {
                    name = "RT_Preview",
                    antiAliasing = 1,
                    useMipMap = false,
                    autoGenerateMips = false,
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                };

            AssetDatabase.CreateAsset(texture, PreviewTexturePath);
            Debug.Log($"{nameof(GarageScreenBuilder)} created {PreviewTexturePath}.");
            return texture;
        }

        /// <summary>
        /// Turns the artist's <c>Tank_Preview_3D</c> marker into the window that draws the rig.
        ///
        /// Deviation from Brief 25, which reads the marker as a rect to size a RawImage to. It is
        /// not one: the artist authored it as a plain Transform holding real model prefabs
        /// parented straight into the canvas - the very approach the brief considered and
        /// rejected - at a 3D pose (z -112, scale 80) that means nothing to a UI layout. That
        /// mock-up cannot work as it stands either way: this canvas renders in Screen Space
        /// Overlay, which does not draw meshes at all.
        ///
        /// So the marker is kept for what it is - the artist saying where the preview goes - and
        /// converted: a RectTransform replaces the Transform, it is placed over the window above
        /// the caption, and the mock models under it are switched off rather than deleted, so the
        /// reference is still there for anyone who wants to look at it.
        ///
        /// All of that happens once, on the run that converts. After it the rect is a rect, so a
        /// nudge in the inspector survives - the same promise every other part of this screen
        /// makes. Switching the marker on is the one thing enforced every run: one of the two
        /// shipped switched off, and an inactive window is a preview that never runs at all.
        /// </summary>
        private static ModelPreviewView EnsurePreview3D(RectTransform preview, RenderTexture texture)
        {
            if (preview == null || texture == null)
            {
                // No texture means no layer, which means no rig - see EnsurePreviewLayer. Nothing
                // is converted in that case: the artist's marker is left exactly as it is until
                // the run that can actually finish the job.
                return null;
            }

            Transform slot = preview.Find(Preview3DName);
            if (slot == null)
            {
                // No marker - a panel built before the artist's pass, or a third tab added later.
                // Made rather than skipped, so the preview does not depend on art having landed.
                slot = UiBuilder.EnsureRect(Preview3DName, preview, Preview3DAnchorMin, Preview3DAnchorMax);
            }

            RectTransform rect = EnsurePreview3DRect(slot);
            if (rect == null)
            {
                return null;
            }

            bool created = rect.GetComponent<RawImage>() == null;
            RawImage image = UiBuilder.Ensure<RawImage>(rect.gameObject);
            if (created)
            {
                image.texture = texture;
                image.color = Color.white;

                // Never: there is nothing to tap in the window, and a raycast target over the
                // garage table would only eat drags meant for the list behind it.
                image.raycastTarget = false;

                // Down in the prefab, the same rule the row icons and the EQUIPPED chip follow:
                // the shop switches it on when it has a model to show, and one shipped up would
                // draw whatever the render texture happened to hold last.
                image.enabled = false;
            }

            ModelPreviewView view = UiBuilder.Ensure<ModelPreviewView>(rect.gameObject);
            SerializedObject serialized = new SerializedObject(view);
            UiBuilder.SetIfEmpty(serialized, "image", image);
            UiBuilder.SetIfEmpty(serialized, "previewTexture", texture);
            serialized.ApplyModifiedPropertiesWithoutUndo();

            rect.gameObject.SetActive(true);
            return view;
        }

        /// <summary>
        /// The marker as a rect, converting it the first time and handing back what is already
        /// there afterwards. AddComponent replaces a plain Transform with a RectTransform, which
        /// is the only way to make a UI object out of one; it is checked rather than assumed,
        /// because a null here would otherwise surface as a RawImage that never draws.
        /// </summary>
        private static RectTransform EnsurePreview3DRect(Transform slot)
        {
            if (slot is RectTransform existing)
            {
                return existing;
            }

            GameObject slotObject = slot.gameObject;

            // Before the transform is replaced, while the mock-up's placement is still intact.
            for (int i = slotObject.transform.childCount - 1; i >= 0; i--)
            {
                slotObject.transform.GetChild(i).gameObject.SetActive(false);
            }

            RectTransform rect = slotObject.AddComponent<RectTransform>();
            if (rect == null)
            {
                Debug.LogError(
                    $"{nameof(GarageScreenBuilder)} could not turn \"{Preview3DName}\" into a RectTransform, "
                    + "so that panel has no 3D preview and falls back to the flat icon.");
                return null;
            }

            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
            Place(rect, Preview3DAnchorMin, Preview3DAnchorMax);

            Debug.Log(
                $"{nameof(GarageScreenBuilder)} converted \"{Preview3DName}\" from the artist's in-canvas "
                + "mock-up into the render-texture window, and switched the mock models under it off.");
            return rect;
        }

        /// <summary>
        /// The one rig in the scene: a pivot models spawn under, the camera that draws it into
        /// the texture, and a light of its own.
        ///
        /// Found by its component rather than by its name, so a rig somebody renamed or filed
        /// somewhere else is still the rig - which is also what keeps a second run, with or
        /// without a domain reload, from leaving two of them.
        ///
        /// Poses and settings are written only on creation, except the three that are the whole
        /// point of the rig and are enforced every run: the camera's culling mask, its target
        /// texture, and that it starts switched off. A mask that has drifted is the preview
        /// rendering the playfield; a missing target is the preview rendering over the game.
        /// </summary>
        private static void EnsurePreviewRig(int layer, RenderTexture texture)
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded)
            {
                Debug.LogWarning(
                    $"{nameof(GarageScreenBuilder)} has no loaded scene to put the preview rig in, so the "
                    + "garage will fall back to its flat icons.");
                return;
            }

            ModelPreviewRig rig = Object.FindFirstObjectByType<ModelPreviewRig>(FindObjectsInactive.Include);
            GameObject root;

            if (rig == null)
            {
                root = new GameObject(RigObjectName);
                Undo.RegisterCreatedObjectUndo(root, "Create Preview Rig");
                SceneManager.MoveGameObjectToScene(root, scene);

                Transform section = FindSection(scene, SceneHierarchyOrganizer.SystemHeader);
                if (section != null)
                {
                    root.transform.SetParent(section, false);
                }

                root.transform.position = RigPosition;
                root.transform.rotation = Quaternion.identity;
                root.transform.localScale = Vector3.one;
                rig = Undo.AddComponent<ModelPreviewRig>(root);
            }
            else
            {
                root = rig.gameObject;
            }

            Transform pivot = EnsureRigChild(root.transform, RigPivotName, out bool _);
            Transform cameraObject = EnsureRigChild(root.transform, RigCameraName, out bool cameraCreated);
            Transform lightObject = EnsureRigChild(root.transform, RigLightName, out bool lightCreated);

            Camera camera = UiBuilder.Ensure<Camera>(cameraObject.gameObject);
            if (cameraCreated)
            {
                cameraObject.localPosition = RigCameraLocalPosition;
                cameraObject.localRotation = Quaternion.identity;
                camera.orthographic = false;
                camera.fieldOfView = RigCameraFieldOfView;
                camera.nearClipPlane = 0.05f;
                camera.farClipPlane = 50f;

                // Nothing, not a colour: the garage frame art is what the model should stand on.
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0f, 0f, 0f, 0f);

                camera.useOcclusionCulling = false;
                camera.allowHDR = false;
                camera.allowMSAA = false;
            }

            camera.cullingMask = 1 << layer;
            camera.targetTexture = texture;
            camera.enabled = false;

            Light light = UiBuilder.Ensure<Light>(lightObject.gameObject);
            if (lightCreated)
            {
                // A point light, not the directional one the brief describes. URP ignores a
                // light's culling mask, so a directional light added here would light the whole
                // playfield as a second sun - the mask that was supposed to contain it does
                // nothing. A point light is contained by its own falloff instead: fifty units
                // under the world with a range of twelve, it cannot reach anything the player
                // sees. The scene's own sun still falls on the model, which is harmless because
                // it never moves; this is what makes sure the model is lit whatever it does.
                lightObject.localPosition = new Vector3(1.6f, 2.2f, -2.6f);
                light.type = LightType.Point;
                light.range = 12f;
                light.intensity = 4f;
                light.color = Color.white;
                light.shadows = LightShadows.None;
            }

            light.cullingMask = 1 << layer;

            // Every run, and the whole subtree: a child added by hand on the default layer is a
            // model the preview camera cannot see, which reads as the preview being broken.
            SetLayerRecursively(root.transform, layer);

            SerializedObject serialized = new SerializedObject(rig);
            UiBuilder.SetIfEmpty(serialized, "pivot", pivot);
            UiBuilder.SetIfEmpty(serialized, "previewCamera", camera);
            serialized.ApplyModifiedPropertiesWithoutUndo();

            DropPreviewLayerFromCameras(layer, camera);

            EditorSceneManager.MarkSceneDirty(scene);
        }

        private static Transform EnsureRigChild(Transform parent, string name, out bool created)
        {
            Transform child = parent.Find(name);
            created = child == null;
            if (!created)
            {
                return child;
            }

            GameObject childObject = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(childObject, $"Create {name}");
            child = childObject.transform;
            child.SetParent(parent, false);
            child.localPosition = Vector3.zero;
            child.localRotation = Quaternion.identity;
            child.localScale = Vector3.one;
            return child;
        }

        /// <summary>
        /// Takes the preview layer out of every other camera's culling mask.
        ///
        /// Every camera rather than only the one tagged MainCamera: the mask is what stops a
        /// cannon appearing fifty units under the level, and a second camera nobody remembered
        /// would show it. Only a mask that actually contains the layer is written, so a re-run
        /// over an already-correct scene changes nothing and says nothing.
        /// </summary>
        private static void DropPreviewLayerFromCameras(int layer, Camera previewCamera)
        {
            Camera[] cameras = Object.FindObjectsByType<Camera>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            int bit = 1 << layer;
            for (int i = 0; i < cameras.Length; i++)
            {
                Camera camera = cameras[i];
                if (camera == null || camera == previewCamera || (camera.cullingMask & bit) == 0)
                {
                    continue;
                }

                camera.cullingMask &= ~bit;
                EditorUtility.SetDirty(camera);
                Debug.Log(
                    $"{nameof(GarageScreenBuilder)} dropped the \"{PreviewLayerName}\" layer from "
                    + $"\"{camera.name}\"'s culling mask.",
                    camera);
            }
        }

        private static Transform FindSection(Scene scene, string sectionName)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i].name == sectionName)
                {
                    return roots[i].transform;
                }
            }

            // Not created here. Filing the hierarchy is Organize Scene Hierarchy's job, and it
            // will pick a rootless rig up on its next run.
            return null;
        }

        private static void SetLayerRecursively(Transform node, int layer)
        {
            node.gameObject.layer = layer;
            for (int i = 0; i < node.childCount; i++)
            {
                SetLayerRecursively(node.GetChild(i), layer);
            }
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
        /// <summary>
        /// The screen's own ground, in front of the backdrop and behind everything else.
        ///
        /// Sits after the backdrop rather than replacing it: the backdrop covers the whole canvas
        /// including the strip beneath the bottom bar, while this is the art the garage itself
        /// stands on. Full-bleed and raycast-off, so it is scenery and never eats a tap meant for
        /// a row.
        /// </summary>
        private static void EnsureBackground(RectTransform rect)
        {
            bool created = rect.Find("Background") == null;
            RectTransform background = EnsureImage("Background", rect, BackgroundSprite,
                Vector2.zero, Vector2.one, Image.Type.Simple, false);

            if (created)
            {
                Place(background, Vector2.zero, Vector2.one);
            }

            // Enforced every run: being behind the garage is the whole job, and the backdrop has
            // to stay behind this in turn.
            background.SetSiblingIndex(1);
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
