using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GameJam.UI
{
    /// <summary>
    /// One row of the garage: what the item looks like, what it is called and how far it has been
    /// taken, the strip of level pips, and the two stacked controls on the right.
    ///
    /// Buy spends gold. Above it, SELECT equips what the row describes, and on the row that is
    /// already equipped the EQUIPPED chip stands in its place - the same rect, so the row is the
    /// same shape in every state. There is no room beside a price for a second control, which is
    /// why they are stacked rather than set side by side.
    ///
    /// Being equipped is said twice: by the chip, and by the row standing at full strength while
    /// the others are knocked back. Two cues rather than one, because the alpha alone is a
    /// comparison the player has to make across rows before it means anything.
    ///
    /// The shop decides every word and every state; this only knows where to put them. That is
    /// what lets the ammunition row and the vehicle row be the same picture: the two subclasses
    /// below add nothing but the type of the thing being described.
    /// </summary>
    [DisallowMultipleComponent]
    public abstract class ShopItemView : MonoBehaviour
    {
        /// <summary>
        /// Everything the row draws, worked out by the shop in one place. Passed as one value
        /// rather than eight arguments so that adding a reading later cannot leave one of the two
        /// call sites behind.
        /// </summary>
        public readonly struct State
        {
            /// <summary>Null leaves the slot empty rather than drawing a white square.</summary>
            public readonly Sprite Icon;

            public readonly string DisplayName;
            public readonly bool Unlocked;

            /// <summary>One-based, and ignored while the item is locked.</summary>
            public readonly int Level;

            /// <summary>How many pips the strip has: the highest level that can be bought.</summary>
            public readonly int MaxLevel;

            /// <summary>The equipped row is drawn at full strength and every other one is dimmed.</summary>
            public readonly bool Equipped;

            /// <summary>What the one button says: a price, "MAX", or "N/A".</summary>
            public readonly string BuyCaption;

            public readonly bool BuyInteractable;

            public State(
                Sprite icon,
                string displayName,
                bool unlocked,
                int level,
                int maxLevel,
                bool equipped,
                string buyCaption,
                bool buyInteractable)
            {
                Icon = icon;
                DisplayName = displayName;
                Unlocked = unlocked;
                Level = level;
                MaxLevel = maxLevel;
                Equipped = equipped;
                BuyCaption = buyCaption;
                BuyInteractable = buyInteractable;
            }
        }

        [Tooltip("The item's picture, in the slot baked into the row's left edge.")]
        [SerializeField] private Image icon;

        [Tooltip("\"TRUCK \u00b7 LEVEL 2\", or just the name beside the lock graphic.")]
        [SerializeField] private TMP_Text label;

        [Tooltip("The UI_Locked graphic, shown in place of the level on a row the player does "
                 + "not own yet.")]
        [SerializeField] private GameObject locked;

        [SerializeField] private UpgradeLevelBarView levels;

        [SerializeField] private Button buyButton;

        [SerializeField] private TMP_Text buyLabel;

        [Tooltip("The SELECT button, in the band directly above Buy. Up only while the row's "
                 + "item is owned and is not the one equipped. The shop wires the click, since "
                 + "only the shop knows which loadout the item belongs to.")]
        [SerializeField] private Button selectButton;

        [Tooltip("Shown on the equipped row where Select is on the others. A chip, not a button: "
                 + "there is nothing to do to the thing already mounted.")]
        [SerializeField] private GameObject equippedBadge;

        [Tooltip("On the row root. The equipped row is drawn at full strength and the rest are "
                 + "knocked back; interactable is never touched, a dimmed row can still be bought.")]
        [SerializeField] private CanvasGroup group;

        [SerializeField, Range(0f, 1f)] private float equippedAlpha = 1f;
        [SerializeField, Range(0f, 1f)] private float otherAlpha = 0.5f;

        /// <summary>The shop wires the click, since what it does depends on what is owned.</summary>
        public Button BuyButton => buyButton;

        /// <summary>
        /// The SELECT control. Hidden rather than disabled in the states where it does not apply:
        /// a greyed-out SELECT sitting above a greyed-out Buy would read as a second thing the
        /// player cannot afford, and taking it out of the picture is also what lets the EQUIPPED
        /// chip stand in the same rect without anything reflowing. interactable is never touched,
        /// so a refusal is still decided in one place - the loadout - rather than guessed at here.
        /// </summary>
        public Button SelectButton => selectButton;

        /// <summary>
        /// Fills the row in. Any part the prefab does not have is skipped rather than folded in
        /// elsewhere: this row was authored deliberately, so a missing part is a decision.
        /// </summary>
        public void Bind(in State state)
        {
            if (icon != null)
            {
                icon.sprite = state.Icon;

                // Disabled rather than left drawing: an Image with no sprite is a white block,
                // and the slot it sits in is already art.
                icon.enabled = state.Icon != null;
            }

            if (locked != null)
            {
                locked.SetActive(!state.Unlocked);
            }

            if (label != null)
            {
                string displayName = string.IsNullOrEmpty(state.DisplayName)
                    ? string.Empty
                    : state.DisplayName.ToUpperInvariant();

                // A locked row's level would be the level it would start at, which reads as a
                // promise; the lock graphic beside the name says the true thing instead.
                label.text = state.Unlocked
                    ? $"{displayName} {LevelSeparator} LEVEL {state.Level}"
                    : displayName;
            }

            if (levels != null)
            {
                levels.Bind(state.MaxLevel, state.Unlocked ? state.Level : 0);
            }

            if (buyLabel != null)
            {
                buyLabel.text = state.BuyCaption;
            }

            if (buyButton != null)
            {
                buyButton.interactable = state.BuyInteractable;
            }

            bool unlocked = state.Unlocked;

            if (selectButton != null && selectButton.gameObject != gameObject)
            {
                // Never the row's own object. A row still wired the old way - the whole row as
                // the equip target, which is what selectButton pointed at before SELECT was its
                // own button - would hide itself here, and an empty list is a much worse bug
                // than a missing control. The builder migrates such a row on its next run.
                selectButton.gameObject.SetActive(unlocked && !state.Equipped);
            }

            if (equippedBadge != null)
            {
                // The two share one rect, so exactly one of them is up - or neither, while the
                // row is locked, since there is nothing to equip and nothing equipped to say.
                equippedBadge.SetActive(unlocked && state.Equipped);
            }

            if (group != null)
            {
                // Alpha only. Dimming a row must not stop it being bought from: the player
                // upgrading the vehicle they are saving toward is the normal case.
                group.alpha = state.Equipped ? equippedAlpha : otherAlpha;
            }
        }

        /// <summary>
        /// Between the name and the level. A middle dot, which the default font carries; pulled
        /// out as a constant so a font that does not can be answered in one place.
        /// </summary>
        private const string LevelSeparator = "\u00b7";

        /// <summary>
        /// Fills in whatever was left empty from the children, by the names the prefab uses.
        /// Adding this component to an existing row then wires itself rather than leaving seven
        /// references to be dragged in, and anything set by hand is never overwritten - the same
        /// rule <see cref="VehicleShopRowView"/> follows.
        /// </summary>
        private void ResolveMissingReferences()
        {
            if (buyButton == null)
            {
                buyButton = FindChild<Button>("Buy");
            }

            if (buyLabel == null && buyButton != null)
            {
                buyLabel = buyButton.GetComponentInChildren<TMP_Text>(true);
            }

            if (icon == null)
            {
                icon = FindChild<Image>("Icon");
            }

            if (label == null)
            {
                label = FindLabel("Label");
            }

            if (locked == null)
            {
                Image lockedImage = FindChild<Image>("Locked");
                if (lockedImage != null)
                {
                    locked = lockedImage.gameObject;
                }
            }

            if (levels == null)
            {
                levels = GetComponentInChildren<UpgradeLevelBarView>(true);
            }

            if (group == null)
            {
                // On the row itself: dimming a child would leave the row's own frame lit.
                group = GetComponent<CanvasGroup>();
            }

            if (selectButton == null)
            {
                // By name, so the one on Buy is never picked up: a search by type alone would
                // find whichever button came first in the hierarchy.
                selectButton = FindChild<Button>("Select");
            }

            if (selectButton == null)
            {
                // The legacy shape, kept only for a row authored before SELECT was its own
                // button: back then the row root was the button and the whole row equipped.
                selectButton = GetComponent<Button>();
            }

            if (equippedBadge == null)
            {
                Image badge = FindChild<Image>("Equipped");
                if (badge != null)
                {
                    equippedBadge = badge.gameObject;
                }
            }
        }

        private T FindChild<T>(string objectName) where T : Component
        {
            T[] candidates = GetComponentsInChildren<T>(true);
            for (int i = 0; i < candidates.Length; i++)
            {
                if (string.Equals(candidates[i].gameObject.name, objectName, StringComparison.OrdinalIgnoreCase))
                {
                    return candidates[i];
                }
            }

            return null;
        }

        /// <summary>
        /// Matches on the object's name, and never returns text living inside the buy button:
        /// that text belongs to the button, and writing the item's name into it would replace the
        /// price with it.
        /// </summary>
        private TMP_Text FindLabel(string objectName)
        {
            TMP_Text[] candidates = GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < candidates.Length; i++)
            {
                if (candidates[i] == buyLabel)
                {
                    continue;
                }

                if (buyButton != null && candidates[i].transform.IsChildOf(buyButton.transform))
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
