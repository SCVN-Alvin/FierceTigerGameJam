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

            [Tooltip("Optional. When both are set the tab swaps sprites instead of tinting, and "
                     + "the tint colours are ignored for it. The text is part of these sprites.")]
            public Sprite selectedSprite;

            public Sprite unselectedSprite;
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

                ApplyState(tab, selected);
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
        /// Shows a tab as open or closed, either of two ways.
        ///
        /// A tab that was given both of its sprites swaps between them: the garage's tabs have
        /// their words painted into the art, so there is no label to tint and tinting the art
        /// would only wash the whole button out. Anything else falls back to the tint, which is
        /// what a tab strip made of plain coloured rectangles needs, so a second strip elsewhere
        /// keeps working without being given art it does not have.
        /// </summary>
        private void ApplyState(Tab tab, bool selected)
        {
            if (tab.button == null)
            {
                return;
            }

            if (tab.selectedSprite != null && tab.unselectedSprite != null
                && tab.button.targetGraphic is Image image)
            {
                image.sprite = selected ? tab.selectedSprite : tab.unselectedSprite;

                // Back to white: a tint left over from the other path would darken the art the
                // swap just chose. The button's own ColorTint transition still tints on press,
                // which is the feedback a tap needs and is undone when the finger lifts.
                image.color = Color.white;
                return;
            }

            Color tint = selected ? selectedTint : unselectedTint;

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
