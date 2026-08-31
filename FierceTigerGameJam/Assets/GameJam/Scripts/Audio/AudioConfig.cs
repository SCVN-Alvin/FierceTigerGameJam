using UnityEngine;

namespace GameJam.Audio
{
    /// <summary>
    /// Which clips play for which event, and how loud. The one place the game's sound is authored.
    ///
    /// Every slot is an array rather than a single clip. One entry behaves exactly like a single
    /// clip would, so nothing is lost by it, but it means a second and third take of the same
    /// sound can be dropped in later without touching a line of code - and repetition is the thing
    /// that makes a game with twenty breaks a second sound cheap. A random entry plays each time.
    ///
    /// Not every file in the imported pack is wired here. sfx_*aluminum, *steel, *wood,
    /// *blackhole, the heart sounds, sfx_nexthousecomplete and the remaining ice breaks have no
    /// event in this game yet; they are imported and left unwired on purpose, so that the next
    /// feature that wants one finds it already in the project rather than having to go back to the
    /// source pack. sfx_hitice stands in for glass: it is the closest clink the pack has, and it is
    /// meant to be replaced when a real glass-hit clip arrives.
    /// </summary>
    [CreateAssetMenu(menuName = "GameJam/Audio Config", fileName = "AudioConfig")]
    public sealed class AudioConfig : ScriptableObject
    {
        [Header("Cannon")]
        [Tooltip("A shot leaves the cannon.")]
        [SerializeField] private AudioClip[] fire;

        [Tooltip("The ball is accepted as having hit a block.")]
        [SerializeField] private AudioClip[] ballImpact;

        [Tooltip("The ball lands on the floor. Once per flight, so a miss that rolls still lands.")]
        [SerializeField] private AudioClip[] ballFall;

        [Header("Damaged But Surviving")]
        [SerializeField] private AudioClip[] hitBrick;
        [SerializeField] private AudioClip[] hitConcrete;

        [Tooltip("sfx_hitice stands in until a real glass hit exists.")]
        [SerializeField] private AudioClip[] hitGlass;

        [Header("Shattered")]
        [SerializeField] private AudioClip[] breakBrick;
        [SerializeField] private AudioClip[] breakConcrete;
        [SerializeField] private AudioClip[] breakGlass;

        [Header("UI")]
        [Tooltip("Any button, added to every one of them by ButtonClickSound.")]
        [SerializeField] private AudioClip[] uiClick;

        [Tooltip("A purchase, upgrade or continue the game refused.")]
        [SerializeField] private AudioClip[] denied;

        [Tooltip("Gold granted or spent successfully.")]
        [SerializeField] private AudioClip[] coin;

        [SerializeField] private AudioClip[] stageClear;
        [SerializeField] private AudioClip[] stageFailed;

        [Header("Music")]
        [Tooltip("Menu, shops, mission board and the splash.")]
        [SerializeField] private AudioClip[] musicTitle;

        [Tooltip("A run, and the result screen that ends it.")]
        [SerializeField] private AudioClip[] musicGame;

        [Header("Levels")]
        [Range(0f, 1f)]
        [SerializeField] private float musicVolume = 0.5f;

        [Range(0f, 1f)]
        [SerializeField] private float sfxVolume = 1f;

        /// <summary>How loud the looping track plays. There is no mute UI yet; see Brief 21.</summary>
        public float MusicVolume => musicVolume;

        public float SfxVolume => sfxVolume;

        /// <summary>
        /// A clip to play for this slot, chosen at random among the ones authored for it.
        ///
        /// False rather than a silent clip when the slot is empty, because an event with nothing
        /// behind it should cost nothing at all: the caller skips renting a source entirely.
        /// </summary>
        public bool TryGetClip(AudioSlot slot, out AudioClip clip)
        {
            clip = null;

            AudioClip[] clips = Slot(slot);
            if (clips == null || clips.Length == 0)
            {
                return false;
            }

            // A single-entry slot skips the random draw, which is the overwhelmingly common case
            // today and keeps Random out of the per-impact path while only one take exists.
            clip = clips.Length == 1 ? clips[0] : clips[Random.Range(0, clips.Length)];

            // An array with a hole in it is a half-finished authoring pass, not a reason to play
            // nothing forever: the caller simply gets no sound for this one draw.
            return clip != null;
        }

        /// <summary>
        /// The array behind a slot. A switch rather than a serialized list indexed by the enum,
        /// because a list would silently re-map every slot the moment the enum gained a member in
        /// the middle - the same trap <see cref="Gameplay.Flow.GameFlowController.GameState"/>
        /// carries a warning about.
        /// </summary>
        private AudioClip[] Slot(AudioSlot slot)
        {
            switch (slot)
            {
                case AudioSlot.Fire: return fire;
                case AudioSlot.BallImpact: return ballImpact;
                case AudioSlot.BallFall: return ballFall;
                case AudioSlot.HitBrick: return hitBrick;
                case AudioSlot.HitConcrete: return hitConcrete;
                case AudioSlot.HitGlass: return hitGlass;
                case AudioSlot.BreakBrick: return breakBrick;
                case AudioSlot.BreakConcrete: return breakConcrete;
                case AudioSlot.BreakGlass: return breakGlass;
                case AudioSlot.UiClick: return uiClick;
                case AudioSlot.Denied: return denied;
                case AudioSlot.Coin: return coin;
                case AudioSlot.StageClear: return stageClear;
                case AudioSlot.StageFailed: return stageFailed;
                case AudioSlot.MusicTitle: return musicTitle;
                case AudioSlot.MusicGame: return musicGame;
                default: return null;
            }
        }

        /// <summary>
        /// Whether this clip is one of the ones authored for the slot. What lets
        /// <see cref="AudioService.PlayMusic"/> answer "is the title track already looping?" when
        /// the slot could have picked any of several takes.
        /// </summary>
        public bool SlotContains(AudioSlot slot, AudioClip clip)
        {
            if (clip == null)
            {
                return false;
            }

            AudioClip[] clips = Slot(slot);
            if (clips == null)
            {
                return false;
            }

            for (int i = 0; i < clips.Length; i++)
            {
                if (clips[i] == clip)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
