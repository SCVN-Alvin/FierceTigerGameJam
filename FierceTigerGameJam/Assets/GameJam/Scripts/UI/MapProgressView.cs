using System;
using GameJam.Data;
using GameJam.Gameplay.Wall;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GameJam.UI
{
    /// <summary>
    /// The "3 / 10" chip on the main menu: how many maps the player has beaten out of how many
    /// exist.
    ///
    /// The total is counted from the map config rather than typed in, so adding a map to the list
    /// is the only step needed: a hand-written total would go stale the moment the list grows.
    /// </summary>
    public sealed class MapProgressView : MonoBehaviour
    {
        [Tooltip("The maps that exist. Left empty, the chip reads zero of zero rather than "
                 + "guessing at a total.")]
        [SerializeField] private MapConfig mapConfig;

        [SerializeField] private TMP_Text label;

        [Header("Optional")]
        [Tooltip("A filled Image whose fillAmount is set to passed over total.")]
        [SerializeField] private Image progressFill;

        [Tooltip("Composite format for the two numbers, e.g. \"{0}/{1}\" or \"{0} of {1} maps\".")]
        [SerializeField] private string format = "{0}/{1}";

        /// <summary>Set once a bad format string has been reported, so it is not logged per redraw.</summary>
        private bool formatWarningLogged;

        /// <summary>How many maps in the config the player has passed, as of the last refresh.</summary>
        public int PassedCount { get; private set; }

        /// <summary>How many maps the config lists, as of the last refresh.</summary>
        public int TotalCount { get; private set; }

        private void OnEnable()
        {
            // A finished run saves, and saving raises this, so the chip is current the moment the
            // player is back on the menu without the menu having to be rebuilt.
            UserData.Changed += HandleUserDataChanged;
            Refresh();
        }

        private void OnDisable()
        {
            // UserData is static, so a handler left subscribed outlives this object and keeps a
            // destroyed view alive along with it.
            UserData.Changed -= HandleUserDataChanged;
        }

        /// <summary>Recounts the passed maps and redraws.</summary>
        [ContextMenu("Refresh")]
        public void Refresh()
        {
            Count(out int passed, out int total);
            PassedCount = passed;
            TotalCount = total;
            Draw(passed, total);
        }

        /// <summary>
        /// A map counts once it has been passed, which includes one cleared to a hundred percent.
        /// Failed attempts are recorded against the map but leave it unpassed, so they do not
        /// count here.
        /// </summary>
        private void Count(out int passed, out int total)
        {
            passed = 0;
            total = 0;

            if (mapConfig == null)
            {
                return;
            }

            UserMapProgressData progress = UserData.Maps;
            for (int i = 0; i < mapConfig.Count; i++)
            {
                MapInfo map = mapConfig.Get(i);
                if (map == null || string.IsNullOrEmpty(map.Id))
                {
                    // An entry with no id cannot be looked up, and counting it in the total would
                    // give the player a map they can never tick off.
                    continue;
                }

                total++;

                if (progress.IsPassed(map.Id))
                {
                    passed++;
                }
            }
        }

        private void Draw(int passed, int total)
        {
            if (label != null)
            {
                label.text = Compose(passed, total);
            }

            if (progressFill != null)
            {
                // Total is zero in an unwired scene, and dividing by it would leave the bar showing
                // NaN, which Unity draws as an empty or garbled fill.
                progressFill.fillAmount = total > 0 ? Mathf.Clamp01((float)passed / total) : 0f;
            }
        }

        /// <summary>
        /// A format string is authored per scene and can be wrong, and a wrong one must not take
        /// the chip down with it: the two numbers are what the player needs to see.
        /// </summary>
        private string Compose(int passed, int total)
        {
            if (string.IsNullOrEmpty(format))
            {
                return $"{passed}/{total}";
            }

            try
            {
                return string.Format(format, passed, total);
            }
            catch (FormatException exception)
            {
                if (!formatWarningLogged)
                {
                    formatWarningLogged = true;
                    Debug.LogWarning($"{name}: \"{format}\" is not a usable format, showing the plain "
                                     + $"counts instead: {exception.Message}", this);
                }

                return $"{passed}/{total}";
            }
        }

        private void HandleUserDataChanged()
        {
            Refresh();
        }
    }
}
