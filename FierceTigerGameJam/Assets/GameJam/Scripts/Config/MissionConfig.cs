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

        [Tooltip("Backdrop picture the playfield shows for every level of this mission. Left "
                 + "empty the scene keeps whatever its backdrops were authored with.")]
        public Sprite background;

        [Tooltip("Picture tiled across the ground plane for this mission. Left empty the ground "
                 + "keeps its authored material.")]
        public Texture2D floorTexture;

        [Tooltip("Repeats of the floor picture across the ground. Small numbers stretch, big "
                 + "numbers tile. 0 counts as 1.")]
        [Range(0f, 32f)] public float floorTiling;

        [Tooltip("Slides the whole backdrop strip up or down, in world units, so the picture's "
                 + "horizon can be put where this mission wants it. Tune it live in Play mode - "
                 + "the playfield re-reads it every frame.")]
        [Range(-20f, 20f)] public float backdropOffsetY;

        [Tooltip("Grows or shrinks the backdrop strip around its authored size. 0 counts as 1.")]
        [Range(0f, 4f)] public float backdropScale;
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

        [Header("Star Thresholds")]
        [Tooltip("Best clear percent that earns the SECOND star. The first star is simply "
                 + "passing the map (its own requiredClearPercent); the third is the threshold "
                 + "below. Shared by every level - stars mean the same thing everywhere.")]
        [Range(0f, 1f)] [SerializeField] private float twoStarClearPercent = 0.75f;

        [Tooltip("Best clear percent that earns the THIRD star. 1 = a full clear.")]
        [Range(0f, 1f)] [SerializeField] private float threeStarClearPercent = 1f;

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
        /// <summary>Index of the mission whose mapIds contain this map, or -1.</summary>
        public int MissionIndexOf(string mapId)
        {
            if (string.IsNullOrEmpty(mapId))
            {
                return -1;
            }

            for (int m = 0; m < missions.Length; m++)
            {
                string[] ids = missions[m] != null ? missions[m].mapIds : null;
                for (int i = 0; ids != null && i < ids.Length; i++)
                {
                    if (string.Equals(ids[i], mapId, StringComparison.Ordinal))
                    {
                        return m;
                    }
                }
            }

            return -1;
        }

        /// <summary>
        /// The scenery authored for the mission this map belongs to. False when the map is in no
        /// mission or its mission has no scenery set - the caller keeps the scene's defaults.
        /// </summary>
        public bool TryGetScenery(string mapId, out Sprite background, out Texture2D floor,
            out float tiling)
        {
            background = null;
            floor = null;
            tiling = 1f;

            int index = MissionIndexOf(mapId);
            if (index < 0)
            {
                return false;
            }

            Mission mission = missions[index];
            background = mission.background;
            floor = mission.floorTexture;
            tiling = mission.floorTiling <= 0f ? 1f : mission.floorTiling;
            return background != null || floor != null;
        }

        /// <summary>
        /// Stars for a record: none unpassed, one for the pass itself, the rest from the best
        /// percent ever - which only rises, so a worse replay can never take a star back.
        /// </summary>
        public int StarsFor(bool passed, float bestClearPercent)
        {
            if (!passed)
            {
                return 0;
            }

            if (bestClearPercent >= threeStarClearPercent)
            {
                return 3;
            }

            return bestClearPercent >= twoStarClearPercent ? 2 : 1;
        }

        /// <summary>How this map's mission wants the backdrop strip placed. Always answers.</summary>
        public void GetBackdropPlacement(string mapId, out float offsetY, out float scale)
        {
            int index = MissionIndexOf(mapId);
            Mission mission = index >= 0 ? missions[index] : null;
            offsetY = mission != null ? mission.backdropOffsetY : 0f;
            scale = mission != null && mission.backdropScale > 0f ? mission.backdropScale : 1f;
        }

    }
}
