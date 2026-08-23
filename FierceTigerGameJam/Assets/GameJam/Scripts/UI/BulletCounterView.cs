using GameJam.Gameplay.Combat;
using TMPro;
using UnityEngine;

namespace GameJam.UI
{
    /// <summary>
    /// The number on the in-run bullet counter: how many shots the player has left, across every
    /// kind they brought.
    ///
    /// Its own component rather than part of the HUD, because it belongs to the counter art and
    /// is positioned with it. A readout owned by whatever draws it cannot drift away from the
    /// thing it is drawn on.
    /// </summary>
    public sealed class BulletCounterView : MonoBehaviour
    {
        [SerializeField] private BulletInventory inventory;
        [SerializeField] private TMP_Text countLabel;

        private void OnEnable()
        {
            if (inventory != null)
            {
                inventory.Changed += Refresh;
            }

            Refresh();
        }

        private void OnDisable()
        {
            if (inventory != null)
            {
                inventory.Changed -= Refresh;
            }
        }

        [ContextMenu("Refresh")]
        public void Refresh()
        {
            if (countLabel != null)
            {
                countLabel.text = (inventory != null ? inventory.TotalCount : 0).ToString();
            }
        }
    }
}
