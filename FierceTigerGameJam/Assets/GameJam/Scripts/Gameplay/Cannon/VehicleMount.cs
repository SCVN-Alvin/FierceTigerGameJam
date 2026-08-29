using GameJam.Gameplay.Combat;
using UnityEngine;

namespace GameJam.Gameplay.Cannon
{
    /// <summary>
    /// Spawns the selected vehicle's model for its current level at a mount point, and swaps it
    /// when the selection or the level changes.
    ///
    /// The vehicle is a base the cannon stands on rather than a replacement for it: the barrel,
    /// its animator, the muzzle and the smoke all stay where they are, and only the model under
    /// them changes. That keeps every shot behaving identically whatever the player is driving,
    /// which is the point - the vehicle is bought for damage, not for aim.
    ///
    /// Nothing here knows about the cannon, so the same component drives a shop preview rig by
    /// pointing its mount point somewhere else.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VehicleMount : MonoBehaviour
    {
        [Tooltip("Optional. Which vehicle is mounted and at what level. Without one nothing is "
                 + "spawned, which is how a test scene runs with no progression wired.")]
        [SerializeField] private VehicleLoadout loadout;

        [Tooltip("Where the model goes. Left empty, this transform.")]
        [SerializeField] private Transform mountPoint;

        [Tooltip("Applied to the spawned model so art can be authored at any scale.")]
        [SerializeField] private Vector3 modelLocalScale = Vector3.one;

        [Tooltip("Shown only while no vehicle model resolves - the old tank, so an unwired or "
                 + "model-less loadout still shows a cannon rather than a floating barrel.")]
        [SerializeField] private GameObject fallbackModel;

        /// <summary>The spawned model's Animator, for whoever presents the shot. Null on the fallback.</summary>
        public Animator CurrentAnimator { get; private set; }

        private GameObject current;
        private VehicleDefinition currentVehicle;
        private int currentLevel;
        private bool warnedAboutMissingModel;

        private void OnEnable()
        {
            if (loadout != null)
            {
                loadout.SelectionChanged += HandleSelectionChanged;
                loadout.LevelChanged += HandleLevelChanged;
            }

            Refresh();
        }

        private void OnDisable()
        {
            if (loadout != null)
            {
                // The loadout is an asset and outlives this scene, so a subscription left behind
                // would fire into a destroyed mount on the next run.
                loadout.SelectionChanged -= HandleSelectionChanged;
                loadout.LevelChanged -= HandleLevelChanged;
            }

            // The model is deliberately left standing. The cannon is visible on every screen the
            // player sees, and tearing the vehicle down whenever this component is switched off
            // would leave the barrel floating.
        }

        /// <summary>
        /// Puts the right model under the cannon, and does nothing at all when it is already
        /// there: a selection event fires for every row the player taps, and re-instantiating an
        /// identical model would restart whatever the art does on spawn.
        /// </summary>
        [ContextMenu("Refresh")]
        public void Refresh()
        {
            if (loadout == null)
            {
                ShowFallbackOnly();
                return;
            }

            VehicleDefinition vehicle = loadout.Selected;
            int level = loadout.SelectedLevel;
            if (vehicle == null)
            {
                ShowFallbackOnly();
                return;
            }

            if (current != null && vehicle == currentVehicle && level == currentLevel)
            {
                return;
            }

            GameObject prefab = vehicle.ResolveModelPrefab(level);
            if (prefab == null)
            {
                // Once, not once per selection: shipping before the art lands is expected, and a
                // warning on every tap would bury everything else in the console.
                if (!warnedAboutMissingModel)
                {
                    warnedAboutMissingModel = true;
                    Debug.LogWarning(
                        $"{nameof(VehicleMount)} on \"{name}\" has no model for {vehicle.DisplayName} at level "
                        + $"{level}, and none at any level below it, so the cannon stands on nothing.",
                        this);
                }

                // Deviation from the brief, which only switches the fallback on here: the model
                // already standing is torn down with it. Leaving it up would put the fallback
                // tank inside the previous vehicle's model, which reads as two cannons rather
                // than as a vehicle that has no art yet.
                ShowFallbackOnly();
                return;
            }

            DestroyCurrent();

            Transform parent = mountPoint != null ? mountPoint : transform;
            current = Instantiate(prefab, parent);
            current.transform.localPosition = Vector3.zero;
            current.transform.localRotation = Quaternion.identity;
            // Two scales, deliberately: the fitted one belongs to the model and comes from the
            // config the fitting tool writes, while modelLocalScale is the mount's own hand
            // tweak - a preview rig that wants everything half size sets it once and every
            // vehicle stays in proportion.
            current.transform.localScale = modelLocalScale * vehicle.ResolveModelScale(level);

            currentVehicle = vehicle;
            currentLevel = level;

            // The pack models animate their own shot, so whoever presents the firing needs this
            // rather than the barrel's animator. Cached here because a shot must not pay for a
            // hierarchy walk.
            CurrentAnimator = current.GetComponentInChildren<Animator>(true);

            SetFallbackActive(false);

            StripColliders(current);
        }

        /// <summary>
        /// The state with nothing mounted: whatever was spawned goes, and the fallback stands in
        /// its place. The two are always opposites, so a level with no vehicle wired still shows
        /// a cannon and a level with one never shows the tank behind it.
        /// </summary>
        private void ShowFallbackOnly()
        {
            DestroyCurrent();
            SetFallbackActive(true);
        }

        private void SetFallbackActive(bool active)
        {
            if (fallbackModel != null && fallbackModel.activeSelf != active)
            {
                fallbackModel.SetActive(active);
            }
        }

        private void HandleSelectionChanged(VehicleDefinition vehicle)
        {
            Refresh();
        }

        private void HandleLevelChanged(VehicleDefinition vehicle, int level)
        {
            // Every vehicle's level comes through here, not only the mounted one; Refresh reads
            // what is actually selected and returns without work when nothing it shows moved.
            Refresh();
        }

        /// <summary>
        /// The model is scenery. A collider on it sits directly in front of the muzzle, and the
        /// shot would hit the player's own vehicle instead of the structure - which reads as the
        /// cannon being broken rather than as an art prefab being wrong, so it is caught here.
        /// The muzzle's own overlap check cannot help: it only ignores what the ball spawns
        /// inside, not what it flies into a metre later.
        /// </summary>
        private void StripColliders(GameObject model)
        {
            Collider[] colliders = model.GetComponentsInChildren<Collider>(true);
            if (colliders.Length == 0)
            {
                return;
            }

            // Disabled in every build, not only in the editor. The whole guard used to be behind
            // UNITY_EDITOR || DEVELOPMENT_BUILD, which was safe while the vehicle models were
            // ours; every prefab in the cannon pack ships with a BoxCollider, so a release build
            // would put a solid box in front of the muzzle and only a release build would show it.
            // The warning stays development-only: it is a note to whoever authored the prefab,
            // and the player's build has nobody to read it.
            for (int i = 0; i < colliders.Length; i++)
            {
                colliders[i].enabled = false;
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.LogWarning(
                $"{nameof(VehicleMount)} on \"{name}\" disabled {colliders.Length} collider(s) on the "
                + $"{model.name} model. A vehicle model is visuals only; anything solid in front of the "
                + "muzzle eats the player's shots.",
                this);
#endif
        }

        private void DestroyCurrent()
        {
            // Cleared even when there was nothing to destroy: an Animator that outlives its model
            // is a null reference the presenter would only meet on the next shot.
            CurrentAnimator = null;
            currentVehicle = null;
            currentLevel = 0;

            if (current == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(current);
            }
            else
            {
                DestroyImmediate(current);
            }

            current = null;
        }
    }
}
