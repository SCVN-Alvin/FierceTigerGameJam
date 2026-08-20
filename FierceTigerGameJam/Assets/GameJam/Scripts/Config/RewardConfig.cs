using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameJam.Config
{
    /// <summary>
    /// The table of rewards the rest of the game hands out.
    ///
    /// Rewards are referenced by id rather than inlined at the place that grants them so the same
    /// reward can be reused by many maps and retuned in exactly one place.
    /// </summary>
    [CreateAssetMenu(menuName = "GameJam/Reward Config", fileName = "RewardConfig")]
    public sealed class RewardConfig : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            [Tooltip("Stable id other configs point at, for example pass_map_small.")]
            public string rewardId;

            [Tooltip("Gold granted when this reward is claimed.")]
            public int gold;
        }

        [SerializeField] private Entry[] entries = Array.Empty<Entry>();

        private Dictionary<string, int> lookup;

        public IReadOnlyList<Entry> Entries => entries;

        /// <summary>
        /// Gold behind a reward id. Returns false when the id is unknown so a typo in a config
        /// shows up as "nothing was granted" rather than as a crash mid-run.
        /// </summary>
        public bool TryGetReward(string rewardId, out int gold)
        {
            gold = 0;
            if (string.IsNullOrEmpty(rewardId))
            {
                return false;
            }

            EnsureLookup();
            return lookup.TryGetValue(rewardId, out gold);
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
                string rewardId = entries[i].rewardId;
                if (string.IsNullOrEmpty(rewardId))
                {
                    continue;
                }

                lookup[rewardId] = entries[i].gold;
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
                entry.gold = Mathf.Max(0, entry.gold);
                entries[i] = entry;

                if (string.IsNullOrEmpty(entry.rewardId))
                {
                    continue;
                }

                if (!seen.Add(entry.rewardId))
                {
                    Debug.LogWarning($"{name} lists the reward id \"{entry.rewardId}\" more than once; the last entry wins.", this);
                }
            }
        }
    }
}
