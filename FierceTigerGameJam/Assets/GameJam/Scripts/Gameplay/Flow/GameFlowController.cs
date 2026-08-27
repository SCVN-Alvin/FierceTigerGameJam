using System;
using GameJam.Gameplay.Cannon;
using GameJam.Gameplay.Wall;
using GameJam.UI;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace GameJam.Gameplay.Flow
{
    /// <summary>
    /// Drives the whole loop: the menu, the shops, choosing a map, choosing ammunition, the run,
    /// and the result that sends the player back to the menu.
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

            /// <summary>Choosing what ammunition to take into the chosen map.</summary>
            AmmoPick,

            Playing,

            /// <summary>The run is over and the result is on screen.</summary>
            Result,
        }

        [SerializeField] private MapSelection mapSelection;
        [SerializeField] private KnockdownLayoutMapAuthoring mapBuilder;
        [SerializeField] private LevelRunController runController;

        [Header("Screens")]
        [SerializeField] private GameObject mainMenuRoot;
        [SerializeField] private GameObject mapSelectionRoot;
        [SerializeField] private GameObject ammoPickRoot;
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

        [SerializeField] private GameObject resultRoot;

        [Tooltip("The bottom tab bar. Shown on the menu and the shops, since its middle button is "
                 + "what returns from a shop; hidden once the player is on their way into a run.")]
        [SerializeField] private GameObject bottomBarRoot;

        [Tooltip("Settings overlay. A modal rather than a screen: it opens over whatever is up "
                 + "and closing it returns there, so it does not disturb the flow's state.")]
        [SerializeField] private GameObject settingsRoot;

        [Header("Buttons")]
        [SerializeField] private Button playButton;
        [SerializeField] private Button iapShopButton;
        [SerializeField] private Button homeButton;
        [FormerlySerializedAs("bulletShopButton")]
        [SerializeField] private Button shopButton;

        [Tooltip("The X in the garage's top corner. A second way out beside the bottom bar's "
                 + "Home button, not a replacement: the bar is off screen on a phone held in one "
                 + "hand as often as it is under a thumb.")]
        [SerializeField] private Button closeShopButton;
        [SerializeField] private Button startRunButton;
        [Tooltip("Leaves the result for the main menu.")]
        [SerializeField] private Button resultContinueButton;

        [Tooltip("Another go at the same map. Returns to the ammunition pick rather than straight "
                 + "into a run, because the last attempt is what proved the mix was wrong.")]
        [SerializeField] private Button retryButton;
        [SerializeField] private Button backButton;

        [Header("Settings Buttons")]
        [SerializeField] private Button openSettingsButton;
        [SerializeField] private Button openSettingsInRunButton;
        [SerializeField] private Button closeSettingsButton;

        [Tooltip("Abandons the run from the settings overlay. Hidden outside a run, where it would "
                 + "only lead to the screen the player is already on.")]
        [SerializeField] private Button settingsMainMenuButton;

        [Header("Reset On Entering A Map")]
        [SerializeField] private CannonAimController aimController;
        [SerializeField] private SpinOnAxis structureSpinner;

        [Tooltip("Optional. Warmed at the start of a run so the first shot of a map costs no "
                 + "more than the tenth.")]
        [SerializeField] private GridKnockdownCannonFireController fireController;

        public event Action<GameState> StateChanged;

        /// <summary>Raised with the finished run, for a result screen to draw.</summary>
        public event Action<LevelRunController.RunResult> RunFinished;

        public GameState State { get; private set; } = GameState.MainMenu;

        /// <summary>True while a structure is up, so settings can offer a way out of it.</summary>
        public bool IsInRun => State == GameState.Playing || State == GameState.Result;

        private GameObject selectionRoot;

        private void Awake()
        {
            selectionRoot = ResolveSelectionRoot();

            if (aimController == null && gameplayRoot != null)
            {
                aimController = gameplayRoot.GetComponentInChildren<CannonAimController>(true);
            }

            if (structureSpinner == null && mapBuilder != null)
            {
                structureSpinner = mapBuilder.StructureSpinner;
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
            Wire(startRunButton, ConfirmAmmoPick);
            Wire(resultContinueButton, ReturnToMainMenu);
            Wire(retryButton, RetryMap);
            Wire(backButton, GoBack);
            Wire(openSettingsButton, OpenSettings);
            Wire(openSettingsInRunButton, OpenSettings);
            Wire(closeSettingsButton, CloseSettings);
            Wire(settingsMainMenuButton, AbandonRun);
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
            Unwire(startRunButton, ConfirmAmmoPick);
            Unwire(resultContinueButton, ReturnToMainMenu);
            Unwire(retryButton, RetryMap);
            Unwire(backButton, GoBack);
            Unwire(openSettingsButton, OpenSettings);
            Unwire(openSettingsInRunButton, OpenSettings);
            Unwire(closeSettingsButton, CloseSettings);
            Unwire(settingsMainMenuButton, AbandonRun);
        }

        private void Start()
        {
            ReturnToMainMenu();
        }

        [ContextMenu("Main Menu")]
        public void ReturnToMainMenu()
        {
            TearDownRun();
            Enter(GameState.MainMenu);
        }

        [ContextMenu("Map Selection")]
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

        /// <summary>
        /// Choosing a map does not drop straight into play: the player picks ammunition first, and
        /// the structure is not built until they commit, so the pick screen is never sitting on
        /// top of a live physics scene.
        /// </summary>
        private void HandleMapSelected(MapInfo map)
        {
            if (map != null)
            {
                EnterAmmoPick();
            }
        }

        [ContextMenu("Ammo Pick")]
        public void EnterAmmoPick()
        {
            if (mapBuilder != null)
            {
                mapBuilder.ClearMap();
            }

            // Opened before the screen shows, so the budget and the pass bar are already read from
            // the map by the time the pick UI draws itself.
            if (runController != null)
            {
                runController.BeginPick();
            }

            Enter(GameState.AmmoPick);
        }

        /// <summary>Commits the pick, builds the structure and starts the run.</summary>
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
        /// Another attempt at the same map, back at the ammunition pick. The selection is left
        /// alone, so the player keeps the map and only re-chooses what to bring; going straight
        /// back into a run would hand them the same mix that just failed.
        /// </summary>
        [ContextMenu("Retry Map")]
        public void RetryMap()
        {
            EnterAmmoPick();
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
                case GameState.AmmoPick:
                    EnterMapSelection();
                    break;
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
            // The structure is left standing behind the result: the player should see what they
            // did to it while they read what it was worth.
            SetRootActive(resultRoot, true);
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
            }

            CloseSettings();

            SetRootActive(mainMenuRoot, state == GameState.MainMenu);
            SetRootActive(selectionRoot, state == GameState.MapSelection);
            SetRootActive(ammoPickRoot, state == GameState.AmmoPick);
            SetRootActive(iapShopRoot, state == GameState.IapShop);
            SetRootActive(shopRoot, state == GameState.Shop);
            SetRootActive(gameplayRoot, state == GameState.Playing);
            SetRootActive(hudRoot, state == GameState.Playing);
            SetRootActive(resultRoot, false);

            // The bar carries the way back out of a shop, so it belongs wherever the player might
            // want that; once they are heading into a run it is only clutter.
            SetRootActive(bottomBarRoot, IsMenuState(state));

            // Back is for the screens the bar does not cover.
            SetRootActive(
                backButton != null ? backButton.gameObject : null,
                state == GameState.MapSelection || state == GameState.AmmoPick);

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
                   || state == GameState.Shop;
        }

        private static bool IsPlayState(GameState state)
        {
            return state == GameState.Playing || state == GameState.Result;
        }

        private void ResetPlayfield()
        {
            if (structureSpinner != null)
            {
                structureSpinner.ResetRotation();
            }

            if (aimController != null)
            {
                aimController.ResetAim();
            }
        }

        /// <summary>
        /// The map list spawns its buttons under its own transform, so hiding anything below the
        /// view switches off the backdrop and leaves the buttons on screen. Whatever was pointed
        /// at, hide the view that owns them.
        /// </summary>
        private GameObject ResolveSelectionRoot()
        {
            if (mapSelectionRoot == null)
            {
                MapListView foundView = FindFirstObjectByType<MapListView>(FindObjectsInactive.Include);
                return foundView != null ? foundView.gameObject : null;
            }

            MapListView view = mapSelectionRoot.GetComponentInParent<MapListView>(true);
            return view != null ? view.gameObject : mapSelectionRoot;
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
