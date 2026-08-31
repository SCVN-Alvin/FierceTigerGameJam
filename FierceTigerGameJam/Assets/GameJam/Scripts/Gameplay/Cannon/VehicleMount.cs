using System;
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
    /// By default only the barrel of that model is drawn - see <see cref="BarrelOnly"/> - which is
    /// the look the game had before there were vehicles at all. The whole machine is one checkbox
    /// away, and the difference is purely what is switched on: the same model spawns either way,
    /// carrying the same armature, so the aim and the shot animation do not know which look they
    /// are in.
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

        [Tooltip("How far the barrel stands up when nothing is aimed, in degrees, positive being "
                 + "nose-up. 31.44 is what the old cannon rested at: the whole vehicle used to "
                 + "inherit that tilt, which swung its rear wheels through the floor, so the "
                 + "barrel carries it alone now and the chassis stays level. Zero leaves the "
                 + "barrel wherever the model authored it.")]
        [SerializeField] private float barrelRestPitchDegrees = 31.44f;

        [Tooltip("Replaces the pack's own controller on every spawned model. Theirs holds one "
                 + "looping state, so an unmounted-over model fires its shot animation forever; "
                 + "ours idles and plays the shot on a trigger.")]
        [SerializeField] private RuntimeAnimatorController mountedController;

        [Tooltip("Shows only the mounted model's barrel, switching its base and its wheels off as "
                 + "it spawns - the look the game had before vehicles arrived. Off draws the whole "
                 + "machine, which is what the pack's own demo scenes and a shop rig showing what "
                 + "the player is buying want, so the two looks are a checkbox apart.")]
        [SerializeField] private bool barrelOnly = true;

        [Tooltip("What barrelOnly hides, matched against a spawned node's name as a case-"
                 + "insensitive prefix. Prefixes rather than whole names because the pack spells "
                 + "its base three ways - Cannone_Pase, Cannon_Base and Cannon_Pase - and because "
                 + "a re-import can leave a trailing space or a .001 on any of them. A part no "
                 + "prefix matches stays visible, so an unfamiliar model shows too much rather "
                 + "than nothing at all. Never add \"Cannon\" itself: that is the barrel mesh, and "
                 + "Cannon.A is the bone that aims it.")]
        [SerializeField] private string[] hiddenPartPrefixes =
        {
            "Cannone_Pase",
            "Cannon_Base",
            "Cannon_Pase",
            "wheel",
        };

        /// <summary>The spawned model's Animator, for whoever presents the shot. Null on the fallback.</summary>
        public Animator CurrentAnimator { get; private set; }

        /// <summary>
        /// Whether mounted models show their barrel alone. Public so the editor's fit tool can
        /// measure the same thing the player will see: a base that is never drawn must not count
        /// towards the height a model is scaled by.
        /// </summary>
        public bool BarrelOnly => barrelOnly;

        /// <summary>
        /// The prefixes <see cref="BarrelOnly"/> hides by, handed out as the array itself rather
        /// than copied - the fit tool reads it once per run and nobody writes to it. Same reason
        /// as <see cref="BarrelOnly"/>: one list, read by the thing that hides and by the thing
        /// that measures, so the two cannot drift apart.
        /// </summary>
        public string[] HiddenPartPrefixes => hiddenPartPrefixes;

        private GameObject current;
        private VehicleDefinition currentVehicle;
        private int currentLevel;

        /// <summary>
        /// Which look the standing model was spawned with. Hiding a part is one-way - the model
        /// is never un-hidden, it is replaced - so this is what lets the checkbox be tried from
        /// the inspector: flip it, hit Refresh, and the mount notices the look it is showing is
        /// not the one it is set to and re-spawns.
        /// </summary>
        private bool currentBarrelOnly;

        private bool warnedAboutMissingModel;
        private bool warnedAboutMissingBarrelNode;
        private bool warnedAboutHiddenBarrel;

        private Transform barrelNode;
        private Transform barrelNodeParent;
        private Transform barrelReferenceParent;
        private Quaternion barrelRestLocalRotation = Quaternion.identity;
        private Quaternion referenceRestLocalRotation = Quaternion.identity;

        /// <summary>
        /// Reused by the breadth-first walks over a freshly spawned model - the search for the
        /// barrel bone and the pass that hides everything that is not the barrel. A field rather
        /// than a local so a vehicle swap - which happens on a tap, not on a frame - does not hand
        /// the collector a fresh list every time.
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

            if (current != null
                && vehicle == currentVehicle
                && level == currentLevel
                && barrelOnly == currentBarrelOnly)
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
            currentBarrelOnly = barrelOnly;

            // The pack models animate their own shot, so whoever presents the firing needs this
            // rather than the barrel's animator. Cached here because a shot must not pay for a
            // hierarchy walk.
            CurrentAnimator = current.GetComponentInChildren<Animator>(true);

            // Every spawn, not once: the controller belongs to the freshly instantiated model,
            // and an upgrade brings a new model carrying the pack's looping one again.
            if (CurrentAnimator != null && mountedController != null)
            {
                CurrentAnimator.runtimeAnimatorController = mountedController;
            }

            CacheBarrelNode(current);

            // After the bone is cached, not before: the hiding pass asks whether a part it is
            // about to switch off is carrying that bone, and a null bone would make it unable to
            // tell.
            HideNonBarrelParts(current);

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
        /// Switches off everything on a freshly spawned model that is not the barrel, leaving the
        /// look the game had before vehicles: a barrel and nothing under it.
        ///
        /// Written as a list of what to hide rather than a list of what to keep, and deliberately
        /// so. Every pack model is seven objects at its root - the armature, one barrel mesh, one
        /// base and four wheels - but only the wheels are named the same in all twelve; the base
        /// is spelled Cannone_Pase, Cannon_Base or Cannon_Pase depending on the family, and the
        /// barrel mesh is "Cannon" in nine of them and "Cannon " with a trailing space in the
        /// other three. A keep-list keyed on any of that would hide the barrel on the models it
        /// guessed wrong and leave the player looking at an empty cannon. Hiding by name instead
        /// means the worst an unfamiliar part can do is stay on screen, which somebody can see and
        /// fix rather than mistake for the game failing to load.
        ///
        /// The armature is never touched: the shot animation plays on it and the aim follow drives
        /// a bone inside it, so a deactivated one would cost both at once. Nothing in the default
        /// list can match it, and the guard below catches a list that has been edited until it can.
        ///
        /// Only ever called on a model that has just been instantiated, so there is nothing to
        /// un-hide: a mount that stops wanting this look re-spawns rather than restoring parts.
        /// </summary>
        private void HideNonBarrelParts(GameObject model)
        {
            if (!barrelOnly || hiddenPartPrefixes == null || hiddenPartPrefixes.Length == 0)
            {
                return;
            }

            // The root is skipped rather than tested. It is the model itself, and a prefab that
            // happened to be named after one of its own parts would otherwise disappear whole.
            searchFrontier.Clear();
            Transform root = model.transform;
            for (int child = 0; child < root.childCount; child++)
            {
                searchFrontier.Add(root.GetChild(child));
            }

            for (int i = 0; i < searchFrontier.Count; i++)
            {
                Transform node = searchFrontier[i];
                if (IsHiddenPart(node.name, hiddenPartPrefixes))
                {
                    if (!CarriesBarrelNode(node))
                    {
                        // Not queued for descent: a part's children go dark with it, and walking
                        // into something already switched off would only be work.
                        node.gameObject.SetActive(false);
                        continue;
                    }

                    WarnAboutHiddenBarrel(node, model);
                }

                for (int child = 0; child < node.childCount; child++)
                {
                    searchFrontier.Add(node.GetChild(child));
                }
            }

            searchFrontier.Clear();
        }

        /// <summary>
        /// Whether a node's name marks it as one of the parts <see cref="BarrelOnly"/> hides.
        ///
        /// Static, and taking the list rather than reading the field, so the editor's fit tool can
        /// ask the same question of a model prefab it is measuring without instantiating a mount -
        /// the rule that decides what is drawn and the rule that decides what is measured have to
        /// be one rule or the fitted scales are fitted to something nobody sees.
        ///
        /// Prefix matching, not equality: it costs nothing and it absorbs the two ways this pack's
        /// names vary - a trailing space, and the .001 an FBX re-import adds to a duplicate. It
        /// also cannot catch the barrel by accident, because every prefix here is longer than the
        /// name the barrel mesh carries.
        /// </summary>
        public static bool IsHiddenPart(string nodeName, string[] prefixes)
        {
            if (string.IsNullOrEmpty(nodeName) || prefixes == null)
            {
                return false;
            }

            for (int i = 0; i < prefixes.Length; i++)
            {
                string prefix = prefixes[i];

                // Ordinal rather than culture-aware: these are asset names, and a Turkish locale
                // deciding what "I" folds to has no business changing which parts of a cannon are
                // drawn. It allocates nothing either.
                if (!string.IsNullOrEmpty(prefix)
                    && nodeName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Whether the barrel bone is this node or hangs somewhere beneath it. Walked upwards from
        /// the bone rather than downwards from the node: the bone sits four deep at most, while a
        /// part could be a whole subtree.
        /// </summary>
        private bool CarriesBarrelNode(Transform node)
        {
            for (Transform walk = barrelNode; walk != null; walk = walk.parent)
            {
                if (walk == node)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Once per component: a list edited until it swallows the armature would otherwise log on
        /// every upgrade. Nothing is broken when this fires - the part is left visible - but the
        /// look asked for is not the look being shown, and that is worth saying out loud.
        /// </summary>
        private void WarnAboutHiddenBarrel(Transform node, GameObject model)
        {
            if (warnedAboutHiddenBarrel)
            {
                return;
            }

            warnedAboutHiddenBarrel = true;
            Debug.LogWarning(
                $"{nameof(VehicleMount)} on \"{name}\" left \"{node.name}\" visible on the {model.name} "
                + $"model: {nameof(hiddenPartPrefixes)} matches it, but the barrel bone "
                + $"\"{barrelNodeName}\" is inside it and hiding it would stop the cannon aiming and "
                + "animating. Narrow the prefix.",
                this);
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

            // The rotation the aim added, measured in the reference's parent space, plus the
            // barrel's own standing elevation. Both are rotations about the same axis in this
            // space, so they simply compose: the barrel sits at the rest pitch when nothing is
            // aimed and swings from there.
            Quaternion aimDelta = barrelReference.localRotation * Quaternion.Inverse(referenceRestLocalRotation);
            if (barrelRestPitchDegrees != 0f)
            {
                aimDelta *= Quaternion.AngleAxis(-barrelRestPitchDegrees, Vector3.right);
            }

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
        ///
        /// Runs after the barrel-only pass, and is unaffected by it: the search below includes
        /// inactive objects, so a wheel that has just been switched off is still disabled rather
        /// than skipped. Switching it off already took it out of the physics scene - this only
        /// makes sure that stays true if anything ever switches it back on.
        /// </summary>
        private void StripColliders(GameObject model)
        {
            // true, and load-bearing: the base and the wheels may already be switched off.
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
