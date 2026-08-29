using System;
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
        [Serializable]
        public struct Cell
        {
            public GameObject Prefab;
            public string Name;
            public Vector3 PositionInWall;
            public Quaternion RotationInWall;
            public Vector3Int GridPosition;
            public Vector3Int LogicalSize;
        }

        [Tooltip("What the wall is made of. Ammunition damage is authored per material, and a "
                 + "wall of mixed materials takes the identity of the first block in it.")]
        [SerializeField] private string materialId;

        [Tooltip("Taken from the blocks that make it up, so a longer wall is harder to bring "
                 + "down than a short one and much harder than a lone block.")]
        [SerializeField] private float maxHitPoints = 1f;

        [Tooltip("Impacts slower than this do nothing, which keeps a wall from grinding itself "
                 + "down against its neighbours while the structure settles.")]
        [SerializeField] private float minimumImpactSpeed = 3f;

        [Tooltip("Hit points taken per metre/second of impact above the minimum. This is what "
                 + "makes a wall come apart when it lands after being toppled.")]
        [SerializeField] private float damagePerImpactSpeed = 1.5f;

        // Serialized so a wall baked into a map prefab keeps its manifest: without it, a baked
        // wall would break up into nothing. Populated by Initialize on the build path.
        [SerializeField, HideInInspector] private List<Cell> cells = new List<Cell>();
        private WallBlockPhysicsSetup physicsSetup;
        private KnockdownBlock body;
        private float remainingHitPoints;
        private bool hasBrokenUp;

        public int CellCount => cells.Count;
        public string MaterialId => materialId;
        public float MaxHitPoints => maxHitPoints;
        public float RemainingHitPoints => remainingHitPoints;
        public bool IsBroken => hasBrokenUp;

        /// <summary>0 while untouched, 1 when the next hit brings it down.</summary>
        public float DamageFraction => maxHitPoints <= 0f
            ? 1f
            : Mathf.Clamp01(1f - (remainingHitPoints / maxHitPoints));

        public void Initialize(IEnumerable<Cell> wallCells, WallBlockPhysicsSetup setup, string wallMaterialId, float hitPoints)
        {
            cells.Clear();
            cells.AddRange(wallCells);
            physicsSetup = setup;
            materialId = wallMaterialId;
            maxHitPoints = Mathf.Max(0.01f, hitPoints);
            remainingHitPoints = maxHitPoints;
        }

        /// <summary>
        /// Hands a wall from a baked map prefab the physics setup it could not serialize. The
        /// manifest, material and hit points are already in the prefab; this is the one runtime
        /// reference that has to arrive from the scene.
        /// </summary>
        public void AttachPhysicsSetup(WallBlockPhysicsSetup setup)
        {
            physicsSetup = setup;
        }

        private void Awake()
        {
            if (remainingHitPoints <= 0f)
            {
                remainingHitPoints = maxHitPoints;
            }
        }

        /// <summary>
        /// Chips the wall, and brings it down when there is nothing left. Ammunition that does no
        /// damage to this material leaves it completely alone, which is what makes an upgrade an
        /// unlock rather than a speed-up.
        /// </summary>
        public void ApplyDamage(float amount, Vector3 impactPoint, Vector3 impactDirection)
        {
            if (hasBrokenUp || amount <= 0f)
            {
                return;
            }

            remainingHitPoints -= amount;
            if (remainingHitPoints <= 0f)
            {
                BreakUp();
            }
        }

        /// <summary>
        /// A wall that has been toppled comes apart when it lands. Without this a knocked wall
        /// would slide around the floor as one slab forever.
        /// </summary>
        private void OnCollisionEnter(Collision collision)
        {
            if (hasBrokenUp)
            {
                return;
            }

            float impactSpeed = collision.relativeVelocity.magnitude;
            if (impactSpeed <= minimumImpactSpeed)
            {
                return;
            }

            ContactPoint contact = collision.GetContact(0);
            ApplyDamage((impactSpeed - minimumImpactSpeed) * damagePerImpactSpeed, contact.point, -contact.normal);
        }

        /// <summary>
        /// Remembers the body the physics setup gave this wall. The wall used to come apart the
        /// moment that body was activated, but a wall now has hit points: ammunition too weak for
        /// the material has to be able to shove it without destroying it.
        /// </summary>
        public void Listen(KnockdownBlock knockdownBlock)
        {
            body = knockdownBlock;
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
