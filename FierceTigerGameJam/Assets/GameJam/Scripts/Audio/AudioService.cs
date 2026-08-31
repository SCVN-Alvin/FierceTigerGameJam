using System;
using UnityEngine;

namespace GameJam.Audio
{
    /// <summary>
    /// The one thing that plays sound. A scene object under =====SYSTEM=====, not a singleton that
    /// survives loads: the game is a single scene, so an object that outlived it would only be a
    /// second copy waiting to happen.
    ///
    /// No sound this class plays is load-bearing. Every caller reaches it through the static
    /// <see cref="Play(AudioSlot)"/> helpers, which do nothing at all when there is no service, so
    /// deleting or disabling this object leaves a game that runs silently rather than one that
    /// throws. That is a deliberate property and worth keeping: audio is the last thing that should
    /// be able to break a run.
    /// </summary>
    /// <remarks>
    /// Why a pool of sources rather than one AudioSource.PlayOneShot: PlayOneShot mixes onto a
    /// single voice, and a collapse can fire twenty breaks in one frame. A round-robin over a
    /// handful of real sources gives each sound its own voice, and - the reason it is worth the
    /// code - it gives us somewhere to count from, which is what <see cref="perFrameClipCap"/>
    /// needs to stop a cascade turning into one flat roar.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class AudioService : MonoBehaviour
    {
        /// <summary>
        /// The service the static helpers below play through.
        ///
        /// Set in OnEnable and cleared in OnDisable, so with domain reload switched off it does not
        /// carry a destroyed object from the last play session into the next one: leaving play
        /// disables the object, which clears this. Should that ever be missed - a scene torn down
        /// without OnDisable running - the helpers still hold: they compare with Unity's == , which
        /// reports a destroyed object as null where C#'s ?. would not.
        /// </summary>
        public static AudioService Instance { get; private set; }

        [Tooltip("Which clips play for which event, and how loud. Without one the service is "
                 + "harmlessly silent rather than broken.")]
        [SerializeField] private AudioConfig config;

        [Tooltip("How many SFX can overlap. Eight is enough for a busy collapse without giving a "
                 + "cascade enough voices to drown everything else out.")]
        [SerializeField] private int sfxSourceCount = 8;

        [Tooltip("How many copies of the SAME clip may start in one frame. A wall coming apart "
                 + "asks for the same break sound a dozen times in a single physics step; past "
                 + "about four they stop reading as separate hits and start reading as noise.")]
        [SerializeField] private int perFrameClipCap = 4;

        /// <summary>Round-robin over these, so a new sound never cuts off the one just started.</summary>
        private AudioSource[] sfxSources;
        private int nextSource;

        private AudioSource musicSource;

        /// <summary>
        /// The clips started this frame and how many times each. A fixed pair of arrays scanned
        /// linearly rather than a Dictionary: the count is a handful even in the worst frame, the
        /// scan is faster than hashing at that size, and - the point - nothing here allocates while
        /// a structure is coming down.
        /// </summary>
        private AudioClip[] frameClips;
        private int[] frameCounts;
        private int frameClipCount;

        /// <summary>
        /// Which frame <see cref="frameClips"/> describes. Compared on the way in rather than
        /// cleared from an Update, so a silent frame costs nothing at all.
        /// </summary>
        private int frameStamp = -1;

        private void Awake()
        {
            sfxSourceCount = Mathf.Max(1, sfxSourceCount);
            perFrameClipCap = Mathf.Max(1, perFrameClipCap);

            // Built here rather than authored in the scene, so there is exactly one music source
            // and exactly sfxSourceCount effect sources however many times a builder is re-run.
            musicSource = gameObject.AddComponent<AudioSource>();
            musicSource.playOnAwake = false;
            musicSource.loop = true;
            musicSource.spatialBlend = 0f;

            sfxSources = new AudioSource[sfxSourceCount];
            for (int i = 0; i < sfxSources.Length; i++)
            {
                AudioSource source = gameObject.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.loop = false;

                // Flat 2D. The camera sits a long way from a structure that is metres across, so
                // panning a break by where it happened would only make half the collapse quiet.
                source.spatialBlend = 0f;
                sfxSources[i] = source;
            }

            // Sized to the cap's worst case: distinct clips in a frame cannot usefully exceed the
            // number of voices, so a frame can never overflow these and they are never regrown.
            frameClips = new AudioClip[sfxSources.Length];
            frameCounts = new int[sfxSources.Length];
        }

        private void OnEnable()
        {
            // Unity's ==, so a service left over from a scene that has been torn down reads as
            // null here and this one takes over cleanly.
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning(
                    $"{name}: a second {nameof(AudioService)} is in the scene. The first one keeps "
                    + "playing; this one will do nothing. Delete one of them.",
                    this);
                return;
            }

            Instance = this;
        }

