using UnityEngine;
using UnityEngine.InputSystem;

namespace GameJam.Gameplay
{
    public class CannonInputShooter : MonoBehaviour
    {
        [SerializeField] private Camera targetCamera;
        [SerializeField] private CannonProjectile projectilePrefab;
        [SerializeField] private Transform projectileParent;
        [SerializeField] private Transform fireOrigin;
        [SerializeField] private float firePlaneZ = -20f;
        [SerializeField] private float projectileSpeed = 35f;
        [SerializeField] private float projectileLifetime = 5f;
        [SerializeField] private Vector3 fireDirection = Vector3.forward;
        [Header("Drag Rotation")]
        [SerializeField] private SpinOnAxis wallSpinner;
        [SerializeField] private float dragStartThresholdPixels = 12f;
        [SerializeField] private float dragRotationSpeed = 90f;

        private bool isPointerDown;
        private bool isDragging;
        private Vector2 pressScreenPosition;
        private Vector2 previousScreenPosition;
        private Vector2 currentScreenPosition;

        private void Awake()
        {
            if (targetCamera == null)
            {
                targetCamera = Camera.main;
            }
        }

        private void Update()
        {
            Pointer pointer = Pointer.current;
            if (pointer == null)
            {
                SetWallSpinSpeed(0f);
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
            SetWallSpinSpeed(0f);
        }

        private void BeginPress(Vector2 screenPosition)
        {
            isPointerDown = true;
            isDragging = false;
            pressScreenPosition = screenPosition;
            previousScreenPosition = screenPosition;
            SetWallSpinSpeed(0f);
        }

        private void UpdatePress(Vector2 screenPosition)
        {
            if (!isDragging && Vector2.Distance(pressScreenPosition, screenPosition) >= dragStartThresholdPixels)
            {
                isDragging = true;
            }

            if (isDragging)
            {
                UpdateWallSpinSpeed(screenPosition);
            }

            previousScreenPosition = screenPosition;
        }

        private void EndPress(Vector2 screenPosition)
        {
            if (isDragging)
            {
                UpdateWallSpinSpeed(screenPosition);
            }
            else
            {
                FireAtScreenPosition(screenPosition);
            }

            isPointerDown = false;
            isDragging = false;
            SetWallSpinSpeed(0f);
        }

        public void FireAtScreenPosition(Vector2 screenPosition)
        {
            if (targetCamera == null)
            {
                Debug.LogWarning($"{nameof(CannonInputShooter)} needs a camera.");
                return;
            }

            Vector3 spawnPosition = GetWorldPositionOnFirePlane(screenPosition);
            Fire(spawnPosition);
        }

        private void UpdateWallSpinSpeed(Vector2 screenPosition)
        {
            float dragDeltaX = screenPosition.x - previousScreenPosition.x;
            if (Mathf.Approximately(dragDeltaX, 0f))
            {
                SetWallSpinSpeed(0f);
                return;
            }

            SetWallSpinSpeed(Mathf.Sign(dragDeltaX) * dragRotationSpeed);
        }

        private void SetWallSpinSpeed(float speed)
        {
            if (wallSpinner != null)
            {
                wallSpinner.SetSpeed(speed);
            }
        }

        private Vector3 GetWorldPositionOnFirePlane(Vector2 screenPosition)
        {
            Ray ray = targetCamera.ScreenPointToRay(screenPosition);
            Plane firePlane = new Plane(Vector3.forward, new Vector3(0f, 0f, firePlaneZ));

            if (firePlane.Raycast(ray, out float enter))
            {
                return ray.GetPoint(enter);
            }

            Vector3 fallback = fireOrigin != null ? fireOrigin.position : transform.position;
            return new Vector3(fallback.x, fallback.y, firePlaneZ);
        }

        private void Fire(Vector3 spawnPosition)
        {
            CannonProjectile projectile = projectilePrefab != null
                ? Instantiate(projectilePrefab, spawnPosition, Quaternion.identity, projectileParent)
                : CreateDefaultProjectile(spawnPosition);

            Vector3 direction = fireDirection.sqrMagnitude > 0f ? fireDirection.normalized : Vector3.forward;
            projectile.Launch(direction, projectileSpeed, projectileLifetime);
        }

        private CannonProjectile CreateDefaultProjectile(Vector3 spawnPosition)
        {
            GameObject projectileObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            projectileObject.name = "Cannon Projectile";
            projectileObject.transform.SetParent(projectileParent);
            projectileObject.transform.position = spawnPosition;
            projectileObject.transform.localScale = Vector3.one * 0.55f;

            Rigidbody projectileRigidbody = projectileObject.AddComponent<Rigidbody>();
            projectileRigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            projectileRigidbody.interpolation = RigidbodyInterpolation.Interpolate;
            projectileRigidbody.useGravity = false;

            return projectileObject.AddComponent<CannonProjectile>();
        }
    }
}
