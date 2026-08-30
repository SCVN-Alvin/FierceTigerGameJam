#if UNITY_EDITOR
using System.IO;
using GameJam.Gameplay.Cannon;
using GameJam.Gameplay.Combat;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GameJam.EditorTools
{
    /// <summary>
    /// Creates the three cannons the player drives: A, the one they start with, and B and C to
    /// earn. Each is three levels of damage multiplier with a pack model per level, so an upgrade
    /// is something the player can see on the cannon rather than only a number in the shop.
    ///
    /// The numbers are a playable starting point rather than a balance pass. The one shape worth
    /// keeping is that a fully upgraded A (1.40) beats a fresh B (1.30): what the next cannon
    /// sells is its ceiling, not an instant win, so upgrading the starter is never wasted gold.
    ///
    /// This also retires the truck and the tank the game shipped with before the pack arrived.
    /// A save naming one of them falls back to the default on its own, so no migration is needed.
    /// </summary>
    public static class VehicleDefinitionBuilder
    {
        private const string ConfigFolder = "Assets/GameJam/Config/Vehicles";

        /// <summary>
        /// The pack's own folder, en dash and their "Prefaps" typo included. Written with an
        /// escape rather than the character itself so the path survives this file being re-saved
        /// in an encoding that does not keep the dash intact - a mangled byte here would look
        /// like the models simply failing to load.
        /// </summary>
        private const string PackFolder =
            "Assets/Hyper-Casual Cannon Pack \u2013 Animated Turrets (URP + Built-in)/Cannon_Pack_URP/Prefaps_URP";

        /// <summary>The pack's FBXs, alongside their own looping controller. Same escaped dash.</summary>
        private const string PackModelFolder =
            "Assets/Hyper-Casual Cannon Pack \u2013 Animated Turrets (URP + Built-in)/Models";

        private const string VehicleAnimationFolder = "Assets/GameJam/Animations/Vehicles";

        private const string MountedControllerPath = VehicleAnimationFolder + "/VehicleCannon.controller";

        /// <summary>
        /// The A family's FBX. Every model in the pack carries the same armature under the same
        /// names, so one clip drives all nine - and taking it from one place means a re-import of
        /// the B or C models cannot quietly change what the others play.
        /// </summary>
        private const string ShotClipSourcePath = PackModelFolder + "/Cannon_A.fbx";

        /// <summary>The pack's spelling of its only take. Theirs, not ours.</summary>
        private const string ShotClipName = "Armature|Shoting";

        private const string ShotTriggerName = "Shot";

        private const string SlingshotPrefabPath =
            "Assets/GameJam/Imported/LunaSmashdown/Prefabs/SmashFest/Slingshot.prefab";

        /// <summary>The aim target that carries the barrel's Animator; the model mounts here.</summary>
        private const string CannonObjectName = "Cannon";

        /// <summary>The model the cannon wore before the pack, kept as the mount's fallback.</summary>
        private const string FallbackModelName = "CannonTank_Default_Red";

        /// <summary>
        /// The old barrel. Switched off since the pack arrived, but it is still what the aim
        /// rotates and still the size the game was laid out around, so it is both the yardstick
        /// the models are fitted to and the transform the mounted barrel follows.
        /// </summary>
        private const string BarrelObjectName = "CannonA";

        /// <summary>
        /// The frame the vehicle model hangs in: a live child of Cannon, seeded at the aim pivot's
        /// pose when it is first created and hand-tunable from then on. It does not follow the aim
        /// - the barrel bone does that - so the base and wheels stay planted.
        /// </summary>
        private const string CannonRootObjectName = "CannonRoot";

        /// <summary>Where the shot leaves from, and so where the smoke belongs.</summary>
        private const string MuzzleObjectName = "MuzzlePoint";

        /// <summary>The nested muzzle-flash effect, authored against the old tank's barrel tip.</summary>
        private const string SmokeObjectName = "Cannon_Smoke";

        /// <summary>
        /// The live twin of CannonA that the muzzle flash hangs in. Unlike CannonRoot it follows
        /// the aim, so the flash swings with the barrel; the model stays in the static frame so
        /// the base and wheels stay planted.
        /// </summary>
        private const string MuzzleFlashRootName = "MuzzleFlashRoot";

        /// <summary>
        /// What the aim rotates now that the old cannon art is gone. CannonA used to be both the
        /// barrel you could see and the transform the aim turned; deleting it took the aim pivot
        /// and the muzzle with it, and the game cannot run without them. This is the same role
        /// with nothing to draw - an empty the aim turns and the muzzle hangs off.
        /// </summary>
        private const string AimPivotName = "AimPivot";

        /// <summary>
        /// CannonA's pose, kept as numbers because the object it came from no longer exists. This
        /// is where the cannon was aimed from when the whole game was laid out around it, so a
        /// rebuilt pivot has to land here or every shot leaves from somewhere new.
        /// </summary>
        private static readonly Vector3 AimPivotLocalPosition =
            new Vector3(0.0073143532f, 0.07317917f, -0.46163517f);

        private static readonly Quaternion AimPivotLocalRotation =
            new Quaternion(-0.27092943f, 0f, 0f, 0.9625992f);

        /// <summary>MuzzlePoint's offset inside that pivot, kept for the same reason.</summary>
        private static readonly Vector3 MuzzleLocalPosition =
            new Vector3(-0.0073143532f, 0.5320612f, 0.10477993f);

        /// <summary>
        /// A fitted model may be a twentieth of the pack's size or twice it; anything outside
        /// that is a measurement gone wrong (an empty prefab, a stray renderer a kilometre away)
        /// and a clamp keeps it from writing a scale that makes the vehicle invisible.
        /// </summary>
        private const float MinFittedScale = 0.05f;

        private const float MaxFittedScale = 2f;

        /// <summary>
        /// Vehicles the catalogue no longer contains. Listed rather than simply deleted so the
        /// loadout can be told to stop naming them before their assets go.
        /// </summary>
        private static readonly string[] RetiredVehicleIds = { "vehicle_truck", "vehicle_tank" };

        [MenuItem("Tools/Smashdown/Create Default Vehicle Definitions")]
        public static void CreateDefaults()
        {
            EnsureFolder(ConfigFolder);

            VehicleDefinition cannonA = CreateVehicle(
                "cannon_a",
                "Cannon A",
                "The one you start with. Cheap to improve, and improving it is never wasted.",
                new[]
                {
                    Level("Cannon A I", 1.00f, "Cannon_A_URP"),
                    Level("Cannon A II", 1.20f, "Cannon_A_B_URP"),
                    Level("Cannon A III", 1.40f, "Cannon_A_C_URP"),
                });

            VehicleDefinition cannonB = CreateVehicle(
                "cannon_b",
                "Cannon B",
                "Starts harder than A ever finishes, and keeps going.",
                new[]
                {
                    Level("Cannon B I", 1.30f, "Cannon_B_URP"),
                    Level("Cannon B II", 1.60f, "Cannon_B_B_URP"),
                    Level("Cannon B III", 2.00f, "Cannon_B_C_URP"),
                });

            // The pack ships no Cannon_C_C; C_D is its third C model.
            VehicleDefinition cannonC = CreateVehicle(
                "cannon_c",
                "Cannon C",
                "The last thing you buy, and the last thing the wall sees.",
                new[]
                {
                    Level("Cannon C I", 1.50f, "Cannon_C_URP"),
                    Level("Cannon C II", 2.00f, "Cannon_C_B_URP"),
                    Level("Cannon C III", 2.60f, "Cannon_C_D_URP"),
                });

            EnsureMountedController();

            CreateLoadout(cannonA, cannonB, cannonC);

            // Only once the loadout points elsewhere: deleting an asset something still
            // references leaves a missing reference rather than an empty slot, and an empty slot
            // is what the runtime knows how to survive.
            DeleteRetiredVehicles();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            WireVehicleMount();

            Debug.Log($"Created default vehicle definitions in {ConfigFolder}.");
        }

        /// <summary>
        /// Puts the mount on the cannon inside the Slingshot prefab, so every scene holding an
        /// instance gets it. Its own menu item as well as part of the pass above: the prefab is
        /// the one thing here that a merge can undo without touching an asset.
        /// </summary>
        [MenuItem("Tools/Smashdown/Wire Vehicle Mount")]
        public static void WireVehicleMount()
        {
            VehicleLoadout loadout = AssetDatabase.LoadAssetAtPath<VehicleLoadout>($"{ConfigFolder}/VehicleLoadout.asset");

            // Before the prefab contents are opened, not inside them: this writes and saves an
            // asset, and doing that while an editable copy of a prefab is loaded is asking the
            // asset database to refresh underneath it.
            RuntimeAnimatorController mountedController = EnsureMountedController();

            GameObject root = PrefabUtility.LoadPrefabContents(SlingshotPrefabPath);
            if (root == null)
            {
                Debug.LogWarning(
                    $"{nameof(VehicleDefinitionBuilder)} found no prefab at {SlingshotPrefabPath}, "
                    + "so no vehicle model will be mounted under the cannon.");
                return;
            }

            try
            {
                Transform cannon = FindDescendant(root.transform, CannonObjectName);
                if (cannon == null)
                {
                    Debug.LogWarning(
                        $"{nameof(VehicleDefinitionBuilder)} found no \"{CannonObjectName}\" object inside "
                        + $"{SlingshotPrefabPath}, so the mount was not added.");
                    return;
                }

                VehicleMount mount = cannon.GetComponent<VehicleMount>();
                bool changed = false;
                if (mount == null)
                {
                    mount = cannon.gameObject.AddComponent<VehicleMount>();
                    changed = true;
                }

                Transform fallback = FindDescendant(root.transform, FallbackModelName);

                // The transform the aim turns. CannonA while the old cannon art is still there,
                // and the rebuilt empty once it has been deleted - the aim, the muzzle, the model
                // frame and the flash all hang off whichever one is present, so resolving it in
                // one place is what keeps the deletion from breaking four things at once.
                changed |= EnsureAimPivot(cannon, out Transform barrel, out Transform muzzle);

                changed |= EnsureCannonRoot(cannon, barrel, out Transform cannonRoot);
                changed |= EnsureMuzzleFlashRoot(cannon, barrel, out Transform muzzleFlashRoot);
                changed |= MoveSmokeToMuzzle(root.transform, muzzleFlashRoot, muzzle);

                SerializedObject serializedMount = new SerializedObject(mount);
                SetIfEmpty(serializedMount, "loadout", loadout);

                // The model hangs in CannonRoot, not in Cannon: mounting at Cannon put the model
                // at the cannon's own origin and orientation, which is neither where the barrel is
                // nor which way it points. CannonRoot copies CannonA's rest pose, so a model drops
                // in already aligned with the barrel it stands in for, and the barrel-bone follow
                // adds only the aim delta on top - no double rotation.
                changed |= RepointMountPoint(serializedMount, cannon, cannonRoot);
                SetIfEmpty(serializedMount, "fallbackModel", fallback != null ? fallback.gameObject : null);
                SetIfEmpty(serializedMount, "barrelReference", barrel);

                // Ensured on this pass as well as in the defaults one, so wiring the mount on its
                // own into a project that predates the controller still leaves the cannon idle
                // rather than firing forever.
                SetIfEmpty(serializedMount, "mountedController", mountedController);
                changed |= serializedMount.ApplyModifiedPropertiesWithoutUndo();

                CannonShotPresenter presenter = root.GetComponentInChildren<CannonShotPresenter>(true);
                if (presenter != null)
                {
                    SerializedObject serializedPresenter = new SerializedObject(presenter);
                    SetIfEmpty(serializedPresenter, "mount", mount);
                    changed |= serializedPresenter.ApplyModifiedPropertiesWithoutUndo();
                }
                else
                {
                    Debug.LogWarning(
                        $"{nameof(VehicleDefinitionBuilder)} found no {nameof(CannonShotPresenter)} in "
                        + $"{SlingshotPrefabPath}, so the mounted model's shot animation will not play.");
                }

                // Saved only when something actually moved. Writing the prefab on every run would
                // put a re-serialised copy of it in front of a reviewer on a pass that changed
                // nothing, which is exactly the noise that hides the run that did change something.
                if (!changed)
                {
                    return;
                }

                PrefabUtility.SaveAsPrefabAsset(root, SlingshotPrefabPath);
                Debug.Log($"{nameof(VehicleDefinitionBuilder)} wired the vehicle mount into {SlingshotPrefabPath}.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            // After the prefab is saved and its contents released, so the scene's instance is
            // already carrying whatever the pass just added.
            RepairSceneCannonReferences();
        }

        /// <summary>
        /// Re-points the scene's aim and fire references when the objects they named have been
        /// deleted from the prefab.
        ///
        /// These two live on a scene object, not in the Slingshot prefab, so rebuilding the pivot
        /// and the muzzle inside the prefab cannot heal them: the scene still holds a reference to
        /// a transform that no longer exists, which loads as null and stops the run before the
        /// first shot. Unity gives no warning for this - the inspector simply reads "Missing".
        ///
        /// Only a reference that is actually empty is filled, so a cannon deliberately aimed at
        /// something else is left alone; a dangling reference reads as null here, which is exactly
        /// the case worth repairing.
        /// </summary>
        private static void RepairSceneCannonReferences()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return;
            }

            VehicleMount mount = Object.FindFirstObjectByType<VehicleMount>(FindObjectsInactive.Include);
            if (mount == null)
            {
                return;
            }

            // The pivot the prefab pass settled on, found in the scene's own instance: CannonA
            // where it survives, the rebuilt empty where it does not.
            Transform pivot = FindDescendant(mount.transform, BarrelObjectName)
                ?? FindDescendant(mount.transform, AimPivotName);
            Transform muzzle = pivot != null ? FindDescendant(pivot, MuzzleObjectName) : null;
            if (pivot == null || muzzle == null)
            {
                return;
            }

            bool repaired = false;

            CannonAimController aim = Object.FindFirstObjectByType<CannonAimController>(FindObjectsInactive.Include);
            if (aim != null)
            {
                SerializedObject serializedAim = new SerializedObject(aim);
                SetIfEmpty(serializedAim, "aimPivot", pivot);
                repaired |= serializedAim.ApplyModifiedPropertiesWithoutUndo();
            }

            GridKnockdownCannonFireController fire =
                Object.FindFirstObjectByType<GridKnockdownCannonFireController>(FindObjectsInactive.Include);
            if (fire != null)
            {
                SerializedObject serializedFire = new SerializedObject(fire);
                SetIfEmpty(serializedFire, "fireOrigin", muzzle);
                repaired |= serializedFire.ApplyModifiedPropertiesWithoutUndo();
            }

            if (!repaired)
            {
                return;
            }

            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log(
                $"{nameof(VehicleDefinitionBuilder)} re-pointed the scene's aim pivot and muzzle at "
                + $"\"{pivot.name}\". Save the scene to keep the repair.");
        }

        /// <summary>
        /// The controller the mounted models run instead of the pack's own.
        ///
        /// The pack ships one state holding one clip whose import settings loop it, and no
        /// parameters, so a mounted cannon fires its recoil animation for as long as it is on
        /// screen. Ours starts in an empty Idle - the pack has no idle take, so the rest pose is
        /// the idle - and passes through the shot exactly once whenever the trigger is set, which
        /// makes the clip's baked loop flag irrelevant.
        ///
        /// Built only when it is missing. Once it exists it is a project asset somebody may have
        /// opened and tuned (a transition duration, an added idle clip), and a scaffolding tool
        /// that rebuilt it on every run would throw that away.
        /// </summary>
        private static RuntimeAnimatorController EnsureMountedController()
        {
            AnimatorController existing = AssetDatabase.LoadAssetAtPath<AnimatorController>(MountedControllerPath);
            if (existing != null)
            {
                return existing;
            }

            AnimationClip shotClip = LoadShotClip();
            if (shotClip == null)
            {
                // Still built, deliberately: a controller with an empty Shot state leaves the
                // cannon still rather than looping, which is most of what this section is for.
                Debug.LogWarning(
                    $"{nameof(VehicleDefinitionBuilder)} found no \"{ShotClipName}\" clip inside "
                    + $"{ShotClipSourcePath}, so the mounted cannon will idle but not animate its shot.");
            }

            EnsureFolder(VehicleAnimationFolder);

            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(MountedControllerPath);
            controller.AddParameter(ShotTriggerName, AnimatorControllerParameterType.Trigger);

            AnimatorStateMachine machine = controller.layers[0].stateMachine;

            AnimatorState idle = machine.AddState("Idle");
            machine.defaultState = idle;

            AnimatorState shot = machine.AddState("Shot");
            shot.motion = shotClip;

            AnimatorStateTransition toShot = idle.AddTransition(shot);
            toShot.hasExitTime = false;
            toShot.exitTime = 0f;
            toShot.hasFixedDuration = true;
            toShot.duration = 0f;
            toShot.AddCondition(AnimatorConditionMode.If, 0f, ShotTriggerName);

            // No condition on the way back, only a full pass: the shot is over when the clip is,
            // and hanging the return off a second parameter would need somebody to remember to
            // set it.
            AnimatorStateTransition toIdle = shot.AddTransition(idle);
            toIdle.hasExitTime = true;
            toIdle.exitTime = 1f;
            toIdle.hasFixedDuration = true;
            toIdle.duration = 0f;

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            Debug.Log($"{nameof(VehicleDefinitionBuilder)} created {MountedControllerPath}.");
            return controller;
        }

        /// <summary>
        /// The firing take, pulled out of the FBX it is imported into. Sub-assets have to be
        /// enumerated rather than loaded by path, and the preview clip Unity generates alongside
        /// them is skipped by matching the pack's own take name exactly.
        /// </summary>
        private static AnimationClip LoadShotClip()
        {
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath(ShotClipSourcePath);
            for (int i = 0; i < assets.Length; i++)
            {
                AnimationClip clip = assets[i] as AnimationClip;
                if (clip != null && clip.name == ShotClipName)
                {
                    return clip;
                }
            }

            return null;
        }

        /// <summary>
        /// Measures every vehicle model against the old barrel and writes the scale that makes
        /// them the same height into the definitions.
        ///
        /// The one tool here that overwrites rather than fills: a fitted scale is a measurement,
        /// not a decision, and re-running after the art changes has to produce the new number
        /// rather than politely keep the stale one. Hand tuning survives only until the next run,
        /// which is why the field's tooltip sends a tuner to it rather than to the inspector.
        ///
        /// Everything is measured inside loaded prefab contents, so no open scene is touched and
        /// the barrel is switched on and back off without the prefab ever being saved.
        /// </summary>
        [MenuItem("Tools/Smashdown/Fit Vehicle Models")]
        public static void FitVehicleModels()
        {
            if (!TryMeasureBarrelHeight(out float targetHeight))
            {
                return;
            }

            string[] guids = AssetDatabase.FindAssets($"t:{nameof(VehicleDefinition)}", new[] { ConfigFolder });
            if (guids.Length == 0)
            {
                Debug.LogWarning(
                    $"{nameof(VehicleDefinitionBuilder)} found no vehicle definitions in {ConfigFolder}, so "
                    + "there was nothing to fit. Run Create Default Vehicle Definitions first.");
                return;
            }

            int fitted = 0;
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                VehicleDefinition vehicle = AssetDatabase.LoadAssetAtPath<VehicleDefinition>(path);
                if (vehicle == null)
                {
                    continue;
                }

                fitted += FitLevels(vehicle, targetHeight);
            }

            AssetDatabase.SaveAssets();
            Debug.Log(
                $"{nameof(VehicleDefinitionBuilder)} fitted {fitted} vehicle model(s) to the "
                + $"{BarrelObjectName} height of {targetHeight:0.000} (measured in the mount's own space).");
        }

        /// <summary>
        /// Writes one vehicle's scales, and returns how many levels it actually measured.
        /// Levels with no model of their own are skipped: they show the level below them, whose
        /// scale is already fitted, and writing a number onto an empty slot would only be a
        /// number nobody reads.
        /// </summary>
        private static int FitLevels(VehicleDefinition vehicle, float targetHeight)
        {
            SerializedObject serialized = new SerializedObject(vehicle);
            SerializedProperty levels = serialized.FindProperty("levels");
            int fitted = 0;

            for (int i = 0; i < levels.arraySize; i++)
            {
                SerializedProperty level = levels.GetArrayElementAtIndex(i);
                SerializedProperty modelProperty = level.FindPropertyRelative("modelPrefab");
                SerializedProperty scaleProperty = level.FindPropertyRelative("modelScale");
                if (modelProperty == null || scaleProperty == null)
                {
                    continue;
                }

                GameObject model = modelProperty.objectReferenceValue as GameObject;
                if (model == null)
                {
                    continue;
                }

                if (!TryMeasurePrefabHeight(AssetDatabase.GetAssetPath(model), out float modelHeight))
                {
                    continue;
                }

                float previous = scaleProperty.floatValue;
                float scale = Mathf.Clamp(targetHeight / modelHeight, MinFittedScale, MaxFittedScale);
                scaleProperty.floatValue = scale;
                fitted++;

                // The raw heights go in the log next to the ratio on purpose: a scale that looks
                // wrong is almost always a model measured wrong, and the only way to tell the two
                // apart from a console line is to see what was measured.
                Debug.Log(
                    $"{nameof(VehicleDefinitionBuilder)} fitted {vehicle.name} level {i + 1} ({model.name}): "
                    + $"modelScale {previous:0.000} -> {scale:0.000} (model {modelHeight:0.000} high, "
                    + $"target {targetHeight:0.000}).",
                    vehicle);
            }

            if (serialized.ApplyModifiedPropertiesWithoutUndo())
            {
                EditorUtility.SetDirty(vehicle);
            }

            return fitted;
        }

        /// <summary>
        /// The yardstick: how tall the old barrel is, expressed in the mount point's own local
        /// space.
        ///
        /// Deviation from the brief, which records the barrel's world height directly. The
        /// Slingshot root is scaled to 0.5, so the barrel's world height is half its height in
        /// the space the model is actually mounted in - and the mount overwrites the model's own
        /// root scale with the fitted number, so a world measurement would fit every vehicle at
        /// half the size it should be.
        /// </summary>
        private static bool TryMeasureBarrelHeight(out float height)
        {
            height = 0f;

            GameObject root = PrefabUtility.LoadPrefabContents(SlingshotPrefabPath);
            if (root == null)
            {
                Debug.LogWarning(
                    $"{nameof(VehicleDefinitionBuilder)} found no prefab at {SlingshotPrefabPath}, so there "
                    + "was nothing to measure the vehicle models against.");
                return false;
            }

            try
            {
                Transform barrel = FindDescendant(root.transform, BarrelObjectName);
                if (barrel == null)
                {
                    Debug.LogWarning(
                        $"{nameof(VehicleDefinitionBuilder)} found no \"{BarrelObjectName}\" inside "
                        + $"{SlingshotPrefabPath}, so no vehicle model could be fitted.");
                    return false;
                }

                // Switched on only long enough to be measured: an inactive renderer's bounds are
                // whatever they last were, which for a model that has never been drawn is zero.
                // The contents are unloaded without ever being saved, so the prefab keeps the
                // barrel switched off.
                bool wasActive = barrel.gameObject.activeSelf;
                barrel.gameObject.SetActive(true);
                bool measured = TryMeasureHeight(barrel.gameObject, out float worldHeight);
                barrel.gameObject.SetActive(wasActive);

                if (!measured)
                {
                    Debug.LogWarning(
                        $"{nameof(VehicleDefinitionBuilder)} found no renderer under \"{BarrelObjectName}\", "
                        + "so the vehicle models have nothing to be fitted to.");
                    return false;
                }

                Transform mountPoint = FindDescendant(root.transform, CannonObjectName);
                float mountScale = mountPoint != null ? Mathf.Abs(mountPoint.lossyScale.y) : 1f;
                if (mountScale < Mathf.Epsilon)
                {
                    Debug.LogWarning(
                        $"{nameof(VehicleDefinitionBuilder)} read a zero scale on \"{CannonObjectName}\", so "
                        + "the barrel height could not be expressed in the mount's space.");
                    return false;
                }

                height = worldHeight / mountScale;
                return height > Mathf.Epsilon;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>
        /// A model prefab's height at scale 1 - the size the mount will draw it at before the
        /// fitted scale is applied, since the mount overwrites whatever root scale the prefab
        /// carries.
        /// </summary>
        private static bool TryMeasurePrefabHeight(string prefabPath, out float height)
        {
            height = 0f;

            // Guarded rather than trusted: LoadPrefabContents throws on anything that is not a
            // prefab file, and a level pointing straight at an FBX would otherwise abort the
            // whole run partway through instead of skipping one model.
            if (string.IsNullOrEmpty(prefabPath) || !prefabPath.EndsWith(".prefab"))
            {
                Debug.LogWarning(
                    $"{nameof(VehicleDefinitionBuilder)} cannot measure \"{prefabPath}\": vehicle models "
                    + "have to be prefabs. That level keeps the scale it already had.");
                return false;
            }

            GameObject contents = PrefabUtility.LoadPrefabContents(prefabPath);
            if (contents == null)
            {
                Debug.LogWarning(
                    $"{nameof(VehicleDefinitionBuilder)} could not open {prefabPath}, so that level keeps "
                    + "the scale it already had.");
                return false;
            }

            try
            {
                contents.transform.localScale = Vector3.one;
                if (!TryMeasureHeight(contents, out height) || height <= Mathf.Epsilon)
                {
                    Debug.LogWarning(
                        $"{nameof(VehicleDefinitionBuilder)} measured no height on {prefabPath}, so that "
                        + "level keeps the scale it already had.");
                    return false;
                }

                return true;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        /// <summary>
        /// Union of every mesh renderer's world bounds under an object. Particle and trail
        /// renderers are left out: their bounds are whatever they last emitted, which is a
        /// property of a simulation rather than of the model's size.
        /// </summary>
        private static bool TryMeasureHeight(GameObject root, out float height)
        {
            height = 0f;
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            bool any = false;
            Bounds bounds = new Bounds();

            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer is ParticleSystemRenderer || renderer is TrailRenderer || renderer is LineRenderer)
                {
                    continue;
                }

                if (!any)
                {
                    bounds = renderer.bounds;
                    any = true;
                    continue;
                }

                bounds.Encapsulate(renderer.bounds);
            }

            if (!any)
            {
                return false;
            }

            height = bounds.size.y;
            return true;
        }

        private static VehicleDefinition.Level Level(string displayName, float damageMultiplier, string modelPrefabName)
        {
            return new VehicleDefinition.Level
            {
                displayName = displayName,
                damageMultiplier = damageMultiplier,
                modelPrefab = LoadPackModel(modelPrefabName),
            };
        }

        private static GameObject LoadPackModel(string prefabName)
        {
            string path = $"{PackFolder}/{prefabName}.prefab";
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (model == null)
            {
                Debug.LogWarning(
                    $"{nameof(VehicleDefinitionBuilder)} found no model at {path}. The level will be written "
                    + "without one and the cannon will show the level below it, or the fallback tank.");
            }

            return model;
        }

        /// <summary>
        /// Writes a definition, filling only what is not already there. By the time this is
        /// re-run somebody has retuned a multiplier or swapped a model, and rewriting those is
        /// the one thing a scaffolding tool must never do.
        /// </summary>
        private static VehicleDefinition CreateVehicle(
            string id,
            string displayName,
            string description,
            VehicleDefinition.Level[] levels)
        {
            string path = $"{ConfigFolder}/{id}.asset";
            VehicleDefinition vehicle = AssetDatabase.LoadAssetAtPath<VehicleDefinition>(path);
            if (vehicle == null)
            {
                vehicle = ScriptableObject.CreateInstance<VehicleDefinition>();
                AssetDatabase.CreateAsset(vehicle, path);
                Debug.Log($"{nameof(VehicleDefinitionBuilder)} created {path}.");
            }

            SerializedObject serialized = new SerializedObject(vehicle);
            SetIfEmpty(serialized.FindProperty("id"), id);
            SetIfEmpty(serialized.FindProperty("displayName"), displayName);
            SetIfEmpty(serialized.FindProperty("description"), description);

            SerializedProperty levelsProperty = serialized.FindProperty("levels");

            // Grown, never shrunk. A fourth level somebody added by hand is theirs to keep, and
            // dropping it would silently un-buy whatever a save had already reached.
            if (levelsProperty.arraySize < levels.Length)
            {
                levelsProperty.arraySize = levels.Length;
            }

            for (int i = 0; i < levels.Length; i++)
            {
                SerializedProperty level = levelsProperty.GetArrayElementAtIndex(i);
                SetIfEmpty(level.FindPropertyRelative("displayName"), levels[i].displayName);
                SetIfEmpty(level.FindPropertyRelative("modelPrefab"), levels[i].modelPrefab);

                // Zero counts as unset here. A vehicle authored to multiply by nothing would be
                // one that disarms the bullet, which no shop copy can explain, so treating it as
                // an untouched slot costs nothing and fills in a freshly grown array element.
                SerializedProperty multiplier = level.FindPropertyRelative("damageMultiplier");
                if (multiplier != null && multiplier.floatValue <= 0f)
                {
                    multiplier.floatValue = levels[i].damageMultiplier;
                }
            }

            // Dirtied only when a property actually moved, so a second run leaves every asset's
            // file untouched rather than re-saving an identical one.
            if (serialized.ApplyModifiedPropertiesWithoutUndo())
            {
                EditorUtility.SetDirty(vehicle);
            }

            return vehicle;
        }

        private static void CreateLoadout(params VehicleDefinition[] vehicles)
        {
            string path = $"{ConfigFolder}/VehicleLoadout.asset";
            VehicleLoadout loadout = AssetDatabase.LoadAssetAtPath<VehicleLoadout>(path);
            if (loadout == null)
            {
                loadout = ScriptableObject.CreateInstance<VehicleLoadout>();
                AssetDatabase.CreateAsset(loadout, path);
                Debug.Log($"{nameof(VehicleDefinitionBuilder)} created {path}.");
            }

            SerializedObject serialized = new SerializedObject(loadout);
            SerializedProperty list = serialized.FindProperty("vehicles");

            // Rewritten when the list is empty and when it still names a vehicle this pass is
            // about to delete. A list holding the retired ones is not a hand-tuned catalogue, it
            // is the old one, and leaving it would fill the garage with rows that load nothing.
            if (list.arraySize == 0 || NamesRetiredVehicle(list))
            {
                list.arraySize = vehicles.Length;
                for (int i = 0; i < vehicles.Length; i++)
                {
                    list.GetArrayElementAtIndex(i).objectReferenceValue = vehicles[i];
                }
            }

            // Cannon A starts owned and mounted; everything else is something to earn.
            SerializedProperty defaultVehicle = serialized.FindProperty("defaultVehicle");
            if (defaultVehicle.objectReferenceValue == null || IsRetired(defaultVehicle.objectReferenceValue))
            {
                defaultVehicle.objectReferenceValue = vehicles.Length > 0 ? vehicles[0] : null;
            }

            if (serialized.ApplyModifiedPropertiesWithoutUndo())
            {
                EditorUtility.SetDirty(loadout);
            }
        }

        private static bool NamesRetiredVehicle(SerializedProperty list)
        {
            for (int i = 0; i < list.arraySize; i++)
            {
                if (IsRetired(list.GetArrayElementAtIndex(i).objectReferenceValue))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// True for the old truck and tank, and for a slot that has gone missing - a reference to
        /// an asset that no longer exists reads as null, and both mean the same thing here: the
        /// list is stale and should be rewritten rather than preserved.
        /// </summary>
        private static bool IsRetired(Object asset)
        {
            if (asset == null)
            {
                return true;
            }

            string path = AssetDatabase.GetAssetPath(asset);
            for (int i = 0; i < RetiredVehicleIds.Length; i++)
            {
                if (path == $"{ConfigFolder}/{RetiredVehicleIds[i]}.asset")
                {
                    return true;
                }
            }

            return false;
        }

        private static void DeleteRetiredVehicles()
        {
            for (int i = 0; i < RetiredVehicleIds.Length; i++)
            {
                string path = $"{ConfigFolder}/{RetiredVehicleIds[i]}.asset";
                if (AssetDatabase.LoadAssetAtPath<VehicleDefinition>(path) == null)
                {
                    continue;
                }

                if (AssetDatabase.DeleteAsset(path))
                {
                    Debug.Log($"{nameof(VehicleDefinitionBuilder)} deleted the retired {path}.");
                }
            }
        }

        /// <summary>Depth-first by name, inactive included: the fallback model may be switched off.</summary>
        /// <summary>
        /// Resolves what the aim turns, rebuilding it if the old cannon art has been deleted.
        ///
        /// CannonA carried three jobs at once: a barrel you could see, the transform the aim
        /// rotated, and the parent of MuzzlePoint. Deleting it to be rid of the first took the
        /// other two with it, which leaves CannonAimController with no pivot and the fire
        /// controller with no origin - and a run cannot start without either. Rebuilding them as
        /// empties keeps the art gone and the wiring intact.
        ///
        /// CannonA is still preferred when it exists, so a project that has not deleted it keeps
        /// aiming exactly the object it always did rather than quietly gaining a second pivot.
        /// </summary>
        /// <returns>True when the prefab changed and needs saving.</returns>
        private static bool EnsureAimPivot(Transform cannon, out Transform pivot, out Transform muzzle)
        {
            bool changed = false;

            Transform legacyBarrel = FindDescendant(cannon, BarrelObjectName);
            pivot = legacyBarrel;
            if (pivot == null)
            {
                pivot = FindDescendant(cannon, AimPivotName);
                if (pivot == null)
                {
                    pivot = new GameObject(AimPivotName).transform;
                    pivot.SetParent(cannon, false);

                    // Only on creation. The pose is CannonA's, recorded rather than copied because
                    // the object it belongs to is gone; writing it every run would overwrite a
                    // deliberate re-aim by whoever tunes the cannon next.
                    pivot.localPosition = AimPivotLocalPosition;
                    pivot.localRotation = AimPivotLocalRotation;
                    pivot.localScale = Vector3.one;
                    changed = true;

                    Debug.Log(
                        $"{nameof(VehicleDefinitionBuilder)} rebuilt \"{AimPivotName}\" inside "
                        + $"{SlingshotPrefabPath}: \"{BarrelObjectName}\" is gone, and the aim and the "
                        + "muzzle both hung off it.");
                }
            }

            muzzle = FindDescendant(pivot, MuzzleObjectName);
            if (muzzle == null)
            {
                muzzle = new GameObject(MuzzleObjectName).transform;
                muzzle.SetParent(pivot, false);
                muzzle.localPosition = MuzzleLocalPosition;
                muzzle.localRotation = Quaternion.identity;
                muzzle.localScale = Vector3.one;
                changed = true;
            }

            return changed;
        }

        /// <summary>
        /// Ensures the live frame the model mounts in, seeded where the aim pivot sits.
        ///
        /// The pose is written on creation only. It used to be re-copied every run, on the
        /// reasoning that the pivot is the yardstick the cannon is laid out around and the mount
        /// should follow it - but that only holds while nobody touches the mount. Once the model's
        /// placement is tuned by hand, re-copying silently drags it back onto the pivot on the
        /// next run, and a builder that undoes deliberate work is worse than one that leaves a
        /// stale pose a human can see and move. Same rule as every other reference here: fill what
        /// is missing, never overwrite what is set.
        /// </summary>
        /// <returns>True when the prefab changed and needs saving.</returns>
        private static bool EnsureCannonRoot(Transform cannon, Transform barrel, out Transform cannonRoot)
        {
            cannonRoot = FindDescendant(cannon, CannonRootObjectName);
            if (cannonRoot != null)
            {
                return false;
            }

            cannonRoot = new GameObject(CannonRootObjectName).transform;
            cannonRoot.SetParent(cannon, false);

            if (barrel != null)
            {
                cannonRoot.localPosition = barrel.localPosition;
                cannonRoot.localRotation = barrel.localRotation;
            }

            // Without the pivot the frame still exists, so the mount has somewhere to put a model;
            // it just starts at identity under Cannon, which is where it sat before any of this.
            cannonRoot.localScale = Vector3.one;
            return true;
        }

        /// <summary>
        /// Ensures the live frame the muzzle flash hangs in: CannonA's position, and its rotation
        /// mirrored every frame so the flash aims with the barrel. A sibling of CannonA rather
        /// than a child of the static CannonRoot, because sharing a parent is what lets the
        /// mirror copy the rotation as it stands instead of rebasing it between two frames.
        /// </summary>
        /// <returns>True when the prefab changed and needs saving.</returns>
        private static bool EnsureMuzzleFlashRoot(Transform cannon, Transform barrel, out Transform muzzleFlashRoot)
        {
            muzzleFlashRoot = FindDescendant(cannon, MuzzleFlashRootName);
            bool changed = false;
            if (muzzleFlashRoot == null)
            {
                muzzleFlashRoot = new GameObject(MuzzleFlashRootName).transform;
                muzzleFlashRoot.SetParent(cannon, false);
                changed = true;
            }

            CannonAimMirror mirror = muzzleFlashRoot.GetComponent<CannonAimMirror>();
            if (mirror == null)
            {
                mirror = muzzleFlashRoot.gameObject.AddComponent<CannonAimMirror>();
                changed = true;
            }

            SerializedObject serializedMirror = new SerializedObject(mirror);
            SetIfEmpty(serializedMirror, "source", barrel);
            changed |= serializedMirror.ApplyModifiedPropertiesWithoutUndo();

            if (barrel == null)
            {
                return changed;
            }

            // Position only - the rotation is the mirror's job at runtime, and writing a rest
            // rotation here as well would be a value that is overwritten on the first frame and
            // so only ever misleads whoever reads the prefab.
            if (muzzleFlashRoot.localPosition != barrel.localPosition || muzzleFlashRoot.localScale != Vector3.one)
            {
                muzzleFlashRoot.localPosition = barrel.localPosition;
                muzzleFlashRoot.localScale = Vector3.one;
                changed = true;
            }

            return changed;
        }

        /// <summary>
        /// Moves the muzzle flash out to the aiming frame and onto the muzzle. It was authored
        /// against the old tank's barrel tip, roughly three quarters of a unit behind where shots
        /// actually leave, and the obvious anchor - MuzzlePoint - is inside the switched-off
        /// CannonA, where a particle would never render. MuzzleFlashRoot stands where CannonA
        /// stands, so the muzzle's own local offset reused here lands on the same point in the
        /// world - and because that frame mirrors the aim, the flash swings with the barrel.
        /// </summary>
        /// <returns>True when the prefab changed and needs saving.</returns>
        private static bool MoveSmokeToMuzzle(Transform root, Transform muzzleFlashRoot, Transform muzzle)
        {
            if (muzzleFlashRoot == null || muzzle == null)
            {
                return false;
            }

            Transform smoke = FindDescendant(root, SmokeObjectName);
            if (smoke == null)
            {
                Debug.LogWarning(
                    $"{nameof(VehicleDefinitionBuilder)} found no \"{SmokeObjectName}\" inside "
                    + $"{SlingshotPrefabPath}, so the muzzle flash was left where it was.");
                return false;
            }

            bool changed = false;
            if (smoke.parent != muzzleFlashRoot)
            {
                // worldPositionStays: false - the pose is set outright below, and letting Unity
                // preserve the world pose here would only bake the old tank-tip offset into new
                // local numbers.
                smoke.SetParent(muzzleFlashRoot, false);
                changed = true;
            }

            // Identity, not the muzzle's rotation: the flash should fire along the barrel, and
            // the frame it now sits in is aimed along the barrel already. The old value was the
            // tank turret's tilt, which no longer means anything now the tank is not what aims.
            if (smoke.localPosition != muzzle.localPosition || smoke.localRotation != Quaternion.identity)
            {
                smoke.localPosition = muzzle.localPosition;
                smoke.localRotation = Quaternion.identity;
                changed = true;
            }

            return changed;
        }

        /// <summary>
        /// Points the mount at CannonRoot. Deliberately not a SetIfEmpty: every project built
        /// before this change already has mountPoint set to Cannon itself, so filling only an
        /// empty slot would leave exactly the projects that need the fix without it. Only that
        /// one known-stale value is replaced - a reference someone aimed somewhere else by hand
        /// is left alone.
        /// </summary>
        /// <returns>True when the reference moved.</returns>
        private static bool RepointMountPoint(SerializedObject serializedMount, Transform cannon, Transform cannonRoot)
        {
            if (cannonRoot == null)
            {
                return false;
            }

            SerializedProperty property = serializedMount.FindProperty("mountPoint");
            if (property == null)
            {
                return false;
            }

            Object current = property.objectReferenceValue;
            if (current == cannonRoot)
            {
                return false;
            }

            if (current != null && current != (Object)cannon)
            {
                return false;
            }

            property.objectReferenceValue = cannonRoot;
            return true;
        }

        private static Transform FindDescendant(Transform root, string childName)
        {
            if (root.name == childName)
            {
                return root;
            }

            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindDescendant(root.GetChild(i), childName);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static void SetIfEmpty(SerializedProperty property, string value)
        {
            if (property != null && string.IsNullOrEmpty(property.stringValue))
            {
                property.stringValue = value;
            }
        }

        private static void SetIfEmpty(SerializedProperty property, Object value)
        {
            if (property != null && property.objectReferenceValue == null && value != null)
            {
                property.objectReferenceValue = value;
            }
        }

        private static void SetIfEmpty(SerializedObject serialized, string propertyName, Object value)
        {
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property == null)
            {
                Debug.LogWarning(
                    $"{nameof(VehicleDefinitionBuilder)} found no field \"{propertyName}\" on "
                    + $"{serialized.targetObject.GetType().Name}.");
                return;
            }

            SetIfEmpty(property, value);
        }

        private static void EnsureFolder(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath))
            {
                return;
            }

            string parent = Path.GetDirectoryName(folderPath)?.Replace('\\', '/');
            string leaf = Path.GetFileName(folderPath);
            if (!string.IsNullOrEmpty(parent) && !string.IsNullOrEmpty(leaf))
            {
                EnsureFolder(parent);
                AssetDatabase.CreateFolder(parent, leaf);
            }
        }
    }
}
#endif
