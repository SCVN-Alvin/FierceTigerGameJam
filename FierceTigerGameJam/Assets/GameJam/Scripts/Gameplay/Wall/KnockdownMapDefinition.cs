using System;
using UnityEngine;

namespace GameJam.Gameplay.Wall
{
    /// <summary>
    /// Serialized shape of a knockdown map JSON file. Field names match the JSON keys because
    /// JsonUtility maps them directly, so renaming a field here changes the file format.
    ///
    /// Dropping a field is the safe direction: JsonUtility ignores a key it has no field for, so
    /// the maps still carrying the retired "wall" objects parse exactly as before and simply lose
    /// the data. That is what lets the files be cleaned in their own pass rather than this one.
    /// </summary>
    [Serializable]
    public sealed class KnockdownMapDefinition
    {
        public const int SupportedSchemaVersion = 1;

        public int schemaVersion;
        public string id;
        public KnockdownMapGrid grid;
        public KnockdownMapLayer[] layers;

        /// <summary>
        /// Parses and validates a map. Shared by the runtime builder and the inspector so both
        /// accept exactly the same files.
        /// </summary>
        public static bool TryParse(string json, out KnockdownMapDefinition map, out string error)
        {
            map = null;

            if (string.IsNullOrEmpty(json))
            {
                error = "The map JSON is empty.";
                return false;
            }

            try
            {
                map = JsonUtility.FromJson<KnockdownMapDefinition>(json);
            }
            catch (Exception exception)
            {
                error = $"The map JSON could not be parsed: {exception.Message}";
                return false;
            }

            if (map == null || map.grid == null || map.layers == null)
            {
                error = "The map JSON is missing a grid or layers block.";
                return false;
            }

            if (map.schemaVersion != SupportedSchemaVersion)
            {
                error = $"The map declares schemaVersion {map.schemaVersion}, but only {SupportedSchemaVersion} is supported.";
                return false;
            }

            if (map.grid.width <= 0 || map.grid.height <= 0)
            {
                error = "The map grid needs a positive width and height.";
                return false;
            }

            if (map.grid.cellSize <= 0f || map.grid.layerDepth <= 0f)
            {
                error = "The map grid needs a positive cellSize and layerDepth.";
                return false;
            }

            error = null;
            return true;
        }

        /// <summary>Highest Z index present, used to centre the layer stack.</summary>
        public int MaxLevel()
        {
            int maxLevel = 0;
            for (int i = 0; i < layers.Length; i++)
            {
                if (layers[i] != null)
                {
                    maxLevel = Mathf.Max(maxLevel, layers[i].level);
                }
            }

            return maxLevel;
        }

        public int CountBlocks()
        {
            int total = 0;
            for (int i = 0; i < layers.Length; i++)
            {
                if (layers[i]?.blocks != null)
                {
                    total += layers[i].blocks.Length;
                }
            }

            return total;
        }
    }

    /// <summary>
    /// A layer is a vertical XY slice, so the grid is sized in columns (width) and rows
    /// (height). Layers are stacked along Z, one <see cref="layerDepth"/> apart.
    /// </summary>
    [Serializable]
    public sealed class KnockdownMapGrid
    {
        public int width;
        public int height;
        public float cellSize;
        public float layerDepth;
    }

    /// <summary>One XY slice of the structure. <see cref="level"/> is its Z index.</summary>
    [Serializable]
    public sealed class KnockdownMapLayer
    {
        public int level;
        public KnockdownMapBlock[] blocks;
    }

    [Serializable]
    public sealed class KnockdownMapBlock
    {
        public string id;
        public string type;
        public KnockdownMapCell position;

        /// <summary>
        /// Quarter turn of the block's footprint inside its layer, in degrees. This describes
        /// which cells the block occupies, not a transform angle: 0 and 180 keep the authored
        /// width and height, 90 and 270 swap them. A 2x1 block at (2,1) covers (2,1) and (3,1)
        /// at rotation 0, and (2,1) and (2,2) at rotation 90.
        /// </summary>
        public float rotation;
    }

    /// <summary>Cell coordinate inside a layer: x is the column, y is the row upward.</summary>
    [Serializable]
    public sealed class KnockdownMapCell
    {
        public int x;
        public int y;
    }
}
