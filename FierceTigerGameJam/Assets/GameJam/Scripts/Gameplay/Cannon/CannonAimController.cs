using System;
using UnityEngine;

namespace GameJam.Gameplay.Cannon
{
    public sealed class CannonAimController : MonoBehaviour
    {
        public event Action OnAimStart;
        public event Action OnAimUpdated;
        public event Action OnFired;

        [SerializeField] private Transform aimPivot;
        [SerializeField] private float aimSpeed = 2f;
        [SerializeField] private float minYaw = -90f;
        [SerializeField] private float maxYaw = 90f;
        [SerializeField] private float defaultAimPlaneHeight = 0.8f;
        [SerializeField] private bool rotateAimPivotWithAim = true;
        [SerializeField] private CannonAimPlaneAnchor aimPlane;

        private const float InverseDragScale = 0.01f;
        private const float MinAimDirectionSqrMagnitude = 0.0001f;

        private Transform muzzlePoint;
        private Transform aimPlaneAnchor;
        private Vector2 lastDragPosition;
        private Vector3 lastAimWorldPoint;
        private float currentYaw;
        private float currentPitch;
        private bool isDragging;
        private bool hasLastAimWorldPoint;
        private Quaternion baseLocalRotation = Quaternion.identity;

        public bool HasValidAimPoint => hasLastAimWorldPoint;
        public Vector3 LastAimWorldPoint => lastAimWorldPoint;

        private void Awake()
        {
            Initialize(muzzlePoint);
        }

        public void Initialize(Transform muzzle)
        {
            muzzlePoint = muzzle;
            baseLocalRotation = aimPivot != null ? aimPivot.localRotation : Quaternion.identity;
            currentYaw = 0f;
            currentPitch = 0f;
            isDragging = false;
            hasLastAimWorldPoint = false;
            ApplyRotation();
        }

        public void SetAimPlaneAnchor(Transform anchor)
        {
            aimPlaneAnchor = anchor;
            aimPlane = null;
        }

        public void SetAimPlane(CannonAimPlaneAnchor plane)
        {
            aimPlane = plane;
            aimPlaneAnchor = null;
        }

        public bool TryAimAtScreenPoint(Camera camera, Vector2 screenPosition)
        {
            return TryAimAtScreenPoint(camera, screenPosition, out _);
        }

        public bool TryAimAtScreenPoint(Camera camera, Vector2 screenPosition, out AimRejectReason rejectReason)
        {
            if (camera == null)
            {
                rejectReason = AimRejectReason.NoCamera;
                return false;
            }

            if (aimPivot == null)
            {
                rejectReason = AimRejectReason.MissingAimPivot;
                return false;
            }

            if (!TryGetAimWorldPoint(camera, screenPosition, out Vector3 worldTarget, out rejectReason))
            {
                return false;
            }

            lastAimWorldPoint = worldTarget;
            hasLastAimWorldPoint = true;

            if (rotateAimPivotWithAim)
            {
                AimAtWorldPoint(worldTarget);
            }

            return true;
        }

        public Vector3 GetFireDirection(Vector3 muzzleWorldPosition, float projectileSpeed, float spawnOffset)
        {
            if (!hasLastAimWorldPoint)
            {
                return aimPivot != null ? aimPivot.forward : Vector3.forward;
            }

            Vector3 launchOrigin = muzzleWorldPosition;
            if (CannonBallisticAimMath.TryGetLaunchDirection(
                    launchOrigin,
                    lastAimWorldPoint,
                    projectileSpeed,
                    out Vector3 direction))
            {
                if (spawnOffset > 0f)
                {
                    launchOrigin = muzzleWorldPosition + direction * spawnOffset;
                    CannonBallisticAimMath.TryGetLaunchDirection(
                        launchOrigin,
                        lastAimWorldPoint,
                        projectileSpeed,
                        out direction);
                }

                return direction;
            }

            Vector3 fallbackDirection = lastAimWorldPoint - launchOrigin;
            if (fallbackDirection.sqrMagnitude < MinAimDirectionSqrMagnitude)
            {
                return aimPivot != null ? aimPivot.forward : Vector3.forward;
            }

            return fallbackDirection.normalized;
        }

