using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GameJam.Gameplay.Wall
{
    internal static class RuntimeGlassFracture
    {
        private const float RelativeWeldTolerance = 0.000001f;
        private const int MaximumShardCount = 128;

        private readonly struct WeldedVertex : System.IEquatable<WeldedVertex>
        {
            private readonly int x;
            private readonly int y;
            private readonly int z;

            public WeldedVertex(Vector3 position, float tolerance)
            {
                x = Mathf.RoundToInt(position.x / tolerance);
                y = Mathf.RoundToInt(position.y / tolerance);
                z = Mathf.RoundToInt(position.z / tolerance);
            }

            public bool Equals(WeldedVertex other)
            {
                return x == other.x && y == other.y && z == other.z;
            }

            public override bool Equals(object obj)
            {
                return obj is WeldedVertex other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = x;
                    hash = (hash * 397) ^ y;
                    return (hash * 397) ^ z;
                }
            }
        }

        private sealed class ShardTemplate
        {
            public Mesh Mesh;
            public Vector3 LocalCenter;
        }

        private static readonly Dictionary<Mesh, ShardTemplate[]> Cache = new Dictionary<Mesh, ShardTemplate[]>();

        public static bool TryFracture(
            SmashBlock source,
            MeshFilter meshFilter,
            MeshRenderer meshRenderer,
            Collider sourceCollider,
            Vector3 impactPoint,
            Vector3 impulse)
        {
            Mesh mesh = meshFilter.sharedMesh;
            if (mesh == null)
            {
                return false;
            }

            ShardTemplate[] templates = GetOrCreateTemplates(mesh);
            if (templates.Length <= 1)
            {
                return false;
            }

            Transform meshTransform = meshFilter.transform;
            Material[] materials = meshRenderer.sharedMaterials;
            float shardMass = Mathf.Max(0.015f, source.GetMass() / templates.Length);

            for (int i = 0; i < templates.Length; i++)
            {
                ShardTemplate template = templates[i];
                GameObject shard = new GameObject($"GlassShard_{i:00}");
                shard.transform.position = meshTransform.TransformPoint(template.LocalCenter);
                shard.transform.rotation = Quaternion.identity;
                shard.transform.localScale = Vector3.one;

                MeshFilter shardFilter = shard.AddComponent<MeshFilter>();
                shardFilter.sharedMesh = CreateWorldSpaceShardMesh(template.Mesh, meshTransform.localToWorldMatrix);
                MeshRenderer shardRenderer = shard.AddComponent<MeshRenderer>();
                shardRenderer.sharedMaterials = materials;
                MaterialPropertyBlock propertyBlock = new MaterialPropertyBlock();
                meshRenderer.GetPropertyBlock(propertyBlock);
                shardRenderer.SetPropertyBlock(propertyBlock);

                BoxCollider shardCollider = shard.AddComponent<BoxCollider>();
                shardCollider.center = template.Mesh.bounds.center;
                shardCollider.size = template.Mesh.bounds.size;

                Rigidbody shardBody = shard.AddComponent<Rigidbody>();
                shardBody.mass = shardMass;
                shardBody.linearDamping = 0.08f;
                shardBody.angularDamping = 0.04f;
                shardBody.interpolation = RigidbodyInterpolation.Interpolate;
                shardBody.collisionDetectionMode = CollisionDetectionMode.Continuous;

                Vector3 away = (shard.transform.position - impactPoint).normalized;
                if (away.sqrMagnitude < 0.001f)
                {
                    away = Random.onUnitSphere;
                }

                Vector3 scatter = (away + Random.insideUnitSphere * 0.35f + Vector3.up * 0.18f).normalized;
                shardBody.AddForce(impulse * 0.42f + scatter * impulse.magnitude * 0.24f, ForceMode.Impulse);
                shardBody.AddTorque(Random.insideUnitSphere * impulse.magnitude * 0.08f, ForceMode.Impulse);
                Object.Destroy(shardFilter.sharedMesh, 8f);
                Object.Destroy(shard, 8f);
            }

            if (sourceCollider != null)
            {
                sourceCollider.enabled = false;
            }

            return true;
        }

        private static Mesh CreateWorldSpaceShardMesh(Mesh template, Matrix4x4 localToWorld)
        {
            Vector3[] sourceVertices = template.vertices;
            Vector3[] vertices = new Vector3[sourceVertices.Length];
            for (int i = 0; i < sourceVertices.Length; i++)
            {
                vertices[i] = localToWorld.MultiplyVector(sourceVertices[i]);
            }

            Vector3[] sourceNormals = template.normals;
            Vector3[] normals = new Vector3[sourceNormals.Length];
            Matrix4x4 normalMatrix = localToWorld.inverse.transpose;
            for (int i = 0; i < sourceNormals.Length; i++)
            {
                normals[i] = normalMatrix.MultiplyVector(sourceNormals[i]).normalized;
            }

            Mesh worldMesh = new Mesh
            {
                name = template.name + "_World",
                indexFormat = template.indexFormat
            };
            worldMesh.vertices = vertices;
            worldMesh.triangles = template.triangles;
            if (normals.Length == vertices.Length)
            {
                worldMesh.normals = normals;
            }
            else
            {
                worldMesh.RecalculateNormals();
            }

            Vector2[] uv = template.uv;
            if (uv.Length == vertices.Length)
            {
                worldMesh.uv = uv;
            }

            worldMesh.RecalculateBounds();
            return worldMesh;
        }

        private static ShardTemplate[] GetOrCreateTemplates(Mesh source)
        {
            if (Cache.TryGetValue(source, out ShardTemplate[] cached))
            {
                return cached;
            }

            int[] triangles = source.triangles;
            Vector3[] vertices = source.vertices;
            Vector3[] normals = source.normals;
            Vector2[] uv = source.uv;
            int triangleCount = triangles.Length / 3;
            float weldTolerance = Mathf.Max(1e-9f, source.bounds.size.magnitude * RelativeWeldTolerance);

            Dictionary<WeldedVertex, List<int>> trianglesByVertex = new Dictionary<WeldedVertex, List<int>>();
            for (int triangle = 0; triangle < triangleCount; triangle++)
            {
                for (int corner = 0; corner < 3; corner++)
                {
                    int vertex = triangles[triangle * 3 + corner];
                    WeldedVertex weldedVertex = new WeldedVertex(vertices[vertex], weldTolerance);
                    if (!trianglesByVertex.TryGetValue(weldedVertex, out List<int> adjacent))
                    {
                        adjacent = new List<int>();
                        trianglesByVertex.Add(weldedVertex, adjacent);
                    }

                    adjacent.Add(triangle);
                }
            }

            bool[] visited = new bool[triangleCount];
            List<ShardTemplate> templates = new List<ShardTemplate>();
            Queue<int> queue = new Queue<int>();

            for (int seed = 0; seed < triangleCount; seed++)
            {
                if (visited[seed])
                {
                    continue;
                }

                List<int> component = new List<int>();
                visited[seed] = true;
                queue.Enqueue(seed);
                while (queue.Count > 0)
                {
                    int triangle = queue.Dequeue();
                    component.Add(triangle);
                    for (int corner = 0; corner < 3; corner++)
                    {
                        int vertex = triangles[triangle * 3 + corner];
                        List<int> adjacent = trianglesByVertex[new WeldedVertex(vertices[vertex], weldTolerance)];
                        for (int i = 0; i < adjacent.Count; i++)
                        {
                            int neighbor = adjacent[i];
                            if (visited[neighbor])
                            {
                                continue;
                            }

                            visited[neighbor] = true;
                            queue.Enqueue(neighbor);
                        }
                    }
                }

                templates.Add(BuildTemplate(source.name, templates.Count, component, triangles, vertices, normals, uv));
                if (templates.Count > MaximumShardCount)
                {
                    Debug.LogError($"Glass mesh '{source.name}' produced more than {MaximumShardCount} connected shards. Fracture disabled to protect runtime performance.");
                    cached = System.Array.Empty<ShardTemplate>();
                    Cache.Add(source, cached);
                    return cached;
                }
            }

            cached = templates.ToArray();
            Cache.Add(source, cached);
            return cached;
        }

        private static ShardTemplate BuildTemplate(
            string sourceName,
            int shardIndex,
            List<int> component,
            int[] sourceTriangles,
            Vector3[] sourceVertices,
            Vector3[] sourceNormals,
            Vector2[] sourceUv)
        {
            Dictionary<int, int> remap = new Dictionary<int, int>();
            List<Vector3> vertices = new List<Vector3>();
            List<Vector3> normals = new List<Vector3>();
            List<Vector2> uv = new List<Vector2>();
            List<int> triangles = new List<int>();
            Vector3 center = Vector3.zero;

            for (int i = 0; i < component.Count; i++)
            {
                int triangle = component[i];
                for (int corner = 0; corner < 3; corner++)
                {
                    int sourceVertex = sourceTriangles[triangle * 3 + corner];
                    if (!remap.TryGetValue(sourceVertex, out int localVertex))
                    {
                        localVertex = vertices.Count;
                        remap.Add(sourceVertex, localVertex);
                        Vector3 position = sourceVertices[sourceVertex];
                        vertices.Add(position);
                        center += position;
                        normals.Add(sourceNormals.Length == sourceVertices.Length ? sourceNormals[sourceVertex] : Vector3.zero);
                        uv.Add(sourceUv.Length == sourceVertices.Length ? sourceUv[sourceVertex] : Vector2.zero);
                    }

                    triangles.Add(localVertex);
                }
            }

            center /= Mathf.Max(1, vertices.Count);
            for (int i = 0; i < vertices.Count; i++)
            {
                vertices[i] -= center;
            }

            Mesh mesh = new Mesh
            {
                name = $"{sourceName}_Shard_{shardIndex:00}",
                indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16
            };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            if (sourceNormals.Length == sourceVertices.Length)
            {
                mesh.SetNormals(normals);
            }
            else
            {
                mesh.RecalculateNormals();
            }

            if (sourceUv.Length == sourceVertices.Length)
            {
                mesh.SetUVs(0, uv);
            }

            mesh.RecalculateBounds();
            return new ShardTemplate { Mesh = mesh, LocalCenter = center };
        }
    }
}
