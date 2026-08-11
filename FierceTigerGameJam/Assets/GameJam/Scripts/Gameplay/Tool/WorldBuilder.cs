using UnityEngine;

#if UNITY_EDITOR
using System.IO;
using UnityEditor;
#endif

[DisallowMultipleComponent]
public class WorldBuilder : MonoBehaviour
{
    private const string CenterObjectName = "Structure Center";

    [Header("Center Settings")]
    [SerializeField]
    private bool includeInactiveObjects = true;

    [Header("Prefab Settings")]
    [SerializeField]
    private string prefabName = "StructurePrefab1";

    [SerializeField]
    private string saveFolder =
        "Assets/GameJam/Prefabs/StructurePrefabs/";

    public void CreateOrUpdateCenter()
    {
        TryCreateOrUpdateCenter();
    }

    private bool TryCreateOrUpdateCenter()
    {
        Transform centerTransform = transform.Find(CenterObjectName);

        MeshRenderer[] meshRenderers =
            GetComponentsInChildren<MeshRenderer>(includeInactiveObjects);

        Bounds combinedBounds = default;
        bool foundRenderer = false;

        foreach (MeshRenderer meshRenderer in meshRenderers)
        {
            // Ignore anything placed inside the center marker.
            if (centerTransform != null &&
                meshRenderer.transform.IsChildOf(centerTransform))
            {
                continue;
            }

            if (!foundRenderer)
            {
                combinedBounds = meshRenderer.bounds;
                foundRenderer = true;
            }
            else
            {
                combinedBounds.Encapsulate(meshRenderer.bounds);
            }
        }

        if (!foundRenderer)
        {
            Debug.LogWarning(
                $"No MeshRenderers were found inside {name}.",
                gameObject
            );

            return false;
        }

        if (centerTransform == null)
        {
            GameObject centerObject =
                new GameObject(CenterObjectName);

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                Undo.RegisterCreatedObjectUndo(
                    centerObject,
                    "Create Structure Center"
                );
            }
#endif

            centerTransform = centerObject.transform;
            centerTransform.SetParent(transform, false);
        }
        else
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                Undo.RecordObject(
                    centerTransform,
                    "Update Structure Center"
                );
            }
#endif
        }

        // Renderer bounds are in world space.
        centerTransform.position = combinedBounds.center;
        centerTransform.localRotation = Quaternion.identity;
        centerTransform.localScale = Vector3.one;

#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            EditorUtility.SetDirty(centerTransform);
            EditorUtility.SetDirty(gameObject);
        }
#endif

        return true;
    }

#if UNITY_EDITOR
    public void SaveAsPrefab()
    {
        if (Application.isPlaying)
        {
            Debug.LogWarning(
                "WorldBuilder prefabs can only be saved in Edit Mode.",
                gameObject
            );

            return;
        }

        if (EditorUtility.IsPersistent(gameObject))
        {
            Debug.LogWarning(
                "WorldBuilder must be used on an object in the scene.",
                gameObject
            );

            return;
        }

        string validatedName = ValidatePrefabName(prefabName);

        if (string.IsNullOrEmpty(validatedName))
            return;

        string validatedFolder = ValidateFolder(saveFolder);

        if (string.IsNullOrEmpty(validatedFolder))
            return;

        if (!TryCreateOrUpdateCenter())
        {
            Debug.LogError(
                "The prefab was not saved because no MeshRenderers " +
                "were found.",
                gameObject
            );

            return;
        }

        EnsureFolderExists(validatedFolder);

        string prefabPath =
            $"{validatedFolder}/{validatedName}.prefab";

        GameObject temporaryRoot = null;

        try
        {
            // Clone the complete hierarchy so references between children
            // remain intact.
            temporaryRoot = Instantiate(gameObject);
            temporaryRoot.name = validatedName;
            temporaryRoot.transform.SetParent(null, true);

            // Remove the authoring tool from the saved prefab.
            WorldBuilder copiedBuilder =
                temporaryRoot.GetComponent<WorldBuilder>();

            if (copiedBuilder != null)
                DestroyImmediate(copiedBuilder);

            if (File.Exists(prefabPath))
            {
                bool overwrite = EditorUtility.DisplayDialog(
                    "Overwrite Prefab?",
                    $"A prefab already exists at:\n\n{prefabPath}",
                    "Overwrite",
                    "Cancel"
                );

                if (!overwrite)
                    return;
            }

            GameObject savedPrefab =
                PrefabUtility.SaveAsPrefabAsset(
                    temporaryRoot,
                    prefabPath,
                    out bool success
                );

            if (!success || savedPrefab == null)
            {
                Debug.LogError(
                    $"Failed to save prefab at {prefabPath}.",
                    gameObject
                );

                return;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorGUIUtility.PingObject(savedPrefab);

            Debug.Log(
                $"Saved structure prefab: {prefabPath}",
                savedPrefab
            );
        }
        finally
        {
            if (temporaryRoot != null)
                DestroyImmediate(temporaryRoot);
        }
    }

    private static string ValidatePrefabName(string requestedName)
    {
        string result = requestedName.Trim();

        if (result.EndsWith(".prefab"))
        {
            result = result.Substring(
                0,
                result.Length - ".prefab".Length
            );
        }

        char[] invalidCharacters =
        {
            '/', '\\', ':', '*', '?', '"', '<', '>', '|'
        };

        if (string.IsNullOrWhiteSpace(result) ||
            result.IndexOfAny(invalidCharacters) >= 0)
        {
            Debug.LogError(
                "Enter a valid prefab name without slashes or " +
                "special file-name characters."
            );

            return null;
        }

        return result;
    }

    private static string ValidateFolder(string requestedFolder)
    {
        string result = requestedFolder
            .Trim()
            .Replace('\\', '/')
            .TrimEnd('/');

        if (result != "Assets" &&
            !result.StartsWith("Assets/"))
        {
            Debug.LogError(
                "The prefab save folder must be inside Assets. " +
                "Example: Assets/GameJam/Prefabs/StructurePrefabs"
            );

            return null;
        }

        if (result.Contains(".."))
        {
            Debug.LogError(
                "The prefab save folder cannot contain '..'."
            );

            return null;
        }

        return result;
    }

    private static void EnsureFolderExists(string folderPath)
    {
        if (folderPath == "Assets")
            return;

        string[] folderParts = folderPath.Split('/');
        string currentPath = "Assets";

        for (int i = 1; i < folderParts.Length; i++)
        {
            string nextPath =
                $"{currentPath}/{folderParts[i]}";

            if (!AssetDatabase.IsValidFolder(nextPath))
            {
                AssetDatabase.CreateFolder(
                    currentPath,
                    folderParts[i]
                );
            }

            currentPath = nextPath;
        }
    }
#endif
}