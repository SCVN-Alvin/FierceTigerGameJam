using System;
using UnityEngine;
using UnityEngine.UI;

namespace GameJam.UI
{
    /// <summary>
    /// The settings panel behind the gear button, on the main menu and in game.
    ///
    /// A mock for now: there are no settings to change yet, so what it has to get right is opening,
    /// closing, and offering a way out of a run. It reports what was pressed and nothing more,
    /// because the flow controller owns screen transitions and a view that changed screens itself
    /// would be a second place they are decided.
    /// </summary>
    public sealed class SettingsPanelView : MonoBehaviour
    {
        [SerializeField] private Button closeButton;

        [Header("Abandon the run")]
        [SerializeField] private Button mainMenuButton;

        [Tooltip("What is shown or hidden for the main menu button, usually the button's own "
                 + "object or a row holding it. Left empty, the button object itself is used.")]
        [SerializeField] private GameObject mainMenuButtonRoot;

        [Tooltip("Whether the main menu button is offered. Set this on the in-game copy of the "
                 + "panel, or drive it from the flow with SetInRun.")]
        [SerializeField] private bool showMainMenuButton;

        /// <summary>Raised when the player asks to close the panel and carry on.</summary>
        public event Action CloseRequested;

        /// <summary>
        /// Raised when the player asks to leave for the main menu. Whether that needs confirming,
        /// and what happens to the run in progress, is the flow controller's call.
        /// </summary>
        public event Action MainMenuRequested;

        private void OnEnable()
        {
            if (closeButton != null)
            {
                closeButton.onClick.AddListener(HandleCloseClicked);
            }

            if (mainMenuButton != null)
            {
                mainMenuButton.onClick.AddListener(HandleMainMenuClicked);
            }

            // The panel is opened and closed repeatedly, so the current setting is reapplied on
            // every open rather than only once at startup.
            ApplyMainMenuButtonVisibility();
        }

        private void OnDisable()
        {
            // Paired with the adds above: a panel opened five times would otherwise raise its
            // events five times per press.
            if (closeButton != null)
            {
                closeButton.onClick.RemoveListener(HandleCloseClicked);
            }

            if (mainMenuButton != null)
            {
                mainMenuButton.onClick.RemoveListener(HandleMainMenuClicked);
            }
        }

        /// <summary>
        /// Shows or hides the main menu button. Abandoning a run only means something during one:
        /// on the main menu it would be a button that goes where the player already is.
        /// </summary>
        public void SetInRun(bool inRun)
        {
            showMainMenuButton = inRun;
            ApplyMainMenuButtonVisibility();
        }

        private void ApplyMainMenuButtonVisibility()
        {
            GameObject root = ResolveMainMenuButtonRoot();
            if (root != null)
            {
                root.SetActive(showMainMenuButton);
            }
        }

        /// <summary>
        /// The explicit root wins, since the button is often wrapped in a row with a label. The
        /// button's own object is the fallback so the panel still behaves with only the button
        /// assigned.
        /// </summary>
        private GameObject ResolveMainMenuButtonRoot()
        {
            if (mainMenuButtonRoot != null)
            {
                return mainMenuButtonRoot;
            }

            return mainMenuButton != null ? mainMenuButton.gameObject : null;
        }

        private void HandleCloseClicked()
        {
            CloseRequested?.Invoke();
        }

        private void HandleMainMenuClicked()
        {
            MainMenuRequested?.Invoke();
        }
    }
}
