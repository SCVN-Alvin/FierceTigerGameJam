using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using GameJam.Gameplay.Flow;
using GameJam.Gameplay.Wall;

namespace GameJam.Gameplay.Cannon
{
    public class CannonInputShooter : MonoBehaviour
    {
        [SerializeField] private GridKnockdownCannonFireController gridFireController;
        [SerializeField] private CannonFireController fireController;
        [SerializeField] private StructureRotateController structureRotateController;

        [Header("Drag Rotation")]
        [SerializeField] private float dragStartThresholdPixels = 12f;

        private bool isPointerDown;
        private bool isDragging;
        private Vector2 pressScreenPosition;
        private Vector2 previousScreenPosition;
        private Vector2 currentScreenPosition;

        private void Awake()
        {
            if (gridFireController == null)
            {
                gridFireController = GetComponent<GridKnockdownCannonFireController>();
            }

            if (fireController == null)
            {
                fireController = GetComponent<CannonFireController>();
            }

            if (structureRotateController == null)
            {
                structureRotateController = GetComponent<StructureRotateController>();
            }
        }

        private void Update()
        {
            Pointer pointer = Pointer.current;
            if (pointer == null)
            {
                StopStructureRotation();
                return;
            }

            currentScreenPosition = pointer.position.ReadValue();

            if (pointer.press.wasPressedThisFrame)
            {
                BeginPress(currentScreenPosition);
            }

            if (isPointerDown && pointer.press.isPressed)
            {
                UpdatePress(currentScreenPosition);
            }

            if (isPointerDown && pointer.press.wasReleasedThisFrame)
            {
                EndPress(currentScreenPosition);
            }
        }

        private void OnDisable()
        {
            StopStructureRotation();
        }

        /// <summary>
        /// Whether a screen position is over an interactive UI element.
        ///
        /// Asked of the EventSystem by raycast rather than the parameterless
        /// <c>IsPointerOverGameObject</c>: that overload answers for the mouse pointer id and is
        /// unreliable on touch, where each finger has an id of its own, and it is documented to
        /// return the previous frame's answer inside an input callback. Raycasting the position we
        /// already have is exact for both.
        ///
        /// Anything with raycastTarget on counts, which is why the backdrops behind the menus take
        /// raycasts deliberately: a tap on the dim around a panel is a tap on that screen, not a
        /// shot through it.
        /// </summary>
        private bool IsPointerOverUi(Vector2 screenPosition)
        {
            EventSystem events = EventSystem.current;
            if (events == null)
            {
                // No EventSystem means no UI can be hit at all, so nothing can be blocking.
                return false;
            }

            pointerEventData ??= new PointerEventData(events);
            pointerEventData.Reset();
            pointerEventData.position = screenPosition;

            uiHits.Clear();
            events.RaycastAll(pointerEventData, uiHits);
            return uiHits.Count > 0;
        }

        /// <summary>Reused so a press costs no allocation; both are cleared before every use.</summary>
        private PointerEventData pointerEventData;

        private readonly System.Collections.Generic.List<RaycastResult> uiHits =
            new System.Collections.Generic.List<RaycastResult>();

        private void BeginPress(Vector2 screenPosition)
        {
            // A gesture that starts on UI belongs to the UI, whole. Without this, every HUD
            // button tap also fired the cannon, and a drag across a screen spun the orbit
            // behind it.
            if (IsPointerOverUi(screenPosition))
            {
                return;
            }

            // The Double/Triple Shoot intro owns the whole screen: no rotating the board
            // behind it, no gesture banked for a shot after it closes.
            if (ShotBoostIntroController.BlockingInput)
            {
                return;
            }

            isPointerDown = true;
            isDragging = false;
            pressScreenPosition = screenPosition;
            previousScreenPosition = screenPosition;
            StopStructureRotation();
        }

        private void UpdatePress(Vector2 screenPosition)
        {
            if (!isDragging && Vector2.Distance(pressScreenPosition, screenPosition) >= dragStartThresholdPixels)
            {
                isDragging = true;
            }

            if (isDragging)
            {
                UpdateStructureRotation(screenPosition);
            }

            previousScreenPosition = screenPosition;
        }

        private void EndPress(Vector2 screenPosition)
        {
            if (isDragging)
            {
                UpdateStructureRotation(screenPosition);
            }
            else if (!IsPointerOverUi(screenPosition) && !DragHintController.BlockingFire
                     && !ShotBoostIntroController.BlockingInput)
            {
                // Checked again at release: a screen (Cleared, Fail) can have appeared while the
                // finger was down, and the tap that dismisses it must not also fire a shot. And
                // while the drag lesson is up, taps buy nothing - the cannon waits until the
                // player has actually held and dragged.
                FireAtScreenPosition(screenPosition);
            }

            isPointerDown = false;
            isDragging = false;
            StopStructureRotation();
        }

        public void FireAtScreenPosition(Vector2 screenPosition)
        {
            if (gridFireController != null)
            {
                gridFireController.TryFireAtScreenPoint(screenPosition);
                return;
            }

            if (fireController == null)
            {
#if UNITY_EDITOR
                Debug.LogWarning($"{nameof(CannonInputShooter)} needs a fire controller.");
#endif
                return;
            }

            fireController.TryFireAtScreenPoint(screenPosition);
        }

        private void UpdateStructureRotation(Vector2 screenPosition)
        {
            float dragDeltaX = screenPosition.x - previousScreenPosition.x;
            if (structureRotateController != null)
            {
                structureRotateController.RotateFromScreenDelta(dragDeltaX);
            }
            else
            {
                StopStructureRotation();
            }
        }

        private void StopStructureRotation()
        {
            if (structureRotateController != null)
            {
                structureRotateController.Stop();
            }
        }
    }
}
