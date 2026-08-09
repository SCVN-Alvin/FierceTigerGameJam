using UnityEngine;

namespace GameJam.Gameplay.Cameras
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class GameJamCameraSizeController : MonoBehaviour
    {
        private const float Deg2RadHalf = Mathf.Deg2Rad / 2f;
        private const float Rad2DegDouble = Mathf.Rad2Deg * 2f;
        private const float MinRatioEpsilon = 0.0001f;
        private const float MinNearClip = 0.01f;
        private const int FrustumOffsetSolveIterations = 12;

        [Header("Camera")]
        [SerializeField] private Camera targetCamera;

        [Header("Aspect thresholds")]
        [SerializeField] private float aspectNormalToWide = 1f;
        [SerializeField] private float aspectWideUpper = 2.37f;

        [Header("Perspective FOV")]
        [SerializeField] private Vector2Int referenceResolutionPixels = new Vector2Int(720, 1280);
        [SerializeField] private float viewSizeNormal = 20f;
        [SerializeField] private float viewSizeAtReference = 22.5f;
        [SerializeField] private float viewSizeWide = 22.5f;
        [SerializeField] private float wideAspectLerpExponent = 1f;

        [Header("Perspective camera distance")]
        [SerializeField] private float perspectiveCameraZNormal = -8.74f;
        [SerializeField] private float ultraTallAspectForMaxPullback = 0.45f;
        [SerializeField] private float perspectiveReferenceZExtraPullback = 0.45f;
        [SerializeField] private float perspectiveReferenceYDownOffset = 0.32f;

        [Header("Perspective bottom lock")]
        [SerializeField] private bool bottomLockEnabled = true;
        [SerializeField] private Transform bottomLockPoint;
        [SerializeField] private Vector3 bottomLockWorldFallback = new Vector3(0f, -0.627f, -3.244f);
        [SerializeField] private float bottomLockViewportY = 0.05f;

        private Transform cameraTransform;
        private float resolvedPerspectiveCameraZ;
        private float resolvedPerspectiveCameraY;
        private bool usingCustomPerspectiveProjection;

        private void Reset()
        {
            targetCamera = GetComponent<Camera>();
        }

        private void OnEnable()
        {
            if (targetCamera == null)
            {
                targetCamera = GetComponent<Camera>();
            }

            if (targetCamera == null || targetCamera.orthographic)
            {
                return;
            }

            cameraTransform = targetCamera.transform;
            resolvedPerspectiveCameraZ = Mathf.Abs(perspectiveCameraZNormal) < MinRatioEpsilon
                ? cameraTransform.localPosition.z
                : perspectiveCameraZNormal;
            resolvedPerspectiveCameraY = cameraTransform.localPosition.y;
            ResetPerspectiveProjectionIfNeeded();
        }

        private void OnDisable()
        {
            RestorePerspectiveCameraPose();
            ResetPerspectiveProjectionIfNeeded();
        }

        private void LateUpdate()
        {
            if (targetCamera == null || targetCamera.orthographic)
            {
                return;
            }

            float currentAspect = Screen.width / (float)Screen.height;
            ApplyPerspectiveForAspect(currentAspect);
        }

        private void ApplyPerspectiveForAspect(float currentAspect)
        {
            float referenceAspect = GetReferenceDesignAspect();
            float targetFov;
            float tall01;

            if (currentAspect >= referenceAspect)
            {
                targetFov = ResolveFovAtOrAboveReference(currentAspect);
                tall01 = 0f;
            }
            else
            {
                float tallRatio = referenceAspect / currentAspect;
                if (tallRatio < 1f)
                {
                    tallRatio = 1f;
                }

                float tanHalf = tallRatio * Mathf.Tan(viewSizeNormal * Deg2RadHalf);
                targetFov = Mathf.Atan(tanHalf) * Rad2DegDouble;
                tall01 = ComputePortraitTallnessBelowReference01(currentAspect, referenceAspect);
            }

            if (!Mathf.Approximately(targetCamera.fieldOfView, targetFov))
            {
                targetCamera.fieldOfView = targetFov;
            }

            ApplyPerspectiveDistance();
            ApplyPerspectiveBottomLock(targetFov, tall01);
        }

        private float GetReferenceDesignAspect()
        {
            float refWidth = Mathf.Max(1f, referenceResolutionPixels.x);
            float refHeight = Mathf.Max(1f, referenceResolutionPixels.y);
            return refWidth / refHeight;
        }

        private float ResolveFovAtOrAboveReference(float currentAspect)
        {
            float referenceFov = viewSizeAtReference > MinRatioEpsilon ? viewSizeAtReference : viewSizeNormal;
            if (currentAspect < aspectNormalToWide)
            {
                return referenceFov;
            }

            float span = aspectWideUpper - aspectNormalToWide;
            if (span <= MinRatioEpsilon)
            {
                return viewSizeWide;
            }

            float t = Mathf.Clamp01((currentAspect - aspectNormalToWide) / span);
            if (!Mathf.Approximately(wideAspectLerpExponent, 1f))
            {
                t = Mathf.Pow(t, wideAspectLerpExponent);
            }

            return Mathf.Lerp(referenceFov, viewSizeWide, t);
        }

        private float ComputePortraitTallnessBelowReference01(float currentAspect, float referenceAspect)
        {
            if (currentAspect >= referenceAspect)
            {
                return 0f;
            }

            float span = referenceAspect - ultraTallAspectForMaxPullback;
            if (span < MinRatioEpsilon)
            {
                return 1f;
            }

            return Mathf.Clamp01((referenceAspect - currentAspect) / span);
        }

        private void ApplyPerspectiveDistance()
        {
            if (cameraTransform == null)
            {
                return;
            }

            Vector3 localPosition = cameraTransform.localPosition;
            localPosition.y = resolvedPerspectiveCameraY - perspectiveReferenceYDownOffset;
            localPosition.z = resolvedPerspectiveCameraZ - perspectiveReferenceZExtraPullback;
            cameraTransform.localPosition = localPosition;
        }

        private void ApplyPerspectiveBottomLock(float aspectFov, float tall01)
        {
            if (!bottomLockEnabled || tall01 <= MinRatioEpsilon)
            {
                ResetPerspectiveProjectionIfNeeded();
                return;
            }

            if (aspectFov <= viewSizeNormal + MinRatioEpsilon)
            {
                ResetPerspectiveProjectionIfNeeded();
                return;
            }

            float near = Mathf.Max(targetCamera.nearClipPlane, MinNearClip);
            float far = targetCamera.farClipPlane;
            float aspect = Mathf.Max(targetCamera.aspect, MinRatioEpsilon);
            float halfHeight = Mathf.Tan(aspectFov * Deg2RadHalf) * near;
            float designHalfHeight = Mathf.Tan(viewSizeNormal * Deg2RadHalf) * near;
            float maxOffset = Mathf.Max(0f, halfHeight - designHalfHeight);
            float halfWidth = halfHeight * aspect;

            float frustumOffset = SolveFrustumOffsetForViewportY(
                -halfWidth,
                halfWidth,
                halfHeight,
                near,
                far,
                maxOffset,
                ResolveBottomLockWorld(),
                bottomLockViewportY);

            ApplyPerspectiveOffCenter(-halfWidth, halfWidth, -halfHeight + frustumOffset, halfHeight + frustumOffset, near, far);
            usingCustomPerspectiveProjection = true;
        }

        private float SolveFrustumOffsetForViewportY(
            float left,
            float right,
            float halfHeight,
            float near,
            float far,
            float maxOffset,
            Vector3 worldPoint,
            float targetViewportY)
        {
            float low = 0f;
            float high = maxOffset;
            float best = maxOffset;

            for (int i = 0; i < FrustumOffsetSolveIterations; i++)
            {
                float mid = (low + high) * 0.5f;
                ApplyPerspectiveOffCenter(left, right, -halfHeight + mid, halfHeight + mid, near, far);
                float viewportY = targetCamera.WorldToViewportPoint(worldPoint).y;

                best = mid;
                if (viewportY > targetViewportY)
                {
                    low = mid;
                }
                else
                {
                    high = mid;
                }
            }

            return best;
        }

        private Vector3 ResolveBottomLockWorld()
        {
            return bottomLockPoint != null ? bottomLockPoint.position : bottomLockWorldFallback;
        }

        private void RestorePerspectiveCameraPose()
        {
            if (cameraTransform == null)
            {
                return;
            }

            Vector3 localPosition = cameraTransform.localPosition;
            localPosition.y = resolvedPerspectiveCameraY;
            localPosition.z = resolvedPerspectiveCameraZ;
            cameraTransform.localPosition = localPosition;
        }

        private void ResetPerspectiveProjectionIfNeeded()
        {
            if (!usingCustomPerspectiveProjection || targetCamera == null)
            {
                return;
            }

            targetCamera.ResetProjectionMatrix();
            usingCustomPerspectiveProjection = false;
        }

        private void ApplyPerspectiveOffCenter(float left, float right, float bottom, float top, float near, float far)
        {
            float width = right - left;
            float height = top - bottom;
            if (width < MinRatioEpsilon || height < MinRatioEpsilon)
            {
                return;
            }

            Matrix4x4 projection = default;
            projection.m00 = 2f * near / width;
            projection.m02 = (right + left) / width;
            projection.m11 = 2f * near / height;
            projection.m12 = (top + bottom) / height;
            projection.m22 = -(far + near) / (far - near);
            projection.m23 = -(2f * far * near) / (far - near);
            projection.m32 = -1f;
            targetCamera.projectionMatrix = projection;
        }
    }
}
