using System;
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

        public event Action<GameState> StateChanged;

        public GameState State { get; private set; } = GameState.MapSelection;

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
            SetRootActive(mapSelectionRoot, false);

            if (mapBuilder != null)
            {
                mapBuilder.BuildMap();
            }

            SetState(GameState.Playing);
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
            SetRootActive(mapSelectionRoot, true);

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

        private static void SetRootActive(GameObject root, bool active)
        {
            if (root != null && root.activeSelf != active)
            {
                root.SetActive(active);
            }
        }
    }
}
