using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameJam.Config
{
    /// <summary>
    /// What it costs to unlock each kind of ammunition. Prices live here rather than on the
    /// BulletDefinition itself so the shop can be retuned without touching the assets that
    /// describe how the ammunition behaves in combat.
    /// </summary>
    [CreateAssetMenu(menuName = "GameJam/Purchase Bullet Config", fileName = "PurchaseBulletConfig")]
    public sealed class PurchaseBulletConfig : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            [Tooltip("Bullet type id, matching BulletDefinition.Id, for example rock_type.")]
            public string bulletId;

            [Tooltip("Gold the player pays once to unlock this ammunition. Zero means it is free, "
                     + "which is how a starting bullet is expressed.")]
            public int goldPrice;
        }

        [SerializeField] private Entry[] entries = Array.Empty<Entry>();

        private Dictionary<string, int> lookup;

        public IReadOnlyList<Entry> Entries => entries;

        /// <summary>
        /// Price of unlocking a bullet. Returns false when the bullet is not listed, which the
        /// caller should read as "not for sale" rather than "free".
        /// </summary>
        public bool TryGetPrice(string bulletId, out int goldPrice)
        {
            goldPrice = 0;
            if (string.IsNullOrEmpty(bulletId))
            {
                return false;
            }

            EnsureLookup();
            return lookup.TryGetValue(bulletId, out goldPrice);
        }

        private void EnsureLookup()
        {
            if (lookup != null)
            {
                return;
            }

            lookup = new Dictionary<string, int>(entries.Length, StringComparer.Ordinal);
            for (int i = 0; i < entries.Length; i++)
            {
                string bulletId = entries[i].bulletId;
                if (string.IsNullOrEmpty(bulletId))
                {
                    continue;
                }

                lookup[bulletId] = entries[i].goldPrice;
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
                entry.goldPrice = Mathf.Max(0, entry.goldPrice);
                entries[i] = entry;

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
