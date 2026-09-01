using System;
using System.Collections.Generic;
using GameJam.Data;
using GameJam.Gameplay.Wall;
using UnityEngine;

namespace GameJam.Config
{
    /// <summary>
    /// One mission: a name and the maps it is made of, in the order they are played.
    ///
    /// The maps are named by id rather than held as <see cref="MapInfo"/> values. Brief 25 asked
    /// for MapInfo[] here, but MapInfo is a plain [Serializable] class owned by
    /// <see cref="MapConfig"/>, and Unity serializes those by VALUE: an array of them on this
    /// asset would be a second copy of every map, free to drift from the registry, which is the
    /// opposite of the brief's own decision that "MapConfig keeps the maps". An id is the only
    /// thing Unity will store here that stays a reference to the registry entry.
    ///
    /// The typing the brief was after - a typo failing where a human can see it rather than
    /// showing up as "NO MAP YET!" at draw time - is bought back by
    /// <see cref="MissionConfig.OnValidate"/> instead, which checks every id against the registry
    /// and names the bad ones in the console.
    /// </summary>
    [Serializable]
    public sealed class Mission
    {
        [Tooltip("Stable id - mission_1, mission_2. Never renamed once a save could refer to it.")]
        public string id;

        [Tooltip("What the board's title tab reads. Empty falls back to MISSION n.")]
        public string displayName;

        [Tooltip("Map ids from the MapConfig, in the order the row draws them.")]
        public string[] mapIds = Array.Empty<string>();
    }

    /// <summary>
    /// How the campaign is grouped into missions, and the rule for when the next one opens.
    ///
    /// The unlock rule lives here rather than on the board that draws it because it is a fact
    /// about progress, not about a screen: the cleared screen has to ask the same question when
    /// it decides what comes next, and a rule inside a UI component cannot be asked from there.
    /// </summary>
    [CreateAssetMenu(menuName = "GameJam/Mission Config", fileName = "MissionConfig")]
    public sealed class MissionConfig : ScriptableObject
    {
        [Tooltip("The registry the ids below name. Only used to catch typos; the board resolves "
                 + "maps through its own MapSelection, so leaving this empty costs validation "
                 + "and nothing else.")]
        [SerializeField] private MapConfig maps;

        [SerializeField] private Mission[] missions = Array.Empty<Mission>();

        public IReadOnlyList<Mission> Missions => missions;

        public int Count => missions.Length;

        public Mission Get(int index)
        {
            return index >= 0 && index < missions.Length ? missions[index] : null;
        }

        public bool TryGet(string id, out Mission mission)
        {
            if (!string.IsNullOrEmpty(id))
            {
                for (int i = 0; i < missions.Length; i++)
                {
                    if (missions[i] != null && string.Equals(missions[i].id, id, StringComparison.Ordinal))
                    {
                        mission = missions[i];
                        return true;
                    }
                }
            }

            mission = null;
            return false;
        }

        /// <summary>
        /// Whether the player has earned their way into this mission: the first one always, and
        /// every other one once every map of the mission before it has been passed.
        ///
        /// Passed, not fully cleared, is the bar on purpose - it is the existing per-map bar from
        /// <see cref="MapProgressionConfig.Entry.requiredClearPercent"/>, so a player who beats
        /// every level moves on whether or not they went back for a hundred percent.
        ///
        /// A previous mission with no maps in it reads as unlocked. It has nothing left to do, and
        /// the alternative - a mission nobody can ever open because the one before it was authored
        /// empty - is a worse way to be wrong.
        /// </summary>
        public bool IsUnlocked(int index)
        {
            if (index <= 0)
            {
                return true;
            }

            Mission previous = Get(index - 1);
            if (previous == null || previous.mapIds == null)
            {
                return true;
            }

            UserMapProgressData progress = UserData.Maps;
            for (int i = 0; i < previous.mapIds.Length; i++)
            {
                string mapId = previous.mapIds[i];
                if (!string.IsNullOrEmpty(mapId) && !progress.IsPassed(mapId))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// The check the brief wanted the type system to do. A map id that is not in the registry
        /// would otherwise only show up as a card reading "NO MAP YET!", which is indistinguishable
        /// from a slot nobody has authored yet; said here, it names the mission and the id.
        /// </summary>
        private void OnValidate()
        {
            HashSet<string> seenMissionIds = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> seenMapIds = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < missions.Length; i++)
            {
                Mission mission = missions[i];
                if (mission == null)
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(mission.id) && !seenMissionIds.Add(mission.id))
                {
                    Debug.LogWarning($"{name} lists the mission id \"{mission.id}\" more than once.", this);
                }

                if (mission.mapIds == null)
                {
                    continue;
                }

                for (int m = 0; m < mission.mapIds.Length; m++)
                {
                    string mapId = mission.mapIds[m];
                    if (string.IsNullOrEmpty(mapId))
                    {
                        // Missions are drawn one card per map now, so a blank is not a placeholder
                        // slot any more - it is an entry somebody left half-filled.
                        Debug.LogWarning($"{name}: mission \"{mission.id}\" has an empty map id at {m}.", this);
                        continue;
                    }

                    if (!seenMapIds.Add(mapId))
                    {
                        Debug.LogWarning(
                            $"{name}: map \"{mapId}\" appears in more than one mission, so passing it "
                            + "would count towards two unlocks.",
                            this);
                    }

                    if (maps != null && !maps.TryGet(mapId, out _))
                    {
                        Debug.LogWarning(
                            $"{name}: mission \"{mission.id}\" names the map \"{mapId}\", which is not "
                            + $"in {maps.name}.",
                            this);
                    }
                }
            }
        }
    }
}
