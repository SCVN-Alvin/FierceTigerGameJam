using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameJam.Data
{
    /// <summary>How far the player has got on one map.</summary>
    [Serializable]
    public sealed class MapProgress
    {
        public string mapId;

        /// <summary>Best fraction of the structure ever destroyed on this map, 0 to 1.</summary>
        public float bestClearPercent;

        public bool passed;
        public bool fullyCleared;

        /// <summary>
        /// Tracked separately from <see cref="passed"/> and <see cref="fullyCleared"/> so a reward
        /// is granted exactly once. Reaching the bar and being paid for reaching it are different
        /// events, and only the second must never repeat.
        /// </summary>
        public bool passRewardClaimed;

        public bool clearRewardClaimed;

        /// <summary>The one-off +gold for first reaching the 2-star bar (75%) has been paid.</summary>
        public bool twoStarRewardClaimed;
    }

    /// <summary>What changed as a result of one attempt, so the caller knows what to pay out.</summary>
    public struct MapAttemptResult
    {
        public float ClearPercent;
        public float BestClearPercent;
        public bool Passed;
        public bool FullyCleared;

        /// <summary>True only on the attempt that first crossed the bar, and only once ever.</summary>
        public bool NewlyPassed;

        public bool NewlyCleared;

        /// <summary>First time at or above the 2-star bar, and its bonus not yet paid.</summary>
        public bool NewlyTwoStar;
    }

    /// <summary>
    /// Per-map progress. The player can come back to a map they have already passed to push for
    /// a hundred percent, so the best result is kept rather than the last one.
    /// </summary>
    [Serializable]
    public sealed class UserMapProgressData
    {
        /// <summary>
        /// The clear bonus's bar. Falcon 2026-09-02: dropped from 100% to 95% so it lands with
        /// the 3rd star - keep it equal to MissionConfig.threeStarClearPercent.
        /// </summary>
        public const float ClearRewardPercent = 0.95f;

        /// <summary>The 2-star bonus's bar - keep equal to MissionConfig.twoStarClearPercent.</summary>
        public const float TwoStarRewardPercent = 0.75f;

        /// <summary>Bumped when the shape of this record changes, so old saves can be migrated.</summary>
        public int version = 1;

        public List<MapProgress> maps = new List<MapProgress>();

        public bool TryGet(string mapId, out MapProgress progress)
        {
            progress = null;
            if (string.IsNullOrEmpty(mapId))
            {
                return false;
            }

            for (int i = 0; i < maps.Count; i++)
            {
                if (maps[i] != null && string.Equals(maps[i].mapId, mapId, StringComparison.Ordinal))
                {
                    progress = maps[i];
                    return true;
                }
            }

            return false;
        }

        public MapProgress GetOrCreate(string mapId)
        {
            if (TryGet(mapId, out MapProgress existing))
            {
                return existing;
            }

            MapProgress created = new MapProgress { mapId = mapId };
            maps.Add(created);
            return created;
        }

        public float GetBestClearPercent(string mapId)
        {
            return TryGet(mapId, out MapProgress progress) ? progress.bestClearPercent : 0f;
        }

        public bool IsPassed(string mapId)
        {
            return TryGet(mapId, out MapProgress progress) && progress.passed;
        }

        public bool IsFullyCleared(string mapId)
        {
            return TryGet(mapId, out MapProgress progress) && progress.fullyCleared;
        }

        /// <summary>
        /// Records the result of one run and reports what it newly achieved. A worse run than a
        /// previous one still counts as an attempt but cannot lower the best, take a pass away,
        /// or earn a reward that was already paid.
        /// </summary>
        public MapAttemptResult RegisterAttempt(string mapId, float clearPercent, float requiredClearPercent)
        {
            clearPercent = Mathf.Clamp01(clearPercent);
            MapProgress progress = GetOrCreate(mapId);

            if (clearPercent > progress.bestClearPercent)
            {
                progress.bestClearPercent = clearPercent;
            }

            bool passedNow = clearPercent >= requiredClearPercent;
            bool clearedNow = clearPercent >= ClearRewardPercent;

            MapAttemptResult result = new MapAttemptResult
            {
                ClearPercent = clearPercent,
                BestClearPercent = progress.bestClearPercent,
                Passed = passedNow,
                FullyCleared = clearedNow,
                NewlyPassed = passedNow && !progress.passRewardClaimed,
                NewlyCleared = clearedNow && !progress.clearRewardClaimed,
                NewlyTwoStar = clearPercent >= TwoStarRewardPercent && !progress.twoStarRewardClaimed,
            };

            progress.passed |= passedNow;
            progress.fullyCleared |= clearedNow;
            return result;
        }

        /// <summary>
        /// Called once the reward has actually been handed over, so a run that is interrupted
        /// between earning and being paid can still be paid next time.
        /// </summary>
        public void MarkTwoStarRewardClaimed(string mapId)
        {
            GetOrCreate(mapId).twoStarRewardClaimed = true;
        }

        public void MarkPassRewardClaimed(string mapId)
        {
            GetOrCreate(mapId).passRewardClaimed = true;
        }

        public void MarkClearRewardClaimed(string mapId)
        {
            GetOrCreate(mapId).clearRewardClaimed = true;
        }
    }
}
