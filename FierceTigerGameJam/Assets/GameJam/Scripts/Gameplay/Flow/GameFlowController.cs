using System;
using GameJam.Gameplay.Cannon;
using GameJam.Gameplay.Wall;
using UnityEngine;
using UnityEngine.UI;

namespace GameJam.Gameplay.Flow
{
    /// <summary>
    /// Drives the top level loop: the player starts on the map list, picking a map drops them into
    /// gameplay with that structure built, and Back returns them to the list.
    /// </summary>
    public sealed class GameFlowController : MonoBehaviour
    {
        public enum GameState
        {
            MapSelection,
            Playing,
        }

        [SerializeField] private MapSelection mapSelection;
        [SerializeField] private KnockdownLayoutMapAuthoring mapBuilder;

        [Tooltip("Root holding the map list UI.")]
        [SerializeField] private GameObject mapSelectionRoot;

        [Tooltip("Root holding the slingshot, the structure and anything else only alive in play.")]
        [SerializeField] private GameObject gameplayRoot;

        [Tooltip("Optional. Wired to Back automatically; a UI Button can also call EnterMapSelection directly.")]
        [SerializeField] private Button backButton;

        [Header("Reset On Entering A Map")]
        [Tooltip("Optional. Put back to its rest rotation so a new map is not aimed at with the "
                 + "angle left over from the last one. Found under the gameplay root if empty.")]
        [SerializeField] private CannonAimController aimController;

        [Tooltip("Optional. Straightened out with the aim. Taken from the map builder if empty.")]
        [SerializeField] private SpinOnAxis structureSpinner;

        public event Action<GameState> StateChanged;

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

        private void OnEnable()
        {
            if (mapSelection != null)
            {
                mapSelection.SelectionChanged += HandleMapSelected;
            }

            if (backButton != null)
            {
                backButton.onClick.AddListener(EnterMapSelection);
            }
        }

        private void OnDisable()
        {
            if (mapSelection != null)
            {
                mapSelection.SelectionChanged -= HandleMapSelected;
            }

            if (backButton != null)
            {
                backButton.onClick.RemoveListener(EnterMapSelection);
            }
        }

        private void Start()
        {
            EnterMapSelection();
        }

        private void HandleMapSelected(MapInfo map)
        {
            if (map != null)
            {
                EnterGameplay();
            }
        }

        [ContextMenu("Enter Gameplay")]
        public void EnterGameplay()
        {
            // Activated before building: the builder cannot spawn into a hierarchy that is off,
            // and its own Start would otherwise race this call.
            SetRootActive(gameplayRoot, true);
            SetRootActive(selectionRoot, false);
            SetBackButtonVisible(true);

            // Before the build, so the map is laid out around a root that is back at its rest
            // pose rather than wherever the last map was dragged to.
            ResetPlayfield();

            if (mapBuilder != null)
            {
                mapBuilder.BuildMap();
            }

            SetState(GameState.Playing);
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

        [ContextMenu("Enter Map Selection")]
        public void EnterMapSelection()
        {
            // Cleared while the gameplay root is still live so the blocks can actually be reached.
            if (mapBuilder != null)
            {
                mapBuilder.ClearMap();
            }

            SetRootActive(gameplayRoot, false);
            SetRootActive(selectionRoot, true);
            SetBackButtonVisible(false);

            // Silent, so leaving does not immediately bounce back in, and so re-picking the same
            // map still registers as a change.
            if (mapSelection != null)
            {
                mapSelection.Clear();
            }

            SetState(GameState.MapSelection);
        }

        private void SetState(GameState next)
        {
            State = next;
            StateChanged?.Invoke(next);
        }

        /// <summary>
        /// Back only means anything while a map is up. Skipped when the button lives inside the
        /// selection UI, since that whole branch is already being switched.
        /// </summary>
        private void SetBackButtonVisible(bool visible)
        {
            if (backButton == null)
            {
                return;
            }

            if (selectionRoot != null && backButton.transform.IsChildOf(selectionRoot.transform))
            {
                return;
            }

            SetRootActive(backButton.gameObject, visible);
        }

        private static void SetRootActive(GameObject root, bool active)
        {
            if (root != null && root.activeSelf != active)
            {
                root.SetActive(active);
            }
        }
    }
}
