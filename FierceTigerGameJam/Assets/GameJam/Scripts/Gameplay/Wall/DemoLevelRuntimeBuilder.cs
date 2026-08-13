using System;
using System.Collections;
using System.Collections.Generic;
using GameJam.Gameplay.Cannon;
using UnityEngine;

namespace GameJam.Gameplay.Wall
{
    [DisallowMultipleComponent]
    public sealed class DemoLevelRuntimeBuilder : MonoBehaviour
    {
        [SerializeField] private Transform modelRoot;
        [SerializeField] private bool buildOnAwake = true;
        [SerializeField] private int blocksPerFrame;

        private static readonly int BaseColorProperty = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorProperty = Shader.PropertyToID("_Color");

        public Transform ModelRoot => modelRoot;

        private void Awake()
        {
            CannonAimPlaneAnchor aimPlane = FindFirstObjectByType<CannonAimPlaneAnchor>();
            if (aimPlane != null)
            {
                aimPlane.ConfigureBounds(aimPlane.PlaneHalfWidth, aimPlane.PlaneHalfHeight, false);
            }

            if (buildOnAwake)
            {
                StartCoroutine(BuildPhysics());
            }
        }

        public IEnumerator BuildPhysics()
        {
            Transform root = modelRoot != null ? modelRoot : transform;
            List<Transform> wrappers = CollectWrappers(root);
            bool buildDetails = true;
            for (int i = 0; i < wrappers.Count; i++)
            {
                SmashMaterialType type = Classify(wrappers[i]);
                if (type == SmashMaterialType.Detail)
                {
                    if (buildDetails)
                    {
                        ConfigureDetails(wrappers[i]);
                        buildDetails = false;
                    }
                }
                else
                {
                    ConfigureWrapper(wrappers[i], type);
                }

                if (blocksPerFrame > 0 && (i + 1) % blocksPerFrame == 0)
                {
                    yield return null;
                }
            }
        }

        private static void ConfigureDetails(Transform detailsRoot)
        {
            MeshRenderer[] renderers = detailsRoot.GetComponentsInChildren<MeshRenderer>(true);
            if (renderers.Length == 0)
            {
                return;
            }

            const float clusterSize = 0.55f;
            Dictionary<Vector3Int, DetailCluster> clusters = new Dictionary<Vector3Int, DetailCluster>();
            Matrix4x4 worldToRoot = detailsRoot.worldToLocalMatrix;
            for (int i = 0; i < renderers.Length; i++)
            {
                Bounds worldBounds = renderers[i].bounds;
                Vector3Int key = new Vector3Int(
                    Mathf.FloorToInt(worldBounds.center.x / clusterSize),
                    Mathf.FloorToInt(worldBounds.center.y / clusterSize),
                    Mathf.FloorToInt(worldBounds.center.z / clusterSize));
                Bounds localBounds = TransformWorldBounds(worldBounds, worldToRoot);
                if (clusters.TryGetValue(key, out DetailCluster clusterData))
                {
                    clusterData.Bounds.Encapsulate(localBounds);
                    clusterData.Renderers.Add(renderers[i].transform);
                }
                else
                {
                    clusters.Add(key, new DetailCluster(localBounds, renderers[i].transform));
                }
            }

            int clusterIndex = 0;
            foreach (KeyValuePair<Vector3Int, DetailCluster> pair in clusters)
            {
                GameObject cluster = new GameObject($"DetailPhysics_{clusterIndex++:000}");
                cluster.transform.SetParent(detailsRoot, false);
                BoxCollider collider = cluster.AddComponent<BoxCollider>();
                collider.center = pair.Value.Bounds.center;
                collider.size = pair.Value.Bounds.size;
                for (int i = 0; i < pair.Value.Renderers.Count; i++)
                {
                    pair.Value.Renderers[i].SetParent(cluster.transform, true);
                }

                SmashBlock block = cluster.AddComponent<SmashBlock>();
                block.Configure(SmashMaterialType.Detail, 1.35f, 0.9f, 0.28f, 0.22f, 2.1f, false);
            }
        }

        private sealed class DetailCluster
        {
            public Bounds Bounds;
            public readonly List<Transform> Renderers = new List<Transform>();

            public DetailCluster(Bounds bounds, Transform renderer)
            {
                Bounds = bounds;
                Renderers.Add(renderer);
            }
        }

        private static Bounds TransformWorldBounds(Bounds worldBounds, Matrix4x4 worldToLocal)
        {
            Bounds result = default;
            bool hasBounds = false;
            EncapsulateTransformedBounds(ref result, ref hasBounds, worldBounds, worldToLocal);
            return result;
        }

