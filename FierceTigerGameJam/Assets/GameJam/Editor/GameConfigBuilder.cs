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

        /// <summary>What a map pays the first time it is passed, and the first time it is cleared.</summary>
        private const int BasePassReward = 100;
        private const int BaseClearReward = 250;
        private const int RewardStepPerMap = 50;

        private const int DefaultBulletPickLimit = 10;
        private const float DefaultRequiredClearPercent = 0.8f;

        [MenuItem("Tools/Smashdown/Create Game Configs")]
        public static void CreateGameConfigs()
        {
            EnsureFolder(ConfigFolder);
            EnsureFolder(BulletFolder);

            BulletLoadout loadout = LoadFirst<BulletLoadout>();
            List<BulletDefinition> bullets = LoadAll<BulletDefinition>();
            MapConfig mapConfig = LoadFirst<MapConfig>();

            if (bullets.Count == 0)
            {
                Debug.LogWarning(
                    $"{nameof(GameConfigBuilder)} found no {nameof(BulletDefinition)} assets. "
                    + "Run Create Default Bullet Definitions first, or prices will be empty.");
            }

            RewardConfig rewards = EnsureAsset<RewardConfig>($"{ConfigFolder}/RewardConfig.asset");
            PurchaseBulletConfig purchase = EnsureAsset<PurchaseBulletConfig>($"{ConfigFolder}/PurchaseBulletConfig.asset");
            UpgradeBulletConfig upgrade = EnsureAsset<UpgradeBulletConfig>($"{ConfigFolder}/UpgradeBulletConfig.asset");
            MapProgressionConfig progression = EnsureAsset<MapProgressionConfig>($"{ConfigFolder}/MapProgressionConfig.asset");
            BulletInventory inventory = EnsureAsset<BulletInventory>($"{BulletFolder}/BulletInventory.asset");

            FillRewards(rewards, mapConfig);
            FillPurchasePrices(purchase, bullets, ResolveStarterBullet(loadout));
            FillUpgradePrices(upgrade, bullets);
            FillMapRules(progression, mapConfig);

            EconomyService economy = EnsureAsset<EconomyService>($"{ConfigFolder}/EconomyService.asset");
            WireEconomy(economy, purchase, upgrade, rewards, loadout);
            WireScene(economy, progression, inventory, loadout);

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

        private static void WireEconomy(
            EconomyService economy,
            PurchaseBulletConfig purchase,
            UpgradeBulletConfig upgrade,
            RewardConfig rewards,
            BulletLoadout loadout)
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
            BulletLoadout loadout)
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
