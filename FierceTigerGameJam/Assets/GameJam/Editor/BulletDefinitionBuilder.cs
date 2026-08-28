#if UNITY_EDITOR
using System.IO;
using GameJam.Gameplay.Cannon;
using GameJam.Gameplay.Combat;
using UnityEditor;
using UnityEngine;

namespace GameJam.EditorTools
{
    /// <summary>
    /// Creates the starting ammunition: a rock that handles glass and loose brick but only chips
    /// a brick wall and cannot touch concrete, and a cannon that opens concrete up. The numbers
    /// are a playable starting point rather than a balance pass - they are meant to be edited on
    /// the assets afterwards.
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

            // Block hit points for reference: glass 1, brick 3, concrete 6. A wall's hit points
            // are the sum of its blocks', so a four-brick wall holds 12.
            BulletDefinition rock = CreateBullet(
                "Rock",
                "rock_type",
                "Rock",
                RockProjectilePath,
                new[]
                {
                    // Takes glass and a lone brick in one shot. Against a brick wall it is a
                    // chip - 12 hit points at 1 a shot is not a route, it is a hint to upgrade.
                    Level("Rock I", 0.3f,
                        Damage("glass", 5f, 5f),
                        Damage("brick", 3f, 1f),
                        Damage("concrete", 0f, 0f)),

                    // Now it does real work on brick walls, but concrete is still untouchable.
                    Level("Rock II", 0.35f,
                        Damage("glass", 6f, 6f),
                        Damage("brick", 4f, 3f),
                        Damage("concrete", 0f, 0f)),
                });

            BulletDefinition cannon = CreateBullet(
                "Cannon",
                "cannon_type",
                "Cannon Ball",
                CannonProjectilePath,
                new[]
                {
                    Level("Cannon I", 0.4f,
                        Damage("glass", 8f, 8f),
                        Damage("brick", 6f, 5f),
                        Damage("concrete", 4f, 2f)),

                    Level("Cannon II", 0.45f,
                        Damage("glass", 10f, 10f),
                        Damage("brick", 8f, 7f),
                        Damage("concrete", 7f, 5f)),
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
