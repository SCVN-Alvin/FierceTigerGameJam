using System.Collections.Generic;
using GameJam.Gameplay;
using UnityEngine;

namespace GameJam.Gameplay.Wall
{
    /// <summary>
    /// A run of same-type blocks built as one body. While nothing has touched it the wall is a
    /// single mesh, a single collider and a single rigidbody; the moment it is knocked it spawns
    /// the blocks it stands for and hands them its velocity, so what falls apart is the same
    /// crowd of blocks the map asked for.
    ///
    /// This is the same swap <see cref="BreakableBlock"/> makes when a block turns into debris,
    /// one level up: an aggregate that only pays for its parts once something disturbs it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BreakableWall : MonoBehaviour
    {
        /// <summary>
        /// Everything needed to rebuild one block of the wall, worked out at build time. The
        /// pose is stored relative to the wall itself, so the blocks appear wherever the wall has
        /// got to rather than snapping back to where it was built.
        /// </summary>
        public struct Cell
        {
            public GameObject Prefab;
            public string Name;
            public Vector3 PositionInWall;
            public Quaternion RotationInWall;
            public Vector3Int GridPosition;
            public Vector3Int LogicalSize;
        }

        private readonly List<Cell> cells = new List<Cell>();
        private WallBlockPhysicsSetup physicsSetup;
        private KnockdownBlock body;
        private bool hasBrokenUp;

        public int CellCount => cells.Count;

        public void Initialize(IEnumerable<Cell> wallCells, WallBlockPhysicsSetup setup)
        {
            cells.Clear();
            cells.AddRange(wallCells);
            physicsSetup = setup;
        }

        /// <summary>
        /// Subscribed late rather than in Awake: the KnockdownBlock is added by the physics setup
        /// after the wall has been built, so it does not exist yet when this component wakes.
        /// </summary>
        public void Listen(KnockdownBlock knockdownBlock)
        {
            if (body != null)
            {
                body.Activated -= HandleActivated;
            }

            body = knockdownBlock;
            if (body != null)
            {
                body.Activated += HandleActivated;
            }
        }

        private void OnDestroy()
        {
            if (body != null)
            {
                body.Activated -= HandleActivated;
            }
        }

        private void HandleActivated(KnockdownBlock knockdownBlock)
        {
            BreakUp();
        }

        [ContextMenu("Break Up")]
        public void BreakUp()
        {
            if (hasBrokenUp)
            {
                return;
            }

            hasBrokenUp = true;

            Vector3 inheritedVelocity = TryGetComponent(out Rigidbody wallBody)
                ? wallBody.linearVelocity
                : Vector3.zero;
            Vector3 inheritedSpin = wallBody != null ? wallBody.angularVelocity : Vector3.zero;

            Transform parent = transform.parent;
            for (int i = 0; i < cells.Count; i++)
            {
                SpawnCell(cells[i], parent, inheritedVelocity, inheritedSpin);
            }


            Destroy(gameObject);
        }

        private void SpawnCell(Cell cell, Transform parent, Vector3 velocity, Vector3 spin)
        {
            if (cell.Prefab == null)
            {
                return;
            }

            // Placed through the wall's current transform, so a wall that was already shoved
            // before it came apart drops its blocks where it actually is.
            GameObject block = Instantiate(
                cell.Prefab,
                transform.TransformPoint(cell.PositionInWall),
                transform.rotation * cell.RotationInWall,
                parent);
            block.name = cell.Name;
            block.transform.localScale = Vector3.one;

            if (block.TryGetComponent(out KnockdownBlockAuthoring authoring))
            {
                authoring.SetGridPosition(cell.GridPosition);
                authoring.SetLogicalSize(cell.LogicalSize);
            }

            if (physicsSetup != null)
            {
                physicsSetup.PrepareBlock(block);
            }

            // Already loose, because the wall they came from was: leaving them frozen would hang
            // a knocked wall in mid-air.
            if (block.TryGetComponent(out KnockdownBlock knockdownBlock))
            {
                knockdownBlock.Activate();
            }

            if (block.TryGetComponent(out Rigidbody blockBody) && !blockBody.isKinematic)
            {
                blockBody.linearVelocity = velocity;
                blockBody.angularVelocity = spin;
            }
        }
    }
}
