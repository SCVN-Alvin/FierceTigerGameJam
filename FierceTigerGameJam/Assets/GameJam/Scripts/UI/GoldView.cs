using System;
using GameJam.Data;
using GameJam.Economy;
using TMPro;
using UnityEngine;

namespace GameJam.UI
{
    /// <summary>
    /// A readout of the player's gold, for menu and result screens.
    ///
    /// A gain is counted up rather than snapped, because a reward the player watches arrive reads
    /// as something they earned, while a number that simply changes reads as a number.
    /// </summary>
    public sealed class GoldView : MonoBehaviour
    {
        [Tooltip("Where gold changes are announced from. Left empty, the saved record is read "
                 + "directly so the readout still works in a scene that is not fully wired.")]
        [SerializeField] private EconomyService economy;

        [SerializeField] private TMP_Text goldLabel;

        [Tooltip("Composite format for the amount, e.g. \"{0}\" or \"Gold: {0}\".")]
        [SerializeField] private string format = "{0}";

        [Tooltip("Seconds a gain takes to count up on screen. Zero snaps straight to the total.")]
        [SerializeField] private float countUpSeconds = 0.4f;

        /// <summary>What is on screen right now, which lags the real total while counting up.</summary>
        private float displayedGold;

        private int targetGold;
        private float goldPerSecond;
        private bool isCountingUp;

        /// <summary>
        /// False until the first refresh. The very first draw has to snap: counting up from zero
        /// on entering a menu would announce gold the player earned in some earlier session.
        /// </summary>
        private bool hasDrawn;

        /// <summary>Set once a bad format string has been reported, so it is not logged per frame.</summary>
        private bool formatWarningLogged;

        private void OnEnable()
        {
            if (economy != null)
            {
                economy.GoldChanged += HandleGoldChanged;
            }
            else
            {
                // The fallback path has nothing announcing gold at it, so it listens to the save
                // itself. Only in the fallback: with a service assigned this would redraw twice
                // for every transaction, since a purchase saves and then raises GoldChanged.
                UserData.Changed += HandleGoldChanged;
            }

            Refresh();
        }

        private void OnDisable()
        {
            if (economy != null)
            {
                economy.GoldChanged -= HandleGoldChanged;
            }
            else
            {
                UserData.Changed -= HandleGoldChanged;
            }

            // Anything mid-count is finished off rather than left frozen part way, so the readout
            // is correct the moment this is enabled again.
            FinishCountUp();
        }

        /// <summary>Redraws from the current total, counting up if it has gone up.</summary>
        [ContextMenu("Refresh")]
        public void Refresh()
        {
            int gold = ReadGold();

            bool canAnimate = hasDrawn && countUpSeconds > 0f && gold > displayedGold;
            hasDrawn = true;

            // Spending snaps. A price the player has already agreed to should read as paid at
            // once, and counting down would suggest it is still being decided.
            if (!canAnimate)
            {
                targetGold = gold;
                displayedGold = gold;
                isCountingUp = false;
                Draw(gold);
                return;
            }

            targetGold = gold;
            goldPerSecond = (targetGold - displayedGold) / countUpSeconds;
            isCountingUp = true;
        }

        /// <summary>
        /// Driven from Update rather than a coroutine: a coroutine dies with the object, which
        /// would leave a result screen showing a part-counted total if it were hidden mid-count.
        /// </summary>
        private void Update()
        {
            if (!isCountingUp)
            {
                return;
            }

            // Unscaled, so the count still runs on a result screen that has paused the game.
            displayedGold = Mathf.MoveTowards(displayedGold, targetGold, goldPerSecond * Time.unscaledDeltaTime);

            if (displayedGold >= targetGold)
            {
                FinishCountUp();
                return;
            }

            Draw(Mathf.FloorToInt(displayedGold));
        }

        private void FinishCountUp()
        {
            if (!isCountingUp)
            {
                return;
            }

            isCountingUp = false;
            displayedGold = targetGold;
            Draw(targetGold);
        }

        private int ReadGold()
        {
            return economy != null ? economy.Gold : UserData.Inventory.gold;
        }

        private void HandleGoldChanged()
        {
            Refresh();
        }

        private void Draw(int gold)
        {
            if (goldLabel == null)
            {
                return;
            }

            goldLabel.text = Compose(gold);
        }

        /// <summary>
        /// A format string is authored per scene and can be wrong, and a wrong one must not take
        /// the readout down with it: the number itself is what the player needs to see.
        /// </summary>
        private string Compose(int gold)
        {
            if (string.IsNullOrEmpty(format))
            {
                return gold.ToString();
            }

            try
            {
                return string.Format(format, gold);
            }
            catch (FormatException exception)
            {
                if (!formatWarningLogged)
                {
                    formatWarningLogged = true;
                    Debug.LogWarning($"{name}: \"{format}\" is not a usable format, showing the plain "
                                     + $"amount instead: {exception.Message}", this);
                }

                return gold.ToString();
            }
        }
    }
}
