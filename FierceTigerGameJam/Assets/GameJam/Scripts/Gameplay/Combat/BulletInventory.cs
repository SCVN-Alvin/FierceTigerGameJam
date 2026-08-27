using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameJam.Gameplay.Combat
{
    /// <summary>
    /// The ammunition the player takes into one run, and what is left of it.
    ///
    /// Deliberately not saved. A run's ammunition is chosen for that run and spent inside it, so
    /// persisting it would only create a way for a half-finished run to leak into the next one.
    /// What the player permanently owns - which kinds are unlocked, and at what level - is a
    /// different question, and lives in the saved bullet record.
    ///
    /// Picking and spending are the same counts at two different times, so they are one object.
    /// The player fills a budget before the run, and the run draws that budget down.
    /// </summary>
    [CreateAssetMenu(menuName = "GameJam/Bullet Inventory", fileName = "BulletInventory")]
    public sealed class BulletInventory : ScriptableObject
    {
        [NonSerialized] private readonly Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.Ordinal);
        [NonSerialized] private int pickLimit;

        /// <summary>Raised whenever any count changes, during picking or during the run.</summary>
        public event Action Changed;

        /// <summary>
        /// Raised the moment the last shot is spent. This is the losing condition's cue, though
        /// the run should let the structure settle before judging: the shot that empties the
        /// inventory may still be about to bring half the map down.
        /// </summary>
        public event Action Emptied;

        /// <summary>How many bullets this map allows in total, across every type.</summary>
        public int PickLimit => pickLimit;

        public int TotalCount
        {
            get
            {
                int total = 0;
                foreach (KeyValuePair<string, int> entry in counts)
                {
                    total += entry.Value;
                }

                return total;
            }
        }

        /// <summary>How many more may still be picked. Zero once the budget is full.</summary>
        public int RemainingPicks => Mathf.Max(0, pickLimit - TotalCount);

        public bool IsEmpty => TotalCount <= 0;

        public IReadOnlyDictionary<string, int> Counts => counts;

        /// <summary>Starts a fresh pick with this map's budget, discarding anything held before.</summary>
        public void BeginPick(int limit)
        {
            counts.Clear();
            pickLimit = Mathf.Max(0, limit);
            Changed?.Invoke();
        }

        public int GetCount(string bulletId)
        {
            return !string.IsNullOrEmpty(bulletId) && counts.TryGetValue(bulletId, out int count) ? count : 0;
        }

        /// <summary>
        /// Adds to the pick if the budget allows. The player mixes types freely, so the only rule
        /// is the total: ten bullets may be ten rocks, or four rocks and six cannon balls.
        /// </summary>
        public bool TryPick(string bulletId, int amount = 1)
        {
            if (string.IsNullOrEmpty(bulletId) || amount <= 0 || amount > RemainingPicks)
            {
                return false;
            }

            counts[bulletId] = GetCount(bulletId) + amount;
            Changed?.Invoke();
            return true;
        }

        /// <summary>
        /// Adds rounds outside the pick. The pick limit is the rule for what may be carried in, not
        /// for what may be bought mid-run, so this ignores it; a continue is the only caller.
        /// </summary>
        public void Grant(string bulletId, int amount)
        {
            if (string.IsNullOrEmpty(bulletId) || amount <= 0)
            {
                return;
            }

            counts[bulletId] = GetCount(bulletId) + amount;
            Changed?.Invoke();
        }

        public bool TryUnpick(string bulletId, int amount = 1)
        {
            int current = GetCount(bulletId);
            if (amount <= 0 || current < amount)
            {
                return false;
            }

            int next = current - amount;
            if (next > 0)
            {
                counts[bulletId] = next;
            }
            else
            {
                counts.Remove(bulletId);
            }

            Changed?.Invoke();
            return true;
        }

        /// <summary>
        /// Takes one bullet of a kind for a shot that is actually being fired. Returns false when
        /// there are none of that kind left, which is the caller's cue to refuse the shot rather
        /// than fire for free.
        /// </summary>
        public bool TrySpend(string bulletId)
        {
            if (GetCount(bulletId) <= 0)
            {
                return false;
            }

            TryUnpick(bulletId);

            if (IsEmpty)
            {
                Emptied?.Invoke();
            }

            return true;
        }

        /// <summary>The first kind with any left, for defaulting the selection after one runs out.</summary>
        public string FindFirstAvailable()
        {
            foreach (KeyValuePair<string, int> entry in counts)
            {
                if (entry.Value > 0)
                {
                    return entry.Key;
                }
            }

            return null;
        }

        public void Clear()
        {
            counts.Clear();
            pickLimit = 0;
            Changed?.Invoke();
        }

        private void OnDisable()
        {
            // Domain reload does this anyway, but not when the editor is set to skip it, and a
            // run's leftovers must never appear at the start of the next one.
            counts.Clear();
            pickLimit = 0;
        }
    }
}
