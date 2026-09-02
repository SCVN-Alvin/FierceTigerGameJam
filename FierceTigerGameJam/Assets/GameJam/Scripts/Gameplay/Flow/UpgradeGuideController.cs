using GameJam.Data;
using GameJam.Economy;
using GameJam.Gameplay.Combat;
using GameJam.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GameJam.Gameplay.Flow
{
    /// <summary>
    /// Lesson three: the walk through the garage, offered once mission1_map1 has been passed.
    ///
    /// Map 2 is tuned so it cannot be beaten on a level-1 cannon firing level-1 rounds, and its
    /// rewards are set so both upgrades are affordable the moment map 1 is done. A player who
    /// does not know the garage exists therefore meets a wall rather than a decision. This points
    /// them at it: the garage, then the vehicle upgrade, then the ammunition upgrade, each with
    /// the same hole and hand the drag lesson uses (see <see cref="TutorialOverlay"/>, which was
    /// lifted out of <see cref="DragHintController"/> so both draw the same furniture).
    ///
    /// It never traps anyone. The dim is not a raycast wall - only the SKIP chip takes a tap, and
    /// everything else falls straight through to the screen underneath - so the player can ignore
    /// the whole thing, wander into a run, or buy in the other order and be caught up with. It is
    /// shown ONLY on menu screens, never in a run, which is why it has no
    /// <c>BlockingFire</c>/<c>BlockingInput</c> gate of its own: there is no shot for it to be in
    /// the way of, and adding a third static gate to CannonInputShooter for an overlay that is off
    /// whenever the cannon is up would be a guard that could only ever be wrong.
    ///
    /// It owns no scene object. <see cref="Install"/> adds it to the GameFlowController's own
    /// object after the scene loads, and it finds everything it needs itself, because this
    /// project has already been bitten by a serialized reference nobody remembered to fill in.
    /// The art comes from the drag hint, which is already wired with exactly these four assets.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UpgradeGuideController : MonoBehaviour
    {
        /// <summary>Passing this is what arms the guide.</summary>
        /// <summary>
        /// The map whose failure arms the lesson. Map 2 is authored to need both upgrades, so
        /// losing it is the moment the player has a question the garage answers - offering the
        /// walk after a WIN on map 1 taught the same thing to someone who had not yet met the
        /// problem.
        /// </summary>
        private const string TargetMapId = "mission1_map2";

        /// <summary>Clear space between the spotlit control and the caption panel.</summary>
        /// <summary>How long one loop of the hand's tap takes.</summary>
        private const float TapPeriod = 1.15f;

        /// <summary>Where the hand rests relative to the control it is pointing at.</summary>
        /// <summary>Drawn small enough that it points at a Buy button without covering the row.</summary>
        private const float HandSize = 110f;

        /// <summary>How far above the gap's top edge the fingertip rests.</summary>
        private const float HandTipGap = 4f;

        /// <summary>How far the fingertip travels back along its own axis on each tap.</summary>
        private const float HandTapTravel = 14f;

        /// <summary>Clear air between the gap and the caption, and between the caption and the edge.</summary>
        private const float CaptionGap = 70f;

        /// <summary>Room kept under the caption for the SKIP chip, which hangs off its bottom.</summary>
        private const float SkipReserve = 96f;

        /// <summary>How far outside the control the gap reaches, so a tap on its very edge lands.</summary>
        private const float HoleMargin = 14f;

        /// <summary>Matches the backdrop the menus already dim to, so the two never disagree.</summary>
        private static readonly Color DimColor = new Color(0f, 0f, 0f, 0.78f);

        /// <summary>
        /// What the guide is asking for. Garage first, because the other two are only reachable
        /// through it; a lesson whose thing is already bought is stepped over rather than shown.
        /// </summary>
        private enum Lesson
        {
            Garage,
            Vehicle,
            Ammo,
            Done,
        }

        private GameFlowController flow;
        private DragHintController dragHint;
        private ShopTabsView shopTabs;
        private VehicleShopView vehicleShop;
        private BulletShopView bulletShop;
        private Canvas canvas;

        private RectTransform guideRoot;
        private RectTransform[] blockStrips;
        private RectTransform panelRect;
        private TMP_Text label;
        private Image handImage;
        private RectTransform handRect;

        private Lesson lesson = Lesson.Garage;
        private bool warnedAboutArt;

        /// <summary>Set when there is nowhere to build the overlay, so it is not looked for again.</summary>
        private bool overlayUnavailable;

        /// <summary>
        /// Puts the guide on the flow controller's own GameObject once the scene is up.
        ///
        /// Deliberately not a component anyone has to remember to add. Gameplay.unity is a large,
        /// UI-heavy file that is usually open in the editor while this is being worked on, and a
        /// new component there would be both a merge conflict and four more inspector slots to
        /// leave empty. A scene with no flow controller - a test scene, a prefab stage - gets
        /// nothing, which is the right answer for a screen the guide has nothing to say about.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            GameFlowController host = FindFirstObjectByType<GameFlowController>(FindObjectsInactive.Include);
            if (host == null || host.GetComponent<UpgradeGuideController>() != null)
            {
                return;
            }

            host.gameObject.AddComponent<UpgradeGuideController>();
        }

        private void Awake()
        {
            flow = GetComponent<GameFlowController>();

            // On the same object as the flow in the shipping scene, but found either way: this is
            // added by Install above, so it must not care which object it landed on.
            dragHint = GetComponent<DragHintController>();
            if (dragHint == null)
            {
                dragHint = FindFirstObjectByType<DragHintController>(FindObjectsInactive.Include);
            }
        }

        private void OnDisable()
        {
            Hide();
        }

        private void Update()
        {
            if (!ShouldOffer())
            {
                Hide();
                return;
            }

            // Only ever on a menu screen. A run has its own two overlays and its own input gates,
            // and a third one over a live cannon is how the 09-02 overlap bug happened.
            if (flow.IsInRun || flow.State == GameFlowController.GameState.Loading)
            {
                Hide();
                return;
            }

            AdvanceLesson();
            if (lesson == Lesson.Done)
            {
                Finish();
                return;
            }

            RectTransform target = ResolveTarget(out string caption);
            if (target == null || !target.gameObject.activeInHierarchy)
            {
                // The garage's rows are built by the shop view's OnEnable, so for the frame the
                // screen opens there is nothing to point at yet. Waiting is right; guessing at a
                // position and moving the hole next frame is not.
                Hide();
                return;
            }

            Show(target, caption);
        }

        /// <summary>
        /// Armed by progress, not by an event: reading the save is what makes a player who already
        /// lost map 2 before this shipped get the lesson too, and what makes it survive a quit
        /// halfway through. <see cref="UserTutorialData.upgradeGuideDone"/> is the only thing that
        /// ends it.
        /// </summary>
        private bool ShouldOffer()
        {
            // No economy is a wiring problem, and the answer to one is to stay off rather than to
            // decide there is nothing left to teach: deciding that writes upgradeGuideDone, and
            // that flag never comes back. A guide that quietly did not run is recoverable.
            if (flow == null || flow.Economy == null)
            {
                return false;
            }

            UserTutorialData tutorial = UserData.Tutorial;
            return tutorial.completed
                   && !tutorial.upgradeGuideDone
                   && HasLost(TargetMapId);
        }

        /// <summary>
        /// Whether the player has taken this map on and not beaten it.
        ///
        /// Read off the save with no new field: a map only gets a progress record when a run on it
        /// is registered, so a record that is not yet passed IS an attempt that was lost. It also
        /// answers correctly for someone who lost, quit and came back, and it stops answering the
        /// moment they win - which is the right way round, because a player who beat map 2 without
        /// upgrading has already proved they do not need the lesson.
        /// </summary>
        private static bool HasLost(string mapId)
        {
            return UserData.Maps.TryGet(mapId, out MapProgress progress) && !progress.passed;
        }

        /// <summary>
        /// Walks the lesson forward past everything the player has already done - including doing
        /// it in the other order, which the garage's own default tab invites: it opens on
        /// Ammunition, so buying that first is the natural mistake to be forgiving about.
        /// </summary>
        private void AdvanceLesson()
        {
            if (lesson == Lesson.Garage && flow.State == GameFlowController.GameState.Shop)
            {
                lesson = Lesson.Vehicle;
            }
            else if (lesson != Lesson.Garage && lesson != Lesson.Done
                     && flow.State != GameFlowController.GameState.Shop)
            {
                // Left the garage with buying still to do: back to pointing at the way in.
                lesson = Lesson.Garage;
            }

            if (lesson == Lesson.Vehicle && VehicleLessonSatisfied())
            {
                lesson = Lesson.Ammo;
            }

            if (lesson == Lesson.Ammo && AmmoLessonSatisfied())
            {
                lesson = Lesson.Done;
            }

            // Nothing left to teach at all - a save that arrives here already upgraded, or one
            // where neither catalogue has a second level - closes the guide without ever showing
            // it, rather than pointing at a MAX button forever.
            if (lesson == Lesson.Garage && VehicleLessonSatisfied() && AmmoLessonSatisfied())
            {
                lesson = Lesson.Done;
            }
        }

        /// <summary>
        /// Done when ANY vehicle has been taken past level 1 - not only the equipped one. The
        /// spotlight points at the equipped row because that is the one worth buying, but a player
        /// who levelled a different cannon has learned the lesson and must not be asked twice.
        /// Also done when the equipped vehicle simply cannot go any higher.
        /// </summary>
        private bool VehicleLessonSatisfied()
        {
            foreach (VehicleProgress progress in UserData.Vehicles.vehicles)
            {
                if (progress != null && progress.level > 1)
                {
                    return true;
                }
            }

            // A catalogue with nothing equipped is NOT "satisfied": that would end the guide for
            // good over a missing asset. It stays unsatisfied, the lesson finds nothing to point
            // at, and the overlay simply never appears. ShouldOffer has already ruled out a null
            // economy, so this is the only case left.
            EconomyService economy = flow.Economy;
            VehicleDefinition vehicle = SelectedVehicle();
            return vehicle != null
                   && UserData.Vehicles.GetLevel(vehicle.Id) >= economy.GetVehicleMaxLevel(vehicle);
        }

        /// <summary>The same rule for ammunition; see <see cref="VehicleLessonSatisfied"/>.</summary>
        private bool AmmoLessonSatisfied()
        {
            foreach (BulletProgress progress in UserData.Bullets.bullets)
            {
                if (progress != null && progress.level > 1)
                {
                    return true;
                }
            }

            EconomyService economy = flow.Economy;
            BulletDefinition bullet = SelectedBullet();
            return bullet != null
                   && UserData.Bullets.GetLevel(bullet.Id) >= economy.GetMaxLevel(bullet);
        }

        private VehicleDefinition SelectedVehicle()
        {
            VehicleLoadout loadout = flow.Economy != null ? flow.Economy.VehicleLoadout : null;
            return loadout != null ? loadout.Selected : null;
        }

        private BulletDefinition SelectedBullet()
        {
            BulletLoadout loadout = flow.Economy != null ? flow.Economy.Loadout : null;
            return loadout != null ? loadout.Selected : null;
        }

        /// <summary>
        /// What to spotlight, and what to say over it. Everything is found by name or by type at
        /// the moment it is needed, never by a remembered position: the garage prefab is still
        /// being redesigned, and a hole at a hard-coded coordinate would end up lighting the
        /// backdrop the first time a row moved.
        /// </summary>
        private RectTransform ResolveTarget(out string caption)
        {
            caption = string.Empty;

            switch (lesson)
            {
                case Lesson.Garage:
                    caption = "THIS ONE NEEDS\nBETTER GEAR -\nOPEN THE GARAGE";
                    Button garage = flow.ShopButton;
                    return garage != null ? (RectTransform)garage.transform : null;

                case Lesson.Vehicle:
                {
                    VehicleDefinition vehicle = SelectedVehicle();
                    if (vehicle == null)
                    {
                        return null;
                    }

                    RectTransform tab = ResolveClosedTab(ResolveVehicleShop());
                    if (tab != null)
                    {
                        caption = "OPEN THE\nVEHICLE TAB";
                        return tab;
                    }

                    // The name comes from the definition, never from a string here: the two
                    // catalogues were renamed once already and a caption that spelled its own
                    // copy of a name would still be saying the old one.
                    caption = $"UPGRADE\n{vehicle.DisplayName.ToUpperInvariant()}";
                    return ResolveRowBuyButton(ResolveVehicleShop(), VehicleShopView.RowNamePrefix + vehicle.Id);
                }

                case Lesson.Ammo:
                {
                    BulletDefinition bullet = SelectedBullet();
                    if (bullet == null)
                    {
                        return null;
                    }

                    RectTransform tab = ResolveClosedTab(ResolveBulletShop());
                    if (tab != null)
                    {
                        caption = "OPEN THE\nAMMO TAB";
                        return tab;
                    }

                    caption = $"UPGRADE\n{bullet.DisplayName.ToUpperInvariant()}";
                    return ResolveRowBuyButton(ResolveBulletShop(), BulletShopView.RowNamePrefix + bullet.Id);
                }
            }

            return null;
        }

        /// <summary>
        /// The tab button that has to be pressed before a section can be pointed at, or null when
        /// that section is already open.
        ///
        /// Which tab a section lives on is worked out by asking which tab panel the shop view sits
        /// inside, rather than by an index: the garage opens on Ammunition today and could open on
        /// anything tomorrow, and a third section would silently renumber a hard-coded 0.
        /// </summary>
        private RectTransform ResolveClosedTab(Component shopView)
        {
            if (shopView == null)
            {
                return null;
            }

            if (shopView.gameObject.activeInHierarchy)
            {
                return null;                                // its panel is the one on show
            }

            ShopTabsView tabs = ResolveTabs();
            if (tabs == null)
            {
                return null;
            }

            for (int i = 0; i < tabs.TabCount; i++)
            {
                GameObject panel = tabs.GetTabPanel(i);
                if (panel == null || !shopView.transform.IsChildOf(panel.transform))
                {
                    continue;
                }

                Button button = tabs.GetTabButton(i);
                return button != null ? (RectTransform)button.transform : null;
            }

            return null;
        }

        /// <summary>
        /// The Buy control on one shop row, found by the name the shop gave that row. Both kinds
        /// of row are handled - the garage's own <see cref="ShopItemView"/> and the plain fallback
        /// row the shops build when no prefab is assigned - because which one is in front of the
        /// player is the shop's decision, not this one's.
        /// </summary>
        private RectTransform ResolveRowBuyButton(Component shopView, string rowName)
        {
            if (shopView == null)
            {
                return null;
            }

            Transform row = FindDeep(shopView.transform, rowName);
            if (row == null)
            {
                return null;
            }

            // Written out rather than chained with ??, deliberately: a serialized reference that
            // was never filled in is Unity's "fake null", which is != null to C# but == null to
            // everything that matters, and ?? would hand back exactly that instead of trying the
            // next candidate.
            Button buy = null;

            ShopItemView item = row.GetComponent<ShopItemView>();
            if (item != null && item.BuyButton != null)
            {
                buy = item.BuyButton;
            }

            if (buy == null)
            {
                VehicleShopRowView view = row.GetComponent<VehicleShopRowView>();
                if (view != null && view.PrimaryButton != null)
                {
                    buy = view.PrimaryButton;
                }
            }

            if (buy == null)
            {
                buy = row.GetComponentInChildren<Button>(true);
            }

            return buy != null ? (RectTransform)buy.transform : null;
        }

        private static Transform FindDeep(Transform root, string objectName)
        {
            if (root.name == objectName)
            {
                return root;
            }

            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindDeep(root.GetChild(i), objectName);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private ShopTabsView ResolveTabs()
        {
            if (shopTabs == null)
            {
                shopTabs = FindFirstObjectByType<ShopTabsView>(FindObjectsInactive.Include);
            }

            return shopTabs;
        }

        private VehicleShopView ResolveVehicleShop()
        {
            if (vehicleShop == null)
            {
                vehicleShop = FindFirstObjectByType<VehicleShopView>(FindObjectsInactive.Include);
            }

            return vehicleShop;
        }

        private BulletShopView ResolveBulletShop()
        {
            if (bulletShop == null)
            {
                bulletShop = FindFirstObjectByType<BulletShopView>(FindObjectsInactive.Include);
            }

            return bulletShop;
        }

        /// <summary>Puts the hole on the control, the words beside it and the hand on top.</summary>
        private void Show(RectTransform target, string caption)
        {
            EnsureOverlay();
            if (guideRoot == null)
            {
                return;
            }

            if (!guideRoot.gameObject.activeSelf)
            {
                guideRoot.gameObject.SetActive(true);
            }

            // Last every frame it is shown, not only when built: the shops and the mission board
            // are switched on and off underneath, and a screen raised after the overlay was built
            // would otherwise be drawn over the top of it.
            guideRoot.SetAsLastSibling();

            if (label != null)
            {
                label.text = caption;
            }

            // The menu's pulsing TAP TO PLAY sits mid-screen and invites the one tap the lesson is
            // steering away from, so it goes away while the lesson is up.
            flow.SetTapToPlayVisible(false);

            LayoutAroundTarget(target, out Vector2 holeMin, out Vector2 holeMax);
            PlaceHand(holeMin, holeMax);
            PlaceCaption(holeMin, holeMax);
        }

        /// <summary>
        /// Fences the screen off around the control, leaving a gap the exact size of the control.
        ///
        /// The corners are read from the target every frame rather than cached: the garage's rows
        /// live in a scroll view, so the thing being pointed at moves under the overlay. Working
        /// from the target's own rect is also what makes the gap fit a small Buy button and a wide
        /// tab equally well - the old single filter sprite was one size whatever it was over.
        /// </summary>
        private void LayoutAroundTarget(RectTransform target, out Vector2 min, out Vector2 max)
        {
            Rect canvasRect = guideRoot.rect;

            Vector3[] corners = new Vector3[4];
            target.GetWorldCorners(corners);
            min = ToGuideSpace(corners[0]) - new Vector2(HoleMargin, HoleMargin);
            max = ToGuideSpace(corners[2]) + new Vector2(HoleMargin, HoleMargin);

            TutorialOverlay.LayoutStripsAround(blockStrips, canvasRect, min, max);
        }

        /// <summary>A world point in the overlay's own coordinates, via the screen.</summary>
        private Vector2 ToGuideSpace(Vector3 world)
        {
            Camera uiCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;

            Vector2 screen = RectTransformUtility.WorldToScreenPoint(uiCamera, world);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(guideRoot, screen, uiCamera, out Vector2 local);
            return local;
        }

        /// <summary>
        /// The fingertip rests just above the gap's top edge, a little left of its middle, and taps
        /// back and forth along the finger's own axis.
        ///
        /// Placed off the gap's EDGE, never its centre. Pointing at a centre put the hand squarely
        /// on top of the control it was pointing at, which is what made the lesson read as a bug -
        /// and it got worse the bigger the control, because the offset was a fixed number of units
        /// while the button underneath was not.
        /// </summary>
        private void PlaceHand(Vector2 holeMin, Vector2 holeMax)
        {
            if (handRect == null)
            {
                return;
            }

            float phase = Mathf.Repeat(Time.unscaledTime, TapPeriod) / TapPeriod;
            float press = Mathf.Sin(phase * Mathf.PI);

            Vector2 tip = new Vector2(
                Mathf.Lerp(holeMin.x, (holeMin.x + holeMax.x) * 0.5f, 0.55f),
                holeMax.y + HandTipGap);
            tip -= TutorialOverlay.HandFingerAxis * (press * HandTapTravel);

            // Kept whole on screen. The body hangs left of and below the tip, so the margins are
            // the pivot's own fractions of the sprite rather than half of it.
            Rect canvasRect = guideRoot.rect;
            Vector2 pivot = TutorialOverlay.HandFingertipPivot;
            Vector2 size = handRect.sizeDelta;
            tip.x = Mathf.Clamp(tip.x, canvasRect.xMin + (size.x * pivot.x), canvasRect.xMax - (size.x * (1f - pivot.x)));
            tip.y = Mathf.Clamp(tip.y, canvasRect.yMin + (size.y * pivot.y), canvasRect.yMax - (size.y * (1f - pivot.y)));

            handRect.anchoredPosition = tip;
            TutorialOverlay.SetAlpha(handImage, Mathf.Clamp01(0.35f + (press * 0.65f)));
        }

        /// <summary>
        /// The words go UNDER the gap by preference, because the hand already owns the air directly
        /// above it - putting both in the same place was the other half of the overlap. When the
        /// gap sits too low for that, the caption goes above the hand instead, clear of it.
        /// </summary>
        private void PlaceCaption(Vector2 holeMin, Vector2 holeMax)
        {
            if (panelRect == null)
            {
                return;
            }

            Rect canvasRect = guideRoot.rect;
            float halfY = TutorialOverlay.PanelSize.y * 0.5f;
            float halfX = TutorialOverlay.PanelSize.x * 0.5f;
            float handTop = handRect != null
                ? handRect.sizeDelta.y * (1f - TutorialOverlay.HandFingertipPivot.y)
                : 0f;

            // SkipReserve is the chip hanging off the caption's bottom edge: without it the panel
            // can sit low enough to fit while the SKIP under it runs off the screen.
            float below = holeMin.y - CaptionGap - halfY;
            float above = holeMax.y + handTop + CaptionGap + halfY;
            float y = below - halfY - SkipReserve > canvasRect.yMin ? below : above;

            y = Mathf.Clamp(y, canvasRect.yMin + halfY + SkipReserve, canvasRect.yMax - halfY);
            float x = Mathf.Clamp((holeMin.x + holeMax.x) * 0.5f,
                canvasRect.xMin + halfX, canvasRect.xMax - halfX);

            panelRect.anchoredPosition = new Vector2(x, y);
        }

        /// <summary>
        /// A control's middle, in the overlay root's own coordinates. Goes out through the screen
        /// and back rather than reading anchoredPosition, because the target lives somewhere else
        /// entirely in the hierarchy - inside a scroll view, inside a panel, inside the bar - and
        /// only the screen is common ground.
        /// </summary>
        private Vector2 CanvasPointOf(RectTransform target)
        {
            Camera uiCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;

            Vector3 world = target.TransformPoint(target.rect.center);
            Vector2 screen = RectTransformUtility.WorldToScreenPoint(uiCamera, world);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(guideRoot, screen, uiCamera, out Vector2 local);
            return local;
        }

        /// <summary>The player said no. That answer is kept: it never comes back.</summary>
        private void Skip()
        {
            lesson = Lesson.Done;
            Finish();
        }

        private void Finish()
        {
            Hide();

            if (UserData.Tutorial.upgradeGuideDone)
            {
                return;                                     // already written; do not save again
            }

            UserData.Tutorial.upgradeGuideDone = true;
            UserData.Save();
        }

        /// <summary>
        /// Puts the lesson away and gives the menu its invitation back.
        ///
        /// Every way out runs through here - the frame the lesson stops applying, the component
        /// being disabled, and <see cref="Finish"/> - which is the point: TAP TO PLAY must not be
        /// left hidden by a lesson that ended, whichever way it ended.
        /// </summary>
        private void Hide()
        {
            if (guideRoot != null && guideRoot.gameObject.activeSelf)
            {
                guideRoot.gameObject.SetActive(false);
            }

            if (flow != null)
            {
                flow.SetTapToPlayVisible(true);
            }
        }

        private void EnsureOverlay()
        {
            if (guideRoot != null || overlayUnavailable)
            {
                return;
            }

            RectTransform parent = dragHint != null ? dragHint.HintParent : null;
            if (parent == null)
            {
                Canvas found = FindFirstObjectByType<Canvas>(FindObjectsInactive.Include);
                parent = found != null ? (RectTransform)found.transform : null;
            }

            if (parent == null)
            {
                // Given up on rather than retried: there is no canvas in this scene and there is
                // not going to be one, and searching every frame for the rest of the session is a
                // cost paid for nothing.
                overlayUnavailable = true;
                WarnAboutArtOnce("there is no canvas to build it under");
                return;
            }

            canvas = parent.GetComponentInParent<Canvas>();

            Sprite handSprite = dragHint != null ? dragHint.HandSprite : null;
            Sprite panelSprite = dragHint != null ? dragHint.PanelSprite : null;
            TMP_FontAsset font = dragHint != null ? dragHint.LabelFont : null;

            if (handSprite == null)
            {
                // Still built: a caption and a hand with no hole still says where to go, and a
                // lesson that silently did nothing would be blamed on the flag instead of on the
                // wiring that actually caused it.
                WarnAboutArtOnce("the drag hint has no hand sprite to borrow");
            }

            // blocksRaycasts TRUE, which reads backwards until you know what the flag does: a
            // CanvasGroup set false makes every graphic under it invisible to the raycaster, so
            // the blocking strips would have been inert decoration and the lesson would have
            // fenced nothing off. True only permits its children to be hit - it is not itself a
            // wall. The strips are the wall, and the gap they leave has no graphic in it at all,
            // so a tap there falls straight through to the control being taught.
            guideRoot = TutorialOverlay.CreateRoot(parent, "UpgradeGuide", blocksRaycasts: true);

            blockStrips = TutorialOverlay.CreateBlockingStrips(guideRoot, DimColor);

            panelRect = TutorialOverlay.CreatePanel(guideRoot, panelSprite);
            panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            label = TutorialOverlay.CreateLabel(panelRect, font, panelSprite != null);

            handImage = TutorialOverlay.CreateHand(
                guideRoot, handSprite, TutorialOverlay.HandFingertipPivot, HandSize);
            handRect = handImage.rectTransform;
            handRect.anchorMin = handRect.anchorMax = new Vector2(0.5f, 0.5f);

            CreateSkipChip(font);
        }

        /// <summary>
        /// The way out, hanging under the caption so it travels with the words.
        ///
        /// It carries its own CanvasGroup with ignoreParentGroups set, because the root's group
        /// switches raycasts off for everything beneath it and this one control has to be the
        /// exception - it is the only thing in the overlay a tap is meant to land on.
        /// </summary>
        private void CreateSkipChip(TMP_FontAsset font)
        {
            GameObject skipGo = new GameObject("Skip", typeof(RectTransform), typeof(CanvasGroup));
            RectTransform skipRect = (RectTransform)skipGo.transform;
            skipRect.SetParent(panelRect, false);
            skipRect.anchorMin = new Vector2(0.5f, 0f);
            skipRect.anchorMax = new Vector2(0.5f, 0f);
            skipRect.pivot = new Vector2(0.5f, 1f);
            skipRect.anchoredPosition = new Vector2(0f, -18f);
            skipRect.sizeDelta = new Vector2(240f, 72f);

            CanvasGroup group = skipGo.GetComponent<CanvasGroup>();
            group.blocksRaycasts = true;
            group.ignoreParentGroups = true;

            // Invisible but present: the tap target is the words, and a plate behind them would
            // be one more piece of art to design for a control that should be easy to ignore.
            Image hit = skipGo.AddComponent<Image>();
            hit.color = new Color(0f, 0f, 0f, 0f);
            hit.raycastTarget = true;

            Button button = skipGo.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = hit;
            button.onClick.AddListener(Skip);

            GameObject textGo = new GameObject("Label", typeof(RectTransform));
            RectTransform textRect = (RectTransform)textGo.transform;
            textRect.SetParent(skipRect, false);
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            TMP_Text skipLabel = textGo.AddComponent<TextMeshProUGUI>();
            if (font != null)
            {
                skipLabel.font = font;
            }

            skipLabel.text = "SKIP";
            skipLabel.fontSize = 32f;
            skipLabel.fontStyle = FontStyles.Bold;
            skipLabel.color = Color.white;
            skipLabel.alignment = TextAlignmentOptions.Center;
            skipLabel.raycastTarget = false;
        }

        private void WarnAboutArtOnce(string reason)
        {
            if (warnedAboutArt)
            {
                return;
            }

            warnedAboutArt = true;
            Debug.LogWarning(
                $"{name}: the upgrade guide is drawn without its usual art because {reason}. It "
                + $"borrows the four assets on {nameof(DragHintController)}, so check those.",
                this);
        }
    }
}
