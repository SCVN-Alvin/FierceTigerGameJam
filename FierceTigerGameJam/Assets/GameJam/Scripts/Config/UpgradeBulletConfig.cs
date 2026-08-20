using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameJam.Config
{
    /// <summary>
    /// What it costs to raise each kind of ammunition to a given level. One row per bullet type,
    /// holding the price of every level that can be bought for it.
    ///
    /// Level 1 is where a bullet starts once it has been purchased, so it is never listed and
    /// never has a price: the cheapest row is the price of reaching level 2.
    /// </summary>
    [CreateAssetMenu(menuName = "GameJam/Upgrade Bullet Config", fileName = "UpgradeBulletConfig")]
    public sealed class UpgradeBulletConfig : ScriptableObject
    {
        [Serializable]
        public struct LevelPrice
        {
            [Tooltip("The level this purchase takes the bullet TO, so 2 and up.")]
            public int targetLevel;

            [Tooltip("Gold the player pays to reach that level from the one below it.")]
            public int goldPrice;
        }

        [Serializable]
        public sealed class Entry
        {
            [Tooltip("Bullet type id, matching BulletDefinition.Id, for example rock_type.")]
            public string bulletId;

            [Tooltip("One row per buyable level. A bullet with no rows cannot be upgraded at all.")]
            public LevelPrice[] levels = Array.Empty<LevelPrice>();
        }

        [SerializeField] private Entry[] entries = Array.Empty<Entry>();

        private Dictionary<string, Entry> lookup;

        public IReadOnlyList<Entry> Entries => entries;

        /// <summary>
        /// Price of taking a bullet to a level. Returns false when the bullet is unknown or the
        /// level is not for sale, which includes level 1 because that is the starting level.
        /// </summary>
        public bool TryGetUpgradePrice(string bulletId, int targetLevel, out int goldPrice)
        {
            goldPrice = 0;
            if (!TryGetEntry(bulletId, out Entry entry) || entry.levels == null)
            {
                return false;
            }

            for (int i = 0; i < entry.levels.Length; i++)
            {
                if (entry.levels[i].targetLevel == targetLevel)
                {
                    goldPrice = entry.levels[i].goldPrice;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Highest level a bullet can reach. Returns false when the bullet is unknown; a listed
        /// bullet with no buyable levels reports 1, since it is already at its ceiling.
        /// </summary>
        public bool TryGetMaxLevel(string bulletId, out int maxLevel)
        {
            maxLevel = 0;
            if (!TryGetEntry(bulletId, out Entry entry))
            {
                return false;
            }

            maxLevel = 1;
            if (entry.levels == null)
            {
                return true;
            }

            for (int i = 0; i < entry.levels.Length; i++)
            {
                if (entry.levels[i].targetLevel > maxLevel)
                {
                    maxLevel = entry.levels[i].targetLevel;
                }
            }

            return true;
        }

        private bool TryGetEntry(string bulletId, out Entry entry)
        {
            entry = null;
            if (string.IsNullOrEmpty(bulletId))
            {
                return false;
            }

            EnsureLookup();
            return lookup.TryGetValue(bulletId, out entry) && entry != null;
        }

        private void EnsureLookup()
        {
            if (lookup != null)
            {
                return;
            }

            lookup = new Dictionary<string, Entry>(entries.Length, StringComparer.Ordinal);
            for (int i = 0; i < entries.Length; i++)
            {
                Entry entry = entries[i];
                if (entry == null || string.IsNullOrEmpty(entry.bulletId))
                {
                    continue;
                }

                lookup[entry.bulletId] = entry;
            }
        }

        private void OnValidate()
        {
            // Entries may have been edited in the inspector, so the cached lookup is stale.
            lookup = null;

            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < entries.Length; i++)
            {
                Entry entry = entries[i];
                if (entry == null)
                {
                    continue;
                }

                if (entry.levels != null)
                {
                    HashSet<int> seenLevels = new HashSet<int>();
                    for (int l = 0; l < entry.levels.Length; l++)
                    {
                        LevelPrice price = entry.levels[l];

                        // Level 1 is the starting level and is never bought, so 2 is the floor.
                        price.targetLevel = Mathf.Max(2, price.targetLevel);
                        price.goldPrice = Mathf.Max(0, price.goldPrice);
                        entry.levels[l] = price;

                        if (!seenLevels.Add(price.targetLevel))
                        {
                            Debug.LogWarning($"{name} lists level {price.targetLevel} of \"{entry.bulletId}\" more than once; the first row wins.", this);
                        }
                    }
                }

                if (string.IsNullOrEmpty(entry.bulletId))
                {
                    continue;
                }

                if (!seen.Add(entry.bulletId))
                {
                    Debug.LogWarning($"{name} lists the bullet id \"{entry.bulletId}\" more than once; the last entry wins.", this);
                }
            }
        }
    }
}
