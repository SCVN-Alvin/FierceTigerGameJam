using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameJam.Gameplay.Wall
{
    /// <summary>One selectable map: the JSON that describes it plus how it is presented.</summary>
    [Serializable]
    public sealed class MapInfo
    {
        [Tooltip("Should match the \"id\" inside the map JSON.")]
        [SerializeField] private string id;
        [SerializeField] private string displayName;
        [SerializeField] private TextAsset mapJson;

        [Tooltip("Shown on the cleared screen. Left empty, the screen shows the banner and reward alone.")]
        [SerializeField] private Sprite clearedImage;

        [Tooltip("Optional pre-built structure, baked from the JSON by Tools/Smashdown/Bake Map "
                 + "Prefabs. When set, the map loads by instantiating this instead of parsing "
                 + "and spawning block by block. Delete it (or re-bake) after editing the JSON.")]
        [SerializeField] private GameObject mapPrefab;


        public string Id => id;
        public string DisplayName => string.IsNullOrEmpty(displayName) ? id : displayName;
        public TextAsset MapJson => mapJson;

        /// <summary>The baked structure, or null to build from the JSON at load.</summary>
        public GameObject MapPrefab => mapPrefab;

        /// <summary>
        /// The picture of this map on the cleared screen. Optional on purpose: no map has art yet,
        /// and a missing one is not worth warning about, so the screen hides the image instead.
        /// </summary>
        public Sprite ClearedImage => clearedImage;
    }

    /// <summary>The list of maps the player can choose from.</summary>
    [CreateAssetMenu(menuName = "GameJam/Map Config", fileName = "MapConfig")]
    public sealed class MapConfig : ScriptableObject
    {
        [SerializeField] private MapInfo[] maps = Array.Empty<MapInfo>();

        public IReadOnlyList<MapInfo> Maps => maps;
        public int Count => maps.Length;

        public MapInfo Get(int index)
        {
            return index >= 0 && index < maps.Length ? maps[index] : null;
        }

        public int IndexOf(MapInfo map)
        {
            return Array.IndexOf(maps, map);
        }

        public bool TryGet(string id, out MapInfo map)
        {
            for (int i = 0; i < maps.Length; i++)
            {
                if (maps[i] != null && string.Equals(maps[i].Id, id, StringComparison.Ordinal))
                {
                    map = maps[i];
                    return true;
                }
            }

            map = null;
            return false;
        }

        private void OnValidate()
        {
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < maps.Length; i++)
            {
                MapInfo map = maps[i];
                if (map == null || string.IsNullOrEmpty(map.Id))
                {
                    continue;
                }

                if (!seen.Add(map.Id))
                {
                    Debug.LogWarning($"{name} lists the map id \"{map.Id}\" more than once.", this);
                }

                WarnOnIdMismatch(map);
            }
        }

        /// <summary>
        /// The id lives in two places, so a renamed or reassigned JSON can silently point an entry
        /// at the wrong map. Catching it here is cheaper than debugging it in game.
        /// </summary>
        private void WarnOnIdMismatch(MapInfo map)
        {
            if (map.MapJson == null)
            {
                Debug.LogWarning($"{name}: map \"{map.Id}\" has no JSON assigned.", this);
                return;
            }

            if (!KnockdownMapDefinition.TryParse(map.MapJson.text, out KnockdownMapDefinition definition, out string error))
            {
                Debug.LogWarning($"{name}: map \"{map.Id}\" has invalid JSON. {error}", this);
                return;
            }

            if (!string.Equals(definition.id, map.Id, StringComparison.Ordinal))
            {
                Debug.LogWarning(
                    $"{name}: entry id \"{map.Id}\" does not match the id \"{definition.id}\" "
                    + $"inside {map.MapJson.name}.",
                    this);
            }
        }
    }
}
