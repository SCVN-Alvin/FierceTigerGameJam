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

        /// <summary>
        /// The tutorial art, borrowed by <see cref="UpgradeGuideController"/> - which is created
        /// at runtime and so has no inspector of its own to be given the same four assets in.
        ///
        /// Read-only, and read once: the guide takes copies of the references and never writes
        /// back, so this cannot change what the drag lesson looks like. A serialized field left
        /// unassigned in this project is a live bug (see the note on GameFlowController's
        /// fireController), and four more of them on a second component nobody remembers to wire
        /// is exactly that bug waiting to happen.
        /// </summary>
        public RectTransform HintParent => hintParent;

        public Sprite HandSprite => handSprite;

        public Sprite DimSprite => dimSprite;

        public Sprite PanelSprite => panelSprite;

        public TMP_FontAsset LabelFont => labelFont;

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

            // Built from the shared pieces in TutorialOverlay since the upgrade guide wanted the
            // same hole and hand. Every value this lesson depends on - which anchor each piece
            // sits at, and what the words say - is still decided right here; the helper only
            // knows how to make a dim, a panel and a hand.
            hintRoot = TutorialOverlay.CreateRoot(hintParent, "DragHint", blocksRaycasts: false);

            // The tutorial's own dark filter, oversized so its transparent middle lands on the
            // board and the edges run past the screen: dim everywhere, spotlight on the lesson.
            // Its pivot is the hole, so anchoring it is what puts the hole over the board.
            if (dimSprite != null)
            {
                RectTransform dimRect = TutorialOverlay.CreateSpotlight(hintRoot, dimSprite);
                dimRect.anchorMin = dimRect.anchorMax = new Vector2(0.5f, 0.63f);
            }

            // The words live in the same rounded panel the first tutorial speaks from, just
            // below the board.
            RectTransform panelRect = TutorialOverlay.CreatePanel(hintRoot, panelSprite);
            panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0.42f);

            TMP_Text label = TutorialOverlay.CreateLabel(panelRect, labelFont, panelSprite != null);
            label.text = "HOLD & DRAG\nTO ROTATE";

            // The hand demonstrates ON the structure, in the spotlight, not off in a caption.
            handImage = TutorialOverlay.CreateHand(hintRoot, handSprite);
            hand = handImage.rectTransform;
            hand.anchorMin = hand.anchorMax = new Vector2(0.5f, 0.63f);
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
            TutorialOverlay.SetAlpha(handImage, alpha);
        }
    }
}
