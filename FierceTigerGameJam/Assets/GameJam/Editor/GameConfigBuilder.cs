#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using GameJam.Config;
using GameJam.Economy;
using GameJam.Gameplay.Cannon;
using GameJam.Gameplay.Combat;
using GameJam.Gameplay.Flow;
using GameJam.Gameplay.Wall;
using UnityEditor;
using UnityEngine;

namespace GameJam.EditorTools
{
    /// <summary>
    /// Creates the configuration assets the economy and the level flow need, fills them with a
    /// playable starting point, and points everything at everything else.
    ///
    /// It is driven by what is actually in the project rather than by a hardcoded list: prices
    /// come from the ammunition assets that exist, and map rules from the maps the map config
    /// lists. A bullet or a map added later is picked up by re-running this rather than by
    /// editing it.
    ///
    /// Nothing already set is overwritten. An asset that exists keeps its values, and a reference
    /// that is already pointed somewhere is left alone, so a tuning pass survives the next run.
    /// </summary>
    public static class GameConfigBuilder
    {
        private const string ConfigFolder = "Assets/GameJam/Config";
        private const string BulletFolder = "Assets/GameJam/Config/Bullets";
        private const string VehicleFolder = "Assets/GameJam/Config/Vehicles";

        /// <summary>What a map pays the first time it is passed, and the first time it is cleared.</summary>
        private const int BasePassReward = 100;
        private const int BaseClearReward = 250;
        private const int RewardStepPerMap = 50;

        /// <summary>
        /// What a vehicle costs to unlock, and what each level past the first costs. The starter
        /// is deliberately cheaper to level than the tank is to buy, so the first few maps have
        /// something to spend on rather than one purchase the player saves at for an hour.
        /// </summary>
        private const int VehicleUnlockPrice = 800;

        private static readonly int[] StarterVehicleUpgradePrices = { 300, 700 };
        private static readonly int[] VehicleUpgradePrices = { 1200, 2000 };

        private const int DefaultBulletPickLimit = 10;
        private const float DefaultRequiredClearPercent = 0.8f;

        [MenuItem("Tools/Smashdown/Create Game Configs")]
        public static void CreateGameConfigs()
        {
            EnsureFolder(ConfigFolder);
            EnsureFolder(BulletFolder);
            EnsureFolder(VehicleFolder);

            BulletLoadout loadout = LoadFirst<BulletLoadout>();
            List<BulletDefinition> bullets = LoadAll<BulletDefinition>();
            VehicleLoadout vehicleLoadout = LoadFirst<VehicleLoadout>();
            List<VehicleDefinition> vehicles = LoadAll<VehicleDefinition>();
            MapConfig mapConfig = LoadFirst<MapConfig>();

            if (bullets.Count == 0)
            {
                Debug.LogWarning(
                    $"{nameof(GameConfigBuilder)} found no {nameof(BulletDefinition)} assets. "
                    + "Run Create Default Bullet Definitions first, or prices will be empty.");
            }

            if (vehicles.Count == 0)
            {
                Debug.LogWarning(
                    $"{nameof(GameConfigBuilder)} found no {nameof(VehicleDefinition)} assets. "
                    + "Run Create Default Vehicle Definitions first, or the vehicle prices will be empty.");
            }

            RewardConfig rewards = EnsureAsset<RewardConfig>($"{ConfigFolder}/RewardConfig.asset");
            PurchaseBulletConfig purchase = EnsureAsset<PurchaseBulletConfig>($"{ConfigFolder}/PurchaseBulletConfig.asset");
            UpgradeBulletConfig upgrade = EnsureAsset<UpgradeBulletConfig>($"{ConfigFolder}/UpgradeBulletConfig.asset");
            PurchaseVehicleConfig purchaseVehicle = EnsureAsset<PurchaseVehicleConfig>($"{ConfigFolder}/PurchaseVehicleConfig.asset");
            UpgradeVehicleConfig upgradeVehicle = EnsureAsset<UpgradeVehicleConfig>($"{ConfigFolder}/UpgradeVehicleConfig.asset");
            MapProgressionConfig progression = EnsureAsset<MapProgressionConfig>($"{ConfigFolder}/MapProgressionConfig.asset");
            BulletInventory inventory = EnsureAsset<BulletInventory>($"{BulletFolder}/BulletInventory.asset");

            FillRewards(rewards, mapConfig);
            FillPurchasePrices(purchase, bullets, ResolveStarterBullet(loadout));
            FillUpgradePrices(upgrade, bullets);
            FillVehiclePurchasePrices(purchaseVehicle, vehicles, ResolveStarterVehicle(vehicleLoadout));
            FillVehicleUpgradePrices(upgradeVehicle, vehicles, ResolveStarterVehicle(vehicleLoadout));
            FillMapRules(progression, mapConfig);

            EconomyService economy = EnsureAsset<EconomyService>($"{ConfigFolder}/EconomyService.asset");
            WireEconomy(economy, purchase, upgrade, rewards, loadout, purchaseVehicle, upgradeVehicle, vehicleLoadout);
            WireScene(economy, progression, inventory, loadout, vehicleLoadout);

            // After the economy asset exists, because this wires itself into it. Its own method
            // rather than another argument on WireEconomy: the fail-screen builder needs exactly
            // this one config and none of the rest of this pass, and two callers must not end up
            // with two copies of the path.
            EnsureLoseConfig();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(
                $"{nameof(GameConfigBuilder)} created and wired the game configs. "
                + "Values are a playable starting point, not a balance pass.");
        }

