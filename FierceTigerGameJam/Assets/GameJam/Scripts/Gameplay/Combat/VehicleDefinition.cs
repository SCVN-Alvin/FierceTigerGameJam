using System;
using UnityEngine;

namespace GameJam.Gameplay.Combat
{
    /// <summary>
    /// What the machine the cannon is mounted on does for the shot, at each of its levels.
    ///
    /// A vehicle multiplies the loaded ammunition rather than carrying damage of its own, so the
    /// two progressions read as one decision instead of two: what the bullet can hurt at all is
    /// still the bullet's business, and a material it cannot scratch stays unscratched however
    /// good the vehicle is. That keeps <see cref="BulletDefinition"/>'s unlock design intact - a
    /// vehicle can make the player hit harder, never wider.
    ///
    /// Each level names its own model so art can be dropped in one level at a time; the lookups
    /// below walk down to the nearest level that has one, which is what makes shipping a vehicle
    /// with a single model possible.
    /// </summary>
    [CreateAssetMenu(menuName = "GameJam/Vehicle Definition", fileName = "VehicleDefinition")]
    public sealed class VehicleDefinition : ScriptableObject
    {
        [Serializable]
        public sealed class Level
        {
            [Tooltip("Shown to the player, e.g. \"Truck II\".")]
            public string displayName;

            [Tooltip("Multiplies the loaded bullet's blockDamage and wallDamage. 1 = no boost. "
                     + "A material the bullet cannot hurt (0) stays 0 whatever this is.")]
            [Min(0f)] public float damageMultiplier = 1f;

            [Tooltip("Model spawned under the cannon at this level. Left empty, the nearest lower "
                     + "level's model is used, so one model per vehicle is enough to ship.")]
            public GameObject modelPrefab;

            [Tooltip("Uniform scale applied to modelPrefab when mounted. 0 = not fitted yet, treated as 1. "
                     + "Written by Tools > Smashdown > Fit Vehicle Models; hand-tune after fitting if a model "
                     + "still reads wrong.")]
            [Min(0f)] public float modelScale;

            [Tooltip("Optional. Shown in the shop row / preview. Falls back like modelPrefab.")]
            public Sprite icon;
        }

        [Tooltip("Stable id used in saves and configs, e.g. vehicle_truck. Never rename after release.")]
        [SerializeField] private string id;

        [SerializeField] private string displayName;

        [TextArea]
        [SerializeField] private string description;

        [Tooltip("Index 0 is level 1. Three levels for now; the code does not assume three.")]
        [SerializeField] private Level[] levels = Array.Empty<Level>();

        public string Id => id;
        public string DisplayName => string.IsNullOrEmpty(displayName) ? id : displayName;
        public string Description => description;
        public int LevelCount => levels.Length;

        /// <summary>One-based to the player, clamped like <see cref="BulletDefinition.GetLevel"/>.</summary>
        public Level GetLevel(int level)
        {
            // Clamping rather than failing keeps a mis-set level from silently doing nothing at
            // all, the same reading a bullet gives.
            int index = Mathf.Clamp(level - 1, 0, levels.Length - 1);
            return levels.Length == 0 ? null : levels[index];
        }

        /// <summary>
        /// What a shot is multiplied by at this level. A vehicle that defines no levels reports 1
        /// rather than 0: an unauthored vehicle should leave the bullet alone, not disarm it.
        /// </summary>
        public float GetDamageMultiplier(int level)
        {
            Level vehicleLevel = GetLevel(level);
            return vehicleLevel == null ? 1f : Mathf.Max(0f, vehicleLevel.damageMultiplier);
        }

        /// <summary>
        /// Model for a level, walking down to lower levels when the slot is empty. Null only when
        /// no level has a model at all.
        /// </summary>
        public GameObject ResolveModelPrefab(int level)
        {
            if (levels.Length == 0)
            {
                return null;
            }

            int index = Mathf.Clamp(level - 1, 0, levels.Length - 1);
            for (int i = index; i >= 0; i--)
            {
                if (levels[i] != null && levels[i].modelPrefab != null)
                {
                    return levels[i].modelPrefab;
                }
            }

            return null;
        }

        /// <summary>
        /// How much the mounted model is scaled by at a level. The pack art is authored several
        /// times the size of the cannon it stands on, so a level without a fitted number would
        /// put a building-sized cannon on the playfield; 0 means "nobody has fitted this yet"
        /// and reports 1, the model at its authored size, which is at least honest.
        ///
        /// Deviation from the brief, which reads the clamped level's own <c>modelScale</c> and
        /// stops there: the scale is taken from the same level <see cref="ResolveModelPrefab"/>
        /// took the model from. A level with an empty model slot shows the nearest lower level's
        /// model, and that model's fitted scale is the only one that means anything for it -
        /// reading the empty level's own 0 would draw a shipped-with-one-model vehicle at full
        /// pack size from level 2 up.
        /// </summary>
        public float ResolveModelScale(int level)
        {
            if (levels.Length == 0)
            {
                return 1f;
            }

            int index = Mathf.Clamp(level - 1, 0, levels.Length - 1);
            for (int i = index; i >= 0; i--)
            {
                if (levels[i] != null && levels[i].modelPrefab != null)
                {
                    return levels[i].modelScale > 0f ? levels[i].modelScale : 1f;
                }
            }

            return 1f;
        }

        /// <summary>Same fallback rule for icons.</summary>
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

        private void OnValidate()
        {
            if (string.IsNullOrEmpty(id))
            {
                id = name;
            }

            float previous = 0f;
            for (int i = 0; i < levels.Length; i++)
            {
                if (levels[i] == null)
                {
                    continue;
                }

                levels[i].damageMultiplier = Mathf.Max(0f, levels[i].damageMultiplier);

                // Allowed, but a level that hits softer than the one below it is almost always a
                // typo: the player pays for it and gets weaker, which no shop copy can explain.
                if (i > 0 && levels[i].damageMultiplier < previous)
                {
                    Debug.LogWarning(
                        $"{name} level {i + 1} multiplies by {levels[i].damageMultiplier:0.00}, less than "
                        + $"level {i} at {previous:0.00}. Upgrading would make the vehicle worse.",
                        this);
                }

                previous = levels[i].damageMultiplier;
            }
        }
    }
}
