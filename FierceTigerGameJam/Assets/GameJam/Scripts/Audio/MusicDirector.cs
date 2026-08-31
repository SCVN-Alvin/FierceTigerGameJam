using GameJam.Gameplay.Flow;
using UnityEngine;

namespace GameJam.Audio
{
    /// <summary>
    /// Picks the looping track from where the player is. Two tracks, two groups of states: the
    /// menu-ish screens share the title music, a run and the result screen that ends it share the
    /// game music.
    ///
    /// It lives beside <see cref="AudioService"/> rather than inside it because "which music suits
    /// this screen" is a decision about the game's flow, and the service should not have to know
    /// the flow exists. Moving inside a group never restarts the track - PlayMusic refuses to
    /// restart what is already looping - which is what makes reopening the garage silent rather
    /// than jarring.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MusicDirector : MonoBehaviour
    {
        [Tooltip("Where the state comes from. Without one there is no music, which is a wiring "
                 + "problem rather than a reason to guess at a track.")]
        [SerializeField] private GameFlowController flow;

        private void OnEnable()
        {
            if (flow == null)
            {
                return;
            }

            flow.StateChanged += HandleStateChanged;

            // Covers a director switched off and on again mid-session, which no StateChanged
            // necessarily follows. On the very first enable this may be too early - see Start.
            HandleStateChanged(flow.State);
        }

        /// <summary>
        /// The same sync again, once, after every OnEnable in the scene has run.
        ///
        /// The one in OnEnable can be too early: it reaches AudioService through a static that
        /// AudioService itself sets in its own OnEnable, and both components live on the same
        /// object, so which goes first is decided by the order they happen to sit in. That order
        /// is right today and would be a silent menu the day somebody reordered them. Start runs
        /// after every OnEnable by definition, so this cannot lose the race.
        ///
        /// Calling it twice costs nothing: PlayMusic refuses to restart a track that is already
        /// looping, which is the same property that stops menu-to-shop restarting the title.
        /// </summary>
        private void Start()
        {
            if (flow != null)
            {
                HandleStateChanged(flow.State);
            }
        }

        private void OnDisable()
        {
            if (flow != null)
            {
                flow.StateChanged -= HandleStateChanged;
            }
        }

        private void HandleStateChanged(GameFlowController.GameState state)
        {
            AudioService.PlayTrack(TrackFor(state));
        }

        /// <summary>
        /// Which of the two tracks a state belongs to.
        ///
        /// Written as "a run plays the game track, everything else plays the title track" rather
        /// than as a list of menu states, because that is the rule that stays true when a state is
        /// added: a new screen is far more likely to be another menu than another kind of play,
        /// and the failure mode of guessing wrong that way is the title track under a new screen
        /// rather than silence.
        ///
        /// GameState.AmmoPick is not listed on purpose. It was retired in Brief 17 and nothing
        /// enters it any more; it is only still in the enum because BottomBarView serializes the
        /// state by value. Should something ever enter it, it falls to the title track with the
        /// rest of the menu, which is where the pick screen sat when it existed.
        /// </summary>
        private static AudioSlot TrackFor(GameFlowController.GameState state)
        {
            switch (state)
            {
                case GameFlowController.GameState.Playing:
                case GameFlowController.GameState.Result:
                    return AudioSlot.MusicGame;

                // MainMenu, IapShop, Shop, MapSelection and Loading, plus the retired AmmoPick.
                default:
                    return AudioSlot.MusicTitle;
            }
        }
    }
}
