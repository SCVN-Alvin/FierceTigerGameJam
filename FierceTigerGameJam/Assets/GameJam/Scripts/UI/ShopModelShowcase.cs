using UnityEngine;
using UnityEngine.UI;

namespace GameJam.UI
{
    /// <summary>
    /// Puts a real 3D model on the shop's preview table, photographed rather than simulated: an
    /// offscreen rig (camera, key light, a soft blob shadow) renders the prop into a texture and
    /// a RawImage lays that over the table art. No physics, no layers, no new art - the shadow is
    /// a generated radial gradient, and the rig lives a kilometre away where no scene camera
    /// looks.
    ///
    /// Built entirely at runtime on purpose: both shop screens are baked by editor builders, and
    /// this stays out of their prefabs until someone wants to author it properly.
    /// </summary>
    public sealed class ShopModelShowcase : MonoBehaviour
    {
        private const float FrameSize = 1.6f;        // world units the camera frames
        private static readonly Vector3 RigHome = new Vector3(1000f, -1000f, 1000f);

        private Camera rigCamera;
        private Transform anchor;
        private GameObject shown;
        private RenderTexture output;
        private RawImage screen;

        /// <summary>One rig, made on demand, drawing into a RawImage stretched over the given rect.</summary>
        public static ShopModelShowcase Create(RectTransform over)
        {
            GameObject go = new GameObject("ShopModelShowcase");
            ShopModelShowcase rig = go.AddComponent<ShopModelShowcase>();
            rig.Build(over);
            return rig;
        }

        private void Build(RectTransform over)
        {
            transform.position = RigHome;

            anchor = new GameObject("Anchor").transform;
            anchor.SetParent(transform, false);

            // A quarter-turn of yaw and a touch of camera height is the whole "toy photo" look.
            GameObject cameraGo = new GameObject("Camera");
            cameraGo.transform.SetParent(transform, false);
            cameraGo.transform.localPosition = new Vector3(0f, 0.9f, -2.6f);
            cameraGo.transform.localRotation = Quaternion.Euler(16f, 0f, 0f);
            rigCamera = cameraGo.AddComponent<Camera>();
            rigCamera.clearFlags = CameraClearFlags.SolidColor;
            rigCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            rigCamera.fieldOfView = 34f;
            rigCamera.nearClipPlane = 0.1f;
            rigCamera.farClipPlane = 10f;

            GameObject lightGo = new GameObject("Key Light");
            lightGo.transform.SetParent(transform, false);
            lightGo.transform.localRotation = Quaternion.Euler(45f, -30f, 0f);
            Light key = lightGo.AddComponent<Light>();
            key.type = LightType.Directional;
            key.intensity = 1.15f;

            // The blob under the prop. Unlit, so it reads the same over any table art.
            GameObject blob = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(blob.GetComponent<Collider>());
            blob.name = "Blob Shadow";
            blob.transform.SetParent(transform, false);
            blob.transform.localPosition = new Vector3(0f, -0.48f, 0.1f);
            blob.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            blob.transform.localScale = new Vector3(1.3f, 0.8f, 1f);
            Material blobMaterial = new Material(Shader.Find("Sprites/Default"));
            blobMaterial.mainTexture = BuildBlobTexture();
            blob.GetComponent<MeshRenderer>().material = blobMaterial;

            output = new RenderTexture(512, 512, 16, RenderTextureFormat.ARGB32);
            rigCamera.targetTexture = output;

            // INSIDE the preview rect, stretched to fill it exactly. Copying the preview's
            // anchors out to a sibling went wrong the moment the preview was itself stretched:
            // sizeDelta on stretch anchors is an inset, and re-using it as a size blew the image
            // up over the whole screen.
            GameObject screenGo = new GameObject("ShowcaseImage", typeof(RectTransform));
            RectTransform rect = (RectTransform)screenGo.transform;
            rect.SetParent(over, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            screen = screenGo.AddComponent<RawImage>();
            screen.texture = output;
            screen.raycastTarget = false;
        }

        /// <summary>Puts one prop on the table. Null clears it.</summary>
        public void Show(GameObject modelPrefab)
        {
            gameObject.SetActive(true);
            if (screen != null)
            {
                screen.enabled = modelPrefab != null;
            }

            if (shown != null)
            {
                Destroy(shown);
                shown = null;
            }

            if (modelPrefab == null)
            {
                return;
            }

            shown = Instantiate(modelPrefab, anchor);
            StripToProp(shown);
            FitToFrame(shown);
            shown.transform.localRotation = Quaternion.Euler(0f, 32f, 0f) * shown.transform.localRotation;
        }

        /// <summary>Switches the rig, its camera and its screen off together.</summary>
        public void Hide()
        {
            if (screen != null)
            {
                screen.enabled = false;
            }

            gameObject.SetActive(false);
        }

        /// <summary>A prop poses; it does not simulate, collide, or run its scripts.</summary>
        private static void StripToProp(GameObject prop)
        {
            foreach (MonoBehaviour behaviour in prop.GetComponentsInChildren<MonoBehaviour>(true))
            {
                Destroy(behaviour);
            }

            foreach (Collider collider in prop.GetComponentsInChildren<Collider>(true))
            {
                Destroy(collider);
            }

            foreach (Rigidbody body in prop.GetComponentsInChildren<Rigidbody>(true))
            {
                Destroy(body);
            }
        }

        /// <summary>Scales and centres the prop so any model fills the frame like a toy in a box.</summary>
        private void FitToFrame(GameObject prop)
        {
            Renderer[] renderers = prop.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                return;
            }

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            float largest = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z, 0.0001f);
            float scale = FrameSize / largest;
            prop.transform.localScale *= scale;
            prop.transform.localPosition = -(bounds.center - prop.transform.position) * scale;
        }

        /// <summary>A soft dark ellipse, drawn once. 64 pixels is plenty for a blur this soft.</summary>
        private static Texture2D BuildBlobTexture()
        {
            const int size = 64;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - size * 0.5f) / (size * 0.5f);
                    float dy = (y - size * 0.5f) / (size * 0.5f);
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float alpha = Mathf.SmoothStep(0.55f, 0f, d);
                    texture.SetPixel(x, y, new Color(0f, 0f, 0f, alpha));
                }
            }

            texture.Apply();
            return texture;
        }
    }
}
