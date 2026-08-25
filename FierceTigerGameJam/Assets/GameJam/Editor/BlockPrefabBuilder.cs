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

        /// <summary>Where the shared debris surface is written.</summary>
        private const string PhysicsFolder = "Assets/GameJam/Physics";
        private const string GlassMaterialPath = "Assets/GameJam/Materials/M_Glass.mat";
        private const string UrpLitShaderName = "Universal Render Pipeline/Lit";
        private const string VisualChildName = "Visual";
        private const string ShatteredSuffix = "_Shattered";
        private const string PanelSuffix = "_Panel";
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

            /// <summary>
            /// What earlier runs called this block. The prefabs were renamed by hand after the
            /// first build, so the rename is followed rather than written past: a move keeps the
            /// GUID the block database and every built map already point at.
            /// </summary>
            public string LegacyBlockName;

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

            /// <summary>
            /// Ceilings on what one knock may do to this block's speed. Authored per material
            /// because a pane of glass is meant to scatter further than a concrete slab, and
            /// because leaving either at zero means no ceiling at all.
            /// </summary>
            public float MaxKnockHorizontalSpeed;
            public float MaxKnockVerticalSpeed;

            public float HitPoints;
            public float MinimumImpactSpeed;
            public float DamagePerImpactSpeed;
            public float MaxDamagePerImpact;

            /// <summary>
            /// How many debris chunks the fracture cells are grouped into, per axis. The whole
            /// block volume is covered whatever the number, so this trades piece count against
            /// vertex count rather than against how much of the block appears to survive.
            /// </summary>
            public Vector3Int ShardChunks;
        }

        /// <summary>
        /// Single-mesh wall art, used to draw a whole run of blocks at once. Matched to blocks by
        /// Category, so brick blocks get the brick wall.
        /// </summary>
        private struct WallPanelSpec
        {
            public string Category;
            public string ModelPath;
            public string MaterialPathOverride;
        }

        private static readonly WallPanelSpec[] WallPanels =
        {
            new WallPanelSpec
            {
                Category = "Brick",
                ModelPath = "Assets/GameJam/FBX/Brick_Wall.fbx",
            },
            new WallPanelSpec
            {
                Category = "Concrete",
                ModelPath = "Assets/GameJam/FBX/Concrete_Wall.fbx",
            },
            new WallPanelSpec
            {
                // Glass_Wall.fbx ships without embedded textures, so it borrows the block material.
                Category = "Glass",
                ModelPath = "Assets/GameJam/FBX/Glass_Wall.fbx",
                MaterialPathOverride = GlassMaterialPath,
            },
        };

        private static readonly BlockSpec[] Specs =
        {
            new BlockSpec
            {
                BlockName = "brick_1x1",
                LegacyBlockName = "Block_Brick",
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
                MaxKnockHorizontalSpeed = 8.5f,
                MaxKnockVerticalSpeed = 1.35f,
                HitPoints = 3f,
                MinimumImpactSpeed = 2.5f,
                DamagePerImpactSpeed = 0.5f,
                MaxDamagePerImpact = 3f,
                ShardChunks = new Vector3Int(2, 2, 2),
            },
            // Glass is the one-shot material: a single clean hit takes it, and it comes apart
            // into a grid of flat pieces rather than cubes, because the art is a pane.
            new BlockSpec
            {
                BlockName = "glass_1x1",
                LegacyBlockName = "Block_Glass",
                Category = "Glass",
                ModelPath = "Assets/GameJam/FBX/Glass.fbx",
                MaterialPathOverride = GlassMaterialPath,
                Mass = 0.6f,
                AllowCollisionCascade = true,
                CollisionActivationVelocity = 1f,
                SupportCascadeMode = KnockdownBlock.SupportCascadeMode.ColumnAbove,
                SupportReleaseImpulse = 0.5f,
                CountsTowardKnockdown = true,
                MaxKnockHorizontalSpeed = 11f,
                MaxKnockVerticalSpeed = 1.8f,
                HitPoints = 1f,
                MinimumImpactSpeed = 1.2f,
                DamagePerImpactSpeed = 1.5f,
                MaxDamagePerImpact = 5f,
                ShardChunks = new Vector3Int(3, 3, 1),
            },
            new BlockSpec
            {
                BlockName = "concrete_1x1",
                LegacyBlockName = "Block_Concrete",
                Category = "Concrete",
                ModelPath = "Assets/GameJam/FBX/concrete.fbx",
                MaterialPathOverride = null,
                Mass = 2f,
                AllowCollisionCascade = true,
                CollisionActivationVelocity = 2.5f,
                SupportCascadeMode = KnockdownBlock.SupportCascadeMode.ColumnAbove,
                SupportReleaseImpulse = 0.25f,
                CountsTowardKnockdown = true,
                MaxKnockHorizontalSpeed = 8.5f,
                MaxKnockVerticalSpeed = 1.35f,
                HitPoints = 6f,
                MinimumImpactSpeed = 3.5f,
                DamagePerImpactSpeed = 0.4f,
                MaxDamagePerImpact = 3f,
                ShardChunks = new Vector3Int(2, 2, 2),
            },
            // Mock-up: the same brick art stretched across two cells, so the map loader's
            // multi-cell footprint and rotation can be exercised before real 2x1 art lands.
            new BlockSpec
            {
                BlockName = "brick_2x1",
                LegacyBlockName = "Block_Brick_2x1",
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
                MaxKnockHorizontalSpeed = 8.5f,
                MaxKnockVerticalSpeed = 1.35f,
                HitPoints = 5f,
                MinimumImpactSpeed = 2.5f,
                DamagePerImpactSpeed = 0.5f,
                MaxDamagePerImpact = 3f,
                ShardChunks = new Vector3Int(3, 2, 2),
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

            // Panels first: the database is told about them once every block is known.
            Dictionary<string, GameObject> panelsByCategory = new Dictionary<string, GameObject>();
            for (int i = 0; i < WallPanels.Length; i++)
            {
                GameObject panel = BuildWallPanel(WallPanels[i], targetBlockSize);
                if (panel != null)
                {
                    panelsByCategory[WallPanels[i].Category] = panel;
                }
            }

            List<string> built = new List<string>();
            for (int i = 0; i < Specs.Length; i++)
            {
                // Debris first: the block prefab carries a reference to it.
                GameObject debrisPrefab = BuildShatteredPrefab(Specs[i], targetBlockSize);
                if (debrisPrefab != null)
                {
                    built.Add(AssetDatabase.GetAssetPath(debrisPrefab));
                }

                string prefabPath = BuildBlockPrefab(Specs[i], combineVisualMesh, targetBlockSize, debrisPrefab);
                if (!string.IsNullOrEmpty(prefabPath))
                {
                    built.Add(prefabPath);
                }
            }

            AssignWallPanels(panelsByCategory);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"Built {built.Count} block prefab(s):\n{string.Join("\n", built)}");
        }

        /// <summary>
        /// Points every block database entry at the panel for its material, so a rebuild wires
        /// itself up rather than leaving the panels to be dragged in by hand and mismatched.
        /// Entries are matched to specs by type, and specs carry the category the panel was
        /// built for.
        /// </summary>
        private static void AssignWallPanels(Dictionary<string, GameObject> panelsByCategory)
        {
            if (panelsByCategory.Count == 0)
            {
                return;
            }

            Dictionary<string, GameObject> panelByType = new Dictionary<string, GameObject>();
            for (int i = 0; i < Specs.Length; i++)
            {
                if (!string.IsNullOrEmpty(Specs[i].Category)
                    && panelsByCategory.TryGetValue(Specs[i].Category, out GameObject panel))
                {
                    panelByType[Specs[i].BlockName] = panel;
                }
            }

            string[] databaseGuids = AssetDatabase.FindAssets($"t:{nameof(BlockDatabase)}");
            for (int i = 0; i < databaseGuids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(databaseGuids[i]);
                BlockDatabase database = AssetDatabase.LoadAssetAtPath<BlockDatabase>(path);
                if (database == null)
                {
                    continue;
                }

                SerializedObject serializedDatabase = new SerializedObject(database);
                SerializedProperty entries = serializedDatabase.FindProperty("entries");
                int assigned = 0;

                for (int entry = 0; entry < entries.arraySize; entry++)
                {
                    SerializedProperty element = entries.GetArrayElementAtIndex(entry);
                    string type = element.FindPropertyRelative("type").stringValue;
                    if (string.IsNullOrEmpty(type) || !panelByType.TryGetValue(type, out GameObject panel))
                    {
                        continue;
                    }

                    element.FindPropertyRelative("wallPanel").objectReferenceValue = panel;
                    assigned++;
                }

                if (assigned > 0)
                {
                    serializedDatabase.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(database);
                    Debug.Log($"{nameof(BlockPrefabBuilder)} pointed {assigned} entry(s) in {path} at their wall panel.", database);
                }
            }
        }

        private static string BuildBlockPrefab(
            BlockSpec spec,
            bool combineVisualMesh,
            Vector3 targetBlockSize,
            GameObject debrisPrefab)
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
            MigrateLegacyAsset($"{blockFolder}/{spec.LegacyBlockName}{suffix}.prefab", prefabPath);
            MigrateLegacyAsset($"{OutputFolder}/{spec.LegacyBlockName}{suffix}.prefab", prefabPath);

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

                BreakableBlock breakable = root.AddComponent<BreakableBlock>();
                ApplyBreakableValues(breakable, spec, debrisPrefab);

                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                return prefabPath;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// Builds a one-cell wall panel out of the single-mesh wall art, so a run of blocks can
        /// be drawn with a couple of hundred vertices instead of one block mesh per cell.
        ///
        /// The panel is normalised to exactly one cell and turned so its thinnest axis lies on Z,
        /// which is the axis the map's layers are stacked along. Scaling it by a run's width and
        /// height then gives a wall of the right size, and its UVs are scaled to match so the
        /// bricks keep their size instead of stretching.
        /// </summary>
        private static GameObject BuildWallPanel(WallPanelSpec spec, Vector3 targetBlockSize)
        {
            EnsureModelIsReadable(spec.ModelPath);

            GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(spec.ModelPath);
            if (modelAsset == null)
            {
                Debug.LogWarning(
                    $"{nameof(BlockPrefabBuilder)} found no wall art at {spec.ModelPath}, so "
                    + $"{spec.Category} walls will be drawn by welding block meshes.");
                return null;
            }

            string folder = $"{OutputFolder}/{spec.Category}";
            EnsureFolder(folder);

            string panelName = $"{spec.Category.ToLowerInvariant()}_wall{PanelSuffix}";
            string prefabPath = $"{folder}/{panelName}.prefab";

            GameObject root = new GameObject(panelName);
            GameObject visual = null;
            try
            {
                visual = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset);
                if (visual == null)
                {
                    Debug.LogError($"{nameof(BlockPrefabBuilder)} could not instantiate {spec.ModelPath}.");
                    return null;
                }

                visual.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                visual.transform.localScale = Vector3.one;
                PrefabUtility.UnpackPrefabInstance(visual, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

                Material material = ResolveOverrideMaterial(spec.MaterialPathOverride) ?? FindFirstMaterial(visual);

                Mesh panelMesh = WeldModel(visual, panelName);
                if (panelMesh == null)
                {
                    Debug.LogError($"{nameof(BlockPrefabBuilder)} found no readable meshes in {spec.ModelPath}.");
                    return null;
                }

                Quaternion flatten = ResolvePanelRotation(panelMesh.bounds.size);
                BakePanelTransform(panelMesh, flatten, targetBlockSize);

                string meshFolder = $"{folder}/{MeshFolderName}";
                EnsureFolder(meshFolder);
                panelMesh = SaveMeshInPlace(panelMesh, $"{meshFolder}/{panelName}_Mesh.asset");

                root.AddComponent<MeshFilter>().sharedMesh = panelMesh;
                root.AddComponent<MeshRenderer>().sharedMaterial = material;

                Debug.Log(
                    $"{panelName}: {panelMesh.vertexCount} vertices, normalised to one "
                    + $"{targetBlockSize:F3} cell.");

                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                return AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            }
            finally
            {
                if (visual != null)
                {
                    UnityEngine.Object.DestroyImmediate(visual);
                }

                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        /// <summary>Welds everything under the model into one mesh, in model space.</summary>
        private static Mesh WeldModel(GameObject visual, string meshName)
        {
            MeshFilter[] filters = visual.GetComponentsInChildren<MeshFilter>(true);
            List<CombineInstance> combines = new List<CombineInstance>(filters.Length);
            Matrix4x4 toVisual = visual.transform.worldToLocalMatrix;

            for (int i = 0; i < filters.Length; i++)
            {
                Mesh mesh = filters[i].sharedMesh;
                if (mesh == null)
                {
                    continue;
                }

                for (int subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
                {
                    combines.Add(new CombineInstance
                    {
                        mesh = mesh,
                        subMeshIndex = subMesh,
                        transform = toVisual * filters[i].transform.localToWorldMatrix,
                    });
                }
            }

            if (combines.Count == 0)
            {
                return null;
            }

            Mesh welded = new Mesh
            {
                name = meshName,
                indexFormat = IndexFormat.UInt32,
            };
            welded.CombineMeshes(combines.ToArray(), true, true);
            welded.RecalculateBounds();
            return welded;
        }

        /// <summary>
        /// Turns the panel so its thinnest axis ends up on Z. A wall is a slab, and the map's
        /// layers are stacked along Z, so that is the axis its thickness has to sit on whichever
        /// way the artist happened to build it.
        /// </summary>
        private static Quaternion ResolvePanelRotation(Vector3 size)
        {
            if (size.z <= size.x && size.z <= size.y)
            {
                return Quaternion.identity;
            }

            // Y about the vertical brings X round to Z; X about the horizontal brings Y round.
            return size.x <= size.y ? Quaternion.Euler(0f, 90f, 0f) : Quaternion.Euler(90f, 0f, 0f);
        }

        /// <summary>
        /// Rotates the panel flat, then scales it into a single cell, baking both into the
        /// vertices so the prefab needs no transform of its own. Normals are divided by the fit
        /// rather than multiplied - the inverse transpose - so a panel squashed to a slab is
        /// still lit as the wall it was modelled as.
        /// </summary>
        private static void BakePanelTransform(Mesh mesh, Quaternion rotation, Vector3 targetSize)
        {
            Vector3[] vertices = mesh.vertices;
            Vector3[] normals = mesh.normals;
            Vector4[] tangents = mesh.tangents;

            for (int i = 0; i < vertices.Length; i++)
            {
                vertices[i] = rotation * vertices[i];
            }

            for (int i = 0; i < normals.Length; i++)
            {
                normals[i] = rotation * normals[i];
            }

            for (int i = 0; i < tangents.Length; i++)
            {
                Vector3 rotated = rotation * new Vector3(tangents[i].x, tangents[i].y, tangents[i].z);
                tangents[i] = new Vector4(rotated.x, rotated.y, rotated.z, tangents[i].w);
            }

            mesh.vertices = vertices;
            if (normals.Length > 0)
            {
                mesh.normals = normals;
            }

            if (tangents.Length > 0)
            {
                mesh.tangents = tangents;
            }

            mesh.RecalculateBounds();

            Vector3 fitScale = ResolveFitScale(mesh.bounds.size, targetSize);
            Vector3 normalScale = new Vector3(
                1f / Mathf.Max(MinBoundsExtent, fitScale.x),
                1f / Mathf.Max(MinBoundsExtent, fitScale.y),
                1f / Mathf.Max(MinBoundsExtent, fitScale.z));
            Vector3 center = mesh.bounds.center;

            vertices = mesh.vertices;
            for (int i = 0; i < vertices.Length; i++)
            {
                vertices[i] = Vector3.Scale(vertices[i] - center, fitScale);
            }

            normals = mesh.normals;
            for (int i = 0; i < normals.Length; i++)
            {
                normals[i] = Vector3.Scale(normals[i], normalScale).normalized;
            }

            tangents = mesh.tangents;
            for (int i = 0; i < tangents.Length; i++)
            {
                Vector3 scaled = Vector3.Scale(
                    new Vector3(tangents[i].x, tangents[i].y, tangents[i].z), fitScale).normalized;
                tangents[i] = new Vector4(scaled.x, scaled.y, scaled.z, tangents[i].w);
            }

            mesh.vertices = vertices;
            if (normals.Length > 0)
            {
                mesh.normals = normals;
            }

            if (tangents.Length > 0)
            {
                mesh.tangents = tangents;
            }

            mesh.RecalculateBounds();
        }

        /// <summary>
        /// Builds the debris a block leaves when it breaks: the fracture cells covering the block,
        /// grouped into a handful of chunks and welded one chunk at a time.
        ///
        /// The cells are grouped rather than picked. Taking the largest N of Brick.fbx's 364 cells
        /// would cover a few percent of the block, so the block would appear to mostly evaporate;
        /// grouping every cell into an ShardChunks grid covers the full volume with the same
        /// number of pieces.
        /// </summary>
        private static GameObject BuildShatteredPrefab(BlockSpec spec, Vector3 targetBlockSize)
        {
            EnsureModelIsReadable(spec.ModelPath);

            GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(spec.ModelPath);
            if (modelAsset == null)
            {
                Debug.LogError($"{nameof(BlockPrefabBuilder)} could not find a model at {spec.ModelPath}.");
                return null;
            }

            string blockFolder = ResolveBlockFolder(spec);
            EnsureFolder(blockFolder);

            string prefabPath = $"{blockFolder}/{spec.BlockName}{ShatteredSuffix}.prefab";
            MigrateLegacyAsset($"{blockFolder}/{spec.LegacyBlockName}{ShatteredSuffix}.prefab", prefabPath);

            GameObject root = new GameObject(spec.BlockName + ShatteredSuffix);
            GameObject visual = null;
            try
            {
                visual = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset);
                if (visual == null)
                {
                    Debug.LogError($"{nameof(BlockPrefabBuilder)} could not instantiate {spec.ModelPath}.");
                    return null;
                }

                visual.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                visual.transform.localScale = Vector3.one;
                PrefabUtility.UnpackPrefabInstance(visual, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

                Material material = ResolveOverrideMaterial(spec) ?? FindFirstMaterial(visual);

                if (!TryGetBlockRegion(visual, spec, out Bounds region))
                {
                    Debug.LogError($"{nameof(BlockPrefabBuilder)} found no renderers under {spec.ModelPath}.");
                    return null;
                }

                List<MeshFilter> cells = CollectCellsInRegion(visual, region);
                if (cells.Count == 0)
                {
                    Debug.LogError($"{nameof(BlockPrefabBuilder)} found no fracture cells for {spec.BlockName}.");
                    return null;
                }

                Vector3Int logicalSize = ResolveLogicalSize(spec);
                Vector3 targetSize = Vector3.Scale(targetBlockSize, new Vector3(logicalSize.x, logicalSize.y, logicalSize.z));
                Vector3 fitScale = ResolveFitScale(region.size, targetSize);
                Vector3Int chunkGrid = ResolveShardChunks(spec);

                List<ChunkMesh> chunkMeshes = BuildDebrisChunks(visual, cells, region, fitScale, chunkGrid, spec);
                if (chunkMeshes.Count == 0)
                {
                    Debug.LogError($"{nameof(BlockPrefabBuilder)} produced no debris chunks for {spec.BlockName}.");
                    return null;
                }

                string meshPath = SaveChunkMeshes(spec, chunkMeshes);
                float chunkMass = Mathf.Max(0.01f, spec.Mass / chunkMeshes.Count);
                int totalVertices = 0;

                for (int i = 0; i < chunkMeshes.Count; i++)
                {
                    totalVertices += chunkMeshes[i].Mesh.vertexCount;
                    CreateChunkObject(root, chunkMeshes[i], material, chunkMass, $"{spec.BlockName}_Chunk_{i:00}");
                }

                root.AddComponent<ShatteredBlock>();

                Debug.Log(
                    $"{spec.BlockName}{ShatteredSuffix}: {cells.Count} cell(s) grouped into "
                    + $"{chunkMeshes.Count} chunk(s), {totalVertices} vertices total, meshes at {meshPath}.");

                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                return AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            }
            finally
            {
                if (visual != null)
                {
                    UnityEngine.Object.DestroyImmediate(visual);
                }

                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// The part of the model the block is actually made of. When a spec names its cells, the
        /// rest of the file is a different object entirely - Brick.fbx is a fractured wall and the
        /// block is one brick out of it - so the debris has to be confined to the same region or
        /// a broken block would spray the whole wall.
        /// </summary>
        private static bool TryGetBlockRegion(GameObject visual, BlockSpec spec, out Bounds region)
        {
            if (spec.ShardNames == null || spec.ShardNames.Length == 0)
            {
                return TryGetRendererBounds(visual, out region);
            }

            HashSet<string> named = new HashSet<string>(spec.ShardNames);
            bool found = false;
            region = default;

            Renderer[] renderers = visual.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (!named.Contains(renderers[i].gameObject.name))
                {
                    continue;
                }

                if (!found)
                {
                    region = renderers[i].bounds;
                    found = true;
                    continue;
                }

                region.Encapsulate(renderers[i].bounds);
            }

            return found;
        }

        /// <summary>
        /// Every cell whose middle sits inside the block's region. Cells straddling the edge are
        /// taken whole, which is what keeps the debris looking like a fracture rather than a
        /// sliced box.
        /// </summary>
        private static List<MeshFilter> CollectCellsInRegion(GameObject visual, Bounds region)
        {
            List<MeshFilter> cells = new List<MeshFilter>();
            MeshFilter[] filters = visual.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < filters.Length; i++)
            {
                if (filters[i].sharedMesh == null || !filters[i].TryGetComponent(out Renderer cellRenderer))
                {
                    continue;
                }

                if (region.Contains(cellRenderer.bounds.center))
                {
                    cells.Add(filters[i]);
                }
            }

            return cells;
        }

        /// <summary>
        /// Picks how to cut the block up, because the art varies wildly in how finely it was
        /// fractured. The number of pieces can never exceed the number of source cells covering
        /// the block, so grouping only makes sense when there are cells to spare.
        /// </summary>
        private static List<ChunkMesh> BuildDebrisChunks(
            GameObject visual,
            List<MeshFilter> cells,
            Bounds region,
            Vector3 fitScale,
            Vector3Int chunkGrid,
            BlockSpec spec)
        {
            int requestedChunks = chunkGrid.x * chunkGrid.y * chunkGrid.z;

            if (cells.Count > requestedChunks)
            {
                return BuildChunkMeshes(visual, cells, region, fitScale, chunkGrid, spec.BlockName);
            }

            if (cells.Count > 1)
            {
                // Fewer cells than pieces asked for: grouping could only merge them into even
                // fewer, so each cell becomes a piece and the block breaks along the lines the
                // artist actually fractured it on.
                Debug.Log(
                    $"{spec.BlockName}: {cells.Count} fracture cell(s) cover the block but "
                    + $"{requestedChunks} debris pieces were asked for, so each cell becomes one piece.");
                return BuildChunkMeshes(visual, cells, region, fitScale, OneChunkPerCellGrid(cells.Count), spec.BlockName);
            }

            // A single cell cannot be broken into pieces. Every model in the project is
            // fractured finely enough that this does not happen; if new art trips it, the source
            // needs a cell fracture pass in Blender rather than a workaround here.
            Debug.LogError(
                $"{spec.BlockName}: {spec.ModelPath} has {cells.Count} fracture cell(s) inside the block, "
                + "so it cannot produce debris. Cell-fracture the source model.",
                AssetDatabase.LoadAssetAtPath<GameObject>(spec.ModelPath));
            return new List<ChunkMesh>();
        }

        /// <summary>
        /// A grid fine enough that no two cells are forced to share a bucket in practice. Cells
        /// are not spread evenly, so this is deliberately finer than the cell count needs.
        /// </summary>
        private static Vector3Int OneChunkPerCellGrid(int cellCount)
        {
            int perAxis = Mathf.Max(2, Mathf.CeilToInt(Mathf.Pow(cellCount, 1f / 3f)) + 1);
            return new Vector3Int(perAxis, perAxis, perAxis);
        }

        /// <summary>
        /// Welds each group of cells into one mesh, already in block-local space: vertices are
        /// moved so the block centre is the origin and scaled by the same fit the intact block
        /// uses, then recentred on the chunk so the chunk's transform position is its middle.
        /// </summary>
        private static List<ChunkMesh> BuildChunkMeshes(
            GameObject visual,
            List<MeshFilter> cells,
            Bounds region,
            Vector3 fitScale,
            Vector3Int chunkGrid,
            string blockName)
        {
            int chunkCount = chunkGrid.x * chunkGrid.y * chunkGrid.z;
            List<CombineInstance>[] buckets = new List<CombineInstance>[chunkCount];

            Matrix4x4 toVisual = visual.transform.worldToLocalMatrix;
            for (int i = 0; i < cells.Count; i++)
            {
                Mesh cellMesh = cells[i].sharedMesh;
                Vector3 center = cells[i].GetComponent<Renderer>().bounds.center;
                int bucket = ResolveChunkIndex(center, region, chunkGrid);

                buckets[bucket] ??= new List<CombineInstance>();
                for (int subMesh = 0; subMesh < cellMesh.subMeshCount; subMesh++)
                {
                    buckets[bucket].Add(new CombineInstance
                    {
                        mesh = cellMesh,
                        subMeshIndex = subMesh,
                        transform = toVisual * cells[i].transform.localToWorldMatrix,
                    });
                }
            }

            List<ChunkMesh> chunkMeshes = new List<ChunkMesh>();
            for (int i = 0; i < buckets.Length; i++)
            {
                if (buckets[i] == null || buckets[i].Count == 0)
                {
                    continue;
                }

                Mesh chunkMesh = new Mesh
                {
                    name = $"{blockName}_Chunk_{chunkMeshes.Count:00}",
                    indexFormat = IndexFormat.UInt32,
                };
                chunkMesh.CombineMeshes(buckets[i].ToArray(), true, true);

                chunkMeshes.Add(new ChunkMesh
                {
                    Mesh = chunkMesh,
                    LocalPosition = BakeFitScale(chunkMesh, region.center, fitScale),
                });
            }

            return chunkMeshes;
        }

        private static int ResolveChunkIndex(Vector3 point, Bounds region, Vector3Int chunkGrid)
        {
            int x = AxisChunk(point.x, region.min.x, region.size.x, chunkGrid.x);
            int y = AxisChunk(point.y, region.min.y, region.size.y, chunkGrid.y);
            int z = AxisChunk(point.z, region.min.z, region.size.z, chunkGrid.z);
            return x + (chunkGrid.x * (y + (chunkGrid.y * z)));
        }

        private static int AxisChunk(float value, float min, float size, int divisions)
        {
            float normalized = (value - min) / Mathf.Max(MinBoundsExtent, size);
            return Mathf.Clamp(Mathf.FloorToInt(normalized * divisions), 0, divisions - 1);
        }

        /// <summary>
        /// Bakes the block's fit into the vertices so each chunk can be a plain child at scale 1.
        /// Normals are divided by the scale rather than multiplied - the inverse transpose - or a
        /// non-uniform fit would light the debris as if it were still the shape the artist built.
        /// </summary>
        /// <returns>Where the chunk sat before it was recentred, i.e. its position in the block.</returns>
        private static Vector3 BakeFitScale(Mesh mesh, Vector3 regionCenter, Vector3 fitScale)
        {
            Vector3 normalScale = new Vector3(
                1f / Mathf.Max(MinBoundsExtent, fitScale.x),
                1f / Mathf.Max(MinBoundsExtent, fitScale.y),
                1f / Mathf.Max(MinBoundsExtent, fitScale.z));

            Vector3[] vertices = mesh.vertices;
            for (int i = 0; i < vertices.Length; i++)
            {
                vertices[i] = Vector3.Scale(vertices[i] - regionCenter, fitScale);
            }

            Vector3[] normals = mesh.normals;
            for (int i = 0; i < normals.Length; i++)
            {
                normals[i] = Vector3.Scale(normals[i], normalScale).normalized;
            }

            Vector4[] tangents = mesh.tangents;
            for (int i = 0; i < tangents.Length; i++)
            {
                Vector3 direction = Vector3.Scale(
                    new Vector3(tangents[i].x, tangents[i].y, tangents[i].z),
                    fitScale).normalized;
                tangents[i] = new Vector4(direction.x, direction.y, direction.z, tangents[i].w);
            }

            mesh.vertices = vertices;
            if (normals.Length > 0)
            {
                mesh.normals = normals;
            }

            if (tangents.Length > 0)
            {
                mesh.tangents = tangents;
            }

            mesh.RecalculateBounds();

            // Recentred last, so the chunk's own middle is its origin and its transform position
            // doubles as the direction to throw it in.
            Vector3 chunkCenter = mesh.bounds.center;
            Vector3[] centered = mesh.vertices;
            for (int i = 0; i < centered.Length; i++)
            {
                centered[i] -= chunkCenter;
            }

            mesh.vertices = centered;
            mesh.RecalculateBounds();
            mesh.Optimize();

            return chunkCenter;
        }

        /// <summary>A debris chunk: its welded mesh, and where in the block that mesh belongs.</summary>
        private struct ChunkMesh
        {
            public Mesh Mesh;
            public Vector3 LocalPosition;
        }

        /// <summary>
        /// Writes a mesh into an existing asset instead of replacing the asset. Deleting and
        /// recreating hands the mesh a new GUID, and every reference to it breaks the moment the
        /// old file goes: the prefab about to be rewritten recovers, but anything the editor
        /// still has loaded keeps the broken reference and renders nothing until a reimport.
        /// </summary>
        private static Mesh SaveMeshInPlace(Mesh mesh, string meshPath)
        {
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(mesh, meshPath);
                return mesh;
            }

            // CopySerialized brings the name across too, and an asset whose main object stops
            // matching its file name gets renamed again on the next import.
            string assetName = existing.name;
            EditorUtility.CopySerialized(mesh, existing);
            existing.name = assetName;
            EditorUtility.SetDirty(existing);
            UnityEngine.Object.DestroyImmediate(mesh);
            return existing;
        }

        private static string SaveChunkMeshes(BlockSpec spec, List<ChunkMesh> chunkMeshes)
        {
            string meshFolder = ResolveMeshFolder(spec);
            EnsureFolder(meshFolder);

            // One asset holding every chunk, rather than a file per piece: a dozen loose mesh
            // assets per block turns the folder into noise.
            string meshPath = $"{meshFolder}/{spec.BlockName}_Shards.asset";

            if (TryOverwriteChunkMeshes(meshPath, chunkMeshes))
            {
                AssetDatabase.SaveAssets();
                return meshPath;
            }

            // The piece count changed, so the sub-assets cannot be matched up one to one and the
            // file has to be rebuilt. Only the debris prefab points at these, and it is rewritten
            // in the same run.
            AssetDatabase.DeleteAsset(meshPath);
            AssetDatabase.CreateAsset(chunkMeshes[0].Mesh, meshPath);
            for (int i = 1; i < chunkMeshes.Count; i++)
            {
                AssetDatabase.AddObjectToAsset(chunkMeshes[i].Mesh, meshPath);
            }

            AssetDatabase.SaveAssets();
            return meshPath;
        }

        /// <summary>
        /// Copies the new chunks over the existing sub-assets when there are exactly as many of
        /// them, so the debris meshes keep their identity across a rebuild.
        /// </summary>
        private static bool TryOverwriteChunkMeshes(string meshPath, List<ChunkMesh> chunkMeshes)
        {
            UnityEngine.Object[] loaded = AssetDatabase.LoadAllAssetsAtPath(meshPath);
            List<Mesh> existing = new List<Mesh>();
            for (int i = 0; i < loaded.Length; i++)
            {
                if (loaded[i] is Mesh existingMesh)
                {
                    existing.Add(existingMesh);
                }
            }

            if (existing.Count != chunkMeshes.Count)
            {
                return false;
            }

            for (int i = 0; i < chunkMeshes.Count; i++)
            {
                string assetName = existing[i].name;
                EditorUtility.CopySerialized(chunkMeshes[i].Mesh, existing[i]);
                existing[i].name = assetName;
                EditorUtility.SetDirty(existing[i]);
                UnityEngine.Object.DestroyImmediate(chunkMeshes[i].Mesh);
                chunkMeshes[i] = new ChunkMesh
                {
                    Mesh = existing[i],
                    LocalPosition = chunkMeshes[i].LocalPosition,
                };
            }

            return true;
        }

        /// <remarks>
        /// Named from the index rather than from the mesh: CreateAsset renames whichever mesh
        /// becomes the main asset to match the file, so the first chunk's mesh is called
        /// "..._Shards" by the time this runs.
        /// </remarks>
        private static void CreateChunkObject(
            GameObject root,
            ChunkMesh chunkMesh,
            Material material,
            float chunkMass,
            string chunkName)
        {
            GameObject chunk = new GameObject(chunkName);
            chunk.transform.SetParent(root.transform, false);
            chunk.transform.localPosition = chunkMesh.LocalPosition;

            // Its own layer, so the layer can be made to ignore itself. A broken wall is a
            // hundred chunks in one place, and chunk-on-chunk contacts are both the most
            // expensive and the least noticed thing in that moment.
            chunk.layer = ResolveDebrisLayer();

            MeshRenderer chunkRenderer = chunk.AddComponent<MeshRenderer>();
            chunk.AddComponent<MeshFilter>().sharedMesh = chunkMesh.Mesh;
            chunkRenderer.sharedMaterial = material;

            // Debris is on screen for two seconds and is mostly under whatever it fell out of.
            // Shadowing it costs a second pass over every chunk for something nobody reads.
            chunkRenderer.shadowCastingMode = ShadowCastingMode.Off;
            chunkRenderer.receiveShadows = false;

            // A box rather than a convex hull of the fracture: debris only has to bounce and
            // settle, and a dozen convex meshes per broken block is a real cost on mobile.
            BoxCollider chunkCollider = chunk.AddComponent<BoxCollider>();
            chunkCollider.center = chunkMesh.Mesh.bounds.center;
            chunkCollider.size = chunkMesh.Mesh.bounds.size;
            chunkCollider.sharedMaterial = ResolveDebrisPhysicsMaterial();

            Rigidbody chunkBody = chunk.AddComponent<Rigidbody>();
            chunkBody.mass = chunkMass;

            // Matches what the runtime profile applies, so a chunk behaves the same whether it
            // was launched in play mode or is just sitting in the prefab.
            chunkBody.linearDamping = DebrisPhysicsProfile.LinearDamping;
            chunkBody.angularDamping = DebrisPhysicsProfile.AngularDamping;
            chunkBody.interpolation = RigidbodyInterpolation.None;
            chunkBody.collisionDetectionMode = CollisionDetectionMode.Discrete;
        }

        /// <summary>
        /// The debris layer, or the default layer with a warning. A missing layer only costs
        /// performance, so a build that hits it should still produce usable prefabs.
        /// </summary>
        private static int ResolveDebrisLayer()
        {
            int layer = LayerMask.NameToLayer(DebrisPhysicsProfile.LayerName);
            if (layer >= 0)
            {
                return layer;
            }

            Debug.LogWarning(
                $"No \"{DebrisPhysicsProfile.LayerName}\" layer in the project, so debris chunks "
                + "were left on the default layer and will collide with each other. Add the layer "
                + "in Project Settings > Tags and Layers and build the prefabs again.");
            return 0;
        }

        /// <summary>
        /// The one surface every chunk bounces with, written next to the block prefabs so the
        /// value is visible in the inspector rather than only appearing at runtime. Its numbers
        /// come from <see cref="DebrisPhysicsProfile"/>, which is where they are tuned.
        /// </summary>
        private static PhysicsMaterial ResolveDebrisPhysicsMaterial()
        {
            EnsureFolder(PhysicsFolder);

            string path = $"{PhysicsFolder}/{DebrisPhysicsProfile.MaterialName}.physicMaterial";
            PhysicsMaterial material = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(path);
            bool created = material == null;
            if (created)
            {
                material = new PhysicsMaterial(DebrisPhysicsProfile.MaterialName);
            }

            material.staticFriction = DebrisPhysicsProfile.StaticFriction;
            material.dynamicFriction = DebrisPhysicsProfile.DynamicFriction;
            material.bounciness = DebrisPhysicsProfile.Bounciness;
            material.frictionCombine = PhysicsMaterialCombine.Minimum;
            material.bounceCombine = PhysicsMaterialCombine.Average;

            if (created)
            {
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                EditorUtility.SetDirty(material);
            }

            return material;
        }

        private static Material FindFirstMaterial(GameObject visual)
        {
            MeshRenderer[] renderers = visual.GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i].sharedMaterial != null)
                {
                    return renderers[i].sharedMaterial;
                }
            }

            return null;
        }

        private static Vector3Int ResolveShardChunks(BlockSpec spec)
        {
            return new Vector3Int(
                Mathf.Max(1, spec.ShardChunks.x),
                Mathf.Max(1, spec.ShardChunks.y),
                Mathf.Max(1, spec.ShardChunks.z));
        }

        private static void ApplyBreakableValues(BreakableBlock breakable, BlockSpec spec, GameObject debrisPrefab)
        {
            SerializedObject serializedBreakable = new SerializedObject(breakable);
            // The material is what ammunition damage is looked up against, and the category the
            // block was built from is exactly that: brick, glass, concrete.
            serializedBreakable.FindProperty("materialId").stringValue =
                string.IsNullOrEmpty(spec.Category) ? string.Empty : spec.Category.ToLowerInvariant();
            serializedBreakable.FindProperty("maxHitPoints").floatValue = Mathf.Max(0.01f, spec.HitPoints);
            serializedBreakable.FindProperty("minimumImpactSpeed").floatValue = spec.MinimumImpactSpeed;
            serializedBreakable.FindProperty("damagePerImpactSpeed").floatValue = spec.DamagePerImpactSpeed;
            serializedBreakable.FindProperty("maxDamagePerImpact").floatValue = Mathf.Max(0.01f, spec.MaxDamagePerImpact);
            serializedBreakable.FindProperty("shatteredPrefab").objectReferenceValue = debrisPrefab;
            serializedBreakable.ApplyModifiedPropertiesWithoutUndo();
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

            // The mesh is rebuilt every run and the prefab is rewritten to point at the new one,
            // so copies under the pre-rename name are only clutter.
            if (!string.IsNullOrEmpty(spec.LegacyBlockName))
            {
                AssetDatabase.DeleteAsset($"{meshFolder}/{spec.LegacyBlockName}_Mesh.asset");
                AssetDatabase.DeleteAsset($"{LegacyMeshFolder}/{spec.LegacyBlockName}_Mesh.asset");
            }

            combinedMesh = SaveMeshInPlace(combinedMesh, meshPath);

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
            serializedAuthoring.FindProperty("maxKnockHorizontalSpeed").floatValue = spec.MaxKnockHorizontalSpeed;
            serializedAuthoring.FindProperty("maxKnockVerticalSpeed").floatValue = spec.MaxKnockVerticalSpeed;
            serializedAuthoring.FindProperty("logicalSize").vector3IntValue = logicalSize;
            serializedAuthoring.FindProperty("gridPosition").vector3IntValue = Vector3Int.zero;
            serializedAuthoring.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Material ResolveOverrideMaterial(BlockSpec spec)
        {
            return ResolveOverrideMaterial(spec.MaterialPathOverride);
        }

        private static Material ResolveOverrideMaterial(string materialPath)
        {
            if (string.IsNullOrEmpty(materialPath))
            {
                return null;
            }

            Material existing = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (existing != null)
            {
                return existing;
            }

            if (materialPath == GlassMaterialPath)
            {
                return CreateGlassMaterial();
            }

            Debug.LogError($"{nameof(BlockPrefabBuilder)} could not find a material at {materialPath}.");
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
