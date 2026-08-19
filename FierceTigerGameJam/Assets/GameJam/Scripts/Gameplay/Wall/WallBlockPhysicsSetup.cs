using UnityEngine;
using GameJam.Gameplay.Wall;

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

        public void PrepareBlocks(Transform root)
        {
            blocksRoot = root;
            PrepareBlocks();
        }

        [ContextMenu("Prepare Blocks")]
        public void PrepareBlocks()
        {
            Transform root = blocksRoot != null ? blocksRoot : transform;
            KnockdownBlockAuthoring[] authoredBlocks = root.GetComponentsInChildren<KnockdownBlockAuthoring>(includeInactiveBlocks);

            if (authoredBlocks.Length > 0)
            {
                for (int i = 0; i < authoredBlocks.Length; i++)
                {
                    GameObject block = authoredBlocks[i].gameObject;
                    EnsureCollider(block);
                    EnsureRigidbody(block, authoredBlocks[i]);
                    EnsureKnockdownBlock(block, authoredBlocks[i]);
                }

                return;
            }

            MeshRenderer[] renderers = root.GetComponentsInChildren<MeshRenderer>(includeInactiveBlocks);

            for (int i = 0; i < renderers.Length; i++)
            {
                GameObject block = renderers[i].gameObject;
                EnsureCollider(block);
                EnsureRigidbody(block, null);
                EnsureKnockdownBlock(block, null);
            }
        }

        /// <summary>
        /// Prepares one block that appeared after the map was built - the pieces a wall spawns
        /// when it comes apart - without walking the whole structure again.
        /// </summary>
        public void PrepareBlock(GameObject block)
        {
            if (block == null)
            {
                return;
            }

            block.TryGetComponent(out KnockdownBlockAuthoring authoring);
            EnsureCollider(block);
            EnsureRigidbody(block, authoring);
            EnsureKnockdownBlock(block, authoring);
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

        private void EnsureRigidbody(GameObject block, KnockdownBlockAuthoring authoring)
        {
            if (!block.TryGetComponent(out Rigidbody blockRigidbody))
            {
                blockRigidbody = block.AddComponent<Rigidbody>();
            }

            if (authoring != null)
            {
                blockRigidbody.mass = authoring.Mass;
            }

            blockRigidbody.isKinematic = true;
            blockRigidbody.useGravity = false;
        }

        private void EnsureKnockdownBlock(GameObject block, KnockdownBlockAuthoring authoring)
        {
            if (!block.TryGetComponent(out KnockdownBlock knockdownBlock))
            {
                knockdownBlock = block.AddComponent<KnockdownBlock>();
            }

            if (authoring != null)
            {
                knockdownBlock.ApplyAuthoring(authoring);
            }
        }
    }
}
