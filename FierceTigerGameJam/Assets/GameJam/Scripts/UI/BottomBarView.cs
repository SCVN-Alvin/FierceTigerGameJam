using System;
using GameJam.Gameplay.Flow;
using UnityEngine;

namespace GameJam.UI
{
    /// <summary>
    /// Raises the slot of the screen the player is on. Purely visual: the buttons and what they do
    /// belong to <see cref="GameFlowController"/>, and this only follows its state, so a screen
    /// that is reached some other way - a debug menu, a deep link, the garage's own X - still
    /// lights the right slot.
    ///
    /// Nothing here knows the order of the slots or how many there are. A slot names the state it
    /// belongs to, which is what lets a fourth tab be a fourth entry in the array rather than a
    /// change to this file, and what lets a state with no slot leave the whole bar flat.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BottomBarView : MonoBehaviour
    {
        [Serializable]
        public sealed class Slot
        {
            [Tooltip("Raised while the flow is in this state.")]
            public GameFlowController.GameState state;

            [Tooltip("The UI_Bottom_Btn plate behind the slot. Off unless this slot is the one "
                     + "the player is on.")]
            public GameObject raised;

            [Tooltip("The slot's picture. Slides up onto the plate when the slot is raised.")]
            public RectTransform icon;

            [Tooltip("The slot's word. Only the raised slot is named, which is what the mock-ups "
                     + "draw and what keeps three words off a bar a third of which is text.")]
            public GameObject label;
        }

        [Tooltip("Left empty - a test scene, a prefab opened on its own - every slot stays flat.")]
        [SerializeField] private GameFlowController flow;

        [SerializeField] private Slot[] slots = Array.Empty<Slot>();

        [Tooltip("Where a lowered slot's icon sits, centred in the bar.")]
        [SerializeField] private float iconRestY = 66f;

        [Tooltip("Where a raised slot's icon sits, up on the plate and above the bar's top edge.")]
        [SerializeField] private float iconRaisedY = 106f;

        private void OnEnable()
        {
            if (flow != null)
            {
                flow.StateChanged += Apply;

                // The bar is switched on and off by the flow, so it can miss any number of
                // changes while it is hidden. Drawing the current state on the way in is what
                // makes it right when it comes back rather than one transition behind.
                Apply(flow.State);
            }
            else
            {
                Apply(null);
            }
        }

        /// <summary>
        /// Paired with OnEnable and unconditional about it, so entering play mode twice with the
        /// domain reload turned off cannot leave a second subscription behind.
        /// </summary>
        private void OnDisable()
        {
            if (flow != null)
            {
                flow.StateChanged -= Apply;
            }
        }

        /// <summary>The event's shape. The work is the same either way.</summary>
        private void Apply(GameFlowController.GameState state)
        {
            Apply((GameFlowController.GameState?)state);
        }

        /// <summary>
        /// Exactly the matching slot is raised; with no match, none is. No match is a real case
        /// rather than an error - the mission screen and anything else the bar is shown over is
        /// not a tab - so it flattens the bar quietly.
        /// </summary>
        private void Apply(GameFlowController.GameState? state)
        {
            for (int i = 0; i < slots.Length; i++)
            {
                Slot slot = slots[i];
                if (slot == null)
                {
                    continue;
                }

                bool selected = state.HasValue && slot.state == state.Value;

                // Compared before assigning: SetActive on an object already in that state still
                // walks its children, and this runs on every state change.
                if (slot.raised != null && slot.raised.activeSelf != selected)
                {
                    slot.raised.SetActive(selected);
                }

                if (slot.label != null && slot.label.activeSelf != selected)
                {
                    slot.label.SetActive(selected);
                }

                if (slot.icon != null)
                {
                    slot.icon.anchoredPosition = new Vector2(0f, selected ? iconRaisedY : iconRestY);
                }
            }
        }
    }
}
