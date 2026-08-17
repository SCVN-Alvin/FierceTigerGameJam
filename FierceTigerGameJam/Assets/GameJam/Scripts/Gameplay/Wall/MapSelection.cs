using System;
using UnityEngine;

namespace GameJam.Gameplay.Wall
{
    /// <summary>
    /// Holds which map is currently chosen. Living on an asset rather than a scene object means
    /// anything can read the selection without a reference to whatever UI made it, and the value
    /// can be inspected while playing.
    /// </summary>
    [CreateAssetMenu(menuName = "GameJam/Map Selection", fileName = "MapSelection")]
    public sealed class MapSelection : ScriptableObject
    {
        [SerializeField] private MapConfig config;

        [Tooltip("Used before the player picks anything. Empty falls back to the first map.")]
        [SerializeField] private string defaultMapId;

        /// <summary>
        /// Deliberately not serialized: a choice made while playing should not be baked into the
        /// asset and leak into the next session.
        /// </summary>
        [NonSerialized] private MapInfo selected;

        public event Action<MapInfo> SelectionChanged;

        public MapConfig Config => config;

        public MapInfo Selected => selected ?? ResolveDefault();

        /// <summary>False while falling back to the default, so UI can show nothing as chosen.</summary>
        public bool HasSelection => selected != null;

        public bool Select(MapInfo map)
        {
            if (map == null || selected == map)
            {
                return false;
            }

            selected = map;
            SelectionChanged?.Invoke(selected);
            return true;
        }

        public bool SelectById(string id)
        {
            if (config == null)
            {
                Debug.LogError($"{name} has no map config, so \"{id}\" cannot be selected.", this);
                return false;
            }

            if (config.TryGet(id, out MapInfo map))
            {
                return Select(map);
            }

            Debug.LogError($"{name}: no map with id \"{id}\" in {config.name}.", this);
            return false;
        }

        public bool SelectByIndex(int index)
        {
            if (config == null)
            {
                Debug.LogError($"{name} has no map config, so index {index} cannot be selected.", this);
                return false;
            }

            MapInfo map = config.Get(index);
            if (map != null)
            {
                return Select(map);
            }

            Debug.LogError($"{name}: index {index} is outside {config.name}, which holds {config.Count} map(s).", this);
            return false;
        }

        /// <summary>
        /// Resets the choice without raising SelectionChanged. Clearing is bookkeeping, not the
        /// player picking something, and firing here would send listeners back into gameplay the
        /// moment they left it. It also lets the same map be chosen again and still count as a
        /// change, which is what makes replaying a level work.
        /// </summary>
        public void Clear()
        {
            selected = null;
        }

        private MapInfo ResolveDefault()
        {
            if (config == null || config.Count == 0)
            {
                return null;
            }

            if (!string.IsNullOrEmpty(defaultMapId) && config.TryGet(defaultMapId, out MapInfo map))
            {
                return map;
            }

            return config.Get(0);
        }

        private void OnDisable()
        {
            // Domain reload does this anyway, but not when the editor is set to skip it.
            selected = null;
        }
    }
}
