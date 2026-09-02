using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GameJam.UI
{
    /// <summary>
    /// Hand-rolled vertical scroller for the mission board, added at runtime by
    /// MissionPanelView.EnsureScroll.
    ///
    /// Why not the prefab's own ScrollRect (on "List"): it is authored horizontal-only, and
    /// the two attempts to ride it - adding a second ScrollRect, then flipping its vertical
    /// axis on - produced first snap-back fighting, then a dead board (2026-09-02, three
    /// Falcon screenshots). This component lives on the VIEWPORT, closer to the cards than
    /// that ScrollRect, so event bubbling hands it every drag first and the authored one
    /// simply never sees them; nothing of the dev's setup is modified or disabled.
    ///
    /// Drag follows the finger 1:1; release keeps the last drag velocity and decays it
    /// (Falcon: "giu hand keo len... co quan tinh neu keo manh"); the offset is clamped
    /// between the top row and the last row, both measured live so card-count changes need
    /// no re-setup. Delete this file and EnsureScroll to remove the feature.
    /// </summary>
    public sealed class MissionBoardScroller : MonoBehaviour,
        IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private RectTransform content;
        private float baseY;                                // content y when parked at the top
        private float offset;                               // 0 = top, grows as we scroll down
        private float velocity;
        private bool dragging;
        private Canvas canvas;

        private float MaxOffset
        {
            get
            {
                RectTransform viewport = (RectTransform)transform;
                return Mathf.Max(0f, content.rect.height - viewport.rect.height);
            }
        }

        /// <summary>Wire the card grid; its current position becomes "parked at the top".</summary>
        public void Init(RectTransform scrollContent)
        {
            content = scrollContent;
            baseY = content.anchoredPosition.y;
            canvas = GetComponentInParent<Canvas>();
            ResetToTop();
        }

        /// <summary>Mission switched: back to the first row, no leftover glide.</summary>
        public void ResetToTop()
        {
            offset = 0f;
            velocity = 0f;
            Apply();
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            dragging = true;
            velocity = 0f;
        }

        public void OnDrag(PointerEventData eventData)
        {
            float delta = eventData.delta.y / Scale();
            Scroll(delta);

            // Remember the finger's speed for the release glide, lightly smoothed.
            float instant = delta / Mathf.Max(Time.unscaledDeltaTime, 0.0001f);
            velocity = Mathf.Lerp(velocity, instant, 0.5f);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            dragging = false;
        }

        private void Update()
        {
            if (dragging || content == null || Mathf.Abs(velocity) < 5f)
            {
                return;
            }

            Scroll(velocity * Time.unscaledDeltaTime);
            velocity *= Mathf.Pow(0.05f, Time.unscaledDeltaTime);   // glide dies in ~1s

            // A glide that hits either end stops instead of buzzing against the clamp.
            if (offset <= 0f || offset >= MaxOffset)
            {
                velocity = 0f;
            }
        }

        private void Scroll(float delta)
        {
            if (content == null)
            {
                return;
            }

            offset = Mathf.Clamp(offset + delta, 0f, MaxOffset);
            Apply();
        }

        private void Apply()
        {
            if (content != null)
            {
                Vector2 position = content.anchoredPosition;
                position.y = baseY + offset;
                content.anchoredPosition = position;
            }
        }

        private float Scale()
        {
            return canvas != null && canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;
        }
    }
}