        private static List<Transform> CollectWrappers(Transform root)
        {
            List<Transform> wrappers = new List<Transform>();
            foreach (Transform materialRoot in root)
            {
                SmashMaterialType type = Classify(materialRoot);
                if (type == SmashMaterialType.Detail)
                {
                    if (materialRoot.GetComponentInChildren<MeshRenderer>(true) != null)
                    {
                        wrappers.Add(materialRoot);
                    }

                    continue;
                }

                foreach (Transform wrapper in materialRoot)
                {
                    if (wrapper.GetComponentInChildren<MeshRenderer>(true) != null)
                    {
                        wrappers.Add(wrapper);
                    }
                }
            }

            return wrappers;
        }

        private static SmashMaterialType Classify(Transform transformToClassify)
        {
            Transform current = transformToClassify;
            while (current != null)
            {
                string objectName = current.name;
                if (objectName.IndexOf("Brick", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return SmashMaterialType.Brick;
                }

                if (objectName.IndexOf("Glass", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return SmashMaterialType.Glass;
                }

                if (objectName.IndexOf("Concrete", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return SmashMaterialType.Concrete;
                }

                if (objectName.IndexOf("Detail", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return SmashMaterialType.Detail;
                }

                current = current.parent;
            }

            return SmashMaterialType.Detail;
        }

        private static void ConfigureWrapper(Transform wrapper, SmashMaterialType type)
        {
            if (wrapper.GetComponent<SmashBlock>() != null)
            {
                return;
            }

            Bounds localBounds = CalculateLocalBounds(wrapper);
            if (localBounds.size.sqrMagnitude <= 0f)
            {
                return;
            }

            BoxCollider collider = wrapper.gameObject.AddComponent<BoxCollider>();
            collider.center = localBounds.center;
            collider.size = localBounds.size;

            ApplyVisualTint(wrapper, type);

            SmashBlock block = wrapper.gameObject.AddComponent<SmashBlock>();
            switch (type)
            {
                case SmashMaterialType.Brick:
                    block.Configure(type, 0.38f, 2.2f, 0.04f, 0.035f, 1.1f, false);
                    break;
                case SmashMaterialType.Glass:
                    block.Configure(type, 0.3f, 1.25f, 0.08f, 0.05f, 1.35f, true);
                    break;
                case SmashMaterialType.Concrete:
                    block.Configure(type, 2.8f, 0.52f, 0.55f, 0.38f, 2.8f, false);
                    break;
                default:
                    block.Configure(type, 1.35f, 0.9f, 0.28f, 0.22f, 2.1f, false);
                    break;
            }
        }

        private static void ApplyVisualTint(Transform wrapper, SmashMaterialType type)
        {
            Color tint;
            switch (type)
            {
                case SmashMaterialType.Brick:
                    tint = new Color(0.78f, 0.31f, 0.14f, 1f);
                    break;
                case SmashMaterialType.Concrete:
                    tint = new Color(0.58f, 0.61f, 0.64f, 1f);
                    break;
                case SmashMaterialType.Glass:
                    tint = new Color(0.42f, 0.85f, 0.96f, 0.72f);
                    break;
                default:
                    return;
            }

            MaterialPropertyBlock propertyBlock = new MaterialPropertyBlock();
            MeshRenderer[] renderers = wrapper.GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                MeshRenderer renderer = renderers[i];
                renderer.GetPropertyBlock(propertyBlock);
                propertyBlock.SetColor(BaseColorProperty, tint);
                propertyBlock.SetColor(ColorProperty, tint);
                renderer.SetPropertyBlock(propertyBlock);
            }
        }

        private static Bounds CalculateLocalBounds(Transform wrapper)
        {
            MeshFilter[] filters = wrapper.GetComponentsInChildren<MeshFilter>(true);
            Bounds bounds = default;
            bool hasBounds = false;
            Matrix4x4 worldToWrapper = wrapper.worldToLocalMatrix;
            for (int i = 0; i < filters.Length; i++)
            {
                MeshFilter filter = filters[i];
                if (filter.sharedMesh == null)
                {
                    continue;
                }

                Bounds meshBounds = filter.sharedMesh.bounds;
                Matrix4x4 matrix = worldToWrapper * filter.transform.localToWorldMatrix;
                EncapsulateTransformedBounds(ref bounds, ref hasBounds, meshBounds, matrix);
            }

            return bounds;
        }

        private static void EncapsulateTransformedBounds(ref Bounds target, ref bool hasBounds, Bounds source, Matrix4x4 matrix)
        {
            Vector3 center = source.center;
            Vector3 extents = source.extents;
            for (int x = -1; x <= 1; x += 2)
            {
                for (int y = -1; y <= 1; y += 2)
                {
                    for (int z = -1; z <= 1; z += 2)
                    {
                        Vector3 point = matrix.MultiplyPoint3x4(center + Vector3.Scale(extents, new Vector3(x, y, z)));
                        if (!hasBounds)
                        {
                            target = new Bounds(point, Vector3.zero);
                            hasBounds = true;
                        }
                        else
                        {
                            target.Encapsulate(point);
                        }
                    }
                }
            }
        }
    }
}
