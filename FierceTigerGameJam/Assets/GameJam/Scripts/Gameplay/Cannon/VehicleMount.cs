using System.Collections.Generic;
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

        [Tooltip("The transform the aim actually rotates - CannonA, the old barrel. The mounted "
                 + "model's barrel bone mirrors its rotation, so the vehicle aims without the aim "
                 + "controller learning about vehicles.")]
        [SerializeField] private Transform barrelReference;

        [Tooltip("Name of the barrel bone inside the pack models. Cannon.A carries the barrel's "
                 + "vertices and pivots at its breech; its children Cannon.B and Cannon.C follow "
                 + "it, and the base and the wheels are outside the armature and stay still.")]
        [SerializeField] private string barrelNodeName = "Cannon.A";

        /// <summary>The spawned model's Animator, for whoever presents the shot. Null on the fallback.</summary>
        public Animator CurrentAnimator { get; private set; }

        private GameObject current;
        private VehicleDefinition currentVehicle;
        private int currentLevel;
        private bool warnedAboutMissingModel;
        private bool warnedAboutMissingBarrelNode;

        private Transform barrelNode;
        private Transform barrelNodeParent;
        private Transform barrelReferenceParent;
        private Quaternion barrelRestLocalRotation = Quaternion.identity;
        private Quaternion referenceRestLocalRotation = Quaternion.identity;

        /// <summary>
        /// Reused by the breadth-first search for the barrel bone. A field rather than a local so
        /// a vehicle swap - which happens on a tap, not on a frame - does not hand the collector
        /// a fresh list every time.
        /// </summary>
        private readonly List<Transform> searchFrontier = new List<Transform>();

        /// <summary>
        /// The barrel's rest pose is read here, before anything has aimed. Both fire controllers
        /// call <c>CannonAimController.Initialize</c> in their own Awake, and that re-applies a
        /// zero aim to the same pose it just read, so this sees the authored rotation whichever
        /// of the two Awakes runs first. The flow's ResetAim puts the pivot back to exactly that
        /// pose between runs, so it never needs re-reading.
        /// </summary>
        private void Awake()
        {
            if (barrelReference != null)
            {
                referenceRestLocalRotation = barrelReference.localRotation;
                barrelReferenceParent = barrelReference.parent;
            }
        }

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

            CacheBarrelNode(current);

            SetFallbackActive(false);

            StripColliders(current);
        }

        /// <summary>
        /// Finds the barrel bone in the freshly spawned model and remembers its rest pose, so the
        /// follow below is nothing but quaternion arithmetic on cached transforms. Breadth-first
        /// because the bone sits shallow, under the armature, while the name could also appear
        /// deeper down in a model authored differently.
        /// </summary>
        private void CacheBarrelNode(GameObject model)
        {
            barrelNode = null;
            barrelNodeParent = null;
            barrelRestLocalRotation = Quaternion.identity;

            if (string.IsNullOrEmpty(barrelNodeName))
            {
                return;
            }

            searchFrontier.Clear();
            searchFrontier.Add(model.transform);
            for (int i = 0; i < searchFrontier.Count; i++)
            {
                Transform node = searchFrontier[i];
                if (node.name == barrelNodeName)
                {
                    barrelNode = node;
                    barrelNodeParent = node.parent;
                    barrelRestLocalRotation = node.localRotation;
                    searchFrontier.Clear();
                    return;
                }

                for (int child = 0; child < node.childCount; child++)
                {
                    searchFrontier.Add(node.GetChild(child));
                }
            }

            searchFrontier.Clear();

            // Once per component, not once per swap: a pack that renames its bones would other-
            // wise log on every upgrade, and the model still stands and still fires - it simply
            // does not aim, which is a note for whoever wired it rather than an error.
            if (!warnedAboutMissingBarrelNode)
            {
                warnedAboutMissingBarrelNode = true;
                Debug.LogWarning(
                    $"{nameof(VehicleMount)} on \"{name}\" found no \"{barrelNodeName}\" node inside the "
                    + $"{model.name} model, so its barrel will not follow the aim.",
                    this);
            }
        }

        /// <summary>
        /// The barrel copies whatever the aim did to <see cref="barrelReference"/>. After the
        /// Animator, so the follow wins on this one bone while the shot clip keeps squashing its
        /// children - and the clip never rotates the bone anyway, only translates and scales it.
        /// </summary>
        private void LateUpdate()
        {
            if (barrelNode == null || barrelReference == null)
            {
                return;
            }

            // The rotation the aim added, measured in the reference's parent space.
            Quaternion aimDelta = barrelReference.localRotation * Quaternion.Inverse(referenceRestLocalRotation);

            // Deviation from the brief, which replays the delta straight onto the barrel's own
            // rest pose on the grounds that both parents share an orientation. They do not: the
            // model mounts at identity under Cannon, but the barrel bone hangs off an armature
            // that is turned a quarter turn against it, so an unrebased delta would pitch the
            // barrel sideways and roll it instead. Conjugating by the rotation between the two
            // parents costs four quaternion multiplies and no allocation, and is right whatever
            // the pack's armature does.
            if (barrelNodeParent != null && barrelReferenceParent != null)
            {
                Quaternion intoBarrelSpace =
                    Quaternion.Inverse(barrelNodeParent.rotation) * barrelReferenceParent.rotation;
                aimDelta = intoBarrelSpace * aimDelta * Quaternion.Inverse(intoBarrelSpace);
            }

            barrelNode.localRotation = aimDelta * barrelRestLocalRotation;
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

            // Same reason as the Animator: a barrel node pointing into a destroyed model is a
            // null reference LateUpdate would meet on the very next frame.
            barrelNode = null;
            barrelNodeParent = null;

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
