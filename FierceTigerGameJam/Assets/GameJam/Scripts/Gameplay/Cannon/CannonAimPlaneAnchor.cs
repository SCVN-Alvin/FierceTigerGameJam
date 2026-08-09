using UnityEngine;

namespace GameJam.Gameplay.Cannon
{
    public sealed class CannonAimPlaneAnchor : MonoBehaviour
    {
        [SerializeField] private float planeHalfWidth = 3f;
        [SerializeField] private float planeHalfHeight = 4f;
        [SerializeField] private bool enforceBounds = true;
        [SerializeField] private Color planeFillColor = new Color(0.2f, 0.85f, 1f, 0.28f);
        [SerializeField] private Color planeWireColor = new Color(0.2f, 0.95f, 1f, 0.95f);
        [SerializeField] private bool drawGizmoAlways = true;
        [SerializeField] private Camera previewCamera;
        [SerializeField] private bool drawCameraPreviewWhenSelected = true;
        [SerializeField] private Color blockedPreviewFillColor = new Color(1f, 0.2f, 0.12f, 0.18f);
        [SerializeField] private Color blockedPreviewWireColor = new Color(1f, 0.25f, 0.12f, 0.8f);
        [SerializeField] private bool drawGameViewOverlay;
        [SerializeField] private Color gameViewBlockedColor = new Color(1f, 0.2f, 0.12f, 0.18f);
        [SerializeField] private Color gameViewFireColor = new Color(0.2f, 0.85f, 1f, 0.28f);
        [SerializeField] private Color gameViewLineColor = new Color(0.2f, 0.95f, 1f, 0.95f);

        private const float NormalGizmoLength = 0.35f;
        private static Texture2D whiteTexture;

        public float PlaneHalfWidth => planeHalfWidth;
        public float PlaneHalfHeight => planeHalfHeight;
        public Vector3 PlaneNormal => -transform.forward;

        public bool TryGetAimWorldPoint(Camera camera, Vector2 screenPosition, out Vector3 worldTarget)
        {
            return TryGetAimWorldPoint(camera, screenPosition, out worldTarget, out _);
        }

        public bool TryGetAimWorldPoint(
            Camera camera,
            Vector2 screenPosition,
            out Vector3 worldTarget,
            out AimRejectReason rejectReason)
        {
            worldTarget = Vector3.zero;
            if (camera == null)
            {
                rejectReason = AimRejectReason.NoCamera;
                return false;
            }

            Ray ray = camera.ScreenPointToRay(screenPosition);
            Plane aimPlane = new Plane(-transform.forward, transform.position);
            if (!aimPlane.Raycast(ray, out float distance))
            {
                rejectReason = AimRejectReason.BehindCamera;
                return false;
            }

            worldTarget = ray.GetPoint(distance);
            if (!enforceBounds)
            {
                rejectReason = AimRejectReason.None;
                return true;
            }

            Vector3 localPoint = transform.InverseTransformPoint(worldTarget);
            if (localPoint.y < -planeHalfHeight)
            {
                rejectReason = AimRejectReason.TooLow;
                return false;
            }

            if (localPoint.y > planeHalfHeight)
            {
                rejectReason = AimRejectReason.TooHigh;
                return false;
            }

            if (localPoint.x < -planeHalfWidth)
            {
                rejectReason = AimRejectReason.TooLeft;
                return false;
            }

            if (localPoint.x > planeHalfWidth)
            {
                rejectReason = AimRejectReason.TooRight;
                return false;
            }

            rejectReason = AimRejectReason.None;
            return true;
        }

        public bool TryRaycast(Ray ray, out Vector3 worldPoint)
        {
            Plane aimPlane = new Plane(PlaneNormal, transform.position);
            if (aimPlane.Raycast(ray, out float distance))
            {
                worldPoint = ray.GetPoint(distance);
                return true;
            }

            worldPoint = Vector3.zero;
            return false;
        }

        public Vector3 GetWorldPointOnPlane(float localX, float localY)
        {
            return transform.TransformPoint(new Vector3(localX, localY, 0f));
        }

        private void OnDrawGizmos()
        {
            if (drawGizmoAlways)
            {
                DrawPlaneGizmo(false);
            }
        }

