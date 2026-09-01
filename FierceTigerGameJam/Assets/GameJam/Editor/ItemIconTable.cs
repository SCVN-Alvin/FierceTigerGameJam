#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace GameJam.EditorTools
{
    /// <summary>
    /// Pairs every shop item with the artist's icon for each of its levels, in one table.
    ///
    /// One table rather than a constant beside each definition, because the pairing is the thing
    /// most likely to be wrong: three vehicle families in four colours, and a mis-paired icon is
    /// a bug nobody sees until they open the garage. Read together, a swapped pair is obvious.
    ///
    /// The vehicle colours are not an assumption. They were read off the pack prefabs' materials:
    /// every model shares Yellow, Wheel and Black trim, and exactly one colour material is unique
    /// to a family - blue_URP on the A models, Green_B_URP on the B models, Purple_URP on the C
    /// models, and Orange_URP (with Gray_Light_URP) on the D models the catalogue does not use.
    ///
    /// Deviation from Brief 25, which pairs cannon_c with ICN_Tank_Orange: the C models are
    /// purple, and orange belongs to the unused D family. The brief anticipated this ("if B or C
    /// is actually Purple, swap the constant"), so the swap is made here and ICN_Tank_Orange_* is
    /// what waits for a fourth vehicle instead of ICN_Tank_Purple_*.
    /// </summary>
    internal static class ItemIconTable
    {
        private const string SpriteFolder = "Assets/GameJam/Sprites";

        /// <summary>
        /// Art this project authored. A level icon pointing outside it is never a deliberate
        /// choice - see <see cref="ApplyIcon"/>.
        /// </summary>
        private const string ProjectFolder = "Assets/GameJam/";

        /// <summary>
        /// Keyed on the definition's stable id, not its asset name: the two differ for the
        /// ammunition ("Rock.asset" holds "rock_type"), and the id is what never gets renamed.
        /// </summary>
        private static readonly Entry[] Entries =
        {
            new Entry("cannon_a", "ICN_Tank_Blue_1", "ICN_Tank_Blue_2", "ICN_Tank_Blue_3"),
            new Entry("cannon_b", "ICN_Tank_Green_1", "ICN_Tank_Green_2", "ICN_Tank_Green_3"),
            new Entry("cannon_c", "ICN_Tank_Purple_1", "ICN_Tank_Purple_2", "ICN_Tank_Purple_3"),

            // Two levels each, and their _3 sprites are drawn already: a third level is a number
            // in the upgrade config away, and the icon for it is waiting.
            new Entry("rock_type", "ICN_Boom_1", "ICN_Boom_2", "ICN_Boom_3"),
            new Entry("cannon_type", "ICN_Rocket_1", "ICN_Rocket_2", "ICN_Rocket_3"),
        };

        /// <summary>
        /// The icon for a level, or null when the item is not in the table or the sprite has not
        /// been drawn yet. A level past the end of a row falls off rather than clamping: the
        /// definitions resolve an empty icon down to the nearest lower level themselves, and that
        /// fallback reads better than the same sprite written into two slots.
        /// </summary>
        internal static Sprite Resolve(string definitionId, int levelIndex)
        {
            if (string.IsNullOrEmpty(definitionId) || levelIndex < 0)
            {
                return null;
            }

            for (int i = 0; i < Entries.Length; i++)
            {
                if (!string.Equals(Entries[i].Id, definitionId, StringComparison.Ordinal))
                {
                    continue;
                }

                string[] files = Entries[i].Files;
                if (levelIndex >= files.Length || string.IsNullOrEmpty(files[levelIndex]))
                {
                    return null;
                }

                string path = $"{SpriteFolder}/{files[levelIndex]}.png";
                Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (sprite == null)
                {
                    Debug.LogWarning(
                        $"{nameof(ItemIconTable)} found no sprite at {path}, so {definitionId} level "
                        + $"{levelIndex + 1} keeps the icon it had. Check the texture is imported as "
                        + "Sprite (2D and UI).");
                }

                return sprite;
            }

            return null;
        }

        /// <summary>
        /// Writes a level's icon, filling an empty slot and replacing one that points outside this
        /// project's own art.
        ///
        /// The first half is the usual rule: a builder never overwrites a decision somebody made.
        /// The second half is a migration, and the one place this table overwrites - the same
        /// exception <c>GarageScreenBuilder.MoveRootSelectToChild</c> makes, for the same reason.
        /// cannon_c shipped with three sprites from the Layer Lab GUI pack's language-flag set in
        /// its icon slots; a flag on a cannon is not a different opinion about the art, it is a
        /// stray drag, and leaving it would mean the one vehicle this pass cannot fix is the one
        /// that most obviously looks broken. An icon anywhere under Assets/GameJam is left exactly
        /// as it is, so anything drawn for this game - including a hand-swapped ICN_ - survives.
        /// </summary>
        internal static void ApplyIcon(
            SerializedProperty iconProperty,
            string definitionId,
            int levelIndex,
            UnityEngine.Object owner)
        {
            if (iconProperty == null)
            {
                return;
            }

            UnityEngine.Object current = iconProperty.objectReferenceValue;
            if (current != null && !IsForeignArt(current))
            {
                return;
            }

            Sprite sprite = Resolve(definitionId, levelIndex);
            if (sprite == null || sprite == current)
            {
                return;
            }

            if (current != null)
            {
                Debug.LogWarning(
                    $"{nameof(ItemIconTable)} replaced {definitionId} level {levelIndex + 1}'s icon: it "
                    + $"pointed at \"{AssetDatabase.GetAssetPath(current)}\", which is not this game's art. "
                    + $"It now uses {sprite.name}.",
                    owner);
            }

            iconProperty.objectReferenceValue = sprite;
        }

        /// <summary>
        /// Whether a sprite comes from somewhere other than this game's own folder - an imported
        /// pack, a demo scene's art. A missing reference reads as null and never reaches here.
        /// </summary>
        private static bool IsForeignArt(UnityEngine.Object sprite)
        {
            string path = AssetDatabase.GetAssetPath(sprite);
            return !string.IsNullOrEmpty(path) && !path.StartsWith(ProjectFolder, StringComparison.Ordinal);
        }

        /// <summary>One item, and its icon file per level. Index 0 is level 1.</summary>
        private readonly struct Entry
        {
            public Entry(string id, params string[] files)
            {
                Id = id;
                Files = files;
            }

            public string Id { get; }

            public string[] Files { get; }
        }
    }
}
#endif
