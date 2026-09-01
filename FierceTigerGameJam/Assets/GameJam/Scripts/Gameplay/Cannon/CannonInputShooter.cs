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
        /// Whether the pointer is over any raycastable UI. Every screen dims the canvas with a
        /// full-cover backdrop, so this one check also answers "is a screen up right now" - a tap
        /// on the Cleared screen lands on its dimmer, not on the playfield.
        /// </summary>
        private static bool PointerOverUi()
        {
            EventSystem eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                return false;
            }

            if (eventSystem.IsPointerOverGameObject())
            {
                return true;
            }

            // Touch pointers register under their own ids with the input-system UI module, and
            // the no-argument overload only speaks for the mouse.
            Touchscreen touch = Touchscreen.current;
            return touch != null
                && eventSystem.IsPointerOverGameObject(touch.primaryTouch.touchId.ReadValue());
        }

        private void BeginPress(Vector2 screenPosition)
        {
            // A gesture that starts on UI belongs to the UI, whole. Without this, every HUD
            // button tap also fired the cannon, and a drag across a screen spun the orbit
            // behind it.
            if (PointerOverUi())
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
            else if (!PointerOverUi() && !DragHintController.BlockingFire
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
