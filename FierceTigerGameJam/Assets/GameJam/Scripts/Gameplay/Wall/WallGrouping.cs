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
    }
}
