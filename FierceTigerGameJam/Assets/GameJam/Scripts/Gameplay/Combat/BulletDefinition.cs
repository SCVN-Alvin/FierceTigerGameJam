using System;
using UnityEngine;

namespace GameJam.Gameplay.Combat
{
    /// <summary>
    /// What one kind of ammunition does to each material, at each of its levels. A rock might
    /// shatter glass, take a single brick, barely chip a brick wall and do nothing at all to
    /// concrete; levelling it up changes those numbers, and concrete may need a different kind
    /// of ammunition entirely.
    ///
    /// Damage is authored per material rather than as one number divided by a resistance, so a
    /// matchup can be flatly impossible - zero, not "eventually" - which is what makes an
    /// upgrade feel like it unlocked something rather than sped something up.
    /// </summary>
    [CreateAssetMenu(menuName = "GameJam/Bullet Definition", fileName = "BulletDefinition")]
    public sealed class BulletDefinition : ScriptableObject
    {
        [Serializable]
        public struct MaterialDamage
        {
            [Tooltip("Material this applies to, matching the id on the block: brick, glass, concrete.")]
            public string materialId;

            [Tooltip("Hit points taken off a single block. Zero means this ammunition cannot hurt "
                     + "the material at all, however many times it hits.")]
            public float blockDamage;

            [Tooltip("Hit points taken off a wall of this material. Lower than blockDamage is what "
                     + "makes a shot chip a wall while destroying a lone block.")]
            public float wallDamage;
        }

        [Serializable]
        public sealed class Level
        {
            [Tooltip("Shown to the player, e.g. \"Rock II\".")]
            public string displayName;

            public MaterialDamage[] damage = Array.Empty<MaterialDamage>();

            [Tooltip("Share of the damage dealt to everything else caught in the blast radius.")]
            [Range(0f, 1f)]
            public float splashShare = 0.35f;

            [Tooltip("Shown in the shop row and the preview. Left empty, the nearest lower "
                     + "level's icon is used, so one sprite per ammunition is enough to ship.")]
            public Sprite icon;
        }

        [Tooltip("Stable id used to look this ammunition up, e.g. rock_type.")]
        [SerializeField] private string id;

        [SerializeField] private string displayName;

        [Tooltip("Index 0 is level 1. A bullet with no levels does no damage to anything.")]
        [SerializeField] private Level[] levels = Array.Empty<Level>();

        public string Id => id;
        public string DisplayName => string.IsNullOrEmpty(displayName) ? id : displayName;
        public int LevelCount => levels.Length;

        public Level GetLevel(int level)
        {
            // Levels are one-based to the player and zero-based here; clamping rather than
            // failing keeps a mis-set level from silently doing nothing at all.
            int index = Mathf.Clamp(level - 1, 0, levels.Length - 1);
            return levels.Length == 0 ? null : levels[index];
        }

        /// <summary>
        /// Icon for a level, walking down to lower levels when the slot is empty, the same rule
        /// <see cref="VehicleDefinition.ResolveIcon"/> follows. Null only when no level has one,
        /// which the shop draws as an empty slot rather than a white square.
        /// </summary>
        public Sprite ResolveIcon(int level)
        {
            if (levels.Length == 0)
            {
                return null;
            }

            int index = Mathf.Clamp(level - 1, 0, levels.Length - 1);
            for (int i = index; i >= 0; i--)
            {
                if (levels[i] != null && levels[i].icon != null)
                {
                    return levels[i].icon;
                }
            }

            return null;
        }

        /// <summary>
        /// Damage this ammunition does to a material at a level. Returns false when the material
        /// is not listed, which means this ammunition cannot hurt it.
        /// </summary>
        public bool TryGetDamage(int level, string materialId, out MaterialDamage damage)
        {
            damage = default;
            if (string.IsNullOrEmpty(materialId))
            {
                return false;
            }

            Level bulletLevel = GetLevel(level);
            if (bulletLevel?.damage == null)
            {
                return false;
            }

            for (int i = 0; i < bulletLevel.damage.Length; i++)
            {
                if (string.Equals(bulletLevel.damage[i].materialId, materialId, StringComparison.OrdinalIgnoreCase))
                {
                    damage = bulletLevel.damage[i];
                    return true;
                }
            }

            return false;
        }

        /// <summary>Convenience for the common question: can this shot hurt this material at all?</summary>
        public bool CanDamage(int level, string materialId, bool isWall)
        {
            if (!TryGetDamage(level, materialId, out MaterialDamage damage))
            {
                return false;
            }

            return (isWall ? damage.wallDamage : damage.blockDamage) > 0f;
        }

        private void OnValidate()
        {
            if (string.IsNullOrEmpty(id))
            {
                id = name;
            }

            for (int i = 0; i < levels.Length; i++)
            {
                if (levels[i]?.damage == null)
                {
                    continue;
                }

                for (int d = 0; d < levels[i].damage.Length; d++)
                {
                    MaterialDamage entry = levels[i].damage[d];
                    entry.blockDamage = Mathf.Max(0f, entry.blockDamage);
                    entry.wallDamage = Mathf.Max(0f, entry.wallDamage);
                    levels[i].damage[d] = entry;
                }
            }
        }
    }
}
