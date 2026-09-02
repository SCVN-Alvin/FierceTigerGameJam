using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
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

        /// <summary>
        /// Whether the gesture in progress began on a UI element. The cannon ignores the whole of
        /// such a gesture: no aim, no rotation, no shot.
        /// </summary>
        private bool pressStartedOverUi;

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
                // A press that starts on a screen belongs to that screen. Judged once, here, and
                // remembered for the whole gesture: testing again on release would let a drag that
                // began on the gear end as a shot the moment the finger left the button, and a drag
                // that began on the playfield die the moment it crossed one.
                if (IsPointerOverUi(currentScreenPosition))
                {
                    pressStartedOverUi = true;
                    return;
                }

                pressStartedOverUi = false;
                BeginPress(currentScreenPosition);
            }

            if (pressStartedOverUi)
            {
                if (pointer.press.wasReleasedThisFrame)
                {
                    pressStartedOverUi = false;
                }

                return;
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
            else
            {
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
