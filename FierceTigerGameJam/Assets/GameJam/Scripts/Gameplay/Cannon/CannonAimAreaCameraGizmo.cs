using UnityEngine;

namespace GameJam.Gameplay.Cannon
{
    [RequireComponent(typeof(Camera))]
    public sealed class CannonAimAreaCameraGizmo : MonoBehaviour
    {
        [SerializeField] private CannonAimPlaneAnchor aimPlane;
        [SerializeField] private Color fireZoneFillColor = new Color(0.2f, 0.85f, 1f, 0.32f);
        [SerializeField] private Color fireZoneWireColor = new Color(0.2f, 0.95f, 1f, 1f);
        [SerializeField] private Color blockedZoneFillColor = new Color(1f, 0.2f, 0.12f, 0.18f);
        [SerializeField] private Color blockedZoneWireColor = new Color(1f, 0.25f, 0.12f, 0.8f);
        [SerializeField] private bool drawCameraRays = true;

        private Camera targetCamera;

        private void Awake()
        {
            targetCamera = GetComponent<Camera>();
        }

        private void OnDrawGizmosSelected()
        {
            if (targetCamera == null)
            {
                targetCamera = GetComponent<Camera>();
            }

            if (targetCamera == null || aimPlane == null)
            {
                return;
            }

            if (!TryGetViewportRectOnAimPlane(targetCamera, aimPlane, out Rect visibleRect))
            {
                return;
            }

            DrawRectOnAimPlane(aimPlane, visibleRect, blockedZoneFillColor, blockedZoneWireColor);
            Rect fireRect = BuildFireRect(aimPlane, visibleRect);
            DrawRectOnAimPlane(aimPlane, fireRect, fireZoneFillColor, fireZoneWireColor);

            if (drawCameraRays)
            {
                DrawCameraRays(targetCamera, aimPlane, fireRect);
            }
        }

        private static Rect BuildFireRect(CannonAimPlaneAnchor plane, Rect visibleRect)
        {
            float minX = Mathf.Max(visibleRect.xMin, -plane.PlaneHalfWidth);
            float maxX = Mathf.Min(visibleRect.xMax, plane.PlaneHalfWidth);
            float minY = Mathf.Max(visibleRect.yMin, -plane.PlaneHalfHeight);
            float maxY = Mathf.Min(visibleRect.yMax, plane.PlaneHalfHeight);

            if (maxX <= minX || maxY <= minY)
            {
                return Rect.MinMaxRect(
                    -plane.PlaneHalfWidth,
                    -plane.PlaneHalfHeight,
                    plane.PlaneHalfWidth,
                    plane.PlaneHalfHeight);
            }

            return Rect.MinMaxRect(minX, minY, maxX, maxY);
        }

        private static bool TryGetViewportRectOnAimPlane(Camera camera, CannonAimPlaneAnchor plane, out Rect rect)
        {
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
            Vector2 max = new Vector2(float.MinValue, float.MinValue);

            if (!TryIncludeViewportCorner(camera, plane, new Vector3(0f, 0f, 0f), ref min, ref max)
                || !TryIncludeViewportCorner(camera, plane, new Vector3(1f, 0f, 0f), ref min, ref max)
                || !TryIncludeViewportCorner(camera, plane, new Vector3(1f, 1f, 0f), ref min, ref max)
                || !TryIncludeViewportCorner(camera, plane, new Vector3(0f, 1f, 0f), ref min, ref max))
            {
                rect = default;
                return false;
            }

            rect = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
            return true;
        }

        private static bool TryIncludeViewportCorner(
            Camera camera,
            CannonAimPlaneAnchor plane,
            Vector3 viewportPoint,
            ref Vector2 min,
            ref Vector2 max)
        {
            if (!plane.TryRaycast(camera.ViewportPointToRay(viewportPoint), out Vector3 worldPoint))
            {
                return false;
            }

            Vector3 localPoint = plane.transform.InverseTransformPoint(worldPoint);
            min.x = Mathf.Min(min.x, localPoint.x);
            min.y = Mathf.Min(min.y, localPoint.y);
            max.x = Mathf.Max(max.x, localPoint.x);
            max.y = Mathf.Max(max.y, localPoint.y);
            return true;
        }

        private static void DrawRectOnAimPlane(CannonAimPlaneAnchor plane, Rect rect, Color fillColor, Color wireColor)
        {
            Vector3 corner0 = plane.GetWorldPointOnPlane(rect.xMin, rect.yMin);
            Vector3 corner1 = plane.GetWorldPointOnPlane(rect.xMax, rect.yMin);
            Vector3 corner2 = plane.GetWorldPointOnPlane(rect.xMax, rect.yMax);
            Vector3 corner3 = plane.GetWorldPointOnPlane(rect.xMin, rect.yMax);

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

        private static void DrawCameraRays(Camera camera, CannonAimPlaneAnchor plane, Rect fireRect)
        {
            Vector3[] corners =
            {
                plane.GetWorldPointOnPlane(fireRect.xMin, fireRect.yMin),
                plane.GetWorldPointOnPlane(fireRect.xMax, fireRect.yMin),
                plane.GetWorldPointOnPlane(fireRect.xMax, fireRect.yMax),
                plane.GetWorldPointOnPlane(fireRect.xMin, fireRect.yMax)
            };

            Gizmos.color = Color.white;
            for (int i = 0; i < corners.Length; i++)
            {
                Gizmos.DrawLine(camera.transform.position, corners[i]);
            }
        }
    }
}
