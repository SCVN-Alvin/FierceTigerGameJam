using System.Collections.Generic;
using UnityEngine;

namespace GameJam.Gameplay.Wall
{
    /// <summary>
    /// Finds rectangles of same-type blocks inside one layer, so a run of identical cubes can be
    /// built as a single wall instead of a crowd of separate bodies. A layer is already an XY
    /// plane with its level as the Z index, so a rectangle found here is a wall panel.
    /// </summary>
    public static class WallGrouping
    {
        /// <summary>A run of same-type cells, and the cell addresses that make it up.</summary>
        public sealed class WallRect
        {
            public string Type;
            public int X;
            public int Y;
            public int Width;
            public int Height;

            /// <summary>In the order they were claimed, row by row.</summary>
            public readonly List<Vector2Int> Cells = new List<Vector2Int>();

            public int Area => Width * Height;
        }

        /// <summary>
        /// Greedy maximal-rectangle sweep. Each unclaimed cell grows as wide as it can, then as
        /// tall as it can while every cell of the next row still matches, and the whole block is
        /// claimed before moving on. Cells are visited in row order, so the same layer always
        /// produces the same rectangles.
        ///
        /// The result is not the theoretical minimum number of rectangles - that is a much more
        /// expensive problem - but it collapses the long horizontal and vertical runs that walls
        /// are actually made of, which is where nearly all of the saving is.
        /// </summary>
        public static List<WallRect> Find(IReadOnlyDictionary<Vector2Int, string> cells)
        {
            List<WallRect> rects = new List<WallRect>();
            if (cells == null || cells.Count == 0)
            {
                return rects;
            }

            HashSet<Vector2Int> claimed = new HashSet<Vector2Int>();
            List<Vector2Int> order = new List<Vector2Int>(cells.Keys);
            order.Sort(CompareRowMajor);

            for (int i = 0; i < order.Count; i++)
            {
                Vector2Int start = order[i];
                if (claimed.Contains(start))
                {
                    continue;
                }

                string type = cells[start];

                int width = 1;
                while (Matches(cells, claimed, new Vector2Int(start.x + width, start.y), type))
                {
                    width++;
                }

                int height = 1;
                while (RowMatches(cells, claimed, start, width, height, type))
                {
                    height++;
                }

                WallRect rect = new WallRect
                {
                    Type = type,
                    X = start.x,
                    Y = start.y,
                    Width = width,
                    Height = height,
                };

                for (int row = 0; row < height; row++)
                {
                    for (int column = 0; column < width; column++)
                    {
                        Vector2Int cell = new Vector2Int(start.x + column, start.y + row);
                        claimed.Add(cell);
                        rect.Cells.Add(cell);
                    }
                }

                rects.Add(rect);
            }

            return rects;
        }

        private static bool RowMatches(
            IReadOnlyDictionary<Vector2Int, string> cells,
            HashSet<Vector2Int> claimed,
            Vector2Int start,
            int width,
            int row,
            string type)
        {
            for (int column = 0; column < width; column++)
            {
                if (!Matches(cells, claimed, new Vector2Int(start.x + column, start.y + row), type))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool Matches(
            IReadOnlyDictionary<Vector2Int, string> cells,
            HashSet<Vector2Int> claimed,
            Vector2Int cell,
            string type)
        {
            return !claimed.Contains(cell) && cells.TryGetValue(cell, out string other) && other == type;
        }

        /// <summary>Rows bottom to top, and left to right within a row.</summary>
        private static int CompareRowMajor(Vector2Int left, Vector2Int right)
        {
            int byRow = left.y.CompareTo(right.y);
            return byRow != 0 ? byRow : left.x.CompareTo(right.x);
        }

        /// <summary>The cells one block covers: its lowest cell and how many cells it spans.</summary>
        public readonly struct CellBox
        {
            public readonly Vector3Int Position;
            public readonly Vector3Int Size;

            public CellBox(Vector3Int position, Vector3Int size)
            {
                Position = position;
                Size = size;
            }
        }

        private static readonly Vector3Int[] FaceNeighbours =
        {
            new Vector3Int(1, 0, 0),
            new Vector3Int(-1, 0, 0),
            new Vector3Int(0, 1, 0),
            new Vector3Int(0, -1, 0),
            new Vector3Int(0, 0, 1),
            new Vector3Int(0, 0, -1),
        };

        /// <summary>
        /// Splits blocks into the groups that actually touch, over the six face neighbours of
        /// every cell they cover. A wall is only one body if its blocks are connected: a single
        /// collider stretched around two separate clusters would stop shots in the empty space
        /// between them.
        ///
        /// Groups come back in the order their first block appears in the input, and the indices
        /// inside a group keep the input's order, so the same set of blocks always splits the
        /// same way.
        /// </summary>
        /// <returns>Each group as the indices into <paramref name="boxes"/> that make it up.</returns>
        public static List<List<int>> FindConnectedGroups(IReadOnlyList<CellBox> boxes)
        {
            List<List<int>> groups = new List<List<int>>();
            if (boxes == null || boxes.Count == 0)
            {
                return groups;
            }

            // Cells are reserved before this runs, so no two blocks claim the same one; a
            // duplicate could only come from a caller that skipped that, and the first block to
            // claim the cell keeps it.
            Dictionary<Vector3Int, int> ownerByCell = new Dictionary<Vector3Int, int>();
            for (int i = 0; i < boxes.Count; i++)
            {
                foreach (Vector3Int cell in Cells(boxes[i]))
                {
                    if (!ownerByCell.ContainsKey(cell))
                    {
                        ownerByCell[cell] = i;
                    }
                }
            }

            bool[] reached = new bool[boxes.Count];
            List<int> frontier = new List<int>();

            for (int i = 0; i < boxes.Count; i++)
            {
                if (reached[i])
                {
                    continue;
                }

                List<int> group = new List<int>();
                reached[i] = true;
                frontier.Add(i);

                while (frontier.Count > 0)
                {
                    int index = frontier[frontier.Count - 1];
                    frontier.RemoveAt(frontier.Count - 1);
                    group.Add(index);

                    foreach (Vector3Int cell in Cells(boxes[index]))
                    {
                        for (int n = 0; n < FaceNeighbours.Length; n++)
                        {
                            if (ownerByCell.TryGetValue(cell + FaceNeighbours[n], out int other)
                                && !reached[other])
                            {
                                reached[other] = true;
                                frontier.Add(other);
                            }
                        }
                    }
                }

                group.Sort();
                groups.Add(group);
            }

            return groups;
        }

        private static IEnumerable<Vector3Int> Cells(CellBox box)
        {
            for (int x = 0; x < Mathf.Max(1, box.Size.x); x++)
            {
                for (int y = 0; y < Mathf.Max(1, box.Size.y); y++)
                {
                    for (int z = 0; z < Mathf.Max(1, box.Size.z); z++)
                    {
                        yield return new Vector3Int(box.Position.x + x, box.Position.y + y, box.Position.z + z);
                    }
                }
            }
        }
    }
}
