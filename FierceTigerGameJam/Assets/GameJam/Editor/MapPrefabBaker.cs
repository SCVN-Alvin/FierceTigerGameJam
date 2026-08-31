#if UNITY_EDITOR
using System.Collections.Generic;
using GameJam.Gameplay.Wall;
using UnityEditor;
using UnityEngine;

namespace GameJam.EditorTools
{
    /// <summary>
    /// Turns every map JSON registered in the MapConfig into a pre-built prefab, and points the
    /// config entry at it, so loading a level instantiates one finished structure instead of
    /// parsing JSON and spawning hundreds of blocks inside the frame the player is waiting on.
    ///
    /// The bake runs the exact same build code the game runs - KnockdownLayoutMapAuthoring on a
    /// temporary object - so a baked map cannot drift from what the JSON would have built. Two
    /// things need editor-only care afterwards: welded wall meshes are runtime objects, so copies
    /// of them are saved into the prefab as sub-assets; and the walls' break-up manifests ride
    /// along because BreakableWall serializes them.
    ///
    /// Physics is deliberately NOT baked: rigidbodies and knockdown state are added at load by
    /// WallBlockPhysicsSetup exactly as they are for a JSON build, which is what keeps the two
    /// paths behaviourally identical.
    ///
    /// Re-run after editing any map JSON - a bake is a snapshot, and the runtime prefers the
    /// prefab over the JSON whenever one is assigned.
    /// </summary>
    public static class MapPrefabBaker
    {
        private const string OutputFolder = "Assets/GameJam/Prefabs/Maps";

        [MenuItem("Tools/Smashdown/Bake Map Prefabs")]
        public static void BakeAll()
        {
            MapConfig config = LoadFirst<MapConfig>();
            BlockDatabase blocks = LoadFirst<BlockDatabase>();
            if (config == null || blocks == null)
            {
                Debug.LogError("Baking needs a MapConfig and a BlockDatabase in the project.");
                return;
            }

            EnsureFolder(OutputFolder);

            SerializedObject serializedConfig = new SerializedObject(config);
            SerializedProperty maps = serializedConfig.FindProperty("maps");

            int baked = 0;
            for (int i = 0; i < maps.arraySize; i++)
            {
                SerializedProperty entry = maps.GetArrayElementAtIndex(i);
                TextAsset json = entry.FindPropertyRelative("mapJson").objectReferenceValue as TextAsset;
                string id = entry.FindPropertyRelative("id").stringValue;
                if (json == null || string.IsNullOrEmpty(id))
                {
                    continue;
                }

                GameObject prefab = Bake(id, json, blocks);
                if (prefab == null)
                {
                    continue;
                }

                entry.FindPropertyRelative("mapPrefab").objectReferenceValue = prefab;
                baked++;
            }

            serializedConfig.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            Debug.Log($"Baked {baked} map prefab(s) into {OutputFolder} and pointed the MapConfig at them.");
        }

        /// <summary>
        /// One menu item to go back: clears every mapPrefab reference so all maps load from JSON
        /// again. The prefabs stay on disk for comparison until deleted by hand.
        /// </summary>
        [MenuItem("Tools/Smashdown/Unbake Map Prefabs (load from JSON)")]
        public static void UnbakeAll()
        {
            MapConfig config = LoadFirst<MapConfig>();
            if (config == null)
            {
                return;
            }

            SerializedObject serializedConfig = new SerializedObject(config);
            SerializedProperty maps = serializedConfig.FindProperty("maps");
            for (int i = 0; i < maps.arraySize; i++)
            {
                maps.GetArrayElementAtIndex(i).FindPropertyRelative("mapPrefab").objectReferenceValue = null;
            }

            serializedConfig.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            Debug.Log("Every map loads from JSON again. The baked prefabs are still on disk.");
        }

