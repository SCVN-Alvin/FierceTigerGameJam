using GameJam.Audio;
using UnityEngine;
using UnityEngine.UI;

namespace GameJam.UI
{
    /// <summary>
    /// Gives a button its click. One component per button, added by the builders that create
    /// buttons and swept onto the ones that already existed, so that "every button clicks" is a
    /// property of the project rather than a line somebody has to remember to write.
    ///
    /// It deliberately knows nothing about what the button does. Wiring the sound into each
    /// handler instead would mean every new screen re-deciding whether its buttons make a noise,
    /// and the answer is always yes.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Button))]
    public sealed class ButtonClickSound : MonoBehaviour
    {
        private Button button;

        private void Awake()
        {
            button = GetComponent<Button>();
        }

        private void OnEnable()
        {
            if (button != null)
            {
                button.onClick.AddListener(PlayClick);
            }
        }

        private void OnDisable()
        {
            // A screen switched off and on again is the normal life of every button in this game,
            // so a listener left behind here would stack up one more click per visit.
            if (button != null)
            {
                button.onClick.RemoveListener(PlayClick);
            }
        }

        /// <summary>
        /// Unity does not raise onClick on a non-interactable button, so a dimmed button is
        /// silent without this having to check: the refusal sounds the game does play are raised
        /// at the point the transaction is refused, not here.
        /// </summary>
        private void PlayClick()
        {
            AudioService.Play(AudioSlot.UiClick);
        }
    }
}
