#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace GameJam.EditorTools
{
    /// <summary>
    /// Removes the `wall` member from every block in every map JSON, now that walls are gone from
    /// the game.
    ///
    /// Cosmetic rather than load-bearing, and worth being clear about why it is still worth doing.
    /// `KnockdownMapDefinition.TryParse` is `JsonUtility.FromJson`, which ignores keys it has no
    /// field for - so these files already load correctly with the wall support deleted. What the
    /// data costs is a reader's time: the next person to open a map has no way to tell that a
    /// `wall` block is dead weight rather than something the game still honours.
    ///
    /// It edits text rather than reserialising. The maps are not consistently formatted - the
    /// campaign files are 1-space indented and the dev ones 2-space - so a parse-and-rewrite would
    /// touch every line of every file and bury the one change that matters in a diff nobody can
    /// review. Deleting the exact span of the member leaves every other byte alone, so the diff is
    /// deletions only.
    /// </summary>
    public static class MapWallDataStripper
    {
        private const string MapFolder = "Assets/GameJam/Maps";

        private const string MemberName = "\"wall\"";

        [MenuItem("Tools/Smashdown/Strip Wall Data From Maps")]
        public static void StripWallData()
        {
            string[] paths = Directory.GetFiles(MapFolder, "*.json", SearchOption.TopDirectoryOnly);
            if (paths.Length == 0)
            {
                Debug.LogWarning($"{nameof(MapWallDataStripper)} found no maps in {MapFolder}.");
                return;
            }

            int changedFiles = 0;
            int removed = 0;
            List<string> changed = new List<string>();

            for (int i = 0; i < paths.Length; i++)
            {
                string path = paths[i].Replace('\\', '/');
                string text = File.ReadAllText(path);

                if (!TryStrip(text, out string stripped, out int count))
                {
                    continue;
                }

                File.WriteAllText(path, stripped);
                changedFiles++;
                removed += count;
                changed.Add($"{Path.GetFileName(path)} ({count})");
            }

            if (changedFiles == 0)
            {
                Debug.Log(
                    $"{nameof(MapWallDataStripper)}: no map carries wall data. Nothing to do - this "
                    + "menu item is idempotent, so a second run always lands here.");
                return;
            }

            AssetDatabase.Refresh();
            Debug.Log(
                $"{nameof(MapWallDataStripper)} removed {removed} wall member(s) from {changedFiles} "
                + $"map(s): {string.Join(", ", changed)}. The maps parsed identically before this ran - "
                + "JsonUtility ignores unknown keys - so nothing about the game changes.");
        }

        /// <summary>
        /// Cuts every `"wall": { ... }` member out of the text, along with the comma and whitespace
        /// that joined it to its neighbour.
        ///
        /// Brace-counting rather than a regular expression: the member's value is an object, and an
        /// expression that matched up to the first `}` would stop inside it the moment anyone nests
        /// anything. Counting braces is exact for any shape the member ever grows.
        /// </summary>
        private static bool TryStrip(string text, out string stripped, out int count)
        {
            stripped = text;
            count = 0;

            StringBuilder builder = new StringBuilder(text.Length);
            int cursor = 0;

            while (true)
            {
                int start = text.IndexOf(MemberName, cursor, System.StringComparison.Ordinal);
                if (start < 0)
                {
                    builder.Append(text, cursor, text.Length - cursor);
                    break;
                }

                int valueStart = text.IndexOf('{', start);
                if (valueStart < 0)
                {
                    builder.Append(text, cursor, text.Length - cursor);
                    break;
                }

                int depth = 0;
                int end = -1;
                for (int i = valueStart; i < text.Length; i++)
                {
                    if (text[i] == '{')
                    {
                        depth++;
                    }
                    else if (text[i] == '}')
                    {
                        depth--;
                        if (depth == 0)
                        {
                            end = i;
                            break;
                        }
                    }
                }

                if (end < 0)
                {
                    // Unbalanced braces: leave the file entirely alone rather than write half of it.
                    stripped = text;
                    count = 0;
                    return false;
                }

                // Walk back over the comma and whitespace that attached this member to the previous
                // one, so the block it leaves behind is still valid JSON rather than one holding a
                // trailing comma.
                int cut = start;
                while (cut > 0 && char.IsWhiteSpace(text[cut - 1]))
                {
                    cut--;
                }

                if (cut > 0 && text[cut - 1] == ',')
                {
                    cut--;
                }

                builder.Append(text, cursor, cut - cursor);
                cursor = end + 1;
                count++;
            }

            if (count == 0)
            {
                return false;
            }

            stripped = builder.ToString();
            return true;
        }
    }
}
#endif