        private void OnDrawGizmosSelected()
        {
            if (drawCameraPreviewWhenSelected)
            {
                DrawCameraPreviewGizmo();
            }

            DrawPlaneGizmo(true);
        }

        private void OnGUI()
        {
            if (!drawGameViewOverlay)
            {
                return;
            }

            Camera cameraToPreview = previewCamera != null ? previewCamera : Camera.main;
            if (cameraToPreview == null)
            {
                return;
            }

            Rect fireScreenRect = BuildScreenRect(cameraToPreview, -planeHalfWidth, -planeHalfHeight, planeHalfWidth, planeHalfHeight);
            if (fireScreenRect.width <= 0f || fireScreenRect.height <= 0f)
            {
                return;
            }

            DrawScreenRect(new Rect(0f, 0f, Screen.width, Screen.height), gameViewBlockedColor);
            DrawScreenRect(fireScreenRect, gameViewFireColor);
            DrawScreenRectOutline(fireScreenRect, gameViewLineColor, 2f);
        }

        private void DrawCameraPreviewGizmo()
        {
            Camera cameraToPreview = previewCamera != null ? previewCamera : Camera.main;
            if (cameraToPreview == null || !TryGetViewportRectOnPlane(cameraToPreview, out Rect visibleRect))
            {
                return;
            }

            DrawRectOnPlane(visibleRect, blockedPreviewFillColor, blockedPreviewWireColor);

            Rect fireRect = Rect.MinMaxRect(
                Mathf.Max(visibleRect.xMin, -planeHalfWidth),
                Mathf.Max(visibleRect.yMin, -planeHalfHeight),
                Mathf.Min(visibleRect.xMax, planeHalfWidth),
                Mathf.Min(visibleRect.yMax, planeHalfHeight));

            if (fireRect.width > 0f && fireRect.height > 0f)
            {
                DrawRectOnPlane(fireRect, planeFillColor, planeWireColor);
            }
        }

        private bool TryGetViewportRectOnPlane(Camera cameraToPreview, out Rect rect)
        {
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
            Vector2 max = new Vector2(float.MinValue, float.MinValue);

            if (!TryIncludeViewportCorner(cameraToPreview, new Vector3(0f, 0f, 0f), ref min, ref max)
                || !TryIncludeViewportCorner(cameraToPreview, new Vector3(1f, 0f, 0f), ref min, ref max)
                || !TryIncludeViewportCorner(cameraToPreview, new Vector3(1f, 1f, 0f), ref min, ref max)
                || !TryIncludeViewportCorner(cameraToPreview, new Vector3(0f, 1f, 0f), ref min, ref max))
            {
                rect = default;
                return false;
            }

            rect = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
            return true;
        }

        private bool TryIncludeViewportCorner(Camera cameraToPreview, Vector3 viewportPoint, ref Vector2 min, ref Vector2 max)
        {
            if (!TryRaycast(cameraToPreview.ViewportPointToRay(viewportPoint), out Vector3 worldPoint))
            {
                return false;
            }

            Vector3 localPoint = transform.InverseTransformPoint(worldPoint);
            min.x = Mathf.Min(min.x, localPoint.x);
            min.y = Mathf.Min(min.y, localPoint.y);
            max.x = Mathf.Max(max.x, localPoint.x);
            max.y = Mathf.Max(max.y, localPoint.y);
            return true;
        }

        private void DrawRectOnPlane(Rect rect, Color fillColor, Color wireColor)
        {
            Vector3 corner0 = GetWorldPointOnPlane(rect.xMin, rect.yMin);
            Vector3 corner1 = GetWorldPointOnPlane(rect.xMax, rect.yMin);
            Vector3 corner2 = GetWorldPointOnPlane(rect.xMax, rect.yMax);
            Vector3 corner3 = GetWorldPointOnPlane(rect.xMin, rect.yMax);

#if UNITY_EDITOR
            UnityEditor.Handles.color = fillColor;
            UnityEditor.Handles.DrawAAConvexPolygon(corner0, corner1, corner2, corner3);
#endif

            Gizmos.color = wireColor;
            Gizmos.DrawLine(corner0, corner1);
            Gizmos.DrawLine(corner1, corner2);
            Gizmos.DrawLine(corner2, corner3);
            Gizmos.DrawLine(corner3, corner0);
        }

