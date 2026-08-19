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

            [Tooltip("Optional. One-cell wall panel used to draw a run of these blocks as a "
                     + "single wall. Without one the run is drawn by welding the block meshes, "
                     + "which costs the same vertices as the blocks it replaces.")]
            public GameObject wallPanel;
        }

        [SerializeField] private Entry[] entries = Array.Empty<Entry>();

        private Dictionary<string, GameObject> lookup;
        private Dictionary<string, GameObject> panelLookup;

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

        /// <summary>
        /// The wall panel for a type, if the art for it exists. Types without one still group;
        /// they just fall back to welding the block meshes together.
        /// </summary>
        public bool TryGetWallPanel(string type, out GameObject panel)
        {
            if (string.IsNullOrEmpty(type))
            {
                panel = null;
                return false;
            }

            EnsureLookup();
            return panelLookup.TryGetValue(type, out panel) && panel != null;
        }

        private void EnsureLookup()
        {
            if (lookup != null)
            {
                return;
            }

            lookup = new Dictionary<string, GameObject>(entries.Length, StringComparer.Ordinal);
            panelLookup = new Dictionary<string, GameObject>(entries.Length, StringComparer.Ordinal);
            for (int i = 0; i < entries.Length; i++)
            {
                string type = entries[i].type;
                if (string.IsNullOrEmpty(type))
                {
                    continue;
                }

                lookup[type] = entries[i].prefab;
                panelLookup[type] = entries[i].wallPanel;
            }
        }

        private void OnValidate()
        {
            // Entries may have been edited in the inspector, so the cached lookups are stale.
            lookup = null;
            panelLookup = null;

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
