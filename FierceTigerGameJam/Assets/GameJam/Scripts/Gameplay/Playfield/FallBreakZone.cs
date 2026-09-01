using GameJam.Gameplay;
using GameJam.Gameplay.Cannon;
using GameJam.Gameplay.Wall;
using UnityEngine;

namespace GameJam.Gameplay.Playfield
{
    /// <summary>
    /// A surface or volume that finishes off whatever reaches it. On the ground it breaks blocks
    /// that land hard enough, which is what stops a toppled structure from lying about in one
    /// piece; on an out-of-bounds volume it clears anything that has left the playfield, which is
    /// what stops debris accumulating forever below the floor.
    ///
    /// It drives the break machinery the blocks already have rather than implementing its own, so
    /// a block broken by the ground comes apart into exactly the same debris as one shot off the
    /// structure.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FallBreakZone : MonoBehaviour
    {
        public enum Action
        {
            /// <summary>Break it where it lands, leaving debris.</summary>
            Break,

            /// <summary>Remove it outright. For out-of-bounds, where nobody can see it anyway.</summary>
            Despawn,
        }

        [SerializeField] private Action action = Action.Break;

        [Tooltip("Solid-contact only. A landing slower than this is a block settling, not a block "
                 + "hitting the floor, and breaking those would take the structure apart as it "
                 + "was built. Trigger volumes ignore this and always act.")]
        [SerializeField] private float minimumImpactSpeed = 1.5f;

        [Tooltip("Debris is already on its way out and fades itself, so breaking it again just "
                 + "spawns debris from debris.")]
        [SerializeField] private bool affectDebris;

        private void OnCollisionEnter(Collision collision)
        {
            if (collision == null || collision.relativeVelocity.magnitude < minimumImpactSpeed)
            {
                return;
            }

            ContactPoint contact = collision.GetContact(0);
            Affect(collision.collider, contact.point, -contact.normal);
        }

        private void OnTriggerEnter(Collider other)
        {
            // No speed test: anything that got here has left the playfield, however slowly.
            Affect(other, other.bounds.center, Vector3.down);
        }

        private void Affect(Collider hit, Vector3 point, Vector3 direction)
        {
            if (hit == null)
            {
                return;
            }

            if (!affectDebris && hit.GetComponentInParent<ShatteredBlock>() != null)
            {
                return;
            }

            if (action == Action.Despawn)
            {
                Despawn(hit);
                return;
            }

            BreakableBlock block = hit.GetComponentInParent<BreakableBlock>();
            if (block != null)
            {
                block.Break(point, direction);
                return;
            }

            // Something knockable with no break behaviour of its own: nothing to shatter, so it
            // is left alone rather than silently deleted off the floor.
        }

        private static void Despawn(Collider hit)
        {
            // A cannon ball is borrowed, not owned: destroying one takes a pooled instance out of
            // circulation for the rest of the run, so the pool quietly shrinks every time a shot
            // goes wide and has to instantiate a replacement mid-tap. It goes home instead.
            // This is a live bug, not a precaution - the out-of-bounds zone already exists in the
            // scene and balls already fall through the world into it.
            GridKnockdownCannonProjectile projectile = hit.GetComponentInParent<GridKnockdownCannonProjectile>();
            if (projectile != null)
            {
                projectile.ReturnToPool();
                return;
            }

            ShatteredBlock debris = hit.GetComponentInParent<ShatteredBlock>();
            if (debris != null)
            {
                Destroy(debris.gameObject);
                return;
            }

            KnockdownBlock block = hit.GetComponentInParent<KnockdownBlock>();
            Destroy(block != null ? block.gameObject : hit.gameObject);
        }

        private void OnValidate()
        {
            minimumImpactSpeed = Mathf.Max(0f, minimumImpactSpeed);
        }
    }
}