        private void OnDisable()
        {
            // Only if it is still us: a second service that never claimed the slot must not clear
            // the one that did.
            if (Instance == this)
            {
                Instance = null;
            }

            // Leaving the music running under a disabled service would outlive the object that is
            // supposed to own it.
            if (musicSource != null)
            {
                musicSource.Stop();
            }
        }

        // ------------------------------------------------------------------ static entry points

        /// <summary>
        /// Plays a slot, or does nothing at all when there is no service in the scene.
        ///
        /// Static and null-tolerant so that every call site is one unconditional line. Deliberately
        /// not written as <c>AudioService.Instance?.PlaySfx(slot)</c> at the call sites: <c>?.</c>
        /// tests for a real null reference and does not know about Unity's destroyed-object null,
        /// so it would happily call into the wreckage of a service whose object had already gone.
        /// The <c>== null</c> below catches both.
        /// </summary>
        public static void Play(AudioSlot slot)
        {
            AudioService service = Instance;
            if (service == null)
            {
                return;
            }

            service.PlaySfx(slot);
        }

        /// <summary>The surviving-hit sound for a material. Null-tolerant, as <see cref="Play"/> is.</summary>
        public static void PlayHit(string materialId)
        {
            AudioService service = Instance;
            if (service == null)
            {
                return;
            }

            service.PlayMaterialHit(materialId);
        }

        /// <summary>The shatter sound for a material. Null-tolerant, as <see cref="Play"/> is.</summary>
        public static void PlayBreak(string materialId)
        {
            AudioService service = Instance;
            if (service == null)
            {
                return;
            }

            service.PlayMaterialBreak(materialId);
        }

        /// <summary>Switches the looping track. Null-tolerant, as <see cref="Play"/> is.</summary>
        public static void PlayTrack(AudioSlot slot)
        {
            AudioService service = Instance;
            if (service == null)
            {
                return;
            }

            service.PlayMusic(slot);
        }

        // ------------------------------------------------------------------ instance API

        /// <summary>Plays one effect, subject to the per-frame cap on identical clips.</summary>
        public void PlaySfx(AudioSlot slot)
        {
            if (config == null || sfxSources == null)
            {
                return;
            }

            if (!config.TryGetClip(slot, out AudioClip clip))
            {
                return;
            }

            if (!TryCountThisFrame(clip))
            {
                return;
            }

            // Round-robin: the least recently started source is the one most likely to have
            // finished, and taking it in turn means a burst spreads over every voice instead of
            // one source restarting on top of itself.
            AudioSource source = sfxSources[nextSource];
            nextSource = (nextSource + 1) % sfxSources.Length;

            source.clip = clip;
            source.volume = config.SfxVolume;
            source.Play();
        }

        /// <summary>
        /// The sound of this material taking damage and surviving. Unknown or missing materials
        /// fall back to brick: a block with an unauthored material should still make a noise when
        /// it is hit, and brick is the game's default surface.
        /// </summary>
        public void PlayMaterialHit(string materialId)
        {
            PlaySfx(HitSlot(materialId));
        }

        /// <summary>The sound of this material coming apart. Falls back to brick, as the hit does.</summary>
        public void PlayMaterialBreak(string materialId)
        {
            PlaySfx(BreakSlot(materialId));
        }

