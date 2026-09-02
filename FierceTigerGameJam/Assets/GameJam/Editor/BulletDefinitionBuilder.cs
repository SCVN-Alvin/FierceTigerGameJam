#if UNITY_EDITOR
using System.IO;
using GameJam.Gameplay.Cannon;
using GameJam.Gameplay.Combat;
using UnityEditor;
using UnityEngine;

namespace GameJam.EditorTools
{
    /// <summary>
    /// Creates the starting ammunition: a rock that handles glass and brick but cannot touch
    /// concrete, and a cannon that opens concrete up.
    ///
    /// The numbers below are the balance pass, not a placeholder any more, so this menu item is
    /// what a rebuild has to reproduce. They are authored against measured block hit points -
    /// glass 1, brick_1x1 3, brick_2x1 5, concrete 6 - under three rules:
    ///
    /// - a tier one-shots what it is about (Cannon Ball I takes a 1x1 brick, Cannon Ball II
    ///   takes a 2x1, Rocket II takes concrete);
    /// - splash finishes glass neighbours, so every level's splashShare times its glass damage
    ///   clears glass's single hit point;
    /// - concrete stays flatly Rock-proof at 0. It is the unlock gate the campaign is built
    ///   around rather than a grind, and a vehicle multiplier cannot open it because zero times
    ///   anything is still zero.
    /// </summary>
    public static class BulletDefinitionBuilder
    {
        private const string ConfigFolder = "Assets/GameJam/Config/Bullets";

        // One table, so the pairing of an ammunition with the artist's ball is a single line to
        // read and a single line to change. The legacy CannonBall.prefab is deliberately absent:
        // it still runs the old CannonProjectile script and belongs to the demo scenes.
        private const string RockProjectilePath = "Assets/GameJam/Prefabs/Bullets/CannonBall_01.prefab";
        private const string CannonProjectilePath = "Assets/GameJam/Prefabs/Bullets/CannonBall_02.prefab";

        [MenuItem("Tools/Smashdown/Create Default Bullet Definitions")]
        public static void CreateDefaults()
        {
            EnsureFolder(ConfigFolder);

            // Block hit points, read off the prefabs rather than remembered: glass_1x1 1,
            // brick_1x1 3, brick_2x1 5, concrete_1x1 6. Damage is authored per material, so
            // brick_1x1 and brick_2x1 share one "brick" number and differ only in what it takes
            // to get through them.
            BulletDefinition rock = CreateBullet(
                "Rock",
                "rock_type",
                "Cannon Ball",
                RockProjectilePath,
                new[]
                {
                    // 3 takes a 1x1 brick and glass in one shot, and needs exactly two on a 2x1.
                    // splashShare is 0.35 rather than the 0.3 it used to be so that splash on
                    // glass is 3 x 0.35 = 1.05, over glass's single hit point: at 0.3 it landed
                    // on 0.9 and a glass neighbour survived by a tenth, which is the difference
                    // between a pane that chains and one that does not.
                    Level("Cannon Ball I", 0.35f,
                        Damage("glass", 3f, 5f),
                        Damage("brick", 3f, 1f),
                        Damage("concrete", 0f, 0f)),

                    // 6 clears brick_2x1's 5 in one shot, which is what the level is bought for.
                    // Concrete is still untouchable, whatever vehicle it is fired from.
                    Level("Cannon Ball II", 0.35f,
                        Damage("glass", 4f, 6f),
                        Damage("brick", 6f, 3f),
                        Damage("concrete", 0f, 0f)),
                });

            BulletDefinition cannon = CreateBullet(
                "Cannon",
                "cannon_type",
                "Rocket",
                CannonProjectilePath,
                new[]
                {
                    // Concrete at 3 against 6 hit points is two shots bare, and one from a
                    // vehicle at x2 or better. That is the upsell made visible: the unlock buys
                    // the matchup, the vehicle buys the speed of it.
                    Level("Rocket I", 0.4f,
                        Damage("glass", 6f, 8f),
                        Damage("brick", 6f, 5f),
                        Damage("concrete", 3f, 2f)),

                    // 6 takes concrete in a single shot, and 8 covers every brick going.
                    Level("Rocket II", 0.45f,
                        Damage("glass", 8f, 10f),
                        Damage("brick", 8f, 7f),
                        Damage("concrete", 6f, 5f)),
                });

            CreateLoadout(rock, cannon);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"Created default bullet definitions in {ConfigFolder}.");
        }

        private static BulletDefinition.MaterialDamage Damage(string materialId, float blockDamage, float wallDamage)
        {
            return new BulletDefinition.MaterialDamage
            {
                materialId = materialId,
                blockDamage = blockDamage,
                wallDamage = wallDamage,
            };
        }

        private static BulletDefinition.Level Level(
            string displayName,
            float splashShare,
            params BulletDefinition.MaterialDamage[] damage)
        {
            return new BulletDefinition.Level
            {
                displayName = displayName,
                splashShare = splashShare,
                damage = damage,
            };
        }

