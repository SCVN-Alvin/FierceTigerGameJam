#if UNITY_EDITOR
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
        private const string MoneySprite = MenuTextures + "/UI_Money.png";
        private const string MissionSprite = MenuTextures + "/UI_Mission.png";
        private const string CloseSprite = GarageTextures + "/Btn_Esc.png";
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
        private static readonly Vector2 FrameSize = new Vector2(600f, 884f);

        /// <summary>Hangs the frame off the top of the screen, clear of the gold chip and the X.</summary>
        private static readonly Vector2 FrameOffset = new Vector2(0f, -96f);

        /// <summary>232x209 of card art at the frame's 600/975 scale, so three fit across the inset.</summary>
        private static readonly Vector2 CardSize = new Vector2(143f, 129f);

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

                bool frameCreated = rect.Find("Frame") == null;
                RectTransform frame = EnsureImage("Frame", rect, FrameSprite,
                    Vector2.zero, Vector2.one, Image.Type.Simple, false);

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

                // Its own gold chip, not the menu's: the main menu root is switched off while the
                // mission screen is up, and the levels are picked with the gold in mind.
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

                RectTransform mission = EnsureImage("MissionChip", rect, MissionSprite,
                    new Vector2(0.60f, 0.926f), new Vector2(0.84f, 0.975f), Image.Type.Simple, true);

                // Right-aligned: the chip art has a flag baked into its left end, so the count
                // sits in the paper beside it rather than over it.
                TMP_Text missionLabel = EnsureLabel("MissionLabel", mission, "0/0", 34,
                    TextAlignmentOptions.Right, new Vector2(0.42f, 0.1f), new Vector2(0.92f, 0.9f),
                    out bool missionCreated);
                if (missionCreated)
                {
                    Place((RectTransform)missionLabel.transform, new Vector2(0.42f, 0.1f), new Vector2(0.92f, 0.9f));
                    missionLabel.raycastTarget = false;
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

                WirePanelView(root, grid, itemPrefab);
                WireGoldView(money.gameObject, goldLabel);
                WireProgressView(mission.gameObject, missionLabel);

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
        /// The scrolling board. Three rows fill the inset exactly at three maps, and the ten-level
        /// plan needs a fourth, so the grid is in a ScrollRect from the start rather than becoming
        /// one the day a fourth row spills over the frame.
        /// </summary>
        private static RectTransform BuildGrid(RectTransform inset)
        {
            RectTransform list = EnsureRect("List", inset,
                new Vector2(0.02f, 0.02f), new Vector2(0.98f, 0.650f));

            RectTransform viewport = EnsureRect("Viewport", list, Vector2.zero, Vector2.one);
            UiBuilder.Ensure<RectMask2D>(viewport.gameObject);

            RectTransform grid = EnsureRect("Grid", viewport,
                new Vector2(0f, 1f), new Vector2(1f, 1f), out bool gridCreated);
            if (gridCreated)
            {
                // Hung from the top and growing downward, which is what the fitter below sizes.
                grid.pivot = new Vector2(0.5f, 1f);
                grid.anchoredPosition = Vector2.zero;
                grid.sizeDelta = Vector2.zero;
            }

            GridLayoutGroup layout = EnsureComponent<GridLayoutGroup>(grid.gameObject, out bool layoutCreated);
            if (layoutCreated)
            {
                layout.cellSize = CardSize;
                layout.spacing = new Vector2(17f, 33f);

                // Fixed at three rather than left to fit: the row count has to be the same on
                // every phone, or the board reads as a different shape on each one.
                layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                layout.constraintCount = 3;
                layout.childAlignment = TextAnchor.UpperCenter;
                layout.padding = new RectOffset(0, 0, 0, 10);
            }

            ContentSizeFitter fitter = EnsureComponent<ContentSizeFitter>(grid.gameObject, out bool fitterCreated);
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

                // Clamped rather than elastic: three rows fill the inset exactly, and a board that
                // bounces when there is nothing to scroll reads as broken.
                scroll.movementType = ScrollRect.MovementType.Clamped;
                scroll.viewport = viewport;
                scroll.content = grid;
            }

            return grid;
        }

        private static void WirePanelView(GameObject root, RectTransform grid, GameObject itemPrefab)
        {
            MissionPanelView view = UiBuilder.Ensure<MissionPanelView>(root);
            SerializedObject serialized = new SerializedObject(view);
            UiBuilder.SetIfEmpty(serialized, "mapSelection", UiBuilder.LoadFirstAsset<MapSelection>());
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

        private static void WireGoldView(GameObject chip, TMP_Text goldLabel)
        {
            GoldView view = UiBuilder.Ensure<GoldView>(chip);
            SerializedObject serialized = new SerializedObject(view);
            UiBuilder.SetIfEmpty(serialized, "economy", UiBuilder.LoadFirstAsset<EconomyService>());
            UiBuilder.SetIfEmpty(serialized, "goldLabel", goldLabel);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void WireProgressView(GameObject chip, TMP_Text label)
        {
            MapProgressView view = UiBuilder.Ensure<MapProgressView>(chip);
            SerializedObject serialized = new SerializedObject(view);
            UiBuilder.SetIfEmpty(serialized, "mapConfig", UiBuilder.LoadFirstAsset<MapConfig>());
            UiBuilder.SetIfEmpty(serialized, "label", label);
            serialized.ApplyModifiedPropertiesWithoutUndo();
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

            Transform close = instance.transform.Find("CloseButton");
            Button closeButton = close != null ? close.GetComponent<Button>() : null;

            GameFlowController flow = Object.FindFirstObjectByType<GameFlowController>(FindObjectsInactive.Include);
            if (flow != null)
            {
                SerializedObject serialized = new SerializedObject(flow);
                UiBuilder.SetIfEmpty(serialized, "mapSelectionRoot", instance);
                UiBuilder.SetIfEmpty(serialized, "closeMissionButton", closeButton);
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