        /// <summary>
        /// Starts the looping track for this slot, and does nothing when that track is already the
        /// one playing.
        ///
        /// The no-op is the whole point rather than an optimisation. Menu, shops, mission board and
        /// splash all share the title track, so moving between them is the common case; restarting
        /// the music from the top every time the player opened the garage is exactly the bug this
        /// exists to prevent.
        /// </summary>
        public void PlayMusic(AudioSlot slot)
        {
            if (config == null || musicSource == null)
            {
                return;
            }

            // Asked of the whole slot rather than of one clip: with several takes authored, the
            // track that is looping is "the title music" if it is any of them, and a random draw
            // must not count as a different track.
            if (musicSource.isPlaying && config.SlotContains(slot, musicSource.clip))
            {
                // Still worth re-reading, so a volume edited in the config lands without a restart.
                musicSource.volume = config.MusicVolume;
                return;
            }

            if (!config.TryGetClip(slot, out AudioClip clip))
            {
                // A slot with no music authored means silence from here on, which is a truer
                // answer than leaving the previous track looping under a screen it does not belong
                // to.
                musicSource.Stop();
                return;
            }

            musicSource.clip = clip;
            musicSource.volume = config.MusicVolume;
            musicSource.loop = true;
            musicSource.Play();
        }

        // ------------------------------------------------------------------ per-frame cap

        /// <summary>
        /// Books one play of this clip against the current frame, and says whether it is allowed.
        ///
        /// The table is stamped with the frame it describes and rebuilt lazily on the first play of
        /// a new frame, so there is no Update to run and a frame in which nothing is played costs
        /// nothing. Allocation-free: both arrays are sized once in Awake and only ever written
        /// through.
        /// </summary>
        private bool TryCountThisFrame(AudioClip clip)
        {
            int frame = Time.frameCount;
            if (frame != frameStamp)
            {
                frameStamp = frame;

                // The entries above frameClipCount are stale but unreachable, so only the count
                // has to be reset - no array clearing, and nothing to allocate.
                frameClipCount = 0;
            }

            for (int i = 0; i < frameClipCount; i++)
            {
                if (frameClips[i] != clip)
                {
                    continue;
                }

                if (frameCounts[i] >= perFrameClipCap)
                {
                    return false;
                }

                frameCounts[i]++;
                return true;
            }

            // More distinct clips in one frame than there are voices to play them on. Nothing is
            // gained by tracking the overflow: every voice is already spoken for this frame.
            if (frameClipCount >= frameClips.Length)
            {
                return false;
            }

            frameClips[frameClipCount] = clip;
            frameCounts[frameClipCount] = 1;
            frameClipCount++;
            return true;
        }

        // ------------------------------------------------------------------ material mapping

        /// <summary>
        /// Ordinal and case-insensitive, matching how <see cref="Gameplay.Combat.BulletDefinition"/>
        /// looks damage up against the same ids, so a material that can be damaged can always be
        /// heard being damaged.
        /// </summary>
        private static AudioSlot HitSlot(string materialId)
        {
            if (string.Equals(materialId, GlassMaterialId, StringComparison.OrdinalIgnoreCase))
            {
                return AudioSlot.HitGlass;
            }

            if (string.Equals(materialId, ConcreteMaterialId, StringComparison.OrdinalIgnoreCase))
            {
                return AudioSlot.HitConcrete;
            }

            return AudioSlot.HitBrick;
        }

        private static AudioSlot BreakSlot(string materialId)
        {
            if (string.Equals(materialId, GlassMaterialId, StringComparison.OrdinalIgnoreCase))
            {
                return AudioSlot.BreakGlass;
            }

            if (string.Equals(materialId, ConcreteMaterialId, StringComparison.OrdinalIgnoreCase))
            {
                return AudioSlot.BreakConcrete;
            }

            return AudioSlot.BreakBrick;
        }

        /// <summary>
        /// The ids the block prefab builder writes, lower-cased from the block's category. Named
        /// here rather than repeated as literals so the two maps above cannot drift apart.
        /// </summary>
        private const string GlassMaterialId = "glass";

        private const string ConcreteMaterialId = "concrete";

        private void OnValidate()
        {
            sfxSourceCount = Mathf.Max(1, sfxSourceCount);
            perFrameClipCap = Mathf.Max(1, perFrameClipCap);
        }
    }
}
