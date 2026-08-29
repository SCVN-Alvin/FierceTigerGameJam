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
    /// Being equipped is said once, by the chip. The row used to also be drawn at full strength
    /// while the others were knocked back, and that second cue is gone: an alpha is a comparison
    /// the player has to make across rows before it means anything, and a dimmed row that is
    /// still fully buyable reads as disabled. The chip and the SELECT button say it outright.
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

            /// <summary>
            /// Which of the two stacked controls the row shows: the EQUIPPED chip on the one
            /// item that is mounted, SELECT on the rest. Nothing else in the row changes with it.
            /// </summary>
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

            if (selectButton == null)
            {
                // The child named Select and nothing else. By name, so the one on Buy is never
                // picked up - a search by type alone would find whichever button came first in
                // the hierarchy - and with no fallback to a Button on the root, because a row is
                // never itself a button any more. A row whose Select child is missing is left
                // with no equip control, which is a visible gap; falling back to the root would
                // instead make the whole row an equip target again and, since Bind hides whatever
                // selectButton names, make the equipped row hide itself.
                selectButton = FindChild<Button>("Select");
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
