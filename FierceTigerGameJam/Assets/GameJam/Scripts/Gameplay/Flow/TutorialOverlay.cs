using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GameJam.Gameplay.Flow
{
    /// <summary>
    /// The furniture every tutorial overlay in the game is made of: the dark filter with the
    /// transparent hole, the pointing hand, and the rounded panel the game speaks from.
    ///
    /// Lifted out of <see cref="DragHintController"/> unchanged when the upgrade guide needed the
    /// same hole and the same hand. Nothing here decides anything - no anchors, no text, no
    /// timing - it only builds the pieces, so each lesson stays the one place that says where its
    /// own hole goes and what its own words are. The pieces are still built in code at show time,
    /// the same stopgap pattern the drag hint and the shot-boost popup use.
    ///
    /// The panel art (UI_Tutorial.png) has "Tap to shoot" BAKED INTO THE PIXELS. Rather than edit
    /// the texture, <see cref="CreatePanel"/> lays a flat patch in the art's own paper colour over
    /// the baked words and the real label draws on top ("fake UXUI", user-approved) - which is why
    /// a panel is never just an Image.
    /// </summary>
    internal static class TutorialOverlay
    {
        /// <summary>
        /// Where the transparent middle of the dim sprite sits within its own rect, as a pivot.
        ///
        /// Not measured from the texture - taken from the drag hint, which was positioned by eye
        /// until its hole sat on the board. Using it as the rect's pivot is what lets a caller
        /// drop the hole onto any point by naming that point alone.
        /// </summary>
        public static readonly Vector2 HolePivot = new Vector2(0.5f, 0.48f);

        /// <summary>
        /// Oversized on purpose: the sprite's dark edges have to run off every side of the screen
        /// wherever the hole is put, or the dim would stop short of a corner.
        /// </summary>
        public static readonly Vector2 DimSize = new Vector2(1400f, 2300f);

        public static readonly Vector2 PanelSize = new Vector2(560f, 246f);

        /// <summary>Sampled from the panel art's paper, for the patch over its baked words.</summary>
        public static readonly Color PanelPaper = new Color(0.984f, 0.973f, 0.937f);

        /// <summary>The blue the panel's words are written in.</summary>
        public static readonly Color PanelInk = new Color(0.13f, 0.25f, 0.55f);

        /// <summary>
        /// The full-screen root a lesson hangs its pieces from, last in the canvas so it draws
        /// over whatever screen is up.
        /// </summary>
        /// <param name="blocksRaycasts">
        /// False leaves every tap falling through to the game underneath, which is what a lesson
        /// that teaches a gesture needs. True makes the root a wall.
        /// </param>
        public static RectTransform CreateRoot(RectTransform parent, string name, bool blocksRaycasts)
        {
            GameObject rootGo = new GameObject(name, typeof(RectTransform), typeof(CanvasGroup));
            RectTransform root = (RectTransform)rootGo.transform;
            root.SetParent(parent, false);
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = Vector2.zero;
            root.offsetMax = Vector2.zero;
            root.SetAsLastSibling();
            rootGo.GetComponent<CanvasGroup>().blocksRaycasts = blocksRaycasts;
            return root;
        }

        /// <summary>
        /// The dark filter with the spotlight hole, pivoted on the hole so the caller only has to
        /// say where the hole belongs. Never a raycast target: the point of a spotlight is that
        /// what it lights can still be touched.
        /// </summary>
        public static RectTransform CreateSpotlight(RectTransform root, Sprite dimSprite)
        {
            GameObject dimGo = new GameObject("Dim", typeof(RectTransform));
            RectTransform dimRect = (RectTransform)dimGo.transform;
            dimRect.SetParent(root, false);
            dimRect.pivot = HolePivot;
            dimRect.anchoredPosition = Vector2.zero;
            dimRect.sizeDelta = DimSize;

            Image dim = dimGo.AddComponent<Image>();
            dim.sprite = dimSprite;
            dim.raycastTarget = false;
            return dimRect;
        }

        /// <summary>
        /// The four dark strips that fence a control off from the rest of the screen.
        ///
        /// A cut-out is built out of what surrounds it rather than out of the hole itself: the
        /// strips tile the canvas ABOVE, BELOW, LEFT and RIGHT of the target, leaving an untouched
        /// gap over the control. Unlike the spotlight above they DO take raycasts, so every tap
        /// outside the gap dies on them and the only live thing on screen is the control the
        /// lesson is pointing at. That is the difference between a lesson that suggests and one
        /// that holds - and it is why the hole is always exactly the size of its target, where a
        /// single pre-sized filter sprite can only ever be the size it was drawn.
        ///
        /// Pivoted at the bottom-left so a strip is positioned by its min corner and sized by the
        /// span, which is the form the layout maths falls out in.
        /// </summary>
        public static RectTransform[] CreateBlockingStrips(RectTransform root, Color dimColor)
        {
            RectTransform[] strips = new RectTransform[4];
            for (int i = 0; i < strips.Length; i++)
            {
                GameObject stripGo = new GameObject($"Block{i}", typeof(RectTransform));
                RectTransform strip = (RectTransform)stripGo.transform;
                strip.SetParent(root, false);
                strip.anchorMin = new Vector2(0.5f, 0.5f);
                strip.anchorMax = new Vector2(0.5f, 0.5f);
                strip.pivot = Vector2.zero;

                Image dim = stripGo.AddComponent<Image>();
                dim.color = dimColor;
                dim.raycastTarget = true;
                strips[i] = strip;
            }

            return strips;
        }

        /// <summary>
        /// The soft-edged filter laid over the gap the strips leave, purely to hide their corners.
        ///
        /// Four rectangles meeting around a hole read as four rectangles; the tutorial's filter art
        /// has a feathered transparent middle, so scaling it over the gap turns the join into a
        /// falloff. Visual only - it never takes a raycast, because the whole point of the gap is
        /// that the control inside it is still pressable.
        /// </summary>
        public static RectTransform CreateFeather(RectTransform root, Sprite dimSprite)
        {
            if (dimSprite == null)
            {
                // No art is survivable: the strips alone still fence the control off, they just
                // meet at hard corners. A missing sprite must not cost the lesson its blocking.
                return null;
            }

            GameObject featherGo = new GameObject("Feather", typeof(RectTransform));
            RectTransform feather = (RectTransform)featherGo.transform;
            feather.SetParent(root, false);
            feather.anchorMin = new Vector2(0.5f, 0.5f);
            feather.anchorMax = new Vector2(0.5f, 0.5f);
            feather.pivot = new Vector2(0.5f, 0.5f);

            Image featherImage = featherGo.AddComponent<Image>();
            featherImage.sprite = dimSprite;
            featherImage.raycastTarget = false;
            return feather;
        }

        /// <summary>
        /// Lays the four strips around a rect, leaving it uncovered. Coordinates are in the
        /// overlay root's own space, and a strip that comes out empty is switched off rather than
        /// left as a zero-sized raycast target.
        /// </summary>
        public static void LayoutStripsAround(RectTransform[] strips, Rect canvas, Vector2 min, Vector2 max)
        {
            if (strips == null || strips.Length < 4)
            {
                return;
            }

            SetStrip(strips[0], new Vector2(canvas.xMin, max.y), new Vector2(canvas.xMax, canvas.yMax));
            SetStrip(strips[1], new Vector2(canvas.xMin, canvas.yMin), new Vector2(canvas.xMax, min.y));
            SetStrip(strips[2], new Vector2(canvas.xMin, min.y), new Vector2(min.x, max.y));
            SetStrip(strips[3], new Vector2(max.x, min.y), new Vector2(canvas.xMax, max.y));
        }

        private static void SetStrip(RectTransform strip, Vector2 min, Vector2 max)
        {
            if (strip == null)
            {
                return;
            }

            Vector2 size = Vector2.Max(Vector2.zero, max - min);
            strip.anchoredPosition = min;
            strip.sizeDelta = size;
            strip.gameObject.SetActive(size.x > 0.5f && size.y > 0.5f);
        }

        /// <summary>
        /// The rounded speech panel. With no art it is an empty rect that still positions the
        /// words, which is what a scene missing the sprite should show rather than a white slab.
        /// </summary>
        public static RectTransform CreatePanel(RectTransform root, Sprite panelSprite)
        {
            GameObject panelGo = new GameObject("Panel", typeof(RectTransform));
            RectTransform panelRect = (RectTransform)panelGo.transform;
            panelRect.SetParent(root, false);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.anchoredPosition = Vector2.zero;
            panelRect.sizeDelta = PanelSize;

            if (panelSprite != null)
            {
                Image panel = panelGo.AddComponent<Image>();
                panel.sprite = panelSprite;
                panel.preserveAspect = true;
                panel.raycastTarget = false;

                // The patch over the baked "Tap to shoot" (see the class comment). Offsets keep
                // it inside the art's blue rim.
                GameObject coverGo = new GameObject("Cover", typeof(RectTransform));
                RectTransform coverRect = (RectTransform)coverGo.transform;
                coverRect.SetParent(panelRect, false);
                coverRect.anchorMin = Vector2.zero;
                coverRect.anchorMax = Vector2.one;
                coverRect.offsetMin = new Vector2(70f, 55f);
                coverRect.offsetMax = new Vector2(-70f, -55f);
                Image cover = coverGo.AddComponent<Image>();
                cover.color = PanelPaper;
                cover.raycastTarget = false;
            }

            return panelRect;
        }

        /// <summary>
        /// The words inside a panel. Blue on the paper patch, white when there is no art to write
        /// on, so a scene without the sprite still reads against the dim.
        /// </summary>
        public static TMP_Text CreateLabel(RectTransform panelRect, TMP_FontAsset font, bool onPanelArt)
        {
            GameObject labelGo = new GameObject("Label", typeof(RectTransform));
            RectTransform labelRect = (RectTransform)labelGo.transform;
            labelRect.SetParent(panelRect, false);
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(30f, 20f);
            labelRect.offsetMax = new Vector2(-30f, -20f);

            TMP_Text label = labelGo.AddComponent<TextMeshProUGUI>();
            if (font != null)
            {
                label.font = font;
            }

            label.fontSize = 44f;
            label.fontStyle = FontStyles.Bold;
            label.color = onPanelArt ? PanelInk : Color.white;
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;
            return label;
        }

        /// <summary>
        /// The pointing hand. Returned as its Image because every lesson animates its alpha; the
        /// rect comes off the Image when one needs to move it too.
        /// </summary>
        public static Image CreateHand(RectTransform root, Sprite handSprite)
        {
            GameObject handGo = new GameObject("Hand", typeof(RectTransform));
            RectTransform hand = (RectTransform)handGo.transform;
            hand.SetParent(root, false);
            hand.pivot = new Vector2(0.5f, 0.5f);
            hand.anchoredPosition = Vector2.zero;
            hand.sizeDelta = new Vector2(140f, 140f);

            Image handImage = handGo.AddComponent<Image>();
            if (handSprite != null)
            {
                handImage.sprite = handSprite;
                handImage.preserveAspect = true;
            }

            handImage.raycastTarget = false;
            return handImage;
        }

        public static void SetAlpha(Graphic graphic, float alpha)
        {
            if (graphic == null)
            {
                return;
            }

            Color color = graphic.color;
            color.a = alpha;
            graphic.color = color;
        }
    }
}