        private static GameObject Bake(string id, TextAsset json, BlockDatabase blocks)
        {
            string path = $"{OutputFolder}/Map_{Sanitize(id)}.prefab";
            string meshPath = $"{OutputFolder}/Map_{Sanitize(id)}_Meshes.asset";

            // Deleted rather than overwritten: overwriting would strand the previous bake's
            // meshes inside the files as orphans.
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.DeleteAsset(meshPath);

            GameObject temp = new GameObject($"MapBake_{id}");
            try
            {
                KnockdownLayoutMapAuthoring authoring = temp.AddComponent<KnockdownLayoutMapAuthoring>();
                SerializedObject serialized = new SerializedObject(authoring);
                serialized.FindProperty("mapJson").objectReferenceValue = json;
                serialized.FindProperty("blockDatabase").objectReferenceValue = blocks;
                serialized.FindProperty("buildOnStart").boolValue = false;
                serialized.FindProperty("createStructureCenter").boolValue = false;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                authoring.BuildMap();

                Transform generated = temp.transform.Find(KnockdownLayoutMapAuthoring.GeneratedBlocksRootName);
                if (generated == null || generated.childCount == 0)
                {
                    Debug.LogError($"Map \"{id}\" built nothing, so there is no prefab to save. Check its JSON.");
                    return null;
                }

                generated.gameObject.name = $"Map_{Sanitize(id)}";

                // Only the welded-mesh path needs this. Those meshes are created at build time
                // and belong to the scene, and SaveAsPrefabAsset nulls any scene-only reference,
                // so they have to become assets first or the wall bakes invisible.
                //
                // The default path draws walls with instances of the block prefabs instead, which
                // reference meshes that already exist in the FBX. Nothing is generated, so there
                // is nothing to persist - and the bake stops writing the mesh assets that were
                // costing 122 MB across the campaign against 1.4 MB of source JSON.
                if (authoring.WallVisual == KnockdownLayoutMapAuthoring.WallVisualMode.WeldedMesh)
                {
                    PersistRuntimeMeshes(generated, meshPath);
                }

                return PrefabUtility.SaveAsPrefabAsset(generated.gameObject, path);
            }
            finally
            {
                Object.DestroyImmediate(temp);
            }
        }

        /// <summary>
        /// Welded wall meshes are created at build time and belong to the scene. Copies are
        /// saved into a mesh asset beside the prefab and swapped onto the walls, so the prefab
        /// save that follows keeps real references instead of nulling scene-only ones.
        /// The originals stay with the temp builder, which destroys them on teardown.
        /// </summary>
        private static void PersistRuntimeMeshes(Transform generated, string meshPath)
        {
            Dictionary<Mesh, Mesh> copies = new Dictionary<Mesh, Mesh>();
            foreach (MeshFilter filter in generated.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh mesh = filter.sharedMesh;
                if (mesh == null || EditorUtility.IsPersistent(mesh))
                {
                    continue;
                }

                if (!copies.TryGetValue(mesh, out Mesh copy))
                {
                    copy = Object.Instantiate(mesh);
                    copy.name = mesh.name;
                    if (copies.Count == 0)
                    {
                        AssetDatabase.CreateAsset(copy, meshPath);
                    }
                    else
                    {
                        AssetDatabase.AddObjectToAsset(copy, meshPath);
                    }

                    copies[mesh] = copy;
                }

                filter.sharedMesh = copy;
            }

            if (copies.Count > 0)
            {
                AssetDatabase.SaveAssets();
            }
        }

        private static T LoadFirst<T>() where T : Object
        {
            string[] guids = AssetDatabase.FindAssets($"t:{typeof(T).Name}");
            return guids.Length > 0
                ? AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guids[0]))
                : null;
        }

        private static string Sanitize(string id)
        {
            char[] result = id.ToCharArray();
            for (int i = 0; i < result.Length; i++)
            {
                if (!char.IsLetterOrDigit(result[i]) && result[i] != '_')
                {
                    result[i] = '_';
                }
            }

            return new string(result);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            int split = path.LastIndexOf('/');
            EnsureFolder(path.Substring(0, split));
            AssetDatabase.CreateFolder(path.Substring(0, split), path.Substring(split + 1));
        }
    }
}
#endif
