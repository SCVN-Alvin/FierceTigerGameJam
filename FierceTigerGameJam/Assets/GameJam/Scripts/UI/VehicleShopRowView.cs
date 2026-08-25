using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GameJam.UI
{
    /// <summary>
    /// One row of the vehicle shop: what it is, how far it has been taken, and the two buttons
    /// that spend on it.
    ///
    /// Two, where a bullet row has one, because a vehicle carries two separate decisions: what
    /// the player owns and what they are driving. Buying a vehicle does not mount it and mounting
    /// one costs nothing, so folding both into a single button would make one of them
    /// unreachable.
    ///
    /// The row states what it holds instead of leaving the shop to find labels by name and
    /// buttons by position, which is exactly the guesswork that breaks the moment a row has more
    /// than one button. The shop decides what the row says; this only knows where to put it.
    /// </summary>
    public sealed class VehicleShopRowView : MonoBehaviour
    {
        [SerializeField] private TMP_Text nameLabel;
        [SerializeField] private TMP_Text levelLabel;

        [Tooltip("Buys the vehicle, or buys its next level. Never hidden: a row that loses a "
                 + "button changes width and the list stops lining up.")]
        [SerializeField] private Button primaryButton;

        [Tooltip("Caption on the primary button, usually a TMP text under it.")]
        [SerializeField] private TMP_Text primaryLabel;

        [Tooltip("Mounts the vehicle. Dead on a locked vehicle and on the one already mounted.")]
        [SerializeField] private Button selectButton;

        [Tooltip("Caption on the select button.")]
        [SerializeField] private TMP_Text selectLabel;

        /// <summary>The shop wires the click, since what it does depends on what is owned.</summary>
        public Button PrimaryButton => primaryButton;

        /// <summary>The shop wires this one too, so a row never talks to the loadout itself.</summary>
        public Button SelectButton => selectButton;

        /// <summary>
        /// Fills the row in. Any label the prefab does not have is simply skipped rather than
        /// folded in elsewhere: this row was authored deliberately, so a missing label is a
        /// decision rather than something to work around.
        /// </summary>
        public void Bind(
            string displayName,
            string levelText,
            string primaryText,
            bool primaryInteractable,
            string selectText,
            bool selectInteractable)
        {
            SetText(nameLabel, displayName);
            SetText(levelLabel, levelText);
            SetText(primaryLabel, primaryText);
            SetText(selectLabel, selectText);

            if (primaryButton != null)
            {
                primaryButton.interactable = primaryInteractable;
            }

            if (selectButton != null)
            {
                selectButton.interactable = selectInteractable;
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
        /// Fills in whatever was left empty from the children, by the names the prefab uses.
        /// Adding this component to an existing row then wires itself rather than leaving six
        /// references to be dragged in, and anything set by hand is never overwritten.
        ///
        /// The buttons are matched by name rather than by the order they are found in, unlike the
        /// single-button bullet row: with two of them, taking the first hit would silently swap
        /// buy and select on any prefab whose children happen to be in the other order.
        /// </summary>
        private void ResolveMissingReferences()
        {
            if (primaryButton == null)
            {
                primaryButton = FindButton("Primary");
            }

            if (selectButton == null)
            {
                selectButton = FindButton("Select");
            }

            if (primaryLabel == null && primaryButton != null)
            {
                primaryLabel = primaryButton.GetComponentInChildren<TMP_Text>(true);
            }

            if (selectLabel == null && selectButton != null)
            {
                selectLabel = selectButton.GetComponentInChildren<TMP_Text>(true);
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

        private Button FindButton(string objectName)
        {
            Button[] candidates = GetComponentsInChildren<Button>(true);
            for (int i = 0; i < candidates.Length; i++)
            {
                if (candidates[i].gameObject.name.IndexOf(objectName, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return candidates[i];
                }
            }

            return null;
        }

        /// <summary>
        /// Matches on the object's name, and never returns a button's own caption: that text
        /// belongs to the button, and writing the vehicle's name into it would replace the price.
        /// </summary>
        private TMP_Text FindLabel(string objectName)
        {
            TMP_Text[] candidates = GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < candidates.Length; i++)
            {
                if (candidates[i] == primaryLabel || candidates[i] == selectLabel)
                {
                    continue;
                }

                if (IsUnderButton(candidates[i].transform))
                {
                    continue;
                }

                if (string.Equals(candidates[i].gameObject.name, objectName, StringComparison.OrdinalIgnoreCase))
                {
                    return candidates[i];
                }
            }

            return null;
        }

        private bool IsUnderButton(Transform label)
        {
            if (primaryButton != null && label.IsChildOf(primaryButton.transform))
            {
                return true;
            }

            return selectButton != null && label.IsChildOf(selectButton.transform);
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
