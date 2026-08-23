using System;
using GameJam.Gameplay.Cannon;
using GameJam.Gameplay.Wall;
using UnityEngine;
using UnityEngine.UI;

namespace GameJam.Gameplay.Flow
{
    /// <summary>
    /// Drives the top level loop: choose a map, choose what ammunition to bring, play the run,
    /// then see what it came to.
    ///
    /// The controller only decides which screen is up and when the run starts and stops. It talks
    /// to the screens through plain methods that buttons call, rather than holding references to
    /// the view types, so a screen can be redesigned or replaced without touching the flow.
    /// </summary>
    public sealed class GameFlowController : MonoBehaviour
    {
        public enum GameState
        {
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
        [Tooltip("Root holding the map list UI.")]
        [SerializeField] private GameObject mapSelectionRoot;

        [Tooltip("Root holding the ammunition pick UI.")]
        [SerializeField] private GameObject ammoPickRoot;

        [Tooltip("Root holding the slingshot, the structure and anything else only alive in play.")]
        [SerializeField] private GameObject gameplayRoot;

        [Tooltip("Root holding the in-run readouts. Shown with gameplay, hidden on the result.")]
        [SerializeField] private GameObject hudRoot;

        [Tooltip("Root holding the win and lose panel.")]
        [SerializeField] private GameObject resultRoot;

        [Header("Buttons")]
        [Tooltip("Optional. Leaves whatever screen is up and returns to the map list.")]
        [SerializeField] private Button backButton;

        [Tooltip("Optional. Commits the ammunition pick and starts the run.")]
        [SerializeField] private Button startRunButton;

        [Tooltip("Optional. Plays the same map again from the ammunition pick.")]
        [SerializeField] private Button retryButton;

        [Header("Reset On Entering A Map")]
        [Tooltip("Optional. Put back to its rest rotation so a new map is not aimed at with the "
                 + "angle left over from the last one. Found under the gameplay root if empty.")]
        [SerializeField] private CannonAimController aimController;

        [Tooltip("Optional. Straightened out with the aim. Taken from the map builder if empty.")]
        [SerializeField] private SpinOnAxis structureSpinner;

        public event Action<GameState> StateChanged;

        /// <summary>Raised with the finished run, for a result screen to draw.</summary>
        public event Action<LevelRunController.RunResult> RunFinished;

        public GameState State { get; private set; } = GameState.MapSelection;

        /// <summary>
        /// Cached separately from the serialized field so the authored value is left alone: what
        /// gets hidden may need to be an ancestor of it. See <see cref="ResolveSelectionRoot"/>.
        /// </summary>
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

            Wire(backButton, ReturnToMapSelection);
            Wire(startRunButton, ConfirmAmmoPick);
            Wire(retryButton, RetryMap);
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

            Unwire(backButton, ReturnToMapSelection);
            Unwire(startRunButton, ConfirmAmmoPick);
            Unwire(retryButton, RetryMap);
        }

        private void Start()
        {
            ReturnToMapSelection();
        }

        /// <summary>
        /// Choosing a map no longer drops straight into play: the player picks their ammunition
        /// first, and the structure is not built until they commit, so the pick screen is not
        /// sitting on top of a live physics scene.
        /// </summary>
        private void HandleMapSelected(MapInfo map)
        {
            if (map != null)
            {
                EnterAmmoPick();
            }
        }

        [ContextMenu("Enter Map Selection")]
        public void ReturnToMapSelection()
        {
            if (runController != null)
            {
                runController.CancelRun();
            }

            if (mapBuilder != null)
            {
                mapBuilder.ClearMap();
            }

            ShowOnly(GameState.MapSelection);

            // Silent, so leaving does not immediately bounce back in, and so re-picking the same
            // map still registers as a change.
            if (mapSelection != null)
            {
                mapSelection.Clear();
            }

            SetState(GameState.MapSelection);
        }

        [ContextMenu("Enter Ammo Pick")]
        public void EnterAmmoPick()
        {
            if (mapBuilder != null)
            {
                mapBuilder.ClearMap();
            }

            // Opened before the screen is shown so the budget and the pass bar are already read
            // from the map by the time the pick UI draws itself.
            if (runController != null)
            {
                runController.BeginPick();
            }

            ShowOnly(GameState.AmmoPick);
            SetState(GameState.AmmoPick);
        }

        /// <summary>Commits the pick, builds the structure and starts the run.</summary>
        [ContextMenu("Start Run")]
        public void ConfirmAmmoPick()
        {
            // Activated before building: the builder cannot spawn into a hierarchy that is off.
            ShowOnly(GameState.Playing);

            // Before the build, so the map is laid out around a root that is back at its rest
            // pose rather than wherever the last map was dragged to.
            ResetPlayfield();

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

            SetState(GameState.Playing);
        }

        /// <summary>Another go at the same map, back at the ammunition pick.</summary>
        [ContextMenu("Retry Map")]
        public void RetryMap()
        {
            EnterAmmoPick();
        }

        private void HandleRunFinished(LevelRunController.RunResult result)
        {
            // The structure is left standing behind the result panel: the player should see what
            // they did to it while they read what it was worth.
            SetRootActive(resultRoot, true);
            SetRootActive(hudRoot, false);

            SetState(GameState.Result);
            RunFinished?.Invoke(result);
        }

        /// <summary>Turns on exactly the roots that belong to a state, and everything else off.</summary>
        private void ShowOnly(GameState state)
        {
            SetRootActive(selectionRoot, state == GameState.MapSelection);
            SetRootActive(ammoPickRoot, state == GameState.AmmoPick);
            SetRootActive(gameplayRoot, state == GameState.Playing);
            SetRootActive(hudRoot, state == GameState.Playing);
            SetRootActive(resultRoot, false);

            // Back is only meaningful once the player has left the map list.
            SetRootActive(backButton != null ? backButton.gameObject : null, state != GameState.MapSelection);
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
        /// view - a background panel, say - switches off the backdrop and leaves the buttons on
        /// screen. Whatever was pointed at, hide the view that owns them.
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

        private void SetState(GameState next)
        {
            State = next;
            StateChanged?.Invoke(next);
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