        /// <summary>
        /// One pass reward and one clear reward per map, so maps can be retuned independently
        /// without every other map moving with them.
        /// </summary>
        private static void FillRewards(RewardConfig rewards, MapConfig mapConfig)
        {
            if (rewards == null || !IsEmptyArray(rewards, "entries"))
            {
                return;
            }

            SerializedObject serialized = new SerializedObject(rewards);
            SerializedProperty entries = serialized.FindProperty("entries");
            List<string> mapIds = ResolveMapIds(mapConfig);

            entries.arraySize = mapIds.Count * 2;
            for (int i = 0; i < mapIds.Count; i++)
            {
                int step = i * RewardStepPerMap;

                SerializedProperty pass = entries.GetArrayElementAtIndex(i * 2);
                pass.FindPropertyRelative("rewardId").stringValue = PassRewardId(mapIds[i]);
                pass.FindPropertyRelative("gold").intValue = BasePassReward + step;

                SerializedProperty clear = entries.GetArrayElementAtIndex((i * 2) + 1);
                clear.FindPropertyRelative("rewardId").stringValue = ClearRewardId(mapIds[i]);
                clear.FindPropertyRelative("gold").intValue = BaseClearReward + (step * 2);
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(rewards);
        }

        /// <summary>
        /// Everything except the starter is for sale. The starter is deliberately absent rather
        /// than priced at zero: not being listed means not for sale, and the player already owns
        /// it, so offering it for nothing would be a button that does nothing.
        /// </summary>
        private static void FillPurchasePrices(
            PurchaseBulletConfig purchase,
            List<BulletDefinition> bullets,
            BulletDefinition starter)
        {
            if (purchase == null || !IsEmptyArray(purchase, "entries"))
            {
                return;
            }

            List<BulletDefinition> forSale = new List<BulletDefinition>();
            for (int i = 0; i < bullets.Count; i++)
            {
                if (bullets[i] != starter)
                {
                    forSale.Add(bullets[i]);
                }
            }

            SerializedObject serialized = new SerializedObject(purchase);
            SerializedProperty entries = serialized.FindProperty("entries");
            entries.arraySize = forSale.Count;

            for (int i = 0; i < forSale.Count; i++)
            {
                SerializedProperty entry = entries.GetArrayElementAtIndex(i);
                entry.FindPropertyRelative("bulletId").stringValue = forSale[i].Id;

                // Priced against what the maps can actually pay. Map rewards are granted once
                // ever, so the gold in the game is capped by how many maps exist: everything on
                // sale together has to cost less than every map pays, or the shop contains items
                // no amount of play can reach.
                entry.FindPropertyRelative("goldPrice").intValue = 600 + (i * 400);
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(purchase);
        }

        /// <summary>
        /// A price for every level the ammunition actually defines beyond the first. Level one is
        /// where a bullet starts, so it is never priced.
        /// </summary>
        private static void FillUpgradePrices(UpgradeBulletConfig upgrade, List<BulletDefinition> bullets)
        {
            if (upgrade == null || !IsEmptyArray(upgrade, "entries"))
            {
                return;
            }

            SerializedObject serialized = new SerializedObject(upgrade);
            SerializedProperty entries = serialized.FindProperty("entries");
            entries.arraySize = bullets.Count;

            for (int i = 0; i < bullets.Count; i++)
            {
                SerializedProperty entry = entries.GetArrayElementAtIndex(i);
                entry.FindPropertyRelative("bulletId").stringValue = bullets[i].Id;

                SerializedProperty levels = entry.FindPropertyRelative("levels");
                int upgradeCount = Mathf.Max(0, bullets[i].LevelCount - 1);
                levels.arraySize = upgradeCount;

                for (int level = 0; level < upgradeCount; level++)
                {
                    SerializedProperty levelPrice = levels.GetArrayElementAtIndex(level);
                    levelPrice.FindPropertyRelative("targetLevel").intValue = level + 2;
                    levelPrice.FindPropertyRelative("goldPrice").intValue = 200 * (level + 1) * (i + 1);
                }
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(upgrade);
        }

        /// <summary>
        /// Every vehicle is listed, the starter at nothing. Unlike the bullet table, which leaves
        /// the starter out entirely, a free row here is harmless and says what it means: the
        /// starter is always owned, so the shop never offers the row anyway, and a reader of the
        /// asset can see there is no hidden price on it.
        /// </summary>
        private static void FillVehiclePurchasePrices(
            PurchaseVehicleConfig purchase,
            List<VehicleDefinition> vehicles,
            VehicleDefinition starter)
        {
            if (purchase == null || !IsEmptyArray(purchase, "entries"))
            {
                return;
            }

            SerializedObject serialized = new SerializedObject(purchase);
            SerializedProperty entries = serialized.FindProperty("entries");
            entries.arraySize = vehicles.Count;

            for (int i = 0; i < vehicles.Count; i++)
            {
                SerializedProperty entry = entries.GetArrayElementAtIndex(i);
                entry.FindPropertyRelative("vehicleId").stringValue = vehicles[i].Id;
                entry.FindPropertyRelative("goldPrice").intValue = vehicles[i] == starter ? 0 : VehicleUnlockPrice;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(purchase);
        }

        /// <summary>
        /// A price for every level the vehicle actually defines beyond the first, so a vehicle
        /// authored with two levels never gets a third one priced that it could not deliver. The
        /// starter is on its own cheaper ladder: it is what the player can afford to improve
        /// before they can afford to replace it.
        /// </summary>
        private static void FillVehicleUpgradePrices(
            UpgradeVehicleConfig upgrade,
            List<VehicleDefinition> vehicles,
            VehicleDefinition starter)
        {
            if (upgrade == null || !IsEmptyArray(upgrade, "entries"))
            {
                return;
            }

            SerializedObject serialized = new SerializedObject(upgrade);
            SerializedProperty entries = serialized.FindProperty("entries");
            entries.arraySize = vehicles.Count;

            for (int i = 0; i < vehicles.Count; i++)
            {
                SerializedProperty entry = entries.GetArrayElementAtIndex(i);
                entry.FindPropertyRelative("vehicleId").stringValue = vehicles[i].Id;

                int[] prices = vehicles[i] == starter ? StarterVehicleUpgradePrices : VehicleUpgradePrices;

                SerializedProperty levels = entry.FindPropertyRelative("levels");
                int upgradeCount = Mathf.Clamp(vehicles[i].LevelCount - 1, 0, prices.Length);
                levels.arraySize = upgradeCount;

                for (int level = 0; level < upgradeCount; level++)
                {
                    SerializedProperty levelPrice = levels.GetArrayElementAtIndex(level);
                    levelPrice.FindPropertyRelative("targetLevel").intValue = level + 2;
                    levelPrice.FindPropertyRelative("goldPrice").intValue = prices[level];
                }
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(upgrade);
        }

        private static void FillMapRules(MapProgressionConfig progression, MapConfig mapConfig)
        {
            if (progression == null || !IsEmptyArray(progression, "entries"))
            {
                return;
            }

            List<string> mapIds = ResolveMapIds(mapConfig);

            SerializedObject serialized = new SerializedObject(progression);
            SerializedProperty entries = serialized.FindProperty("entries");
            entries.arraySize = mapIds.Count;

            for (int i = 0; i < mapIds.Count; i++)
            {
                SerializedProperty entry = entries.GetArrayElementAtIndex(i);
                entry.FindPropertyRelative("mapId").stringValue = mapIds[i];
                entry.FindPropertyRelative("requiredClearPercent").floatValue = DefaultRequiredClearPercent;
                entry.FindPropertyRelative("passMapRewardId").stringValue = PassRewardId(mapIds[i]);
                entry.FindPropertyRelative("clearMapRewardId").stringValue = ClearRewardId(mapIds[i]);
                entry.FindPropertyRelative("bulletPickLimit").intValue = DefaultBulletPickLimit;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(progression);
        }

        /// <summary>
        /// The price and the ammunition a continue buys, wired into the economy. Safe to call on
        /// its own: it creates the folder and the asset if they are missing, and never overwrites
        /// a price somebody has already tuned or a reference already pointed somewhere.
        /// </summary>
        internal static LoseConfig EnsureLoseConfig()
        {
            EnsureFolder(ConfigFolder);
            LoseConfig lose = EnsureAsset<LoseConfig>($"{ConfigFolder}/LoseConfig.asset");

            // The asset at the known path first, so a stray second EconomyService somewhere in the
            // project cannot be the one that gets wired.
            EconomyService economy = AssetDatabase.LoadAssetAtPath<EconomyService>($"{ConfigFolder}/EconomyService.asset");
            if (economy == null)
            {
                economy = LoadFirst<EconomyService>();
            }

            if (economy == null)
            {
                Debug.LogWarning(
                    $"{nameof(GameConfigBuilder)} created {nameof(LoseConfig)} but found no {nameof(EconomyService)} "
                    + "to wire it into, so a continue will have no price and nothing will be sold.");
                return lose;
            }

            SerializedObject serialized = new SerializedObject(economy);
            SetIfEmpty(serialized, "loseConfig", lose);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(economy);
            return lose;
        }

        private static void WireEconomy(
            EconomyService economy,
            PurchaseBulletConfig purchase,
            UpgradeBulletConfig upgrade,
            RewardConfig rewards,
            BulletLoadout loadout,
            PurchaseVehicleConfig purchaseVehicle,
            UpgradeVehicleConfig upgradeVehicle,
            VehicleLoadout vehicleLoadout)
        {
            if (economy == null)
            {
                return;
            }

            SerializedObject serialized = new SerializedObject(economy);
            SetIfEmpty(serialized, "purchaseConfig", purchase);
            SetIfEmpty(serialized, "upgradeConfig", upgrade);
            SetIfEmpty(serialized, "rewardConfig", rewards);
            SetIfEmpty(serialized, "loadout", loadout);
            SetIfEmpty(serialized, "purchaseVehicleConfig", purchaseVehicle);
            SetIfEmpty(serialized, "upgradeVehicleConfig", upgradeVehicle);
            SetIfEmpty(serialized, "vehicleLoadout", vehicleLoadout);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(economy);
        }

        /// <summary>
        /// Points the scene's run controller and cannon at the assets. Without this the configs
        /// exist but nothing reads them, which looks exactly like the configs being wrong.
        /// </summary>
        private static void WireScene(
            EconomyService economy,
            MapProgressionConfig progression,
            BulletInventory inventory,
            BulletLoadout loadout,
            VehicleLoadout vehicleLoadout)
        {
            LevelRunController run = Object.FindFirstObjectByType<LevelRunController>(FindObjectsInactive.Include);
            if (run != null)
            {
                SerializedObject serialized = new SerializedObject(run);
                SetIfEmpty(serialized, "mapSelection", LoadFirst<MapSelection>());
                SetIfEmpty(serialized, "progressionConfig", progression);
                SetIfEmpty(serialized, "bulletInventory", inventory);
                SetIfEmpty(serialized, "economy", economy);
                SetIfEmpty(serialized, "progressTracker",
                    Object.FindFirstObjectByType<LevelProgressTracker>(FindObjectsInactive.Include));
                SetIfEmpty(serialized, "fireController",
                    Object.FindFirstObjectByType<GridKnockdownCannonFireController>(FindObjectsInactive.Include));
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            else
            {
                Debug.LogWarning(
                    $"{nameof(GameConfigBuilder)} found no {nameof(LevelRunController)} in the scene, "
                    + "so the run is not wired to the configs.");
            }

            GridKnockdownCannonFireController fire =
                Object.FindFirstObjectByType<GridKnockdownCannonFireController>(FindObjectsInactive.Include);
            if (fire != null)
            {
                SerializedObject serialized = new SerializedObject(fire);
                SetIfEmpty(serialized, "bulletInventory", inventory);
                SetIfEmpty(serialized, "bulletLoadout", loadout);
                SetIfEmpty(serialized, "vehicleLoadout", vehicleLoadout);
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            // Every mount in the scene, not the first: the cannon has one and a shop preview rig
            // would have another, and a preview left unwired looks like the vehicle failing to
            // load rather than a reference nobody filled in.
            VehicleMount[] mounts = Object.FindObjectsByType<VehicleMount>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < mounts.Length; i++)
            {
                SerializedObject serialized = new SerializedObject(mounts[i]);
                SetIfEmpty(serialized, "loadout", vehicleLoadout);
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private static BulletDefinition ResolveStarterBullet(BulletLoadout loadout)
        {
            if (loadout == null)
            {
                return null;
            }

            SerializedObject serialized = new SerializedObject(loadout);
            SerializedProperty defaultBullet = serialized.FindProperty("defaultBullet");
            return defaultBullet?.objectReferenceValue as BulletDefinition;
        }

        private static VehicleDefinition ResolveStarterVehicle(VehicleLoadout loadout)
        {
            if (loadout == null)
            {
                return null;
            }

            SerializedObject serialized = new SerializedObject(loadout);
            SerializedProperty defaultVehicle = serialized.FindProperty("defaultVehicle");
            return defaultVehicle?.objectReferenceValue as VehicleDefinition;
        }

        private static List<string> ResolveMapIds(MapConfig mapConfig)
        {
            List<string> ids = new List<string>();
            if (mapConfig == null)
            {
                Debug.LogWarning($"{nameof(GameConfigBuilder)} found no {nameof(MapConfig)}, so no map rules were written.");
                return ids;
            }

            for (int i = 0; i < mapConfig.Count; i++)
            {
                MapInfo map = mapConfig.Get(i);
                if (map != null && !string.IsNullOrEmpty(map.Id))
                {
                    ids.Add(map.Id);
                }
            }

            return ids;
        }

        private static string PassRewardId(string mapId) => $"pass_map_{mapId}";

        private static string ClearRewardId(string mapId) => $"clear_map_{mapId}";

        /// <summary>
        /// True when the asset has no entries yet. Values are only written into an empty table, so
        /// a table someone has tuned is never quietly reset by re-running.
        /// </summary>
        private static bool IsEmptyArray(Object asset, string propertyName)
        {
            SerializedProperty property = new SerializedObject(asset).FindProperty(propertyName);
            return property != null && property.arraySize == 0;
        }

        private static void SetIfEmpty(SerializedObject serialized, string propertyName, Object value)
        {
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property == null)
            {
                Debug.LogWarning($"{nameof(GameConfigBuilder)} found no field \"{propertyName}\" on {serialized.targetObject.GetType().Name}.");
                return;
            }

            if (property.objectReferenceValue == null && value != null)
            {
                property.objectReferenceValue = value;
            }
        }

        private static T EnsureAsset<T>(string path) where T : ScriptableObject
        {
            T existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null)
            {
                return existing;
            }

            T created = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(created, path);
            Debug.Log($"{nameof(GameConfigBuilder)} created {path}.");
            return created;
        }

        private static T LoadFirst<T>() where T : Object
        {
            List<T> all = LoadAll<T>();
            return all.Count > 0 ? all[0] : null;
        }

        private static List<T> LoadAll<T>() where T : Object
        {
            List<T> results = new List<T>();
            string[] guids = AssetDatabase.FindAssets($"t:{typeof(T).Name}");
            for (int i = 0; i < guids.Length; i++)
            {
                T asset = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guids[i]));
                if (asset != null)
                {
                    results.Add(asset);
                }
            }

            return results;
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
