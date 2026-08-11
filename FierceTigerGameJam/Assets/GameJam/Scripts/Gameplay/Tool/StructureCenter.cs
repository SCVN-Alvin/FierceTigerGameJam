using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

[DisallowMultipleComponent]
public class StructureCenter : MonoBehaviour
{
    private const string CenterObjectName = "Structure Center";

    [SerializeField]
    private bool includeInactiveObjects = true;

    public void CreateOrUpdateCenter()
    {
        Transform existingCenter = transform.Find(CenterObjectName);

        MeshRenderer[] renderers =
            GetComponentsInChildren<MeshRenderer>(includeInactiveObjects);

        Bounds combinedBounds = default;
        bool foundRenderer = false;

        foreach (MeshRenderer meshRenderer in renderers)
        {
            // Ignore renderers placed beneath the center marker.
            if (existingCenter != null &&
                meshRenderer.transform.IsChildOf(existingCenter))
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
                $"No MeshRenderers were found inside {gameObject.name}.",
                gameObject
            );

            return;
        }

        Vector3 worldCenter = combinedBounds.center;

        GameObject centerObject;

        if (existingCenter == null)
        {
            centerObject = new GameObject(CenterObjectName);

#if UNITY_EDITOR
            if (!Application.isPlaying)
                Undo.RegisterCreatedObjectUndo(
                    centerObject,
                    "Create Structure Center"
                );
#endif

            centerObject.transform.SetParent(transform, false);
        }
        else
        {
            centerObject = existingCenter.gameObject;

#if UNITY_EDITOR
            if (!Application.isPlaying)
                Undo.RecordObject(
                    centerObject.transform,
                    "Update Structure Center"
                );
#endif
        }

        // Convert the calculated world position into WorldBuilder local space.
        centerObject.transform.localPosition =
            transform.InverseTransformPoint(worldCenter);

        centerObject.transform.localRotation = Quaternion.identity;
        centerObject.transform.localScale = Vector3.one;

#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            EditorUtility.SetDirty(centerObject.transform);
            EditorUtility.SetDirty(gameObject);
        }
#endif
    }
}