        public void AimAtWorldPoint(Vector3 worldTarget)
        {
            if (aimPivot == null)
            {
                return;
            }

            Transform parent = aimPivot.parent;
            Vector3 worldDirection = worldTarget - ResolveAimOrigin();
            if (worldDirection.sqrMagnitude < MinAimDirectionSqrMagnitude)
            {
                return;
            }

            worldDirection.Normalize();
            Vector3 localDirection = parent != null
                ? parent.InverseTransformDirection(worldDirection)
                : worldDirection;

            float horizontalDistance = Mathf.Sqrt(
                localDirection.x * localDirection.x + localDirection.z * localDirection.z);

            float yaw = Mathf.Atan2(localDirection.x, localDirection.z) * Mathf.Rad2Deg;
            float pitch = -Mathf.Atan2(localDirection.y, horizontalDistance) * Mathf.Rad2Deg;

            currentYaw = Mathf.Clamp(yaw, minYaw, maxYaw);
            currentPitch = pitch;

            ApplyRotation();
            OnAimUpdated?.Invoke();
        }

        public void OnDragStart(Vector2 screenPosition)
        {
            isDragging = true;
            lastDragPosition = screenPosition;
            OnAimStart?.Invoke();
        }

        public void OnDrag(Vector2 screenPosition)
        {
            if (!isDragging)
            {
                return;
            }

            Vector2 delta = screenPosition - lastDragPosition;
            lastDragPosition = screenPosition;

            currentYaw += delta.x * aimSpeed * InverseDragScale;
            currentPitch -= delta.y * aimSpeed * InverseDragScale;
            currentYaw = Mathf.Clamp(currentYaw, minYaw, maxYaw);

            ApplyRotation();
            OnAimUpdated?.Invoke();
        }

        public void OnDragEnd()
        {
            if (!isDragging)
            {
                return;
            }

            isDragging = false;
            OnFired?.Invoke();
        }

        public void ResetAim()
        {
            currentYaw = 0f;
            currentPitch = 0f;
            hasLastAimWorldPoint = false;
            ApplyRotation();
        }

        private bool TryGetAimWorldPoint(Camera camera, Vector2 screenPosition, out Vector3 worldTarget)
        {
            return TryGetAimWorldPoint(camera, screenPosition, out worldTarget, out _);
        }

        private bool TryGetAimWorldPoint(
            Camera camera,
            Vector2 screenPosition,
            out Vector3 worldTarget,
            out AimRejectReason rejectReason)
        {
            if (aimPlane != null)
            {
                return aimPlane.TryGetAimWorldPoint(camera, screenPosition, out worldTarget, out rejectReason);
            }

            Ray ray = camera.ScreenPointToRay(screenPosition);

            if (aimPlaneAnchor != null)
            {
                Plane anchoredPlane = new Plane(-aimPlaneAnchor.forward, aimPlaneAnchor.position);
                if (anchoredPlane.Raycast(ray, out float anchoredDistance))
                {
                    worldTarget = ray.GetPoint(anchoredDistance);
                    rejectReason = AimRejectReason.None;
                    return true;
                }

                worldTarget = Vector3.zero;
                rejectReason = AimRejectReason.BehindCamera;
                return false;
            }

            Plane fallbackPlane = new Plane(Vector3.up, new Vector3(0f, defaultAimPlaneHeight, 0f));
            if (fallbackPlane.Raycast(ray, out float fallbackDistance))
            {
                worldTarget = ray.GetPoint(fallbackDistance);
                rejectReason = AimRejectReason.None;
                return true;
            }

            worldTarget = Vector3.zero;
            rejectReason = AimRejectReason.BehindCamera;
            return false;
        }

        private Vector3 ResolveAimOrigin()
        {
            if (muzzlePoint != null)
            {
                return muzzlePoint.position;
            }

            return aimPivot != null ? aimPivot.position : transform.position;
        }

        private void ApplyRotation()
        {
            if (aimPivot != null)
            {
                aimPivot.localRotation = baseLocalRotation * Quaternion.Euler(currentPitch, currentYaw, 0f);
            }
        }
    }
}
