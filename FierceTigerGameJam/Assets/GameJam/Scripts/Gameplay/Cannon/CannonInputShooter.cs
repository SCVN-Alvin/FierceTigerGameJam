using UnityEngine;
using UnityEngine.InputSystem;
using GameJam.Gameplay.Wall;

namespace GameJam.Gameplay.Cannon
{
    public class CannonInputShooter : MonoBehaviour
    {
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
            if (fireController == null)
            {
                Debug.LogWarning($"{nameof(CannonInputShooter)} needs a {nameof(CannonFireController)}.");
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
