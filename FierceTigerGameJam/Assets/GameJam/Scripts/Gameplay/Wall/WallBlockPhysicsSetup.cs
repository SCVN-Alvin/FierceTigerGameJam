using UnityEngine;
using GameJam.Gameplay.Wall;

namespace GameJam.Gameplay
{
    /// <summary>
    /// Applies per-placement tuning to blocks the map has just laid out.
    ///
    /// It used to build them too - a collider, a body and a KnockdownBlock added to every block
    /// at run start. On a 471-block map that was over nine hundred AddComponent calls inside the
    /// frame the player is waiting on. The block prefabs carry all three now, so this only has to
    /// hand each block the grid position and footprint it was placed at, which is the part that
    /// cannot be known until the map is read.
    ///
    /// The AddComponent path is kept as a fallback for prefabs built before the bake, and says so
    /// once per session rather than once per block.
    /// </summary>
    public class WallBlockPhysicsSetup : MonoBehaviour
    {
        [SerializeField] private Transform blocksRoot;
        [SerializeField] private bool includeInactiveBlocks;
        [SerializeField] private bool addMeshColliders;

        private bool warnedAboutMissingComponents;

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

        /// <summary>
        /// Said once, not once per block: a map that missed the bake would otherwise print the
        /// same warning several hundred times in the frame it can least afford it.
        /// </summary>
        private void WarnAboutMissingComponent(string componentName, GameObject block)
        {
            GameJam.Diagnostics.RuntimeProfileLogger.Count("added_" + componentName);

            if (warnedAboutMissingComponents)
            {
                return;
            }

            warnedAboutMissingComponents = true;
            Debug.LogWarning(
                $"{block.name} has no {componentName}, so it was added at run start. That costs a "
                + "frame on a large map. Rebuild the block prefabs with "
                + "Tools > Smashdown > Build Block Prefabs.",
                block);
        }

        private void EnsureCollider(GameObject block)
        {
            if (block.TryGetComponent(out Collider _))
            {
                return;
            }

            WarnAboutMissingComponent("collider", block);

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
                WarnAboutMissingComponent("rigidbody", block);
                blockRigidbody = block.AddComponent<Rigidbody>();
                blockRigidbody.interpolation = RigidbodyInterpolation.None;
                blockRigidbody.collisionDetectionMode = CollisionDetectionMode.Discrete;
            }

            if (authoring != null)
            {
                blockRigidbody.mass = authoring.Mass;
            }

            // Reasserted whether the body was baked or added: a prefab is saved awake, and a
            // block has to start frozen in the wall until something knocks it.
            blockRigidbody.isKinematic = true;
            blockRigidbody.useGravity = false;
        }

        private void EnsureKnockdownBlock(GameObject block, KnockdownBlockAuthoring authoring)
        {
            if (!block.TryGetComponent(out KnockdownBlock knockdownBlock))
            {
                WarnAboutMissingComponent("knockdown_block", block);
                knockdownBlock = block.AddComponent<KnockdownBlock>();
            }

            if (authoring != null)
            {
                knockdownBlock.ApplyAuthoring(authoring);
            }
        }
    }
}
