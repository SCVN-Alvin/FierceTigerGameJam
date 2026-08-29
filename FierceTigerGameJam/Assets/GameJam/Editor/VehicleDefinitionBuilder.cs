#if UNITY_EDITOR
using System.IO;
using GameJam.Gameplay.Cannon;
using GameJam.Gameplay.Combat;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

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

                SerializedObject serializedMount = new SerializedObject(mount);
                SetIfEmpty(serializedMount, "loadout", loadout);

                // The cannon itself, not a child of it: this is the object the aim rotates, so a
                // model parented here aims without anything driving it.
                SetIfEmpty(serializedMount, "mountPoint", cannon);
                SetIfEmpty(serializedMount, "fallbackModel", fallback != null ? fallback.gameObject : null);

                // The old barrel is what the aim rotates, and it is switched off, so nothing on
                // screen moves with it. Handing it to the mount is what lets the vehicle's own
                // barrel copy the aim without the aim controller ever hearing about vehicles.
                Transform barrel = FindDescendant(root.transform, BarrelObjectName);
                if (barrel == null)
                {
                    Debug.LogWarning(
                        $"{nameof(VehicleDefinitionBuilder)} found no \"{BarrelObjectName}\" inside "
                        + $"{SlingshotPrefabPath}, so the mounted model's barrel will not follow the aim.");
                }

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
