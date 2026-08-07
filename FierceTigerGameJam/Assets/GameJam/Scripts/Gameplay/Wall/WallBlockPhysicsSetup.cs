using UnityEngine;

namespace GameJam.Gameplay
{
    public class WallBlockPhysicsSetup : MonoBehaviour
    {
        [SerializeField] private Transform blocksRoot;
        [SerializeField] private bool includeInactiveBlocks;
        [SerializeField] private bool addMeshColliders;

        private void Awake()
        {
            PrepareBlocks();
        }

        [ContextMenu("Prepare Blocks")]
        public void PrepareBlocks()
        {
            Transform root = blocksRoot != null ? blocksRoot : transform;
            MeshRenderer[] renderers = root.GetComponentsInChildren<MeshRenderer>(includeInactiveBlocks);

            for (int i = 0; i < renderers.Length; i++)
            {
                GameObject block = renderers[i].gameObject;
                EnsureCollider(block);
                EnsureRigidbody(block);
                EnsureKnockdownBlock(block);
            }
        }

        private void EnsureCollider(GameObject block)
        {
            if (block.TryGetComponent(out Collider _))
            {
                return;
            }

            if (addMeshColliders && block.TryGetComponent(out MeshFilter meshFilter) && meshFilter.sharedMesh != null)
            {
                MeshCollider meshCollider = block.AddComponent<MeshCollider>();
                meshCollider.convex = true;
                return;
            }

            BoxCollider boxCollider = block.AddComponent<BoxCollider>();
            if (block.TryGetComponent(out MeshFilter boxMeshFilter) && boxMeshFilter.sharedMesh != null)
            {
                Bounds meshBounds = boxMeshFilter.sharedMesh.bounds;
                boxCollider.center = meshBounds.center;
                boxCollider.size = meshBounds.size;
            }
        }

        private void EnsureRigidbody(GameObject block)
        {
            if (!block.TryGetComponent(out Rigidbody blockRigidbody))
            {
                blockRigidbody = block.AddComponent<Rigidbody>();
            }

            blockRigidbody.isKinematic = true;
            blockRigidbody.useGravity = false;
        }

        private void EnsureKnockdownBlock(GameObject block)
        {
            if (!block.TryGetComponent(out KnockdownBlock _))
            {
                block.AddComponent<KnockdownBlock>();
            }
        }
    }
}
