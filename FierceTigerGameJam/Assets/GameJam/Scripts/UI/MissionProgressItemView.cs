using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GameJam.UI
{
    /// <summary>What the player can do with one level, which is the only thing its card shows.</summary>
    public enum MissionItemState
    {
        /// <summary>Not open yet: the level before it has not been passed.</summary>
        Locked,

        /// <summary>Open and not passed - the one the player is on.</summary>
        Current,

        /// <summary>Passed. Still playable, for a better percentage.</summary>
        Cleared,
    }

    /// <summary>
    /// One level on the mission board: its name and the one thing the player can do with it.
    ///
    /// The card only shows a state; deciding it - what counts as cleared, what is open - is the
    /// panel's, which is what lets the unlock rule change without a card knowing anything about
    /// maps or saves. It does not even wire its own button: the panel does, because only the
    /// panel knows which level this card was built for.
    /// </summary>
    public sealed class MissionProgressItemView : MonoBehaviour
    {
        [SerializeField] private TMP_Text title;

        [Tooltip("The card's one button. Retry, play or a dead lock, depending on the state.")]
        [SerializeField] private Button action;

        [Tooltip("The button's graphic, whose sprite is swapped per state. Falls back to the "
                 + "button's own targetGraphic.")]
        [SerializeField] private Image actionImage;

        [Header("State Sprites")]
        [SerializeField] private Sprite retrySprite;
        [SerializeField] private Sprite playSprite;
        [SerializeField] private Sprite lockedSprite;

        /// <summary>The panel wires the click, since what it opens depends on which level this is.</summary>
        public Button Action => action;

        /// <summary>What the card was last bound to, so a click can be refused before it is acted on.</summary>
        public MissionItemState State { get; private set; }

        /// <summary>Draws the card for one level. Called on every refresh, so it sets everything.</summary>
        public void Bind(string titleText, MissionItemState state)
        {
            State = state;

            if (title != null)
            {
                title.text = titleText;
            }

            Image image = ResolveActionImage();
            if (image != null)
            {
                Sprite sprite = ResolveSprite(state);

                // A state with no art assigned keeps whatever the prefab had rather than blanking
                // the card: an empty Image draws a white block over the badge.
                if (sprite != null)
                {
                    image.sprite = sprite;
                }
            }

            if (action != null)
            {
                // The button's transition is None on purpose (see the builder): a locked card must
                // not look greyed out on top of already being a lock. This still stops the click.
                action.interactable = state != MissionItemState.Locked;
            }
        }

        private Sprite ResolveSprite(MissionItemState state)
        {
            switch (state)
            {
                case MissionItemState.Cleared:
                    return retrySprite;
                case MissionItemState.Current:
                    return playSprite;
                default:
                    return lockedSprite;
            }
        }

        private Image ResolveActionImage()
        {
            if (actionImage != null)
            {
                return actionImage;
            }

            return action != null ? action.targetGraphic as Image : null;
        }

        /// <summary>
        /// Fills in whatever was left empty from the children, by the names the prefab uses, so
        /// adding this component to an authored card wires itself rather than leaving references
        /// to be dragged in. Anything set by hand is never overwritten.
        /// </summary>
        private void ResolveMissingReferences()
        {
            if (action == null)
            {
                action = FindByName<Button>("Action");
            }

            if (actionImage == null && action != null)
            {
                actionImage = action.targetGraphic as Image;
                if (actionImage == null)
                {
                    actionImage = action.GetComponent<Image>();
                }
            }

            if (title == null)
            {
                // Never the button's own caption: the card's button is a picture, but a card
                // authored with a label under it would otherwise have that label taken for the
                // level's name and overwritten on the first bind.
                title = FindLabel("Title");
            }
        }

        private T FindByName<T>(string objectName) where T : Component
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

        private TMP_Text FindLabel(string objectName)
        {
            TMP_Text[] candidates = GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < candidates.Length; i++)
            {
                if (action != null && candidates[i].transform.IsChildOf(action.transform))
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
            // Also at runtime, so a card instantiated from a prefab that was never opened in the
            // inspector still knows its own parts.
            ResolveMissingReferences();
        }
    }
}
