using System;
using GameJam.Gameplay;
using GameJam.Gameplay.Wall;
using UnityEngine;

namespace GameJam.Gameplay.Flow
{
    /// <summary>
    /// How much of the structure the player has destroyed, as a fraction of what was built.
    ///
    /// Progress is counted by looking at what is still standing rather than by tallying
    /// destruction events. A block can leave play several ways - shattered by a shot, broken on
    /// landing, or dropped off the edge into the fall zone - and a wall can take a dozen blocks
    /// with it at once. Counting what remains is right for all of those without a subscription
    /// for each, and cannot drift or double count.
    /// </summary>
    public sealed class LevelProgressTracker : MonoBehaviour
    {
        [SerializeField] private KnockdownLayoutMapAuthoring mapBuilder;

        [Tooltip("Recount this often while playing, for the progress readout. The judgement at "
                 + "the end of a run recounts regardless, so this only affects what is displayed.")]
        [SerializeField] private float sampleInterval = 0.25f;

        /// <summary>Raised when the fraction destroyed changes, for a progress bar to follow.</summary>
        public event Action<float> ProgressChanged;

        private int totalBlocks;
        private float lastReportedPercent;
        private float nextSampleTime;

        /// <summary>Blocks the structure was built from. Zero before a map is built.</summary>
        public int TotalBlocks => totalBlocks;

        public float ClearPercent => CalculateClearPercent();

        /// <summary>
        /// Takes the count the run will be judged against. Called when a map finishes building,
        /// because the denominator has to be what was actually placed rather than what the map
        /// file asked for: a block the map could not place was never there to destroy.
        /// </summary>
        public void BeginRun()
        {
            totalBlocks = ResolveBuilder() != null ? mapBuilder.PlacedBlockCount : 0;
            lastReportedPercent = 0f;
            nextSampleTime = 0f;
            ProgressChanged?.Invoke(0f);
        }

        private void Update()
        {
            if (totalBlocks <= 0 || Time.time < nextSampleTime)
            {
                return;
            }

            nextSampleTime = Time.time + Mathf.Max(0.05f, sampleInterval);

            float percent = CalculateClearPercent();
            if (!Mathf.Approximately(percent, lastReportedPercent))
            {
                lastReportedPercent = percent;
                ProgressChanged?.Invoke(percent);
            }
        }

        /// <summary>
        /// Recounts from the scene. Cheap enough to call at the end of a run and on a slow timer
        /// while playing: a structure is hundreds of objects, not thousands, and this walks only
        /// the direct children of the generated root.
        /// </summary>
        public float CalculateClearPercent()
        {
            if (totalBlocks <= 0)
            {
                return 0f;
            }

            int remaining = CountRemainingBlocks();
            return Mathf.Clamp01(1f - ((float)remaining / totalBlocks));
        }

        public int CountRemainingBlocks()
        {
            Transform generatedRoot = ResolveGeneratedRoot();
            if (generatedRoot == null)
            {
                return 0;
            }

            int remaining = 0;
            for (int i = 0; i < generatedRoot.childCount; i++)
            {
                Transform child = generatedRoot.GetChild(i);

                // A wall stands in for several blocks, so it is worth what it replaced. Checked
                // first because a wall also carries a KnockdownBlock, which would count it once.
                if (child.TryGetComponent(out BreakableWall wall))
                {
                    remaining += Mathf.Max(1, wall.CellCount);
                    continue;
                }

                // Debris is what is left of a destroyed block, so it must not count as standing.
                if (child.GetComponent<ShatteredBlock>() != null)
                {
                    continue;
                }

                if (child.TryGetComponent(out KnockdownBlock block) && block.CountsTowardKnockdown)
                {
                    remaining++;
                }
            }

            return remaining;
        }

        private Transform ResolveGeneratedRoot()
        {
            KnockdownLayoutMapAuthoring builder = ResolveBuilder();
            Transform structureRoot = builder != null ? builder.StructureRoot : null;
            return structureRoot != null
                ? structureRoot.Find(KnockdownLayoutMapAuthoring.GeneratedBlocksRootName)
                : null;
        }

        private KnockdownLayoutMapAuthoring ResolveBuilder()
        {
            if (mapBuilder == null)
            {
                mapBuilder = FindFirstObjectByType<KnockdownLayoutMapAuthoring>(FindObjectsInactive.Include);
            }

            return mapBuilder;
        }
    }
}
