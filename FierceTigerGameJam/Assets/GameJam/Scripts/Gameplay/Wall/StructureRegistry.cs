using System.Collections.Generic;
using UnityEngine;
using GameJam.Gameplay;

namespace GameJam.Gameplay.Wall
{
    /// <summary>
    /// Which block occupies which cell of the structure, so a block that has just been knocked
    /// can find what it was holding up by looking, rather than by asking every other block.
    ///
    /// The support cascade used to scan every sibling under the generated root on each
    /// activation, with a GetComponent per child and a fresh list and sort per call. During a
    /// cascade that is O(N squared) with an allocation per step, and on the test device physics
    /// and its callbacks were taking up to 393 ms of every second with 444 bodies awake. Here a
    /// block walks the cells directly above its own footprint instead, which costs the height of
    /// the structure rather than the size of it.
    ///
    /// Blocks maintain their own entries - registering once they know where they were placed and
    /// dropping out when they are destroyed - so nothing has to remember to keep this in step
    /// when a wall comes apart or a block falls off the table.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StructureRegistry : MonoBehaviour
    {
        private readonly Dictionary<Vector3Int, KnockdownBlock> byCell =
            new Dictionary<Vector3Int, KnockdownBlock>();

        /// <summary>
        /// Reused so a cascade does not allocate a list per activation. A nested lookup borrows
        /// its own list instead: the cascade is not meant to re-enter, but a listener on
        /// Activated could make it, and quietly sharing a buffer through that would drop blocks.
        /// </summary>
        private readonly List<KnockdownBlock> scratch = new List<KnockdownBlock>();
        private bool scratchInUse;

        /// <summary>Highest row anything sits on, which is where a column walk stops.</summary>
        private int highestRow = -1;

        public int CellCount => byCell.Count;

        /// <summary>
        /// Claims every cell the block covers. Called once the block knows where it was placed,
        /// and again if it is ever told somewhere else, so a stale claim cannot outlive it.
        /// </summary>
        public void Register(KnockdownBlock block)
        {
            if (block == null)
            {
                return;
            }

            Vector3Int origin = block.GridPosition;
            Vector3Int size = ResolveSize(block.LogicalSize);

            for (int x = 0; x < size.x; x++)
            {
                for (int y = 0; y < size.y; y++)
                {
                    for (int z = 0; z < size.z; z++)
                    {
                        byCell[new Vector3Int(origin.x + x, origin.y + y, origin.z + z)] = block;
                    }
                }
            }

            int top = origin.y + size.y - 1;
            if (top > highestRow)
            {
                highestRow = top;
            }
        }

        /// <summary>
        /// Releases the cells this block still holds. A cell that has since been claimed by
        /// something else is left alone: the newcomer is the truth about that cell.
        /// </summary>
        public void Unregister(KnockdownBlock block)
        {
            floodPending = true;
            if (block == null)
            {
                return;
            }

            Vector3Int origin = block.GridPosition;
            Vector3Int size = ResolveSize(block.LogicalSize);

            for (int x = 0; x < size.x; x++)
            {
                for (int y = 0; y < size.y; y++)
                {
                    for (int z = 0; z < size.z; z++)
                    {
                        Vector3Int cell = new Vector3Int(origin.x + x, origin.y + y, origin.z + z);
                        if (byCell.TryGetValue(cell, out KnockdownBlock occupant) && occupant == block)
                        {
                            byCell.Remove(cell);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Everything standing over the block's footprint, lowest row first.
        ///
        /// The walk deliberately carries on past an empty row rather than stopping at the first
        /// gap, because that is what the sibling scan it replaces did: anything above the
        /// footprint counted as supported, whether or not the stack below it was solid.
        /// </summary>
        /// <returns>A list owned by the registry. Read it and let go of it.</returns>
        public List<KnockdownBlock> CollectSupportedAbove(KnockdownBlock support, bool stopAtFirstRow)
        {
            List<KnockdownBlock> results = scratchInUse ? new List<KnockdownBlock>() : scratch;
            bool borrowed = results == scratch;
            if (borrowed)
            {
                scratchInUse = true;
                results.Clear();
            }

            Vector3Int origin = support.GridPosition;
            Vector3Int size = ResolveSize(support.LogicalSize);

            for (int row = origin.y + size.y; row <= highestRow; row++)
            {
                int found = results.Count;

                for (int x = 0; x < size.x; x++)
                {
                    for (int z = 0; z < size.z; z++)
                    {
                        Vector3Int cell = new Vector3Int(origin.x + x, row, origin.z + z);
                        if (!byCell.TryGetValue(cell, out KnockdownBlock occupant)
                            || occupant == null
                            || occupant == support
                            || occupant.IsActivated)
                        {
                            continue;
                        }

                        // A wide block covers several of this row's cells and must only be
                        // released once. The list is a handful of entries, so a scan beats
                        // carrying a set around.
                        if (!results.Contains(occupant))
                        {
                            results.Add(occupant);
                        }
                    }
                }

                if (stopAtFirstRow && results.Count > found)
                {
                    break;
                }
            }

            return results;
        }

        /// <summary>Hands the shared list back, so the next cascade step can use it again.</summary>
        public void ReleaseCollected(List<KnockdownBlock> results)
        {
            if (results == scratch)
            {
                scratchInUse = false;
            }
        }

        private bool floodPending;

        /// <summary>
        /// Asks for one connectivity pass on the next physics step. Batched on purpose: a volley
        /// or a collapse activates dozens of blocks in one frame, and one flood serves them all.
        /// </summary>
        public void RequestSupportFlood()
        {
            floodPending = true;
        }

        private void FixedUpdate()
        {
            if (!floodPending)
            {
                return;
            }

            floodPending = false;
            RunSupportFlood();
        }

        /// <summary>
        /// The one structural rule, run whole: everything still standing must REACH THE GROUND
        /// through standing blocks - up, down or sideways, the way the structure was authored to
        /// carry itself. Whatever cannot is released together.
        ///
        /// This replaced two per-block rules that were each wrong in one direction. Climbing the
        /// column above a fallen block left laterally-held overhangs floating forever; releasing
        /// any neighbour without direct support brought a dish standing on four legs down the
        /// moment ONE leg was touched, because every interior plate cell "lacked support" the
        /// instant its neighbour moved. Reachability is the semantic both of those were
        /// approximating: a plate on three remaining legs stays whole, and only a piece truly
        /// cut off from the ground lets go - which is also what makes a 100 percent clear
        /// reachable on every map.
        /// </summary>
        private void RunSupportFlood()
        {
            // Live set first: a block is in play while it is registered and not yet activated.
            HashSet<KnockdownBlock> alive = new HashSet<KnockdownBlock>();
            foreach (KeyValuePair<Vector3Int, KnockdownBlock> entry in byCell)
            {
                if (entry.Value != null && !entry.Value.IsActivated)
                {
                    alive.Add(entry.Value);
                }
            }

            if (alive.Count == 0)
            {
                return;
            }

            Queue<KnockdownBlock> frontier = new Queue<KnockdownBlock>();
            HashSet<KnockdownBlock> reached = new HashSet<KnockdownBlock>();
            foreach (KnockdownBlock block in alive)
            {
                if (block.GridPosition.y <= 0)
                {
                    reached.Add(block);
                    frontier.Enqueue(block);
                }
            }

            while (frontier.Count > 0)
            {
                KnockdownBlock current = frontier.Dequeue();
                Vector3Int origin = current.GridPosition;
                Vector3Int size = ResolveSize(current.LogicalSize);

                for (int x = 0; x < size.x; x++)
                {
                    for (int y = 0; y < size.y; y++)
                    {
                        for (int z = 0; z < size.z; z++)
                        {
                            Vector3Int cell = new Vector3Int(origin.x + x, origin.y + y, origin.z + z);
                            Visit(cell + Vector3Int.up, reached, frontier);
                            Visit(cell + Vector3Int.down, reached, frontier);
                            Visit(cell + Vector3Int.left, reached, frontier);
                            Visit(cell + Vector3Int.right, reached, frontier);
                            Visit(cell + new Vector3Int(0, 0, 1), reached, frontier);
                            Visit(cell + new Vector3Int(0, 0, -1), reached, frontier);
                        }
                    }
                }
            }

            foreach (KnockdownBlock block in alive)
            {
                if (!reached.Contains(block))
                {
                    block.ReleaseFromFlood();
                }
            }
        }

        private void Visit(Vector3Int cell, HashSet<KnockdownBlock> reached, Queue<KnockdownBlock> frontier)
        {
            if (byCell.TryGetValue(cell, out KnockdownBlock neighbour)
                && neighbour != null
                && !neighbour.IsActivated
                && reached.Add(neighbour))
            {
                frontier.Enqueue(neighbour);
            }
        }

        private static Vector3Int ResolveSize(Vector3Int size)
        {
            return new Vector3Int(
                Mathf.Max(1, size.x),
                Mathf.Max(1, size.y),
                Mathf.Max(1, size.z));
        }
    }
}
