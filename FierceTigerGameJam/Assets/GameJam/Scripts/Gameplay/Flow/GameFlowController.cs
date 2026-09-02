using System;
using GameJam.Audio;
using GameJam.Economy;
using GameJam.Gameplay.Cameras;
using GameJam.Gameplay.Cannon;
using GameJam.Gameplay.Combat;
using GameJam.Gameplay.Wall;
using GameJam.UI;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace GameJam.Gameplay.Flow
{
    /// <summary>
    /// Drives the whole loop: the menu, the shops, choosing a map, the run, and the result that
    /// sends the player back to the menu. Ammunition is not one of its screens any more - the
    /// garage is where a bullet is chosen, and choosing a map goes straight into play.
    ///
    /// The controller decides which screen is up and nothing else. It talks to screens through
    /// buttons it is handed and roots it switches on and off, never by referencing the view types,
    /// so a screen can be redesigned or replaced without the flow knowing anything about it.
    /// </summary>
    public sealed class GameFlowController : MonoBehaviour
    {
        public enum GameState
        {
            MainMenu,
            IapShop,

            /// <summary>
            /// Everything bought with gold, in one screen with a tab per kind. Was a state per
            /// shop; that does not scale past two, and it made the player leave one shop to
            /// reach another showing the same gold in the same corner.
            /// </summary>
            Shop,

            MapSelection,

            /// <summary>
            /// Was the screen for choosing what ammunition to take into the chosen map. That
            /// screen is gone - the garage chooses the bullet and the run fills the map's whole
            /// budget with it - and nothing enters this state any more.
            ///
            /// Kept in the enum all the same, and kept in this position, because
            /// <see cref="UI.BottomBarView.Slot.state"/> is serialized by value: deleting this
            /// would shift Playing, Result and Loading down one and silently re-map every slot
            /// already authored in the bar prefab. Same append-only rule the Loading state below
            /// was added under.
            /// </summary>
            AmmoPick,

            Playing,

            /// <summary>The run is over and the result is on screen.</summary>
            Result,

            /// <summary>
            /// The splash screen, before anything else. Appended rather than put first, and it
            /// must stay last: <see cref="UI.BottomBarView.Slot.state"/> is serialized by value,
            /// so inserting a state here would silently re-map every slot already authored in
            /// the bar prefab - HOME would become the shop and nobody would be told.
            /// </summary>
            Loading,
        }

        [SerializeField] private MapSelection mapSelection;
        [SerializeField] private KnockdownLayoutMapAuthoring mapBuilder;
        [SerializeField] private LevelRunController runController;

        [Tooltip("Charges the continue; also where the loaded ammunition is read from.")]
        [SerializeField] private EconomyService economy;

        [Tooltip("The run's rounds. Filled automatically on the way into a map now that there is "
                 + "no pick screen to fill it by hand.")]
        [SerializeField] private BulletInventory bulletInventory;

        [Header("Screens")]
        [Tooltip("The splash screen every launch opens on. Left empty - a test scene, or a scene "
                 + "Build Loading Screen has not been run over - the game opens on the main menu "
                 + "instead and says so, rather than sitting on a state with nothing to show.")]
        [SerializeField] private GameObject loadingRoot;

        [SerializeField] private GameObject mainMenuRoot;
        [SerializeField] private GameObject mapSelectionRoot;
        [SerializeField] private GameObject iapShopRoot;
        [Tooltip("The one shop screen, with a tab per thing sold.")]
        [FormerlySerializedAs("bulletShopRoot")]
        [SerializeField] private GameObject shopRoot;

        [Tooltip("Optional. Lets a menu button open the shop on a particular tab.")]
        [SerializeField] private ShopTabsView shopTabs;

        [Tooltip("The slingshot, the structure and anything else only alive in play.")]
        [SerializeField] private GameObject gameplayRoot;

        [Tooltip("In-run readouts. Shown with gameplay, hidden once the result is up.")]
        [SerializeField] private GameObject hudRoot;

        [Tooltip("Shown when the run failed: the Continue? screen that offers to sell more rounds.")]
        [SerializeField] private GameObject failRoot;

        /// <summary>
        /// Failures since this map entry began. Drives what the fail screen sells: 1st failure a
        /// by-the-round purchase, 2nd the flat continue, 3rd nothing. Reset by StartSelectedMap,
        /// which every road into a run goes through.
        /// </summary>
        private int failsThisEntry;

        private FailScreenView failView;

        [Tooltip("Shown when the run passed its map.")]
        [SerializeField] private GameObject clearedRoot;

        [Tooltip("The bottom tab bar. Shown on the menu and the shops, since its middle button is "
                 + "what returns from a shop; hidden once the player is on their way into a run.")]
        [SerializeField] private GameObject bottomBarRoot;

        [Tooltip("Settings overlay. A modal rather than a screen: it opens over whatever is up "
                 + "and closing it returns there, so it does not disturb the flow's state.")]
        [SerializeField] private GameObject settingsRoot;

        [Header("Buttons")]
        [SerializeField] private Button playButton;

        [Tooltip("SDF font for the runtime TAP TO PLAY labels; the bitmap default blurs at size.")]
        [SerializeField] private TMPro.TMP_FontAsset tapLabelFont;
        [SerializeField] private Button iapShopButton;
        [SerializeField] private Button homeButton;
        [FormerlySerializedAs("bulletShopButton")]
        [SerializeField] private Button shopButton;

        [Tooltip("The X in the garage's top corner. A second way out beside the bottom bar's "
                 + "Home button, not a replacement: the bar is off screen on a phone held in one "
                 + "hand as often as it is under a thumb.")]
        [SerializeField] private Button closeShopButton;

        [Tooltip("The X on the mission screen. The same second way out as the garage's, for the "
                 + "same reason: the bottom bar's Home is not always under a thumb.")]
        [SerializeField] private Button closeMissionButton;

        [Header("Settings Buttons")]
        [SerializeField] private Button openSettingsButton;
        [SerializeField] private Button openSettingsInRunButton;
        [SerializeField] private Button closeSettingsButton;

        [Tooltip("Abandons the run from the settings overlay. Hidden outside a run, where it would "
                 + "only lead to the screen the player is already on.")]
        [SerializeField] private Button settingsMainMenuButton;

        [Header("Cleared Screen Buttons")]
        [Tooltip("Another go at the map just cleared, the same road RETRY takes.")]
        [SerializeField] private Button clearedReplayButton;

        [Tooltip("On to the next map in the catalogue.")]
        [SerializeField] private Button clearedContinueButton;

        [Tooltip("The X on the cleared screen. Leaves for the main menu.")]
        [SerializeField] private Button clearedCloseButton;

        [Header("Fail Screen Buttons")]
        [Tooltip("Buys more rounds and picks the failed run back up where it stopped.")]
        [SerializeField] private Button failContinueButton;

        [Tooltip("The X on the fail screen. Gives the run up for the main menu.")]
        [SerializeField] private Button failCloseButton;

        [Header("Reset On Entering A Map")]
        [SerializeField] private CannonAimController aimController;

        [Tooltip("The pivot the camera rig orbits. Squared up between runs so every map is "
                 + "entered from the authored viewpoint.")]
        [SerializeField] private CameraOrbit cameraOrbit;

        [Tooltip("Optional. Warmed at the start of a run so the first shot of a map costs no "
                 + "more than the tenth.")]
        [SerializeField] private GridKnockdownCannonFireController fireController;

        [Header("Tutorial")]
        [Tooltip("Optional. Left empty - a test scene, or one Build Tutorial has not been run "
                 + "over - the splash hands straight over to the main menu as it always did.")]
        [SerializeField] private TutorialController tutorial;

        public event Action<GameState> StateChanged;

        /// <summary>Raised with the finished run, for a result screen to draw.</summary>
        public event Action<LevelRunController.RunResult> RunFinished;

        public GameState State { get; private set; } = GameState.MainMenu;

        /// <summary>True while a structure is up, so settings can offer a way out of it.</summary>
        public bool IsInRun => State == GameState.Playing || State == GameState.Result;

        /// <summary>
        /// The gold, the catalogue and the loadouts, for the overlays that have to know what is
        /// left to buy. Exposed read-only rather than given its own scene reference: the guide
        /// that reads it is created at runtime, and one more field in the inspector is one more
        /// field that can be left empty.
        /// </summary>
        public EconomyService Economy => economy;

        /// <summary>The bottom bar's garage button, for an overlay that has to point at it.</summary>
        public Button ShopButton => shopButton;

        /// <summary>The menu's pulsing TAP TO PLAY words, kept so a lesson can put them away.</summary>
        private RectTransform tapToPlayLabel;

        /// <summary>
        /// Shows or hides the menu's TAP TO PLAY invitation.
        ///
        /// A guided lesson needs it gone: the words sit in the middle of the menu, they pulse to
        /// draw the eye, and they invite exactly the tap the lesson is trying to steer somewhere
        /// else. The pulse coroutine keeps running against the hidden transform, which costs
        /// nothing and means the breathing resumes mid-stride when it comes back.
        /// </summary>
        public void SetTapToPlayVisible(bool visible)
        {
            if (tapToPlayLabel == null || tapToPlayLabel.gameObject.activeSelf == visible)
            {
                return;
            }

            tapToPlayLabel.gameObject.SetActive(visible);
        }

        private GameObject selectionRoot;

        /// <summary>
        /// True while the cleared screen showing is the tutorial's. The tutorial map is
        /// standalone - it is not in MapConfig - so "the next map" falls off the end of the
        /// catalogue and <see cref="EnterNextMap"/> would answer with the main menu. See
        /// <see cref="HandleClearedContinuePressed"/>.
        /// </summary>
        private bool clearedRunWasTutorial;

        private void Awake()
        {
            selectionRoot = ResolveSelectionRoot();

            if (aimController == null && gameplayRoot != null)
            {
                aimController = gameplayRoot.GetComponentInChildren<CannonAimController>(true);
            }

            // Not resolved from the map builder the way the old structure spinner was: the orbit
            // belongs to the camera rig rather than to the map, and deliberately hangs outside
            // the structure so rebuilding a map cannot disturb it.
            if (cameraOrbit == null)
            {
                cameraOrbit = FindFirstObjectByType<CameraOrbit>();
            }
        }

        private void OnEnable()
        {
            if (mapSelection != null)
            {
                mapSelection.SelectionChanged += HandleMapSelected;
            }

            if (runController != null)
            {
                runController.Finished += HandleRunFinished;
            }

            Wire(playButton, EnterMapSelection);
            Wire(iapShopButton, EnterIapShop);
            Wire(homeButton, ReturnToMainMenu);
            Wire(shopButton, EnterShop);

            // GoBack already sends Shop to the main menu, so the X needs no state of its own.
            Wire(closeShopButton, GoBack);

            // GoBack sends MapSelection to the main menu too, so this needs no state of its own.
            Wire(closeMissionButton, GoBack);
            Wire(openSettingsButton, OpenSettings);
            Wire(openSettingsInRunButton, OpenSettings);
            Wire(closeSettingsButton, CloseSettings);
            Wire(settingsMainMenuButton, AbandonRun);
            Wire(clearedReplayButton, RetryMap);
            Wire(clearedContinueButton, HandleClearedContinuePressed);
            Wire(clearedCloseButton, HandleClearedClosePressed);
            Wire(failContinueButton, HandleFailContinuePressed);
            Wire(failCloseButton, ReturnToMainMenu);
        }

        private void OnDisable()
        {
            if (mapSelection != null)
            {
                mapSelection.SelectionChanged -= HandleMapSelected;
            }

            if (runController != null)
            {
                runController.Finished -= HandleRunFinished;
            }

            Unwire(playButton, EnterMapSelection);
            Unwire(iapShopButton, EnterIapShop);
            Unwire(homeButton, ReturnToMainMenu);
            Unwire(shopButton, EnterShop);
            Unwire(closeShopButton, GoBack);
            Unwire(closeMissionButton, GoBack);
            Unwire(openSettingsButton, OpenSettings);
            Unwire(openSettingsInRunButton, OpenSettings);
            Unwire(closeSettingsButton, CloseSettings);
            Unwire(settingsMainMenuButton, AbandonRun);
            Unwire(clearedReplayButton, RetryMap);
            Unwire(clearedContinueButton, HandleClearedContinuePressed);
            Unwire(clearedCloseButton, HandleClearedClosePressed);
            Unwire(failContinueButton, HandleFailContinuePressed);
            Unwire(failCloseButton, ReturnToMainMenu);
        }

        private void Start()
        {
            InstallTapToPlay();

            // Deviation from Brief 14, which has Start enter Loading unconditionally: a Loading
            // state with no screen wired under it switches every other root off and puts nothing
            // in their place, which is a black screen with no way out and nothing anywhere saying
            // why. A scene that has not had Build Loading Screen run over it is still playable,
            // and is told what it is missing.
            if (loadingRoot == null)
            {
                Debug.LogWarning(
                    $"{name}: no loading screen is wired, so the game opens on the main menu. "
                    + "Run Tools > Smashdown > Build Loading Screen.",
                    this);
                ReturnToMainMenu();
                return;
            }

            EnterLoading();
        }

        /// <summary>
        /// The splash. Not a menu state, so the normal Enter bookkeeping is all it needs: every
        /// other root, the tab bar and the back button switch themselves off around it.
        /// </summary>
        [ContextMenu("Loading")]
        public void EnterLoading()
        {
            Enter(GameState.Loading);
        }

        /// <summary>
        /// Where the game goes once the splash is done: into the tutorial on a save that has not
        /// finished it, and to the main menu otherwise.
        ///
        /// The tutorial is asked rather than told, and the menu is the answer to a refusal as well
        /// as to a completed tutorial: a launch that shows neither is a black screen with no way
        /// out, which is the same reason Start falls back when no splash is wired.
        /// </summary>
        public void FinishLoading()
        {
            if (tutorial != null && tutorial.ShouldRun && tutorial.TryStartTutorial())
            {
                return;
            }

            ReturnToMainMenu();
        }

        [ContextMenu("Main Menu")]
        public void ReturnToMainMenu()
        {
            TearDownRun();
            Enter(GameState.MainMenu);
        }

        [ContextMenu("Map Selection")]

        /// <summary>
        /// The menu asks for a tap, not a button: the PLAY pill stretches invisible over the
        /// whole menu (first sibling, so the bottom bar's buttons still raycast in front), its
        /// old labels are put away, and a fresh TAP TO PLAY stands in the middle of the lawn.
        /// The menu is the only screen that gets these words: the garage deliberately hangs no
        /// tap label of its own, because it must not offer a route into mission select.
        /// Runtime-built stopgap UI throughout.
        /// </summary>
        private void InstallTapToPlay()
        {
            if (playButton != null)
            {
                RectTransform rect = (RectTransform)playButton.transform;
                rect.SetAsFirstSibling();
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;

                Image pill = playButton.GetComponent<Image>();
                if (pill != null)
                {
                    Color clear = pill.color;
                    clear.a = 0f;
                    pill.color = clear;
                }

                // The pill's own labels live wherever the pill's authored parent puts them, which
                // after the stretch is nowhere predictable - shelved, not reused.
                foreach (TMP_Text label in playButton.GetComponentsInChildren<TMP_Text>(true))
                {
                    label.gameObject.SetActive(false);
                }

                if (mainMenuRoot != null)
                {
                    tapToPlayLabel = MakeTapLabel(mainMenuRoot.transform, "TapToPlayLabel",
                        new Vector2(0.5f, 0.22f), new Vector2(0.5f, 0.5f), Vector2.zero, 56f,
                        null);

                    // A soft breathing pop so the words read as an invitation, not a caption.
                    StartCoroutine(PulseTapLabel(tapToPlayLabel));
                }
            }
        }

        private System.Collections.IEnumerator PulseTapLabel(Transform label)
        {
            while (label != null)
            {
                float k = (Mathf.Sin(Time.unscaledTime * 3.6f) + 1f) * 0.5f;
                label.localScale = Vector3.one * Mathf.Lerp(1f, 1.08f, k);
                yield return null;
            }
        }

        /// <summary>One TAP TO PLAY, optionally clickable. The label is its own hit area.</summary>
        private RectTransform MakeTapLabel(Transform parent, string name, Vector2 anchor, Vector2 pivot,
            Vector2 position, float fontSize, UnityEngine.Events.UnityAction onTap)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            RectTransform rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(560f, 90f);

            TMP_Text label = go.AddComponent<TextMeshProUGUI>();
            if (tapLabelFont != null)
            {
                label.font = tapLabelFont;
            }

            label.text = "TAP TO PLAY";
            label.fontSize = fontSize;
            label.fontStyle = FontStyles.Bold;
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = onTap != null;

            if (onTap != null)
            {
                Button button = go.AddComponent<Button>();
                button.targetGraphic = label;
                button.onClick.AddListener(onTap);
            }

            return rect;
        }

        public void EnterMapSelection()
        {
            // Cleared on the way in, not on the way out: re-picking the map just played has to
            // register as a change, or the flow would sit still on it.
            if (mapSelection != null)
            {
                mapSelection.Clear();
            }

            Enter(GameState.MapSelection);
        }

        public void EnterIapShop()
        {
            Enter(GameState.IapShop);
        }

        public void EnterShop()
        {
            Enter(GameState.Shop);
        }

        /// <summary>
        /// Opens the shop on a particular tab, for a menu button that means a specific section.
        /// Without a tab strip wired it is the same as <see cref="EnterShop"/>.
        /// </summary>
        public void EnterShopTab(int tab)
        {
            Enter(GameState.Shop);

            if (shopTabs != null)
            {
                shopTabs.Show(tab);
            }
        }

        /// <summary>Choosing a map drops straight into play; there is no screen in between.</summary>
        private void HandleMapSelected(MapInfo map)
        {
            // The tutorial selects its own map as one step of a scripted entry that hands out its
            // own three rounds and confirms the pick itself. Answering that selection here would
            // start a second, full-budget run underneath it.
            if (tutorial != null && tutorial.IsStarting)
            {
                return;
            }

            if (map != null)
            {
                StartSelectedMap();
            }
        }

        /// <summary>
        /// Straight from choosing a map to shooting it. The pick screen is gone: the garage is
        /// where ammunition is chosen now, so the run takes the equipped type and fills the map's
        /// whole budget with it. BeginPick still runs first - it is what reads the map's rules.
        /// </summary>
        [ContextMenu("Start Selected Map")]
        public void StartSelectedMap()
        {
            // Deviation from Brief 17, which starts this method at BeginPick. The old road in
            // went through Enter(AmmoPick), and Enter tears a run down on the way into any state
            // that is not a play state - returning debris to its pool, ending the fire controller's
            // run and clearing the map. Enter(Playing) inside ConfirmAmmoPick does none of that,
            // so without this line RETRY and the cleared screen's CONTINUE would build the next
            // structure over the wreckage of the last one, with the pool still believing it owns
            // debris that was about to be destroyed underneath it.
            TearDownRun();
            failsThisEntry = 0;

            // Before the auto-pick, because this is what reads the map's budget into
            // BulletPickLimit and clears whatever the last run left in the inventory.
            if (runController != null)
            {
                runController.BeginPick();
            }

            AutoPickAmmunition();

            // Keeps its name; it is the build-and-begin step, and the tutorial calls it too.
            ConfirmAmmoPick();
        }

        /// <summary>
        /// Fills the run with the bullet equipped in the garage, up to the map's whole budget.
        /// One type fills the budget by design now that the player cannot mix by hand.
        /// </summary>
        private void AutoPickAmmunition()
        {
            BulletLoadout loadout = economy != null ? economy.Loadout : null;
            BulletDefinition bullet = loadout != null ? loadout.Selected : null;

            // No inventory or no catalogue is a scene wiring problem, not a case to invent a
            // fallback for: BeginRun already judges a run that starts with nothing to fire.
            if (bullet == null || bulletInventory == null)
            {
                return;
            }

            int budget = runController != null ? runController.BulletPickLimit : 0;

            // Checked rather than discarded, the way the tutorial checks its own scripted pick:
            // a refusal here means a budget of zero, and a run that cannot fire a shot is
            // otherwise a mystery rather than a misconfiguration.
            if (!bulletInventory.TryPick(bullet.Id, budget))
            {
                Debug.LogWarning(
                    $"{name}: could not load {budget} of \"{bullet.Id}\", so the run starts with "
                    + "nothing to fire. Check the map's bulletPickLimit in the progression config.",
                    this);
            }
        }

        /// <summary>
        /// Commits whatever is in the inventory, builds the structure and starts the run. Keeps
        /// its name now that no screen confirms anything: it is the shared build-and-begin step,
        /// reached from <see cref="StartSelectedMap"/> and from the tutorial's scripted entry.
        /// </summary>
        [ContextMenu("Start Run")]
        public void ConfirmAmmoPick()
        {
            Enter(GameState.Playing);

            // Before the build, so the map is laid out around a root back at its rest pose rather
            // than wherever the last one was dragged to.
            ResetPlayfield();

            // Before the build too: warming the debris queues instantiates a few dozen chunks,
            // and doing that during the first cascade is the cost this is meant to avoid.
            WarmPools();

            if (mapBuilder != null)
            {
                mapBuilder.BuildMap();
            }

            // After the build, because what counts as a hundred percent is what actually got
            // placed, not what the map file asked for.
            if (runController != null)
            {
                runController.BeginRun();
            }
        }

        /// <summary>
        /// Another attempt at the same map, straight back into it. The selection is left alone, so
        /// the map stays; the rounds are re-picked from scratch because BeginPick empties the
        /// inventory before the auto-pick refills it to the map's budget.
        /// </summary>
        [ContextMenu("Retry Map")]
        public void RetryMap()
        {
            StartSelectedMap();
        }

        /// <summary>
        /// The next map in the catalogue, straight into it. Selecting raises SelectionChanged, and
        /// HandleMapSelected does the rest, which is the same road a tap on the mission board
        /// takes; there is no second way into a run. Past the last map there is nothing to
        /// continue to, so the menu it is.
        /// </summary>
        [ContextMenu("Next Map")]
        public void EnterNextMap()
        {
            MapConfig config = mapSelection != null ? mapSelection.Config : null;
            int next = config != null && mapSelection.HasSelection ? config.IndexOf(mapSelection.Selected) + 1 : -1;

            if (config == null || next <= 0 || next >= config.Count)
            {
                ReturnToMainMenu();
                return;
            }

            mapSelection.SelectByIndex(next);
        }

        /// <summary>
        /// Points the fail screen at the right offer for how deep in failures this entry is:
        /// 1st -> buy rounds by the piece, suggested from how far the run fell short (about one
        /// round per 2.5 percent missing); 2nd -> the flat continue; later -> nothing for sale.
        /// </summary>
        private void PresentFailOffer(LevelRunController.RunResult result)
        {
            if (failView == null && failRoot != null)
            {
                failView = failRoot.GetComponentInChildren<FailScreenView>(true);
            }

            if (failView == null)
            {
                return;                              // scene without the view: old behaviour
            }

            FailScreenView.FailMode mode = failsThisEntry <= 1
                ? FailScreenView.FailMode.BuyBullets
                : failsThisEntry == 2
                    ? FailScreenView.FailMode.ClassicContinue
                    : FailScreenView.FailMode.Final;

            float required = runController != null ? runController.RequiredClearPercent : 1f;
            float missing = Mathf.Max(0f, required - result.ClearPercent);
            int max = economy != null ? economy.FirstLoseMaxBullets : 20;

            // The wallet outranks the need. Suggesting what the shortfall wants is useless when
            // the player cannot pay it - a 6,500 suggestion at 1,990 gold just reads as a wall.
            // So: what they can afford first, capped by what the remaining blocks actually need.
            int needed = Mathf.CeilToInt(missing * 40f);
            int affordable = economy != null && economy.LoseBulletPrice > 0
                ? economy.Gold / economy.LoseBulletPrice
                : max;
            int suggested = Mathf.Clamp(Mathf.Min(needed, affordable), 1, max);

            failView.Present(mode, suggested, max);
        }

        private void HandleFailContinuePressed()
        {
            if (failView != null && failView.Mode == FailScreenView.FailMode.BuyBullets)
            {
                ContinueRunWithPurchase();
                return;
            }

            ContinueRun();
        }

        /// <summary>
        /// The first-failure purchase: the dialled-in number of rounds at per-round price. Mirrors
        /// ContinueRun's order exactly - every check first, the charge last, the resume only after
        /// the charge succeeded.
        /// </summary>
        private void ContinueRunWithPurchase()
        {
            if (State != GameState.Result || runController == null || !runController.CanContinueRun())
            {
                return;
            }

            int count = failView != null ? failView.PurchaseCount : 0;
            string bulletId = ResolveContinueBulletId();
            if (economy == null || count <= 0 || string.IsNullOrEmpty(bulletId))
            {
                return;
            }

            if (!economy.TryPayLoseBullets(count))
            {
                AudioService.Play(AudioSlot.Denied);
                return;
            }

            if (!runController.ContinueRun(bulletId, count))
            {
                Debug.LogError(
                    $"{name}: the purchase was paid for but the run refused to resume, so "
                    + $"{count * economy.LoseBulletPrice} gold was spent for nothing.",
                    this);
                return;
            }

            SetRootActive(failRoot, false);
            SetRootActive(hudRoot, true);
            State = GameState.Playing;
            StateChanged?.Invoke(State);
        }

        /// <summary>
        /// Pays for more rounds and picks the failed run back up. Checks come first, the charge
        /// last and only once everything it pays for is certain to happen; nothing here rebuilds
        /// the map or re-enters a state, because Enter tears a run down and this one is being kept.
        /// </summary>
        [ContextMenu("Continue Run")]
        public void ContinueRun()
        {
            if (State != GameState.Result || runController == null || !runController.CanContinueRun())
            {
                return;
            }

            if (economy == null || !economy.CanContinueRun())
            {
                return;
            }

            string bulletId = ResolveContinueBulletId();
            if (string.IsNullOrEmpty(bulletId))
            {
                return;
            }

            // GoldChanged fires in here, which is what makes the fail screen re-read its button.
            if (!economy.TryPayContinue())
            {
                // The fail screen's refusal. It lives here rather than in FailScreenView because
                // the view only draws the price - the flow is what a tap on CONTINUE reaches, and
                // this is the Try* that can turn it down. The guards above are wiring and state
                // problems rather than refusals, so they stay silent.
                AudioService.Play(AudioSlot.Denied);
                return;
            }

            // Checked rather than discarded. Everything this could refuse was already refused
            // above, so false means the run moved between the last check and the charge - and by
            // then the gold is gone. Saying so beats handing the player a HUD over a run that is
            // still finished and letting four thousand gold vanish without a word.
            if (!runController.ContinueRun(bulletId, economy.ContinueAmmo))
            {
                Debug.LogError(
                    $"{name}: the continue was paid for but the run refused to resume, so "
                    + $"{economy.ContinuePrice} gold was spent for nothing.",
                    this);
                return;
            }

            SetRootActive(failRoot, false);
            SetRootActive(hudRoot, true);
            State = GameState.Playing;
            StateChanged?.Invoke(State);
        }

        /// <summary>
        /// The loaded ammunition. Selected already falls back to the starter bullet, so this is
        /// only null in a scene where the economy has no catalogue wired.
        /// </summary>
        private string ResolveContinueBulletId()
        {
            BulletLoadout loadout = economy != null ? economy.Loadout : null;
            BulletDefinition bullet = loadout != null ? loadout.Selected : null;
            return bullet != null ? bullet.Id : null;
        }

        /// <summary>
        /// CONTINUE on the cleared screen. Normally the next map in the catalogue; after the
        /// TUTORIAL run it is the mission board instead.
        ///
        /// The tutorial map is standalone - deliberately not in MapConfig - so IndexOf finds it
        /// nowhere, "next" comes out as index 0 and <see cref="EnterNextMap"/> answers with the
        /// main menu, where the player then has to tap TAP TO PLAY to reach the board they were
        /// always going to. The board is where the team wants them, so they go straight there.
        ///
        /// The cleared screen is still shown first, and this is only what its buttons do
        /// afterwards: the tutorial's 100 gold is paid by the normal reward pipeline and counted
        /// up on that screen, and skipping it to reach the board a second sooner would be paying
        /// the prize where nobody sees it.
        ///
        /// A normal map's CONTINUE is untouched - the branch is on the tutorial flag alone.
        /// </summary>
        private void HandleClearedContinuePressed()
        {
            if (ConsumeTutorialExit())
            {
                EnterMapSelection();
                return;
            }

            EnterNextMap();
        }

        /// <summary>
        /// The X on the cleared screen. The main menu as before, except after the tutorial, where
        /// it lands on the mission board for the same reason CONTINUE does: both ways off that
        /// screen should reach the next level, not a menu asking to be tapped through.
        /// </summary>
        private void HandleClearedClosePressed()
        {
            if (ConsumeTutorialExit())
            {
                EnterMapSelection();
                return;
            }

            ReturnToMainMenu();
        }

        /// <summary>
        /// Whether the cleared screen being left is the tutorial's, clearing the flag as it
        /// answers so that a REPLAY of the tutorial map - an ordinary run of it, since the
        /// tutorial itself is over - cannot inherit the answer.
        /// </summary>
        private bool ConsumeTutorialExit()
        {
            bool wasTutorial = clearedRunWasTutorial;
            clearedRunWasTutorial = false;
            return wasTutorial;
        }

        /// <summary>Leaves a run early from the settings overlay.</summary>
        public void AbandonRun()
        {
            CloseSettings();
            ReturnToMainMenu();
        }

        /// <summary>
        /// One step back from wherever the player is. A single back button that knows the flow
        /// beats one per screen that all do slightly different things.
        /// </summary>
        public void GoBack()
        {
            switch (State)
            {
                case GameState.MapSelection:
                case GameState.IapShop:
                case GameState.Shop:
                case GameState.Playing:
                case GameState.Result:
                    ReturnToMainMenu();
                    break;
            }
        }

        public void OpenSettings()
        {
            SetRootActive(settingsRoot, true);

            // Only worth offering when there is a run to leave.
            SetRootActive(settingsMainMenuButton != null ? settingsMainMenuButton.gameObject : null, IsInRun);
        }

        public void CloseSettings()
        {
            SetRootActive(settingsRoot, false);
        }

        private void HandleRunFinished(LevelRunController.RunResult result)
        {
            // Read here, at the top, and not once RunFinished has been raised: the tutorial drops
            // its own flag inside that event, and which of the two subscribers runs first is not
            // something this should depend on. Only a PASS is remembered - a failed tutorial shows
            // the fail screen, whose buttons this flag has nothing to do with, and the tutorial
            // will simply run again next launch.
            clearedRunWasTutorial = result.Passed && tutorial != null && tutorial.IsRunning;

            // The structure is left standing behind the result: the player should see what they
            // did to it while they read what it was worth.
            //
            // Which of the two results is up is decided here rather than inside a screen, because
            // a pass and a failure ask different questions and only the flow knows which was
            // asked. Both still hear RunFinished; each screen draws only its own kind.
            if (!result.Passed)
            {
                failsThisEntry++;
                PresentFailOffer(result);
            }

            SetRootActive(result.Passed ? clearedRoot : failRoot, true);
            SetRootActive(hudRoot, false);

            State = GameState.Result;
            StateChanged?.Invoke(State);
            RunFinished?.Invoke(result);
        }

        /// <summary>Tears the run down and shows exactly the roots that belong to a state.</summary>
        private void Enter(GameState state)
        {
            if (!IsPlayState(state))
            {
                TearDownRun();

                // Whatever else took the player off the result screen - settings' MAIN MENU, the
                // fail screen's X - has answered the question this was holding, so it must not be
                // waiting the next time a cleared screen goes up.
                clearedRunWasTutorial = false;
            }

            CloseSettings();

            SetRootActive(loadingRoot, state == GameState.Loading);
            SetRootActive(mainMenuRoot, state == GameState.MainMenu);
            SetRootActive(selectionRoot, state == GameState.MapSelection);
            SetRootActive(iapShopRoot, state == GameState.IapShop);
            SetRootActive(shopRoot, state == GameState.Shop);
            SetRootActive(gameplayRoot, state == GameState.Playing);
            SetRootActive(hudRoot, state == GameState.Playing);
            SetRootActive(failRoot, false);
            SetRootActive(clearedRoot, false);

            // The bar carries the way back out of a shop, so it belongs wherever the player might
            // want that; once they are heading into a run it is only clutter.
            SetRootActive(bottomBarRoot, IsMenuState(state));

            // No back button any more. The pick screen was its last home; every screen that is
            // left carries its own X, and the bottom bar's Home is under the rest.

            State = state;
            StateChanged?.Invoke(state);
        }

        private void TearDownRun()
        {
            if (runController != null)
            {
                runController.CancelRun();
            }

            // Before the map goes: active debris hangs under the generated root, and a pooled
            // burst destroyed along with it is one the pool still believes it owns.
            ShatteredBlockPool.ReturnAll();
            BreakEffectPool.StopAll();

            if (fireController != null)
            {
                fireController.EndRun();
            }

            if (mapBuilder != null)
            {
                mapBuilder.ClearMap();
            }
        }

        /// <summary>
        /// Fills the debris and cannon-ball queues for the map about to be played. Both are safe
        /// to call again, which is what a retry does.
        /// </summary>
        private void WarmPools()
        {
            ShatteredBlockPool.Initialize(mapBuilder != null ? mapBuilder.BlockDatabase : null);

            if (fireController != null)
            {
                fireController.PrepareForRun();
            }
        }

        private static bool IsMenuState(GameState state)
        {
            return state == GameState.MainMenu
                   || state == GameState.IapShop
                   || state == GameState.Shop

                   // The mission screen is a menu screen: it is sized to leave the bar showing
                   // under it, and the bar's Home is one of the two ways off it.
                   || state == GameState.MapSelection;
        }

        private static bool IsPlayState(GameState state)
        {
            return state == GameState.Playing || state == GameState.Result;
        }

        private void ResetPlayfield()
        {
            // The view goes back to the authored angle, not the structure: nothing turns the
            // structure any more, so the old structureSpinner.ResetRotation() had nothing left to
            // undo and the reference has gone with it.
            if (cameraOrbit != null)
            {
                cameraOrbit.ResetRotation();
            }

            if (aimController != null)
            {
                aimController.ResetAim();
            }
        }

        /// <summary>
        /// The screen shown for <see cref="GameState.MapSelection"/>, which is simply what was
        /// assigned. The old map list spawned its buttons under its own transform, so this used
        /// to climb to the view that owned them or go looking for one; the mission screen is a
        /// single prefab instance whose root is the thing to switch, so there is nothing to find.
        /// </summary>
        private GameObject ResolveSelectionRoot()
        {
            return mapSelectionRoot;
        }

        private static void SetRootActive(GameObject root, bool active)
        {
            if (root != null && root.activeSelf != active)
            {
                root.SetActive(active);
            }
        }

        private static void Wire(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button != null)
            {
                button.onClick.AddListener(action);
            }
        }

        private static void Unwire(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button != null)
            {
                button.onClick.RemoveListener(action);
            }
        }
    }
}
