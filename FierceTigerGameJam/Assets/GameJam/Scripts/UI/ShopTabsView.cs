using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GameJam.UI
{
    /// <summary>
    /// Switches the shop between its sections.
    ///
    /// There used to be a shop per thing sold, each its own screen and its own game state. That
    /// does not scale past two - every new thing to sell wants another button on a main menu
    /// whose art has three slots - and it makes the player leave one shop to reach another with
    /// the same gold in the corner.
    ///
    /// Each tab owns a panel, and showing a panel is the whole of the switch:
    /// <see cref="BulletShopView"/> and <see cref="VehicleShopView"/> both build their rows and
    /// subscribe to their sources in OnEnable and let go in OnDisable, so activating a panel
    /// refreshes it and deactivating it stops it listening. Neither view needs to know that tabs
    /// exist, and a third section is a tab and a panel rather than a screen and a state.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ShopTabsView : MonoBehaviour
    {
        [Serializable]
        public sealed class Tab
        {
            [Tooltip("Only for reading the inspector; the button's own label is what the player sees.")]
            public string name;

            public Button button;

            [Tooltip("Shown while this tab is selected and switched off otherwise. The view "
                     + "inside it refreshes on the way in because it subscribes in OnEnable.")]
            public GameObject panel;
        }

        [SerializeField] private Tab[] tabs = Array.Empty<Tab>();

        [Tooltip("Opened when the shop is entered for the first time in a session.")]
        [SerializeField] private int defaultTab;

        [SerializeField] private Color selectedTint = Color.white;
        [SerializeField] private Color unselectedTint = new Color(0.62f, 0.62f, 0.62f, 1f);

        /// <summary>
        /// Survives the screen being closed and reopened, so a player who was looking at
        /// vehicles comes back to vehicles. Reset only by reloading the scene.
        /// </summary>
        private int current = -1;

        public int TabCount => tabs.Length;
        public int Current => current;

        /// <summary>
        /// Shows one section. Out-of-range indices are clamped rather than refused: the caller
        /// is usually a menu button, and a shop that opens on the wrong tab is better than a
        /// shop that opens on none.
        /// </summary>
        public void Show(int index)
        {
            if (tabs.Length == 0)
            {
                return;
            }

            current = Mathf.Clamp(index, 0, tabs.Length - 1);

            for (int i = 0; i < tabs.Length; i++)
            {
                Tab tab = tabs[i];
                if (tab == null)
                {
                    continue;
                }

                bool selected = i == current;

                if (tab.panel != null && tab.panel.activeSelf != selected)
                {
                    tab.panel.SetActive(selected);
                }

                ApplyTint(tab, selected);
            }
        }

        private void OnEnable()
        {
            for (int i = 0; i < tabs.Length; i++)
            {
                Button button = tabs[i]?.button;
                if (button == null)
                {
                    continue;
                }

                // The index has to be copied, or every listener closes over the loop variable
                // and each tab opens the last one.
                int index = i;
                button.onClick.AddListener(() => Show(index));
            }

            Show(current >= 0 ? current : defaultTab);
        }

        private void OnDisable()
        {
            for (int i = 0; i < tabs.Length; i++)
            {
                tabs[i]?.button?.onClick.RemoveAllListeners();
            }
        }

        /// <summary>
        /// The selected tab reads at full strength and the rest are knocked back. Applied to the
        /// button's own graphic and its label, so it works whether a tab is a sprite or a plain
        /// coloured rectangle.
        /// </summary>
        private void ApplyTint(Tab tab, bool selected)
        {
            Color tint = selected ? selectedTint : unselectedTint;

            if (tab.button != null)
            {
                if (tab.button.targetGraphic != null)
                {
                    tab.button.targetGraphic.color = tint;
                }

                TMP_Text label = tab.button.GetComponentInChildren<TMP_Text>(true);
                if (label != null)
                {
                    label.color = tint;
                }
            }
        }
    }
}
