using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameJam.Config
{
    /// <summary>
    /// Per-map completion rules: how much has to come down to pass, what passing and fully
    /// clearing are worth, and how much ammunition may be carried in.
    ///
    /// Passing and clearing are separate so a map can be beaten without being emptied, which is
    /// what leaves a reason to come back to it.
    /// </summary>
    [CreateAssetMenu(menuName = "GameJam/Map Progression Config", fileName = "MapProgressionConfig")]
    public sealed class MapProgressionConfig : ScriptableObject
    {
        /// <summary>Rules for a single map. A class rather than a struct so the defaults below apply to new rows.</summary>
        [Serializable]
        public sealed class Entry
        {
            [Tooltip("Map id, matching the id in the map JSON and in MapConfig.")]
            public string mapId;

            [Tooltip("Share of the structure that has to be destroyed to pass the map.")]
            [Range(0f, 1f)]
            public float requiredClearPercent = 0.8f;

            [Tooltip("RewardConfig id granted the first time this map is passed.")]
            public string passMapRewardId;

            [Tooltip("RewardConfig id granted the first time this map is cleared 100 percent. "
                     + "Granted on top of the pass reward, not instead of it.")]
            public string clearMapRewardId;

            [Tooltip("How many bullets the player may take into this map in total, counting every "
                     + "type together. This is the budget the loadout screen spends.")]
            public int bulletPickLimit = 10;
        }

        [SerializeField] private Entry[] entries = Array.Empty<Entry>();

        private Dictionary<string, Entry> lookup;

        public IReadOnlyList<Entry> Entries => entries;

        /// <summary>
        /// Rules for a map. Returns false when the map has no row, which the caller should treat
        /// as "not authored yet" rather than falling back to rules that were never tuned for it.
        /// </summary>
        public bool TryGetMapRules(string mapId, out Entry rules)
        {
            rules = null;
            if (string.IsNullOrEmpty(mapId))
            {
                return false;
            }

            EnsureLookup();
            return lookup.TryGetValue(mapId, out rules) && rules != null;
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
                if (entry == null || string.IsNullOrEmpty(entry.mapId))
                {
                    continue;
                }

                lookup[entry.mapId] = entry;
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

                entry.requiredClearPercent = Mathf.Clamp01(entry.requiredClearPercent);

                // A map you cannot bring any ammunition into is unplayable, so one is the floor.
                entry.bulletPickLimit = Mathf.Max(1, entry.bulletPickLimit);

                if (string.IsNullOrEmpty(entry.mapId))
                {
                    continue;
                }

                if (!seen.Add(entry.mapId))
                {
                    Debug.LogWarning($"{name} lists the map id \"{entry.mapId}\" more than once; the last entry wins.", this);
                }
            }
        }
    }
}