        private static BulletDefinition CreateBullet(
            string assetName,
            string id,
            string displayName,
            string projectilePrefabPath,
            BulletDefinition.Level[] levels)
        {
            string path = $"{ConfigFolder}/{assetName}.asset";
            BulletDefinition bullet = AssetDatabase.LoadAssetAtPath<BulletDefinition>(path);
            bool isNew = bullet == null;
            if (isNew)
            {
                bullet = ScriptableObject.CreateInstance<BulletDefinition>();
            }

            SerializedObject serialized = new SerializedObject(bullet);
            serialized.FindProperty("id").stringValue = id;
            serialized.FindProperty("displayName").stringValue = displayName;

            // Set only when empty, unlike the numbers above: the ball is a wiring decision that
            // someone may have already pointed elsewhere, while the damage tables are the
            // starting point this menu item exists to restore.
            UiBuilder.SetIfEmpty(serialized, "projectilePrefab", LoadProjectilePrefab(projectilePrefabPath));

            SerializedProperty levelsProperty = serialized.FindProperty("levels");
            levelsProperty.arraySize = levels.Length;
            for (int i = 0; i < levels.Length; i++)
            {
                SerializedProperty level = levelsProperty.GetArrayElementAtIndex(i);
                level.FindPropertyRelative("displayName").stringValue = levels[i].displayName;
                level.FindPropertyRelative("splashShare").floatValue = levels[i].splashShare;

                // Filled rather than rewritten, unlike the damage numbers above and for the same
                // reason the ball is: a picture is a wiring decision, and the numbers are the
                // starting point this menu item exists to restore.
                ItemIconTable.ApplyIcon(level.FindPropertyRelative("icon"), id, i, bullet);

                SerializedProperty damageProperty = level.FindPropertyRelative("damage");
                damageProperty.arraySize = levels[i].damage.Length;
                for (int d = 0; d < levels[i].damage.Length; d++)
                {
                    SerializedProperty entry = damageProperty.GetArrayElementAtIndex(d);
                    entry.FindPropertyRelative("materialId").stringValue = levels[i].damage[d].materialId;
                    entry.FindPropertyRelative("blockDamage").floatValue = levels[i].damage[d].blockDamage;
                    entry.FindPropertyRelative("wallDamage").floatValue = levels[i].damage[d].wallDamage;
                }
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();

            if (isNew)
            {
                AssetDatabase.CreateAsset(bullet, path);
            }
            else
            {
                EditorUtility.SetDirty(bullet);
            }

            return bullet;
        }

        /// <summary>
        /// The artist's ball for an ammunition, checked rather than corrected on the way through:
        /// their tuned physics is theirs to keep, and a field that reads oddly is far more likely
        /// to be a deliberate authoring choice than something a builder should quietly overwrite.
        /// </summary>
        private static GridKnockdownCannonProjectile LoadProjectilePrefab(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            GridKnockdownCannonProjectile prefab = AssetDatabase.LoadAssetAtPath<GridKnockdownCannonProjectile>(path);
            if (prefab == null)
            {
                Debug.LogWarning(
                    $"{nameof(BulletDefinitionBuilder)} found no {nameof(GridKnockdownCannonProjectile)} at "
                    + $"{path}. That ammunition will fire the fire controller's own prefab instead.");
                return null;
            }

            SerializedObject serialized = new SerializedObject(prefab);

            // The fire controller tells each shot what fired it. A prefab that names an
            // ammunition itself would pin every shot from that ball to it, whatever the player
            // actually loaded - which is exactly the bug this whole task is meant to remove.
            SerializedProperty bulletOverride = serialized.FindProperty("bulletOverride");
            if (bulletOverride != null && bulletOverride.objectReferenceValue != null)
            {
                Debug.LogWarning(
                    $"{path} has bulletOverride set to {bulletOverride.objectReferenceValue.name}. Clear it, "
                    + "or every shot fired from this ball deals that ammunition's damage.",
                    prefab);
            }

            // Empty is correct here for the same reason.
            SerializedProperty loadout = serialized.FindProperty("loadout");
            if (loadout != null && loadout.objectReferenceValue != null)
            {
                Debug.LogWarning(
                    $"{path} has a loadout assigned. The fire controller supplies the ammunition per "
                    + "shot, so this only ever applies when the prefab is dropped into a scene by hand.",
                    prefab);
            }

            return prefab;
        }

        private static void CreateLoadout(params BulletDefinition[] bullets)
        {
            string path = $"{ConfigFolder}/BulletLoadout.asset";
            BulletLoadout loadout = AssetDatabase.LoadAssetAtPath<BulletLoadout>(path);
            bool isNew = loadout == null;
            if (isNew)
            {
                loadout = ScriptableObject.CreateInstance<BulletLoadout>();
            }

            SerializedObject serialized = new SerializedObject(loadout);
            SerializedProperty list = serialized.FindProperty("bullets");
            list.arraySize = bullets.Length;
            for (int i = 0; i < bullets.Length; i++)
            {
                list.GetArrayElementAtIndex(i).objectReferenceValue = bullets[i];
            }

            // The rock starts unlocked; everything else is something to earn.
            serialized.FindProperty("defaultBullet").objectReferenceValue = bullets.Length > 0 ? bullets[0] : null;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            if (isNew)
            {
                AssetDatabase.CreateAsset(loadout, path);
            }
            else
            {
                EditorUtility.SetDirty(loadout);
            }
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
