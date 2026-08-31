#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEngine;

namespace GameJam.EditorTools
{
    /// <summary>
    /// The one place the game's font is named. Every builder that news up a label and the sweep
    /// that converts the labels already authored read it from here, so replacing the font later
    /// is a single edit instead of a hunt through a dozen builders that each hard-coded a path.
    ///
    /// It is a TMP <em>bitmap</em> asset: the dark outline is baked into the 64 px atlas rather
    /// than drawn by the shader. That is why the sweep also clears <see cref="FontStyles.Bold"/> -
    /// faux-bold thickens a glyph whose outline is already painted on, so the two strokes fight -
    /// and why the material must be assigned alongside the font: a label still carrying its old
    /// font's material samples the old atlas and draws nothing but blank quads.
    /// </summary>
    internal static class GameFonts
    {
        /// <summary>
        /// Spelled out rather than found by name so a second font with a similar name added to
        /// the project later cannot quietly win the search.
        /// </summary>
        internal const string DefaultFontPath =
            "Assets/Layer Lab/GUI Pro-CasualGame/ResourcesData/Fonts/LilitaOne-Regular Outline 64 Bitmap.asset";

        private static TMP_FontAsset cached;

        /// <summary>
        /// Null when the asset has been moved or deleted. Callers are expected to say so and stop,
        /// rather than leaving labels on whatever font they happened to have.
        /// </summary>
        internal static TMP_FontAsset Default
        {
            get
            {
                // Unity's fake-null covers the case where the cached asset was reimported out from
                // under us between calls, so this reloads instead of handing back a dead reference.
                if (cached == null)
                {
                    cached = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(DefaultFontPath);
                }

                return cached;
            }
        }

        /// <summary>
        /// The font's own sub-asset material, which is what a label has to point at for the baked
        /// outline to show up at all. Null whenever <see cref="Default"/> is.
        /// </summary>
        internal static Material DefaultMaterial
        {
            get
            {
                TMP_FontAsset font = Default;
                return font != null ? font.material : null;
            }
        }
    }
}
#endif
