using System.Collections;
using GameJam.Data;
using GameJam.Gameplay.Wall;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace GameJam.Gameplay.Flow
{
    /// <summary>
    /// Lesson two of the tutorial: HOLD &amp; DRAG TO ROTATE, taught on the first campaign map.
    ///
    /// The launch tutorial (TutorialController) teaches tapping to shoot on its own map. Nothing
    /// ever taught the orbit drag, so players cleared what they could see from the front and
    /// called the rest a bug. This overlay appears once, on mission1_map1, right after the play
    /// state starts: dim everywhere but the board, a hand swiping left then right over the
    /// structure, and the words in the same rounded panel the first tutorial speaks from.
    ///
    /// While the overlay is up, firing is refused (see <see cref="BlockingFire"/> and the release
    /// branch of CannonInputShooter) - the one gesture that works is the one being taught. A
    /// sideways drag of 60+ screen pixels while pressed counts as the lesson learned: the flag
    /// is saved (UserTutorialData.dragTaught) and the overlay never returns.
    ///
    /// The whole overlay is built in code at show time - same stopgap pattern as the fail-screen
    /// stepper and the tap-to-play labels; delete this file, its scene component and the
    /// CannonInputShooter check to remove the feature.
    ///
    /// The panel art (UI_Tutorial.png) has "Tap to shoot" BAKED INTO THE PIXELS - that was the
    /// mystery overlap of 2026-09-02. Instead of editing the texture, a flat patch in the art's
    /// own paper colour covers the baked words and the real label draws on top ("fake UXUI",
    /// user-approved).
    /// </summary>
    public sealed class DragHintController : MonoBehaviour
    {
        /// <summary>True while the lesson is on screen; CannonInputShooter refuses to fire.</summary>
        public static bool BlockingFire { get; private set; }

        [SerializeField] private GameFlowController flow;
        [SerializeField] private MapSelection mapSelection;

        [Tooltip("The gameplay canvas the overlay is built under.")]
        [SerializeField] private RectTransform hintParent;

        [SerializeField] private Sprite handSprite;

        [Tooltip("The tutorial's dark filter with the transparent hole; the hole sits over the board.")]
        [SerializeField] private Sprite dimSprite;

        [SerializeField] private TMP_FontAsset labelFont;

        [Tooltip("Rounded speech panel behind the words (the first tutorial's panel art).")]
        [SerializeField] private Sprite panelSprite;

        [Tooltip("The overlay shows only on this map, once.")]
        [SerializeField] private string targetMapId = "mission1_map1";

        private const float DismissDragPixels = 60f;

        private RectTransform hintRoot;
        private RectTransform hand;
        private Image handImage;
        private Coroutine swipeLoop;
        private bool shownThisRun;

        private bool tracking;
        private float heldDistance;
        private Vector2 lastPointer;

        private void Update()
        {
            bool playing = flow != null && flow.State == GameFlowController.GameState.Playing;

            if (!playing)
            {
                shownThisRun = false;
                if (BlockingFire)
                {
                    Hide();
                }

                return;
            }

            if (BlockingFire)
            {
                TrackDismissal();
                return;
            }

            if (shownThisRun)
            {
                return;
            }

            shownThisRun = true;
            if (ShouldTeach())
            {
                Show();
            }
        }

        private void OnDisable()
        {
            Hide();
        }

        /// <summary>
        /// Only after the first tutorial (this is lesson two - both at once is noise), only on
        /// the target map, and only until it lands once.
        /// </summary>
        private bool ShouldTeach()
        {
            if (UserData.Tutorial.dragTaught || !UserData.Tutorial.completed)
            {
                return false;
            }

            MapInfo selected = mapSelection != null ? mapSelection.Selected : null;
            return selected != null && selected.Id == targetMapId;
        }

        /// <summary>
        /// The lesson is done when the player performs it: 60+ screen pixels of sideways travel
        /// in one press. Vertical scrubbing or a plain tap keeps the overlay up.
        /// </summary>
        private void TrackDismissal()
        {
            Pointer pointer = Pointer.current;
            if (pointer == null)
            {
                return;
            }

            bool pressed = pointer.press.isPressed;
            Vector2 position = pointer.position.ReadValue();

            if (pressed && !tracking)
            {
                tracking = true;
                heldDistance = 0f;
            }
            else if (pressed)
            {
                heldDistance += Mathf.Abs(position.x - lastPointer.x);
            }
            else
            {
                tracking = false;
            }

            lastPointer = position;

            if (tracking && heldDistance >= DismissDragPixels)
            {
                UserData.Tutorial.dragTaught = true;
                UserData.Save();
                Hide();
            }
        }

        private void Show()
        {
            EnsureHint();
            if (hintRoot == null)
            {
                return;
            }

            // Like the first tutorial: there the moment it applies, no fade-in.
            hintRoot.gameObject.SetActive(true);
            BlockingFire = true;
            tracking = false;
            heldDistance = 0f;

            if (swipeLoop != null)
            {
                StopCoroutine(swipeLoop);
            }

            swipeLoop = StartCoroutine(SwipeLoop());
        }

        private void Hide()
        {
            BlockingFire = false;

            if (swipeLoop != null)
            {
                StopCoroutine(swipeLoop);
                swipeLoop = null;
            }

            if (hintRoot != null)
            {
                hintRoot.gameObject.SetActive(false);
            }
        }

        private void EnsureHint()
        {
            if (hintRoot != null || hintParent == null)
            {
                return;
            }

            GameObject rootGo = new GameObject("DragHint", typeof(RectTransform), typeof(CanvasGroup));
            hintRoot = (RectTransform)rootGo.transform;
            hintRoot.SetParent(hintParent, false);
            hintRoot.anchorMin = Vector2.zero;
            hintRoot.anchorMax = Vector2.one;
            hintRoot.offsetMin = Vector2.zero;
            hintRoot.offsetMax = Vector2.zero;
            hintRoot.SetAsLastSibling();
            rootGo.GetComponent<CanvasGroup>().blocksRaycasts = false;

            // The tutorial's own dark filter, oversized so its transparent middle lands on the
            // board and the edges run past the screen: dim everywhere, spotlight on the lesson.
            if (dimSprite != null)
            {
                GameObject dimGo = new GameObject("Dim", typeof(RectTransform));
                RectTransform dimRect = (RectTransform)dimGo.transform;
                dimRect.SetParent(hintRoot, false);
                dimRect.anchorMin = dimRect.anchorMax = new Vector2(0.5f, 0.63f);
                dimRect.pivot = new Vector2(0.5f, 0.48f);
                dimRect.anchoredPosition = Vector2.zero;
                dimRect.sizeDelta = new Vector2(1400f, 2300f);
                Image dim = dimGo.AddComponent<Image>();
                dim.sprite = dimSprite;
                dim.raycastTarget = false;                  // the drag must pass through
            }

            // The words live in the same rounded panel the first tutorial speaks from, just
            // below the board.
            GameObject panelGo = new GameObject("Panel", typeof(RectTransform));
            RectTransform panelRect = (RectTransform)panelGo.transform;
            panelRect.SetParent(hintRoot, false);
            panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0.42f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.anchoredPosition = Vector2.zero;
            panelRect.sizeDelta = new Vector2(560f, 246f);
            if (panelSprite != null)
            {
                Image panel = panelGo.AddComponent<Image>();
                panel.sprite = panelSprite;
                panel.preserveAspect = true;
                panel.raycastTarget = false;

                // The patch over the baked "Tap to shoot" (see the class comment). Offsets keep
                // it inside the art's blue rim; colour sampled from the art's paper.
                GameObject coverGo = new GameObject("Cover", typeof(RectTransform));
                RectTransform coverRect = (RectTransform)coverGo.transform;
                coverRect.SetParent(panelRect, false);
                coverRect.anchorMin = Vector2.zero;
                coverRect.anchorMax = Vector2.one;
                coverRect.offsetMin = new Vector2(70f, 55f);
                coverRect.offsetMax = new Vector2(-70f, -55f);
                Image cover = coverGo.AddComponent<Image>();
                cover.color = new Color(0.984f, 0.973f, 0.937f);
                cover.raycastTarget = false;
            }

            GameObject labelGo = new GameObject("Label", typeof(RectTransform));
            RectTransform labelRect = (RectTransform)labelGo.transform;
            labelRect.SetParent(panelRect, false);
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(30f, 20f);
            labelRect.offsetMax = new Vector2(-30f, -20f);
            TMP_Text label = labelGo.AddComponent<TextMeshProUGUI>();
            if (labelFont != null)
            {
                label.font = labelFont;
            }

            label.text = "HOLD & DRAG\nTO ROTATE";
            label.fontSize = 44f;
            label.fontStyle = FontStyles.Bold;
            label.color = panelSprite != null ? new Color(0.13f, 0.25f, 0.55f) : Color.white;
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;

            // The hand demonstrates ON the structure, in the spotlight, not off in a caption.
            GameObject handGo = new GameObject("Hand", typeof(RectTransform));
            hand = (RectTransform)handGo.transform;
            hand.SetParent(hintRoot, false);
            hand.anchorMin = hand.anchorMax = new Vector2(0.5f, 0.63f);
            hand.pivot = new Vector2(0.5f, 0.5f);
            hand.anchoredPosition = Vector2.zero;
            hand.sizeDelta = new Vector2(140f, 140f);
            handImage = handGo.AddComponent<Image>();
            if (handSprite != null)
            {
                handImage.sprite = handSprite;
                handImage.preserveAspect = true;
            }

            handImage.raycastTarget = false;
        }

        /// <summary>
        /// The hand swipes across the board - left first, then right - forever. Each swipe fades
        /// the hand in at the start and out at the end; the dim and the panel hold steady.
        /// </summary>
        private IEnumerator SwipeLoop()
        {
            const float swipeSeconds = 1.1f;
            const float restSeconds = 0.25f;
            const float reach = 150f;

            int direction = -1;                             // the camera orbits left first
            while (true)
            {
                float elapsed = 0f;
                while (elapsed < swipeSeconds)
                {
                    elapsed += Time.unscaledDeltaTime;
                    float t = Mathf.Clamp01(elapsed / swipeSeconds);
                    float eased = Mathf.SmoothStep(0f, 1f, t);
                    hand.anchoredPosition = new Vector2(Mathf.Lerp(0f, direction * reach, eased), 0f);

                    // In at the start, out at the end, full in the middle.
                    float alpha = Mathf.Clamp01(Mathf.Min(t * 4f, (1f - t) * 4f) + 0.15f);
                    SetHandAlpha(alpha);
                    yield return null;
                }

                SetHandAlpha(0f);
                hand.anchoredPosition = Vector2.zero;
                direction = -direction;
                yield return new WaitForSecondsRealtime(restSeconds);
            }
        }

        private void SetHandAlpha(float alpha)
        {
            if (handImage != null)
            {
                Color color = handImage.color;
                color.a = alpha;
                handImage.color = color;
            }
        }
    }
}
