#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using GameJam.Gameplay;
using GameJam.Gameplay.Wall;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace GameJam.EditorTools
{
    /// <summary>
    /// Builds grid-ready knockdown block prefabs out of the imported art.
    /// The art from the Art/import_Asset branch ships as pre-fractured shard sets
    /// (Brick.fbx has 360 cells, Glass.fbx has 44), so each block visual is welded
    /// back into a single mesh before it becomes a block. The shard hierarchy is
    /// kept in a separate variant so the fracture path can use it later.
    /// </summary>
    public static class BlockPrefabBuilder
    {
        private const string OutputFolder = "Assets/GameJam/Prefabs/Blocks";
        private const string MeshFolderName = "Meshes";

        /// <summary>Where earlier runs wrote every block before they were split by material.</summary>
        private const string LegacyMeshFolder = "Assets/GameJam/Prefabs/Blocks/Meshes";
        private const string MaterialFolder = "Assets/GameJam/Materials";
        private const string GlassMaterialPath = "Assets/GameJam/Materials/M_Glass.mat";
        private const string UrpLitShaderName = "Universal Render Pipeline/Lit";
        private const string VisualChildName = "Visual";
        private const float MinBoundsExtent = 0.0001f;
        private const string BlockSizeReferencePrefab = "Assets/GameJam/Prefabs/Cube.prefab";

        /// <summary>
        /// Used only when Cube.prefab cannot be loaded; it is the world size that prefab renders at.
        /// </summary>
        private static readonly Vector3 FallbackBlockSize = new Vector3(0.25f, 0.25f, 0.25f);

        /// <summary>
        /// The six cells Brick_block in Gameplay.unity is assembled from. The other 354 cells
        /// in Brick.fbx are the rest of the fracture set and are not part of the block silhouette.
        /// </summary>
        private static readonly string[] BrickBlockShardNames =
        {
            "Brick.001_cell.001",
            "Brick.001_cell.007",
            "Brick.001_cell.008",
            "Brick.001_cell.009",
            "Brick.001_cell.010",
            "Brick.001_cell.011",
        };

        private struct BlockSpec
        {
            public string BlockName;

            /// <summary>Subfolder of the blocks folder this prefab is written to, e.g. "Brick".</summary>
            public string Category;
            public string ModelPath;
            public string[] ShardNames;
            public string MaterialPathOverride;

            /// <summary>Cells the block covers. The block is sized to this many grid cells.</summary>
            public Vector3Int LogicalSize;
            public float Mass;
            public bool AllowCollisionCascade;
            public float CollisionActivationVelocity;
            public KnockdownBlock.SupportCascadeMode SupportCascadeMode;
            public float SupportReleaseImpulse;
            public bool CountsTowardKnockdown;
        }

        private static readonly BlockSpec[] Specs =
        {
            new BlockSpec
            {
                BlockName = "Block_Brick",
                Category = "Brick",
                ModelPath = "Assets/GameJam/FBX/Brick.fbx",
                ShardNames = BrickBlockShardNames,
                MaterialPathOverride = null,
                Mass = 1.2f,
                AllowCollisionCascade = true,
                CollisionActivationVelocity = 1.75f,
                SupportCascadeMode = KnockdownBlock.SupportCascadeMode.ColumnAbove,
                SupportReleaseImpulse = 0.35f,
                CountsTowardKnockdown = true,
            },
            new BlockSpec
            {
                BlockName = "Block_Glass",
                Category = "Glass",
                ModelPath = "Assets/GameJam/FBX/Glass.fbx",
                MaterialPathOverride = GlassMaterialPath,
                Mass = 0.6f,
                AllowCollisionCascade = true,
                CollisionActivationVelocity = 1f,
                SupportCascadeMode = KnockdownBlock.SupportCascadeMode.ColumnAbove,
                SupportReleaseImpulse = 0.5f,
                CountsTowardKnockdown = true,
            },
            new BlockSpec
            {
                BlockName = "Block_Concrete",
                Category = "Concrete",
                ModelPath = "Assets/GameJam/FBX/concrete.fbx",
                MaterialPathOverride = null,
                Mass = 2f,
                AllowCollisionCascade = true,
                CollisionActivationVelocity = 2.5f,
                SupportCascadeMode = KnockdownBlock.SupportCascadeMode.ColumnAbove,
                SupportReleaseImpulse = 0.25f,
                CountsTowardKnockdown = true,
            },
            // Mock-up: the same brick art stretched across two cells, so the map loader's
            // multi-cell footprint and rotation can be exercised before real 2x1 art lands.
            new BlockSpec
            {
                BlockName = "Block_Brick_2x1",
                Category = "Brick",
                ModelPath = "Assets/GameJam/FBX/Brick.fbx",
                ShardNames = BrickBlockShardNames,
                MaterialPathOverride = null,
                LogicalSize = new Vector3Int(2, 1, 1),
                Mass = 2.4f,
                AllowCollisionCascade = true,
                CollisionActivationVelocity = 1.75f,
                SupportCascadeMode = KnockdownBlock.SupportCascadeMode.ColumnAbove,
                SupportReleaseImpulse = 0.35f,
                CountsTowardKnockdown = true,
            },
        };

        [MenuItem("Tools/Smashdown/Build Block Prefabs")]
        public static void BuildBlockPrefabs()
        {
            BuildAll(true);
        }

        [MenuItem("Tools/Smashdown/Build Block Prefabs (Keep Shards)")]
        public static void BuildFracturedBlockPrefabs()
        {
            BuildAll(false);
        }

        private static void BuildAll(bool combineVisualMesh)
        {
            EnsureFolder(OutputFolder);

            Vector3 targetBlockSize = ResolveTargetBlockSize();
            Debug.Log($"{nameof(BlockPrefabBuilder)} target block size {targetBlockSize:F6} (from {BlockSizeReferencePrefab}).");

            List<string> built = new List<string>();
            for (int i = 0; i < Specs.Length; i++)
            {
                string prefabPath = BuildBlockPrefab(Specs[i], combineVisualMesh, targetBlockSize);
                if (!string.IsNullOrEmpty(prefabPath))
                {
                    built.Add(prefabPath);
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"Built {built.Count} block prefab(s):\n{string.Join("\n", built)}");
        }

        private static string BuildBlockPrefab(BlockSpec spec, bool combineVisualMesh, Vector3 targetBlockSize)
        {
            if (combineVisualMesh)
            {
                // Reimports when needed, so this has to happen before the asset is loaded.
                EnsureModelIsReadable(spec.ModelPath);
            }

            GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(spec.ModelPath);
            if (modelAsset == null)
            {
                Debug.LogError($"{nameof(BlockPrefabBuilder)} could not find a model at {spec.ModelPath}.");
                return null;
            }

            string suffix = combineVisualMesh ? string.Empty : "_Fractured";
            string blockFolder = ResolveBlockFolder(spec);
            EnsureFolder(blockFolder);

            string prefabPath = $"{blockFolder}/{spec.BlockName}{suffix}.prefab";
            MigrateLegacyAsset($"{OutputFolder}/{spec.BlockName}{suffix}.prefab", prefabPath);

            GameObject root = new GameObject(spec.BlockName + suffix);
            try
            {
                GameObject visual = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset);
                if (visual == null)
                {
                    Debug.LogError($"{nameof(BlockPrefabBuilder)} could not instantiate {spec.ModelPath}.");
                    return null;
                }

                visual.name = VisualChildName;
                visual.transform.SetParent(root.transform, false);
                visual.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                visual.transform.localScale = Vector3.one;

                if (!StripUnusedShards(visual, spec))
                {
                    return null;
                }

                Material overrideMaterial = ResolveOverrideMaterial(spec);
                if (overrideMaterial != null)
                {
                    ApplyMaterial(visual, overrideMaterial);
                }

                if (!TryGetRendererBounds(visual, out Bounds sourceBounds))
                {
                    Debug.LogError($"{nameof(BlockPrefabBuilder)} found no renderers under {spec.ModelPath}.");
                    return null;
                }

                if (combineVisualMesh)
                {
                    visual = CombineVisual(root, visual, spec, overrideMaterial);
                    if (visual == null)
                    {
                        return null;
                    }

                    if (!TryGetRendererBounds(visual, out sourceBounds))
                    {
                        Debug.LogError($"{nameof(BlockPrefabBuilder)} lost bounds while combining {spec.BlockName}.");
                        return null;
                    }
                }

                Vector3Int logicalSize = ResolveLogicalSize(spec);
                Vector3 targetSize = Vector3.Scale(targetBlockSize, new Vector3(logicalSize.x, logicalSize.y, logicalSize.z));
                Vector3 fitScale = ResolveFitScale(sourceBounds.size, targetSize);
                visual.transform.localScale = fitScale;
                visual.transform.localPosition = -Vector3.Scale(sourceBounds.center, fitScale);

                // Root stays at scale 1, so the collider is the block's world size directly.
                BoxCollider blockCollider = root.AddComponent<BoxCollider>();
                blockCollider.center = Vector3.zero;
                blockCollider.size = Vector3.Scale(sourceBounds.size, fitScale);

                Debug.Log(
                    $"{spec.BlockName}{suffix}: source bounds {sourceBounds.size:F6} -> visual scale {fitScale:F9}, "
                    + $"collider size {blockCollider.size:F6}");

                KnockdownBlockAuthoring authoring = root.AddComponent<KnockdownBlockAuthoring>();
                ApplyAuthoringValues(authoring, spec, logicalSize);

                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                return prefabPath;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// Keeps only the cells the block is actually built from. Brick.fbx carries the whole
        /// 360-cell fracture set, but Brick_block in Gameplay.unity uses six of them; the rest
        /// would inflate both the silhouette and the bounds the block is scaled against.
        /// </summary>
        private static bool StripUnusedShards(GameObject visual, BlockSpec spec)
        {
            if (spec.ShardNames == null || spec.ShardNames.Length == 0)
            {
                return true;
            }

            HashSet<string> keep = new HashSet<string>(spec.ShardNames);

            // Children of a prefab instance cannot be removed while the link is intact.
            PrefabUtility.UnpackPrefabInstance(visual, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

            List<GameObject> doomed = new List<GameObject>();
            MeshFilter[] shardFilters = visual.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < shardFilters.Length; i++)
            {
                GameObject shard = shardFilters[i].gameObject;
                if (shard != visual && !keep.Contains(shard.name))
                {
                    doomed.Add(shard);
                }
            }

            for (int i = 0; i < doomed.Count; i++)
            {
                UnityEngine.Object.DestroyImmediate(doomed[i]);
            }

            int kept = shardFilters.Length - doomed.Count;
            if (kept != spec.ShardNames.Length)
            {
                Debug.LogError(
                    $"{nameof(BlockPrefabBuilder)} expected {spec.ShardNames.Length} cells for {spec.BlockName} "
                    + $"but matched {kept} in {spec.ModelPath}. Check the cell names.");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Welds the shard hierarchy into one mesh so a grid of blocks does not cost
        /// hundreds of renderers per block. The combined mesh is saved next to the prefab.
        /// </summary>
        private static GameObject CombineVisual(GameObject root, GameObject shardVisual, BlockSpec spec, Material overrideMaterial)
        {
            MeshFilter[] shardFilters = shardVisual.GetComponentsInChildren<MeshFilter>(true);
            List<CombineInstance> combineInstances = new List<CombineInstance>(shardFilters.Length);
            Material sharedMaterial = overrideMaterial;

            for (int i = 0; i < shardFilters.Length; i++)
            {
                Mesh shardMesh = shardFilters[i].sharedMesh;
                if (shardMesh == null)
                {
                    continue;
                }

                if (sharedMaterial == null
                    && shardFilters[i].TryGetComponent(out MeshRenderer shardRenderer)
                    && shardRenderer.sharedMaterial != null)
                {
                    sharedMaterial = shardRenderer.sharedMaterial;
                }

                for (int subMesh = 0; subMesh < shardMesh.subMeshCount; subMesh++)
                {
                    combineInstances.Add(new CombineInstance
                    {
                        mesh = shardMesh,
                        subMeshIndex = subMesh,
                        transform = shardVisual.transform.worldToLocalMatrix * shardFilters[i].transform.localToWorldMatrix,
                    });
                }
            }

            if (combineInstances.Count == 0)
            {
                Debug.LogError($"{nameof(BlockPrefabBuilder)} found no readable meshes for {spec.BlockName}.");
                return null;
            }

            Mesh combinedMesh = new Mesh
            {
                name = $"{spec.BlockName}_Mesh",
                indexFormat = IndexFormat.UInt32,
            };
            // Normals and tangents come across with the shards; recalculating them here would
            // smooth the hard edges the fracture relies on.
            combinedMesh.CombineMeshes(combineInstances.ToArray(), true, true);
            combinedMesh.RecalculateBounds();
            combinedMesh.Optimize();

            string meshFolder = ResolveMeshFolder(spec);
            EnsureFolder(meshFolder);

            string meshPath = $"{meshFolder}/{spec.BlockName}_Mesh.asset";
            string legacyMeshPath = $"{LegacyMeshFolder}/{spec.BlockName}_Mesh.asset";
            if (legacyMeshPath != meshPath)
            {
                // The mesh is regenerated every run, so the stale copy is just clutter.
                AssetDatabase.DeleteAsset(legacyMeshPath);
            }

            AssetDatabase.DeleteAsset(meshPath);
            AssetDatabase.CreateAsset(combinedMesh, meshPath);

            UnityEngine.Object.DestroyImmediate(shardVisual);

            GameObject combinedVisual = new GameObject(VisualChildName);
            combinedVisual.transform.SetParent(root.transform, false);
            combinedVisual.AddComponent<MeshFilter>().sharedMesh = combinedMesh;
            MeshRenderer combinedRenderer = combinedVisual.AddComponent<MeshRenderer>();
            combinedRenderer.sharedMaterial = sharedMaterial;
            return combinedVisual;
        }

        /// <summary>
        /// Per-axis fit so every block ends up the same world size as Cube.prefab, whatever
        /// dimensions the art was authored at. Axes are independent, so a block whose source
        /// mesh is not cubic gets stretched rather than letterboxed inside the cell.
        /// </summary>
        private static Vector3 ResolveFitScale(Vector3 size, Vector3 targetSize)
        {
            return new Vector3(
                targetSize.x / Mathf.Max(MinBoundsExtent, size.x),
                targetSize.y / Mathf.Max(MinBoundsExtent, size.y),
                targetSize.z / Mathf.Max(MinBoundsExtent, size.z));
        }

        /// <summary>
        /// Reads the block size off Cube.prefab so the generated blocks stay in step with it.
        /// </summary>
        private static Vector3 ResolveTargetBlockSize()
        {
            GameObject referenceAsset = AssetDatabase.LoadAssetAtPath<GameObject>(BlockSizeReferencePrefab);
            if (referenceAsset == null)
            {
                Debug.LogWarning(
                    $"{nameof(BlockPrefabBuilder)} could not load {BlockSizeReferencePrefab}; "
                    + $"falling back to {FallbackBlockSize}.");
                return FallbackBlockSize;
            }

            GameObject referenceInstance = (GameObject)PrefabUtility.InstantiatePrefab(referenceAsset);
            try
            {
                if (TryGetRendererBounds(referenceInstance, out Bounds referenceBounds))
                {
                    return referenceBounds.size;
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(referenceInstance);
            }

            Debug.LogWarning(
                $"{nameof(BlockPrefabBuilder)} found no renderer on {BlockSizeReferencePrefab}; "
                + $"falling back to {FallbackBlockSize}.");
            return FallbackBlockSize;
        }

        private static bool TryGetRendererBounds(GameObject visual, out Bounds bounds)
        {
            Renderer[] renderers = visual.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                bounds = default;
                return false;
            }

            bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            return true;
        }

        private static string ResolveBlockFolder(BlockSpec spec)
        {
            return string.IsNullOrEmpty(spec.Category) ? OutputFolder : $"{OutputFolder}/{spec.Category}";
        }

        private static string ResolveMeshFolder(BlockSpec spec)
        {
            return $"{ResolveBlockFolder(spec)}/{MeshFolderName}";
        }

        /// <summary>
        /// Earlier runs wrote every block into one flat folder. Moving the prefab rather than
        /// writing a new one keeps its GUID, so a block database already pointing at it, and any
        /// scene or structure prefab using it, keeps working across the reorganisation.
        /// </summary>
        private static void MigrateLegacyAsset(string legacyPath, string newPath)
        {
            if (legacyPath == newPath
                || AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(legacyPath) == null
                || AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(newPath) != null)
            {
                return;
            }

            string error = AssetDatabase.MoveAsset(legacyPath, newPath);
            if (string.IsNullOrEmpty(error))
            {
                Debug.Log($"{nameof(BlockPrefabBuilder)} moved {legacyPath} to {newPath}.");
                return;
            }

            Debug.LogWarning($"{nameof(BlockPrefabBuilder)} could not move {legacyPath} to {newPath}: {error}");
        }

        /// <summary>Defaults an unset spec to a single cell.</summary>
        private static Vector3Int ResolveLogicalSize(BlockSpec spec)
        {
            return new Vector3Int(
                Mathf.Max(1, spec.LogicalSize.x),
                Mathf.Max(1, spec.LogicalSize.y),
                Mathf.Max(1, spec.LogicalSize.z));
        }

        private static void ApplyAuthoringValues(KnockdownBlockAuthoring authoring, BlockSpec spec, Vector3Int logicalSize)
        {
            SerializedObject serializedAuthoring = new SerializedObject(authoring);
            serializedAuthoring.FindProperty("countsTowardKnockdown").boolValue = spec.CountsTowardKnockdown;
            serializedAuthoring.FindProperty("mass").floatValue = spec.Mass;
            serializedAuthoring.FindProperty("allowCollisionCascade").boolValue = spec.AllowCollisionCascade;
            serializedAuthoring.FindProperty("collisionActivationVelocity").floatValue = spec.CollisionActivationVelocity;
            serializedAuthoring.FindProperty("supportCascadeMode").enumValueIndex = (int)spec.SupportCascadeMode;
            serializedAuthoring.FindProperty("supportReleaseImpulse").floatValue = spec.SupportReleaseImpulse;
            serializedAuthoring.FindProperty("logicalSize").vector3IntValue = logicalSize;
            serializedAuthoring.FindProperty("gridPosition").vector3IntValue = Vector3Int.zero;
            serializedAuthoring.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Material ResolveOverrideMaterial(BlockSpec spec)
        {
            if (string.IsNullOrEmpty(spec.MaterialPathOverride))
            {
                return null;
            }

            Material existing = AssetDatabase.LoadAssetAtPath<Material>(spec.MaterialPathOverride);
            if (existing != null)
            {
                return existing;
            }

            if (spec.MaterialPathOverride == GlassMaterialPath)
            {
                return CreateGlassMaterial();
            }

            Debug.LogError($"{nameof(BlockPrefabBuilder)} could not find a material at {spec.MaterialPathOverride}.");
            return null;
        }

        /// <summary>
        /// Glass.fbx shipped without a material, so the block gets a transparent URP Lit one.
        /// </summary>
        private static Material CreateGlassMaterial()
        {
            Shader litShader = Shader.Find(UrpLitShaderName);
            if (litShader == null)
            {
                Debug.LogError($"{nameof(BlockPrefabBuilder)} could not find the {UrpLitShaderName} shader.");
                return null;
            }

            EnsureFolder(MaterialFolder);
            Material glassMaterial = new Material(litShader)
            {
                name = "M_Glass",
            };

            glassMaterial.SetFloat("_Surface", 1f);
            glassMaterial.SetFloat("_Blend", 0f);
            glassMaterial.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            glassMaterial.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            glassMaterial.SetFloat("_ZWrite", 0f);
            glassMaterial.SetFloat("_Smoothness", 0.92f);
            glassMaterial.SetFloat("_Metallic", 0f);
            glassMaterial.SetColor("_BaseColor", new Color(0.72f, 0.86f, 0.92f, 0.35f));
            glassMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            glassMaterial.DisableKeyword("_ALPHATEST_ON");
            glassMaterial.SetShaderPassEnabled("ShadowCaster", false);
            glassMaterial.renderQueue = (int)RenderQueue.Transparent;

            AssetDatabase.CreateAsset(glassMaterial, GlassMaterialPath);
            return glassMaterial;
        }

        private static void ApplyMaterial(GameObject visual, Material material)
        {
            MeshRenderer[] renderers = visual.GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Material[] materials = new Material[Mathf.Max(1, renderers[i].sharedMaterials.Length)];
                for (int slot = 0; slot < materials.Length; slot++)
                {
                    materials[slot] = material;
                }

                renderers[i].sharedMaterials = materials;
            }
        }

        /// <summary>
        /// Mesh.CombineMeshes needs read/write access, which the art imported without.
        /// </summary>
        private static void EnsureModelIsReadable(string modelPath)
        {
            ModelImporter importer = AssetImporter.GetAtPath(modelPath) as ModelImporter;
            if (importer == null || importer.isReadable)
            {
                return;
            }

            importer.isReadable = true;
            importer.SaveAndReimport();
        }

        private static void EnsureFolder(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath))
            {
                return;
            }

            string parent = Path.GetDirectoryName(folderPath)?.Replace('\\', '/');
            string leaf = Path.GetFileName(folderPath);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(leaf))
            {
                throw new InvalidOperationException($"Cannot create folder {folderPath}.");
            }

            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
#endif
