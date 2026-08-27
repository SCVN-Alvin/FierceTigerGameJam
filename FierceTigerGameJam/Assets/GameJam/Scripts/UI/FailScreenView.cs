using System.Globalization;
using GameJam.Economy;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GameJam.UI
{
    /// <summary>
    /// The failed run's one question. It shows the price and whether the player can pay it; the
    /// paying, and everything that follows, is the flow's, so this view never touches the run.
    ///
    /// It does not listen for RunFinished the way the cleared screen does. The flow only switches
    /// this root on for a failed run, and it does so before raising the event, so being enabled is
    /// already the whole of "show" - and being enabled is also what a continue undoes, which makes
    /// OnEnable the one place the price has to be re-read.
    /// </summary>
    public sealed class FailScreenView : MonoBehaviour
    {
        [Tooltip("Where the price comes from and what says whether the player can cover it.")]
        [SerializeField] private EconomyService economy;

        [Tooltip("Dimmed rather than hidden when the player is short of gold: a price they cannot "
                 + "meet is still the thing the screen is telling them.")]
        [SerializeField] private Button continueButton;

        [SerializeField] private TMP_Text priceLabel;

        private void OnEnable()
        {
            if (economy != null)
            {
                economy.GoldChanged += Refresh;
            }

            Refresh();
        }

        private void OnDisable()
        {
            // The economy is an asset and outlives the scene, so a subscription left behind would
            // fire into a destroyed screen for the rest of the session.
            if (economy != null)
            {
                economy.GoldChanged -= Refresh;
            }
        }

        /// <summary>
        /// Re-reads the price and what the player can afford. Called on every gold change as well
        /// as on show, because the charge for a continue happens while this screen is still up.
        /// </summary>
        private void Refresh()
        {
            if (economy == null)
            {
                return;
            }

            if (priceLabel != null)
            {
                // Invariant, so the thousands separator is the comma the art was drawn around
                // whatever locale the device is set to.
                priceLabel.text = economy.ContinuePrice.ToString("N0", CultureInfo.InvariantCulture);
            }

            if (continueButton != null)
            {
                continueButton.interactable = economy.CanContinueRun();
            }
        }
    }
}
