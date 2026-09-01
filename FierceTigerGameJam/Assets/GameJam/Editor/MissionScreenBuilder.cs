#if UNITY_EDITOR
using GameJam.Config;
using GameJam.Economy;
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
    /// Authors the mission board - the screen PLAY opens - out of the supplied art, and puts it
    /// into the scene in place of the old map list.
    ///
    /// Built the same way as the garage, for the same reasons: prefabs rather than scene objects,
    /// so there is one description of the screen and the scene holds nothing but an instance of
    /// it; and everything positioned by anchor fractions of its parent read off the frame sprite's
    /// own pixel geometry, so <see cref="FrameSize"/> is the single number that scales the screen.
    /// The frame is placed exactly as the garage's - same numbers, same 975x1436 art - so the two
    /// screens do not jump when the player switches between them.
    ///
    /// Nothing is written twice. Every position, component setting and reference is filled in only
    /// when the thing it belongs to did not already exist, because the numbers below are a
    /// starting point and the prefab is where the screen is actually tuned. A builder that rewrote
    /// those on every run would make re-running it cost the tuning, which is the same as not being
    /// able to run it again. Deleting the prefab is how you ask for the numbers below back.
    /// </summary>
    public static class MissionScreenBuilder
    {
        private const string MissionTextures = "Assets/GameJam/Textures/UI/SelectMission";
        private const string MenuTextures = "Assets/GameJam/Textures/UI/MainMenu";
        private const string GarageTextures = "Assets/GameJam/Textures/UI/Garage";

        private const string FrameSprite = MissionTextures + "/UI_Mission_Frame.png";
        private const string BannerSprite = MissionTextures + "/UI_Mission_Banner.png";
        private const string BadgeSprite = MissionTextures + "/UI_Level_Badge.png";
        private const string RetrySprite = MissionTextures + "/Btn_Retry.png";
        private const string PlaySprite = MissionTextures + "/Btn_Play_Small.png";
        private const string LockedSprite = MissionTextures + "/Btn_Locked.png";
        /// <summary>
        /// The same full-screen art the main menu stands on. The board used to sit on a dim alone,
        /// which left the cannon barrel visible through it - the board is a menu, not an overlay on
        /// the run, so it gets the menu's ground.
        /// </summary>
        private const string BackgroundSprite = MenuTextures + "/UI_MainMenu_BG.png";

        private const string PrevSprite = MissionTextures + "/Btn_Mission_Prev.png";
        private const string NextSprite = MissionTextures + "/Btn_Mission_Next.png";

        private const string MissionFolder = "Assets/GameJam/Prefabs/UI/Mission";
        private const string ItemPrefabPath = MissionFolder + "/MissionProgressItemView.prefab";
        internal const string ScreenPrefabPath = MissionFolder + "/MissionScreen.prefab";

        private const string OldMapSelectFolder = "Assets/GameJam/Prefabs/UI/MapSelect";
        private const string OldListPrefabPath = OldMapSelectFolder + "/MapListView.prefab";
        private const string OldButtonPrefabPath = OldMapSelectFolder + "/SelectMapButton.prefab";

        internal const string ScreenName = "MissionScreen";
        private const string OldScreenName = "MapListView";

        /// <summary>
        /// The one number to tune, at the frame sprite's own aspect (975:1436). Everything inside
        /// the frame is a fraction of it. Identical to the garage's on purpose.
        /// </summary>
        private static readonly Vector2 FrameSize = new Vector2(660f, 972f);

        /// <summary>
        /// Hangs the frame off the top of the screen. It used to duck under a row of chips and a
        /// close button; with those gone the board can start higher and simply be bigger.
        /// </summary>
        private static readonly Vector2 FrameOffset = new Vector2(0f, -48f);

        /// <summary>
        /// What the frame measured before the top row was retired. Only a frame still carrying
        /// exactly these numbers is resized, so a board someone has since tuned by hand keeps the
        /// size they gave it - the same "replace the known stale value, never a chosen one" rule
        /// the rest of this builder follows.
        /// </summary>
        private static readonly Vector2 PreviousFrameSize = new Vector2(600f, 884f);

        private static readonly Vector2 PreviousFrameOffset = new Vector2(0f, -96f);

        /// <summary>
        /// The bottom bar navigates now, so the board no longer carries its own gold chip, its own
        /// mission counter or a close button. Named here so a re-run clears them from a board that
        /// was built before the change rather than leaving them floating over the new frame.
        /// </summary>
        private static readonly string[] RetiredChildren = { "MoneyChip", "MissionChip", "CloseButton" };

        /// <summary>232x209 of card art at the frame's 600/975 scale, so three fit across the inset.</summary>
        private static readonly Vector2 CardSize = new Vector2(143f, 129f);

        /// <summary>The grid's gap, horizontal and vertical.</summary>
        private static readonly Vector2 RowSpacing = new Vector2(17f, 33f);

        /// <summary>
        /// How many cards sit across before the grid wraps to the next line.
        ///
        /// Constraining by columns, not rows, and the difference is the whole of two layout bugs.
        /// A row-constrained grid fills top-to-bottom down each column before starting the next, so
        /// a mission of three drew a single vertical stack rather than three cards side by side,
        /// and a mission of nine filled column-first instead of reading left to right. Column-
        /// constrained fills across and wraps down, which is what the reference board shows: nine
        /// cards as 3x3, three cards as one line.
        ///
        /// It follows that the board overflows downward rather than sideways - a grid fixed at
        /// three columns can never be wider than three columns - so the list scrolls vertically.
        /// </summary>
        private const int GridColumns = 3;

        /// <summary>
        /// Top centre. The fitter sizes the grid to its own content, so anchoring the middle of
        /// that content to the middle of the viewport is what centres the cards - a child alignment
        /// alone cannot, because the rect it would align within is already hugging the cards.
        /// </summary>
        private static readonly Vector2 RowAnchorMin = new Vector2(0.5f, 1f);
        private static readonly Vector2 RowAnchorMax = new Vector2(0.5f, 1f);
        private static readonly Vector2 RowPivot = new Vector2(0.5f, 1f);

        /// <summary>
        /// What the board measured while it was a three-across grid that scrolled downward. Only a
        /// board still carrying exactly these is flipped to a row, so one somebody has since
        /// re-anchored keeps what they gave it.
        /// </summary>
        private static readonly Vector2 PreviousGridAnchorMin = new Vector2(0f, 1f);
        private static readonly Vector2 PreviousGridAnchorMax = new Vector2(1f, 1f);
        private static readonly Vector2 PreviousGridPivot = new Vector2(0.5f, 1f);

        /// <summary>
        /// And the left-edge anchors the single-row shape used. Recognising only the original grid
        /// was why the cards stayed pinned to the left after the row was retired: the layout flipped
        /// back to a grid but the rect it lived in was still hung off the viewport's left edge, so
        /// centring the content inside it had nothing to centre against.
        /// </summary>
        private static readonly Vector2 PreviousRowAnchorMin = new Vector2(0f, 0f);
        private static readonly Vector2 PreviousRowAnchorMax = new Vector2(0f, 1f);
        private static readonly Vector2 PreviousRowPivot = new Vector2(0f, 0.5f);
        private const int PreviousGridColumns = 3;

        /// <summary>The single-row shape that briefly replaced the grid, recognised so it migrates too.</summary>
        private const int PreviousRowCount = 1;

        internal const string MissionConfigPath = "Assets/GameJam/Config/MissionConfig.asset";

        /// <summary>
        /// Where the campaign stops and the dev maps start in the MapConfig, which is how the
        /// board's two missions were authored before they became data. Seeding by INDEX rather
        /// than by a list of ids on purpose: Brief 25 §3 renames every one of those ids, and a
        /// hard-coded id table here would seed a wrong board the first time anyone re-created the
        /// asset afterwards.
        /// </summary>
        private const int CampaignMissionSize = 9;

        /// <summary>85x87 of button art at 0.615, which is what the mock draws on the card.</summary>
        private static readonly Vector2 ActionSize = new Vector2(52f, 54f);

        /// <summary>The screen sits above the bottom bar, which is on screen at the same time.</summary>
        private static readonly Vector2 ScreenAnchorMin = new Vector2(0f, 0.135f);
        private static readonly Vector2 ScreenAnchorMax = new Vector2(1f, 1f);

        /// <summary>The mock's brown, #7A4A1E, on the clipboard paper the title is printed on.</summary>
        private static readonly Color TitleColor = new Color(122f / 255f, 74f / 255f, 30f / 255f);

        [MenuItem("Tools/Smashdown/Build Mission Screen")]
        public static void BuildMissionScreen()
        {
            EnsureFolder(MissionFolder);

            GameObject item = BuildItemPrefab();
            GameObject screen = BuildScreenPrefab(item);

            // The scene first and the deletions after: the old prefab may not be thrown away while
            // an instance of it is still standing in the scene.
            bool placed = EnsureSceneInstance(screen);
            if (placed)
            {
                DeleteOldMapSelectPrefabs();
            }

            AssetDatabase.SaveAssets();
            Debug.Log(
                "Built the mission screen into " + ScreenPrefabPath + " and put an instance of it in the "
                + "scene. Save the scene: the old map list prefab has been deleted, and a scene still "
                + "holding the unsaved reference to it would come back broken.");
        }

        // ------------------------------------------------------------------ card

        /// <summary>
        /// One level's card. The clipboard, the clip and the hazard stripe are all painted into
        /// the badge sprite, so what is built here is the two things that change - the level's
        /// name and its one button - and nothing else.
        /// </summary>
        private static GameObject BuildItemPrefab()
        {
            return EnsurePrefab(ItemPrefabPath, "MissionProgressItemView", (root, created) =>
            {
                RectTransform rect = (RectTransform)root.transform;
                if (created)
                {
                    PlaceTop(rect, CardSize);
                }

                EnsureImage("Frame", rect, BadgeSprite, Vector2.zero, Vector2.one, Image.Type.Simple, false);

                TMP_Text title = EnsureLabel("Title", rect, "LEVEL 1", 20,
                    TextAlignmentOptions.Center, new Vector2(0.1f, 0.62f), new Vector2(0.9f, 0.82f),
                    out bool titleCreated);
                if (titleCreated)
                {
                    Place((RectTransform)title.transform, new Vector2(0.1f, 0.62f), new Vector2(0.9f, 0.82f));
                    title.fontStyle = FontStyles.Bold;
                    title.color = TitleColor;

                    // Off, so the name cannot be the thing a tap lands on: the card's button is
                    // the button, and the rest of the card is deliberately not one.
                    title.raycastTarget = false;
                }

                bool actionCreated = rect.Find("Action") == null;
                Button action = UiBuilder.EnsureSpriteButton("Action", rect, PlaySprite,
                    new Vector2(0.5f, 0.45f), new Vector2(0.5f, 0.45f));
                if (actionCreated)
                {
                    PlaceFixed((RectTransform)action.transform, new Vector2(0.5f, 0.45f), ActionSize);

                    // None rather than ColorTint: the three states are three different pictures,
                    // and a locked card tinted grey on top of already being a padlock reads as a
                    // bug rather than as a lock.
                    action.transition = Selectable.Transition.None;

                    if (action.targetGraphic is Image actionImage)
                    {
                        actionImage.preserveAspect = true;
                    }
                }

                MissionProgressItemView view = UiBuilder.Ensure<MissionProgressItemView>(root);
                SerializedObject serialized = new SerializedObject(view);
                UiBuilder.SetIfEmpty(serialized, "title", title);
                UiBuilder.SetIfEmpty(serialized, "action", action);
                UiBuilder.SetIfEmpty(serialized, "actionImage", action.targetGraphic as Image);
                UiBuilder.SetIfEmpty(serialized, "retrySprite", UiBuilder.LoadSprite(RetrySprite));
                UiBuilder.SetIfEmpty(serialized, "playSprite", UiBuilder.LoadSprite(PlaySprite));
                UiBuilder.SetIfEmpty(serialized, "lockedSprite", UiBuilder.LoadSprite(LockedSprite));
                serialized.ApplyModifiedPropertiesWithoutUndo();
            });
        }

        // ------------------------------------------------------------------ screen

        /// <summary>
        /// The whole panel. The MISSION title tab and the dark inset the board sits in are painted
        /// into the frame sprite, and the briefing text and the CONTRACT stamp into the banner, so
        /// what is built here is the empty boxes over them and nothing else.
        ///
        /// The banner is fixed and only the grid scrolls: it is the same sentence on every level,
        /// and a heading that slides away as the player reaches for a level four rows down is a
        /// heading that was never worth the room it took.
        /// </summary>
        private static GameObject BuildScreenPrefab(GameObject itemPrefab)
        {
            return EnsurePrefab(ScreenPrefabPath, ScreenName, (root, created) =>
            {
                RectTransform rect = (RectTransform)root.transform;
                if (created)
                {
                    Place(rect, ScreenAnchorMin, ScreenAnchorMax);
                }

                RemoveRetiredChildren(rect);
                UiBuilder.EnsureBackdrop(rect);
                EnsureBackground(rect);

                bool frameCreated = rect.Find("Frame") == null;
                RectTransform frame = EnsureImage("Frame", rect, FrameSprite,
                    Vector2.zero, Vector2.one, Image.Type.Simple, false);

                if (!frameCreated
                    && frame.sizeDelta == PreviousFrameSize
                    && frame.anchoredPosition == PreviousFrameOffset)
                {
                    // Grown into the space the retired chips used to occupy. Guarded on the old
                    // numbers so this fires once, on a board that has not been touched since.
                    frame.sizeDelta = FrameSize;
                    frame.anchoredPosition = FrameOffset;
                }

                if (frameCreated)
                {
                    // Pinned to the top edge at a fixed size rather than stretched: the frame art
                    // has one aspect, and a taller phone should leave more room under it, not a
                    // taller frame. The same placement as the garage's, so switching between the
                    // two screens does not move the panel.
                    frame.anchorMin = new Vector2(0.5f, 1f);
                    frame.anchorMax = new Vector2(0.5f, 1f);
                    frame.pivot = new Vector2(0.5f, 1f);
                    frame.sizeDelta = FrameSize;
                    frame.anchoredPosition = FrameOffset;
                }

                // The word MISSION used to be painted into the frame art; the tab is blank now so
                // the board can say which mission it is showing. Same white bold as the art had.
                TMP_Text missionTitle = EnsureLabel("MissionTitle", frame, "MISSION 1", 30,
                    TextAlignmentOptions.Center, new Vector2(0.30f, 0.947f), new Vector2(0.70f, 0.994f),
                    out bool missionTitleCreated);
                if (missionTitleCreated)
                {
                    Place((RectTransform)missionTitle.transform, new Vector2(0.30f, 0.947f), new Vector2(0.70f, 0.994f));
                    missionTitle.fontStyle = FontStyles.Bold;
                    missionTitle.color = Color.white;
                    missionTitle.raycastTarget = false;
                }

                // The paging chevrons sit in the tab's ends. Their rects are wider than the art -
                // preserveAspect letterboxes the chevron inside - so a thumb has something to hit.
                bool prevCreated = frame.Find("PrevMissionButton") == null;
                Button previousMission = UiBuilder.EnsureSpriteButton("PrevMissionButton", frame, PrevSprite,
                    new Vector2(0.30f, 0.971f), new Vector2(0.30f, 0.971f));
                if (prevCreated)
                {
                    PlaceFixed((RectTransform)previousMission.transform, new Vector2(0.30f, 0.971f), new Vector2(46f, 44f));
                    if (previousMission.targetGraphic is Image previousImage)
                    {
                        previousImage.preserveAspect = true;
                    }
                }

                bool nextCreated = frame.Find("NextMissionButton") == null;
                Button nextMission = UiBuilder.EnsureSpriteButton("NextMissionButton", frame, NextSprite,
                    new Vector2(0.70f, 0.971f), new Vector2(0.70f, 0.971f));
                if (nextCreated)
                {
                    PlaceFixed((RectTransform)nextMission.transform, new Vector2(0.70f, 0.971f), new Vector2(46f, 44f));
                    if (nextMission.targetGraphic is Image nextImage)
                    {
                        nextImage.preserveAspect = true;
                    }
                }

                // The answer to tapping a level that has no map yet. Lives over the board's
                // middle and stays inactive until the panel needs it, so it costs the layout
                // nothing the rest of the time.
                TMP_Text notice = EnsureLabel("NoticeLabel", frame, "NO MAP YET!", 40,
                    TextAlignmentOptions.Center, new Vector2(0.15f, 0.42f), new Vector2(0.85f, 0.52f),
                    out bool noticeCreated);
                if (noticeCreated)
                {
                    Place((RectTransform)notice.transform, new Vector2(0.15f, 0.42f), new Vector2(0.85f, 0.52f));
                    notice.fontStyle = FontStyles.Bold;
                    notice.color = Color.white;
                    notice.raycastTarget = false;
                    notice.gameObject.SetActive(false);
                }

                RectTransform inset = EnsureRect("Inset", frame,
                    new Vector2(0.047f, 0.040f), new Vector2(0.953f, 0.896f));

                EnsureImage("Banner", inset, BannerSprite,
                    new Vector2(0.071f, 0.697f), new Vector2(0.929f, 0.950f), Image.Type.Simple, true);

                RectTransform grid = BuildGrid(inset);



                WirePanelView(root, grid, itemPrefab);

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
        /// The scrolling board: the grid of cards it always was, overflowing sideways.
        ///
        /// Still a <see cref="GridLayoutGroup"/>, constrained by rows rather than the
        /// <see cref="HorizontalLayoutGroup"/> the brief offered as the alternative. A grid gives
        /// every card the authored <see cref="CardSize"/> whatever the card prefab happens to
        /// measure, which is what keeps 143x129 true; a horizontal group would size each child
        /// from its own rect and leave the card's size an accident of the prefab. Keeping the
        /// component that is already there also means a re-run flips two fields instead of
        /// destroying and re-adding a layout, so nothing tuned on it is lost.
        /// </summary>
        private static RectTransform BuildGrid(RectTransform inset)
        {
            RectTransform list = EnsureRect("List", inset,
                new Vector2(0.02f, 0.02f), new Vector2(0.98f, 0.650f));

            RectTransform viewport = EnsureRect("Viewport", list, Vector2.zero, Vector2.one);
            UiBuilder.Ensure<RectMask2D>(viewport.gameObject);

            RectTransform grid = EnsureRect("Grid", viewport,
                RowAnchorMin, RowAnchorMax, out bool gridCreated);
            if (gridCreated)
            {
                ShapeRowContent(grid);
            }
            else if ((grid.anchorMin == PreviousGridAnchorMin
                      && grid.anchorMax == PreviousGridAnchorMax
                      && grid.pivot == PreviousGridPivot)
                     || (grid.anchorMin == PreviousRowAnchorMin
                         && grid.anchorMax == PreviousRowAnchorMax
                         && grid.pivot == PreviousRowPivot))
            {
                // Was hung from the top to grow downward; now hung from the left to grow
                // rightward. Guarded on the exact old numbers so this fires once, on a board
                // nobody has re-anchored since - the same rule the frame resize above follows.
                grid.anchorMin = RowAnchorMin;
                grid.anchorMax = RowAnchorMax;
                grid.pivot = RowPivot;
                ShapeRowContent(grid);
            }

            GridLayoutGroup layout = EnsureComponent<GridLayoutGroup>(grid.gameObject, out bool layoutCreated);
            if (layoutCreated)
            {
                layout.cellSize = CardSize;
                layout.spacing = RowSpacing;
                ShapeRowLayout(layout);
            }
            else if ((layout.constraint == GridLayoutGroup.Constraint.FixedColumnCount
                      && layout.constraintCount == PreviousGridColumns)
                     || (layout.constraint == GridLayoutGroup.Constraint.FixedRowCount
                         && (layout.constraintCount == PreviousRowCount
                             || layout.constraintCount == GridColumns)))
            {
                // Two earlier shapes to bring forward: the original three-across grid that grew
                // downward, and the single row that briefly replaced it. Both are recognised by
                // their exact numbers, so a board somebody has since tuned keeps what they gave it.
                ShapeRowLayout(layout);
            }

            ContentSizeFitter fitter = EnsureComponent<ContentSizeFitter>(grid.gameObject, out bool fitterCreated);
            if (fitterCreated
                || (fitter.horizontalFit == ContentSizeFitter.FitMode.Unconstrained
                    && fitter.verticalFit == ContentSizeFitter.FitMode.PreferredSize))
            {
                // The grid is as wide as its columns need and as tall as the viewport, so width
                // is what the fitter has to work out.
                fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
                fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            }

            ScrollRect scroll = EnsureComponent<ScrollRect>(list.gameObject, out bool scrollCreated);
            if (scrollCreated)
            {
                // Clamped rather than elastic: a short mission does not fill the viewport, and a
                // board that bounces when there is nothing to scroll reads as broken.
                scroll.movementType = ScrollRect.MovementType.Clamped;
                scroll.viewport = viewport;
                scroll.content = grid;
            }

            if (scrollCreated || (!scroll.horizontal && scroll.vertical))
            {
                // Down, not sideways: three fixed columns can never be wider than the viewport,
                // so a horizontal scroll would have nothing to move. A fourth row is what a long
                // mission produces, and that is what has to be reachable.
                scroll.horizontal = false;
                scroll.vertical = true;
            }

            return grid;
        }

        /// <summary>Hangs the row off the left edge, full viewport height, growing rightward.</summary>
        private static void ShapeRowContent(RectTransform grid)
        {
            grid.pivot = new Vector2(0f, 0.5f);
            grid.anchoredPosition = Vector2.zero;
            grid.sizeDelta = Vector2.zero;
        }

        /// <summary>
        /// A fixed number of rows, however many cards the mission has. FixedRowCount rather than FixedColumnCount
        /// so the row never wraps: a mission with ten maps must be ten cards long and scroll, not
        /// two rows of five on one phone and three of four on another.
        /// </summary>
        private static void ShapeRowLayout(GridLayoutGroup layout)
        {
            layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            layout.constraintCount = GridColumns;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.padding = new RectOffset(0, 0, 0, 0);
        }

        private static void WirePanelView(GameObject root, RectTransform grid, GameObject itemPrefab)
        {
            MissionPanelView view = UiBuilder.Ensure<MissionPanelView>(root);
            SerializedObject serialized = new SerializedObject(view);
            UiBuilder.SetIfEmpty(serialized, "mapSelection", UiBuilder.LoadFirstAsset<MapSelection>());
            UiBuilder.SetIfEmpty(serialized, "missionConfig", EnsureMissionConfig());
            UiBuilder.SetIfEmpty(serialized, "container", grid);
            UiBuilder.SetIfEmpty(serialized, "itemPrefab",
                itemPrefab != null ? itemPrefab.GetComponent<MissionProgressItemView>() : null);
            Transform frameChild = root.transform.Find("Frame");
            if (frameChild != null)
            {
                Transform title = frameChild.Find("MissionTitle");
                Transform previous = frameChild.Find("PrevMissionButton");
                Transform next = frameChild.Find("NextMissionButton");
                UiBuilder.SetIfEmpty(serialized, "missionTitle", title != null ? title.GetComponent<TMP_Text>() : null);
                UiBuilder.SetIfEmpty(serialized, "previousMissionButton", previous != null ? previous.GetComponent<Button>() : null);
                UiBuilder.SetIfEmpty(serialized, "nextMissionButton", next != null ? next.GetComponent<Button>() : null);
                Transform notice = frameChild.Find("NoticeLabel");
                UiBuilder.SetIfEmpty(serialized, "noticeLabel", notice != null ? notice.GetComponent<TMP_Text>() : null);
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }



        // ------------------------------------------------------------------ missions

        /// <summary>
        /// The mission config, seeded once from the map registry and never rewritten afterwards.
        ///
        /// The seed reproduces the two missions the board used to carry in its own inspector -
        /// the nine campaign maps, then the dev maps - by taking the MapConfig in order and
        /// splitting it at <see cref="CampaignMissionSize"/>, which is exactly the order those
        /// arrays were authored in. Only ever filled while it is empty: after that the asset is
        /// where missions are actually arranged, and a builder that reseeded it every run would
        /// make re-running the builder cost the arrangement.
        /// </summary>
        private static MissionConfig EnsureMissionConfig()
        {
            MissionConfig config = AssetDatabase.LoadAssetAtPath<MissionConfig>(MissionConfigPath);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<MissionConfig>();
                AssetDatabase.CreateAsset(config, MissionConfigPath);
                Debug.Log($"{nameof(MissionScreenBuilder)} created {MissionConfigPath}.");
            }

            MapConfig maps = UiBuilder.LoadFirstAsset<MapConfig>();
            SerializedObject serialized = new SerializedObject(config);
            UiBuilder.SetIfEmpty(serialized, "maps", maps);

            SerializedProperty missions = serialized.FindProperty("missions");
            if (missions.arraySize == 0 && maps != null && maps.Count > 0)
            {
                int campaign = Mathf.Min(CampaignMissionSize, maps.Count);
                int reserve = maps.Count - campaign;

                missions.arraySize = reserve > 0 ? 2 : 1;
                FillMission(missions.GetArrayElementAtIndex(0), "mission_1", "MISSION 1", maps, 0, campaign);
                if (reserve > 0)
                {
                    FillMission(missions.GetArrayElementAtIndex(1), "mission_2", "MISSION 2", maps, campaign, reserve);
                }

                Debug.Log(
                    $"Seeded {MissionConfigPath} with {missions.arraySize} mission(s) from "
                    + $"{maps.name}: {campaign} campaign map(s) then {reserve} more.");
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(config);
            return config;
        }

        private static void FillMission(
            SerializedProperty mission,
            string id,
            string displayName,
            MapConfig maps,
            int firstMap,
            int count)
        {
            mission.FindPropertyRelative("id").stringValue = id;
            mission.FindPropertyRelative("displayName").stringValue = displayName;

            SerializedProperty mapIds = mission.FindPropertyRelative("mapIds");
            mapIds.arraySize = count;
            for (int i = 0; i < count; i++)
            {
                MapInfo map = maps.Get(firstMap + i);
                mapIds.GetArrayElementAtIndex(i).stringValue = map != null ? map.Id : string.Empty;
            }
        }

        // ------------------------------------------------------------------ scene

        /// <summary>
        /// Puts the mission screen into the scene in place of the old map list and points the flow
        /// at it.
        ///
        /// The whole old instance goes, which Unity allows - it is only the children of an instance
        /// that may not be removed from a scene - and taking it out is what leaves the flow's
        /// selection reference empty for <see cref="UiBuilder.SetIfEmpty"/> to fill: a reference to
        /// a destroyed object reads as null, but only once the destroy has actually happened, which
        /// is why the order of these two steps matters.
        /// </summary>
        private static bool EnsureSceneInstance(GameObject prefab)
        {
            if (prefab == null)
            {
                return false;
            }

            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded)
            {
                Debug.LogWarning("The mission prefab was built, but there is no loaded scene to put it in.");
                return false;
            }

            Canvas canvas = Object.FindFirstObjectByType<Canvas>(FindObjectsInactive.Include);
            if (canvas == null)
            {
                Debug.LogWarning("The mission prefab was built, but the scene has no Canvas to put it under.");
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
                // left where it is: it may have been moved on purpose, and the flow switches it on
                // and off by itself whatever state it was saved in.
                RectTransform rect = (RectTransform)instance.transform;
                rect.anchorMin = ScreenAnchorMin;
                rect.anchorMax = ScreenAnchorMax;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
                instance.SetActive(false);
            }

            GameFlowController flow = Object.FindFirstObjectByType<GameFlowController>(FindObjectsInactive.Include);
            if (flow != null)
            {
                SerializedObject serialized = new SerializedObject(flow);
                UiBuilder.SetIfEmpty(serialized, "mapSelectionRoot", instance);
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            else
            {
                Debug.LogWarning(
                    "The mission screen is in the scene but there is no GameFlowController to wire it to, "
                    + "so PLAY will not open it.");
            }

            EditorSceneManager.MarkSceneDirty(scene);
            return true;
        }

        /// <summary>
        /// The old map list and its button, gone rather than left behind. Two descriptions of the
        /// same screen is how the last bug happened, and a prefab nobody instantiates is exactly
        /// the kind of thing that gets edited by mistake a month later.
        /// </summary>
        private static void DeleteOldMapSelectPrefabs()
        {
            DeleteIfPresent(OldListPrefabPath);
            DeleteIfPresent(OldButtonPrefabPath);
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
        /// Clears the controls the bottom bar took over. Deleting rather than hiding: a hidden
        /// object is still something a later reader has to work out the purpose of, and the bar
        /// is not going away.
        /// </summary>
        /// <summary>
        /// The screen's own ground, in front of the backdrop and behind everything else.
        ///
        /// Sits after the backdrop in sibling order rather than replacing it: the backdrop covers
        /// the whole canvas including the strip under the bottom bar, while this is the art the
        /// board itself stands on. Full-bleed and raycast-off, so it is scenery and never eats a
        /// tap meant for a card.
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

            // Enforced every run: being behind the board is the whole job, and the backdrop must
            // stay behind this in turn.
            background.SetSiblingIndex(1);
        }

        private static void RemoveRetiredChildren(RectTransform rect)
        {
            for (int i = 0; i < RetiredChildren.Length; i++)
            {
                Transform child = rect.Find(RetiredChildren[i]);
                if (child != null)
                {
                    Object.DestroyImmediate(child.gameObject);
                }
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
                // something behind it. Only a card's button and the close X take input here.
                image.raycastTarget = raycastTarget;

                // An image with no sprite is a white block over art that is already drawn.
                image.enabled = image.sprite != null;
            }

            return rect;
        }

        /// <summary>
        /// A label, configured only when it is new, so a font size or a style set in the inspector
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

        /// <summary>Anchors with no offsets: the whole layout is fractions of its parent.</summary>
        private static void Place(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        /// <summary>A fixed size at one point of the parent, for art that must not stretch.</summary>
        private static void PlaceFixed(RectTransform rect, Vector2 anchor, Vector2 size)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = size;
        }

        /// <summary>The same, hung from the top edge, which is how a card sits in a grid.</summary>
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
