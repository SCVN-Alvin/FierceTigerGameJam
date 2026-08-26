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
                return;
            }

            VehicleDefinition vehicle = loadout.Selected;
            int level = loadout.SelectedLevel;
            if (vehicle == null)
            {
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

                return;
            }

            DestroyCurrent();

            Transform parent = mountPoint != null ? mountPoint : transform;
            current = Instantiate(prefab, parent);
            current.transform.localPosition = Vector3.zero;
            current.transform.localRotation = Quaternion.identity;
            current.transform.localScale = modelLocalScale;

            currentVehicle = vehicle;
            currentLevel = level;

            StripColliders(current);
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
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Collider[] colliders = model.GetComponentsInChildren<Collider>(true);
            if (colliders.Length == 0)
            {
                return;
            }

            for (int i = 0; i < colliders.Length; i++)
            {
                colliders[i].enabled = false;
            }

            Debug.LogWarning(
                $"{nameof(VehicleMount)} on \"{name}\" disabled {colliders.Length} collider(s) on the "
                + $"{model.name} model. A vehicle model is visuals only; anything solid in front of the "
                + "muzzle eats the player's shots.",
                this);
#endif
        }

        private void DestroyCurrent()
        {
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
