#if UNITY_EDITOR
using System.IO;
using GameJam.Gameplay.Combat;
using UnityEditor;
using UnityEngine;

namespace GameJam.EditorTools
{
    /// <summary>
    /// Creates the starting vehicles: a truck the player owns from the first run, and a tank they
    /// have to earn. Both are three levels of pure damage multiplier with no models attached, so
    /// the progression is playable before any art exists and the models can be dropped into the
    /// level slots as they arrive.
    ///
    /// The numbers are a playable starting point rather than a balance pass. The one shape worth
    /// keeping is that a fully upgraded truck (1.40) beats a fresh tank (1.30): what the tank
    /// sells is its ceiling, not an instant win, so upgrading the starter is never wasted gold.
    /// </summary>
    public static class VehicleDefinitionBuilder
    {
        private const string ConfigFolder = "Assets/GameJam/Config/Vehicles";

        [MenuItem("Tools/Smashdown/Create Default Vehicle Definitions")]
        public static void CreateDefaults()
        {
            EnsureFolder(ConfigFolder);

            VehicleDefinition truck = CreateVehicle(
                "vehicle_truck",
                "Truck",
                "The one you start with. Cheap to improve, and improving it is never wasted.",
                new[]
                {
                    Level("Truck I", 1.00f),
                    Level("Truck II", 1.20f),
                    Level("Truck III", 1.40f),
                });

            VehicleDefinition tank = CreateVehicle(
                "vehicle_tank",
                "Tank",
                "Starts harder than a truck ever finishes, and goes twice as far.",
                new[]
                {
                    Level("Tank I", 1.30f),
                    Level("Tank II", 1.60f),
                    Level("Tank III", 2.00f),
                });

            CreateLoadout(truck, tank);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"Created default vehicle definitions in {ConfigFolder}.");
        }

        private static VehicleDefinition.Level Level(string displayName, float damageMultiplier)
        {
            return new VehicleDefinition.Level
            {
                displayName = displayName,
                damageMultiplier = damageMultiplier,
            };
        }

        /// <summary>
        /// Writes a definition, but only when there is not one already. An asset that exists is
        /// handed back untouched: by the time this is re-run somebody has dropped models into the
        /// level slots and retuned the multipliers, and rewriting them is the one thing a
        /// scaffolding tool must never do.
        /// </summary>
        private static VehicleDefinition CreateVehicle(
            string id,
            string displayName,
            string description,
            VehicleDefinition.Level[] levels)
        {
            string path = $"{ConfigFolder}/{id}.asset";
            VehicleDefinition existing = AssetDatabase.LoadAssetAtPath<VehicleDefinition>(path);
            if (existing != null)
            {
                return existing;
            }

            VehicleDefinition vehicle = ScriptableObject.CreateInstance<VehicleDefinition>();
            SerializedObject serialized = new SerializedObject(vehicle);
            serialized.FindProperty("id").stringValue = id;
            serialized.FindProperty("displayName").stringValue = displayName;
            serialized.FindProperty("description").stringValue = description;

            SerializedProperty levelsProperty = serialized.FindProperty("levels");
            levelsProperty.arraySize = levels.Length;
            for (int i = 0; i < levels.Length; i++)
            {
                SerializedProperty level = levelsProperty.GetArrayElementAtIndex(i);
                level.FindPropertyRelative("displayName").stringValue = levels[i].displayName;
                level.FindPropertyRelative("damageMultiplier").floatValue = levels[i].damageMultiplier;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();

            AssetDatabase.CreateAsset(vehicle, path);
            Debug.Log($"{nameof(VehicleDefinitionBuilder)} created {path}.");
            return vehicle;
        }

        private static void CreateLoadout(params VehicleDefinition[] vehicles)
        {
            string path = $"{ConfigFolder}/VehicleLoadout.asset";
            if (AssetDatabase.LoadAssetAtPath<VehicleLoadout>(path) != null)
            {
                return;
            }

            VehicleLoadout loadout = ScriptableObject.CreateInstance<VehicleLoadout>();
            SerializedObject serialized = new SerializedObject(loadout);
            SerializedProperty list = serialized.FindProperty("vehicles");
            list.arraySize = vehicles.Length;
            for (int i = 0; i < vehicles.Length; i++)
            {
                list.GetArrayElementAtIndex(i).objectReferenceValue = vehicles[i];
            }

            // The truck starts owned and mounted; everything else is something to earn.
            serialized.FindProperty("defaultVehicle").objectReferenceValue = vehicles.Length > 0 ? vehicles[0] : null;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            AssetDatabase.CreateAsset(loadout, path);
            Debug.Log($"{nameof(VehicleDefinitionBuilder)} created {path}.");
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
