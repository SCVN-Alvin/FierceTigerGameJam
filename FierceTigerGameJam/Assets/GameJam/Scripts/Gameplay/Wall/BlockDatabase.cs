using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameJam.Gameplay.Wall
{
    /// <summary>
    /// Maps the "type" string in a map JSON onto the prefab that should be spawned for it.
    /// </summary>
    [CreateAssetMenu(menuName = "GameJam/Block Database", fileName = "BlockDatabase")]
    public sealed class BlockDatabase : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            [Tooltip("The \"type\" value used in map JSON, for example brick_1x1.")]
            public string type;
            public GameObject prefab;

        }

        [SerializeField] private Entry[] entries = Array.Empty<Entry>();

        private Dictionary<string, GameObject> lookup;

        public IReadOnlyList<Entry> Entries => entries;

        public bool TryGetPrefab(string type, out GameObject prefab)
        {
            if (string.IsNullOrEmpty(type))
            {
                prefab = null;
                return false;
            }

            EnsureLookup();
            return lookup.TryGetValue(type, out prefab) && prefab != null;
        }

        private void EnsureLookup()
        {
            if (lookup != null)
            {
                return;
            }

            lookup = new Dictionary<string, GameObject>(entries.Length, StringComparer.Ordinal);
            for (int i = 0; i < entries.Length; i++)
            {
                string type = entries[i].type;
                if (string.IsNullOrEmpty(type))
                {
                    continue;
                }

                lookup[type] = entries[i].prefab;
            }
        }

        private void OnValidate()
        {
            // Entries may have been edited in the inspector, so the cached lookup is stale.
            lookup = null;

            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < entries.Length; i++)
            {
                string type = entries[i].type;
                if (string.IsNullOrEmpty(type))
                {
                    continue;
                }

                if (!seen.Add(type))
                {
                    Debug.LogWarning($"{name} lists the block type \"{type}\" more than once; the last entry wins.", this);
                }
            }
        }
    }
}