        private void DrawPlaneGizmo(bool selected)
        {
            Vector3 widthOffset = transform.right * planeHalfWidth;
            Vector3 heightOffset = transform.up * planeHalfHeight;
            Vector3 center = transform.position;
            Vector3 corner0 = center - widthOffset - heightOffset;
            Vector3 corner1 = center + widthOffset - heightOffset;
            Vector3 corner2 = center + widthOffset + heightOffset;
            Vector3 corner3 = center - widthOffset + heightOffset;

            Color fillColor = planeFillColor;
            Color wireColor = planeWireColor;
            if (selected)
            {
                fillColor.a = Mathf.Min(1f, fillColor.a + 0.12f);
                wireColor.a = 1f;
            }

#if UNITY_EDITOR
            UnityEditor.Handles.color = fillColor;
            UnityEditor.Handles.DrawAAConvexPolygon(corner0, corner1, corner2, corner3);
#endif

            Gizmos.color = wireColor;
            Gizmos.DrawLine(corner0, corner1);
            Gizmos.DrawLine(corner1, corner2);
            Gizmos.DrawLine(corner2, corner3);
            Gizmos.DrawLine(corner3, corner0);
            Gizmos.DrawLine(center, center - transform.forward * NormalGizmoLength);
        }

        private void OnValidate()
        {
            planeHalfWidth = Mathf.Max(0.1f, planeHalfWidth);
            planeHalfHeight = Mathf.Max(0.1f, planeHalfHeight);
        }

        private Rect BuildScreenRect(Camera cameraToPreview, float minX, float minY, float maxX, float maxY)
        {
            Vector3 corner0 = cameraToPreview.WorldToScreenPoint(GetWorldPointOnPlane(minX, minY));
            Vector3 corner1 = cameraToPreview.WorldToScreenPoint(GetWorldPointOnPlane(maxX, minY));
            Vector3 corner2 = cameraToPreview.WorldToScreenPoint(GetWorldPointOnPlane(maxX, maxY));
            Vector3 corner3 = cameraToPreview.WorldToScreenPoint(GetWorldPointOnPlane(minX, maxY));

            if (corner0.z <= 0f || corner1.z <= 0f || corner2.z <= 0f || corner3.z <= 0f)
            {
                return Rect.zero;
            }

            float screenMinX = Mathf.Min(corner0.x, corner1.x, corner2.x, corner3.x);
            float screenMaxX = Mathf.Max(corner0.x, corner1.x, corner2.x, corner3.x);
            float screenMinY = Mathf.Min(corner0.y, corner1.y, corner2.y, corner3.y);
            float screenMaxY = Mathf.Max(corner0.y, corner1.y, corner2.y, corner3.y);

            screenMinX = Mathf.Clamp(screenMinX, 0f, Screen.width);
            screenMaxX = Mathf.Clamp(screenMaxX, 0f, Screen.width);
            screenMinY = Mathf.Clamp(screenMinY, 0f, Screen.height);
            screenMaxY = Mathf.Clamp(screenMaxY, 0f, Screen.height);

            return Rect.MinMaxRect(
                screenMinX,
                Screen.height - screenMaxY,
                screenMaxX,
                Screen.height - screenMinY);
        }

        private static void DrawScreenRect(Rect rect, Color color)
        {
            Color previousColor = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, WhiteTexture);
            GUI.color = previousColor;
        }

        private static void DrawScreenRectOutline(Rect rect, Color color, float thickness)
        {
            DrawScreenRect(new Rect(rect.xMin, rect.yMin, rect.width, thickness), color);
            DrawScreenRect(new Rect(rect.xMin, rect.yMax - thickness, rect.width, thickness), color);
            DrawScreenRect(new Rect(rect.xMin, rect.yMin, thickness, rect.height), color);
            DrawScreenRect(new Rect(rect.xMax - thickness, rect.yMin, thickness, rect.height), color);
        }

        private static Texture2D WhiteTexture
        {
            get
            {
                if (whiteTexture == null)
                {
                    whiteTexture = Texture2D.whiteTexture;
                }

                return whiteTexture;
            }
        }
    }
}
