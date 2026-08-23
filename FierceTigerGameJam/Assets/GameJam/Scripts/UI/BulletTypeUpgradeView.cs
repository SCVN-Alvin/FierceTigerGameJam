using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GameJam.UI
{
    /// <summary>
    /// One row of the ammunition shop: what it is, how far it has been taken, and the single
    /// button that buys or upgrades it.
    ///
    /// The row states what it holds instead of leaving the shop to guess. Without this the shop
    /// has to find labels by object name and by position among the buttons, which works until
    /// somebody renames a child or adds a second button, and then fails quietly with a row that
    /// looks fine and says nothing.
    ///
    /// The shop decides what the row says; this only knows where to put it.
    /// </summary>
    public sealed class BulletTypeUpgradeView : MonoBehaviour
    {
        [SerializeField] private TMP_Text nameLabel;
        [SerializeField] private TMP_Text levelLabel;
        [SerializeField] private Button actionButton;

        [Tooltip("Caption on the button, usually a TMP text under it.")]
        [SerializeField] private TMP_Text actionLabel;

        /// <summary>The shop wires the click, since what it does depends on what is owned.</summary>
        public Button ActionButton => actionButton;

        /// <summary>
        /// Fills the row in. Any label the prefab does not have is simply skipped rather than
        /// folded in elsewhere: this row was authored deliberately, so a missing label is a
        /// decision rather than something to work around.
        /// </summary>
        public void Bind(string displayName, string levelText, string actionText, bool interactable)
        {
            SetText(nameLabel, displayName);
            SetText(levelLabel, levelText);
            SetText(actionLabel, actionText);

            if (actionButton != null)
            {
                actionButton.interactable = interactable;
            }
        }

        private static void SetText(TMP_Text label, string value)
        {
            if (label != null)
            {
                label.text = value;
            }
        }

        /// <summary>
        /// Fills in whatever was left empty from the children, by the names the prefab already
        /// uses. Adding this component to an existing row then wires itself rather than leaving
        /// four references to be dragged in, and anything set by hand is never overwritten.
        /// </summary>
        private void ResolveMissingReferences()
        {
            if (actionButton == null)
            {
                actionButton = GetComponentInChildren<Button>(true);
            }

            if (actionLabel == null && actionButton != null)
            {
                actionLabel = actionButton.GetComponentInChildren<TMP_Text>(true);
            }

            if (nameLabel == null)
            {
                nameLabel = FindLabel("Name");
            }

            if (levelLabel == null)
            {
                levelLabel = FindLabel("Level");
            }
        }

        /// <summary>
        /// Matches on the object's name, and never returns the button's own caption: that text
        /// belongs to the button and writing the ammunition's name into it would replace the
        /// price with it.
        /// </summary>
        private TMP_Text FindLabel(string objectName)
        {
            TMP_Text[] candidates = GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < candidates.Length; i++)
            {
                if (candidates[i] == actionLabel)
                {
                    continue;
                }

                if (string.Equals(candidates[i].gameObject.name, objectName, System.StringComparison.OrdinalIgnoreCase))
                {
                    return candidates[i];
                }
            }

            return null;
        }

        private void Reset()
        {
            ResolveMissingReferences();
        }

        private void OnValidate()
        {
            ResolveMissingReferences();
        }

        private void Awake()
        {
            // Also at runtime, so a row instantiated from a prefab that was never opened in the
            // inspector still knows its own parts.
            ResolveMissingReferences();
        }
    }
}
