#if UNITY_EDITOR
using System.IO;
using GameJam.Gameplay.Cannon;
using GameJam.Gameplay.Combat;
using UnityEditor;
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

        private const string SlingshotPrefabPath =
            "Assets/GameJam/Imported/LunaSmashdown/Prefabs/SmashFest/Slingshot.prefab";

        /// <summary>The aim target that carries the barrel's Animator; the model mounts here.</summary>
        private const string CannonObjectName = "Cannon";

        /// <summary>The model the cannon wore before the pack, kept as the mount's fallback.</summary>
        private const string FallbackModelName = "CannonTank_Default_Red";

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
