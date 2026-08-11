using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

[DisallowMultipleComponent]
public class ObjectArrayModifier : MonoBehaviour
{
    public enum ArrayDirection
    {
        Right,
        Left,
        Up,
        Down,
        Forward,
        Backward
    }

    [SerializeField] private ArrayDirection direction = ArrayDirection.Right;

    [Tooltip("Number of additional copies. The original is not included.")]
    [SerializeField, Min(0)] private int numberOfCopies = 3;

    [Tooltip("Empty space between the surfaces of adjacent objects.")]
    [SerializeField, Min(0f)] private float spacing = 0.1f;

    private const string GeneratedRootName = "__Array Generated Objects";

    public void SpawnArray()
    {
#if UNITY_EDITOR
        numberOfCopies = Mathf.Max(0, numberOfCopies);
        spacing = Mathf.Max(0f, spacing);

        ClearArray();

        if (numberOfCopies == 0)
            return;

        Vector3 localDirection = GetLocalDirection();
        float objectSize = CalculateSizeAlongDirection(localDirection);
        float step = objectSize + spacing;

        GameObject template = null;

        try
        {
            // Create the template before creating the generated container.
            template = Instantiate(gameObject);
            template.name = gameObject.name + " Array Template";
            template.SetActive(false);
            template.hideFlags = HideFlags.HideAndDontSave;

            // Remove array components from the template and all its children.
            // Generated copies therefore cannot create their own arrays.
            ObjectArrayModifier[] copiedModifiers =
                template.GetComponentsInChildren<ObjectArrayModifier>(true);

            foreach (ObjectArrayModifier modifier in copiedModifiers)
                DestroyImmediate(modifier);

            GameObject generatedRoot = new GameObject(GeneratedRootName);
            generatedRoot.transform.SetParent(transform, false);

            for (int i = 1; i <= numberOfCopies; i++)
            {
                GameObject copy = Instantiate(template, generatedRoot.transform);

                copy.name = $"{gameObject.name} Array {i}";
                copy.hideFlags = HideFlags.None;

                copy.transform.localPosition = localDirection * step * i;
                copy.transform.localRotation = Quaternion.identity;
                copy.transform.localScale = Vector3.one;

                copy.SetActive(gameObject.activeSelf);
            }

            Undo.RegisterCreatedObjectUndo(
                generatedRoot,
                "Spawn Object Array"
            );

            EditorUtility.SetDirty(gameObject);
        }
        finally
        {
            if (template != null)
                DestroyImmediate(template);
        }
#endif
    }

    public void ClearArray()
    {
#if UNITY_EDITOR
        Transform generatedRoot = FindGeneratedRoot();

        if (generatedRoot != null)
        {
            Undo.DestroyObjectImmediate(generatedRoot.gameObject);
            EditorUtility.SetDirty(gameObject);
        }
#endif
    }

    private Vector3 GetLocalDirection()
    {
        switch (direction)
        {
            case ArrayDirection.Left:
                return Vector3.left;

            case ArrayDirection.Up:
                return Vector3.up;

            case ArrayDirection.Down:
                return Vector3.down;

            case ArrayDirection.Forward:
                return Vector3.forward;

            case ArrayDirection.Backward:
                return Vector3.back;

            default:
                return Vector3.right;
        }
    }

    private float CalculateSizeAlongDirection(Vector3 localDirection)
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);

        float minimum = float.PositiveInfinity;
        float maximum = float.NegativeInfinity;
        bool foundRenderer = false;

        foreach (Renderer currentRenderer in renderers)
        {
            Bounds bounds = currentRenderer.bounds;
            Vector3 min = bounds.min;
            Vector3 max = bounds.max;

            Vector3[] worldCorners =
            {
                new Vector3(min.x, min.y, min.z),
                new Vector3(min.x, min.y, max.z),
                new Vector3(min.x, max.y, min.z),
                new Vector3(min.x, max.y, max.z),
                new Vector3(max.x, min.y, min.z),
                new Vector3(max.x, min.y, max.z),
                new Vector3(max.x, max.y, min.z),
                new Vector3(max.x, max.y, max.z)
            };

            foreach (Vector3 worldCorner in worldCorners)
            {
                Vector3 localPoint =
                    transform.InverseTransformPoint(worldCorner);

                float position =
                    Vector3.Dot(localPoint, localDirection);

                minimum = Mathf.Min(minimum, position);
                maximum = Mathf.Max(maximum, position);
            }

            foundRenderer = true;
        }

        return foundRenderer ? maximum - minimum : 1f;
    }

    private Transform FindGeneratedRoot()
    {
        for (int i = 0; i < transform.childCount; i++)
        {
            Transform child = transform.GetChild(i);

            if (child.name == GeneratedRootName)
                return child;
        }

        return null;
    }
}