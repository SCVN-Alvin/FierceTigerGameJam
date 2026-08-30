using System;
using UnityEngine;

namespace GameJam.Gameplay.Wall
{
    /// <summary>
    /// Serialized shape of a knockdown map JSON file. Field names match the JSON keys because
    /// JsonUtility maps them directly, so renaming a field here changes the file format.
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

        /// <summary>
        /// Optional. Names the wall this block belongs to. Blocks sharing a wall are built as one
        /// body whatever their type or layer, which is how a wall spanning materials or depth is
        /// described. Left out, the block is built as a single block (unless the authoring
        /// component is set to NamedAndDetected, which still merges same-type neighbours on its
        /// own for maps written before wall ids existed).
        ///
        /// JsonUtility fills this field in whether or not the JSON has a "wall" key, so an
        /// absent wall reads as an instance with an empty id: <see cref="WallId"/> is the only
        /// reliable test for whether the map assigned one.
        /// </summary>
        public KnockdownMapWallRef wall;

        /// <summary>The wall this block was assigned to, or null when it was not assigned one.</summary>
        public string WallId => string.IsNullOrEmpty(wall?.wall_id) ? null : wall.wall_id;

        /// <summary>
        /// Whether the wall this block belongs to behaves as an armoured shell. Absent in the
        /// JSON means armoured, so every map written before the flag existed keeps its behaviour.
        /// </summary>
        public bool WallArmored => wall == null || !wall.bare;
    }

    /// <summary>
    /// Membership is held on the block rather than in a separate list of groups: there is then
    /// nothing to keep in sync, and deleting a block cannot leave a group pointing at an id that
    /// no longer exists. Wall-level metadata, if it is ever needed, belongs in its own top-level
    /// table keyed by this id.
    /// </summary>
    [Serializable]
    public sealed class KnockdownMapWallRef
    {
        public string wall_id;

        /// <summary>
        /// Opt OUT of the armoured shell. An armoured wall - the default - takes the
        /// ammunition's wallDamage, which is authored lower than blockDamage so a shot chips the
        /// shell while the same shot would destroy a lone block. A bare wall takes blockDamage,
        /// so it plays as if its cells were loose while staying one cheap rigidbody and one draw
        /// call.
        ///
        /// This is what lets a mission ramp difficulty by adding shells - brick first, concrete
        /// later - without paying for hundreds of loose bodies on the opening frame.
        ///
        /// Stated as an opt-out on purpose. JsonUtility gives a field the JSON does not mention
        /// the type default, and for bool that is false, so a map written before this flag
        /// existed reads as armoured no matter how JsonUtility treats field initialisers. An
        /// "armored = true" field would have been readable but would silently disarm every
        /// existing wall if that assumption were ever wrong.
        /// </summary>
        public bool bare;
    }

    /// <summary>Cell coordinate inside a layer: x is the column, y is the row upward.</summary>
    [Serializable]
    public sealed class KnockdownMapCell
    {
        public int x;
        public int y;
    }
}
