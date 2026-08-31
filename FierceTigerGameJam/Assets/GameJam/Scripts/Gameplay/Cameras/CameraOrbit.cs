using UnityEngine;

namespace GameJam.Gameplay.Cameras
{
    /// <summary>
    /// Turns the view around the structure by rotating a pivot that the camera rig, the cannon,
    /// the aim plane and the backdrop hang from. The structure itself never moves.
    ///
    /// Why the drag moves the camera rather than the map: every collider in the structure is
    /// owned by a block's own Rigidbody, so any way of turning the structure is a way of moving
    /// several hundred bodies. Teleporting them tripped the collision activation thresholds and
    /// read as an explosion; sweeping the kinematic ones with MovePosition is physically honest
    /// but can only carry the blocks that are still kinematic, so after a shot the intact half
    /// turned and the damaged half stayed and the structure read as stuck. Orbiting the camera is
    /// visually the same gesture and disturbs no physics at all: nothing is teleported, no fake
    /// contact velocity is invented, and loose blocks and debris simply keep falling while the
    /// player turns.
    ///
    /// A plain transform write is correct here in a way it never was on the structure. Nothing
    /// under this pivot has a Rigidbody, so there is no physics pose to keep in step, and Update
    /// rather than FixedUpdate is what keeps the turn smooth at the display rate.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CameraOrbit : MonoBehaviour
    {
        [Tooltip("Degrees per second about the world's vertical axis. Driven by the drag; an "
                 + "authored value simply spins the view, which is useful when framing a scene.")]
        [SerializeField] private float speed;

        /// <summary>
        /// The authored pose, so <see cref="ResetRotation"/> has an angle to go back to. Read in
        /// Awake because the scene file is the authority on what "the authored viewpoint" means.
        /// </summary>
        private Quaternion restRotation = Quaternion.identity;

        /// <summary>
        /// Total turn from the authored pose. The one accumulating value, and it is a scalar:
        /// composing a delta onto the transform's quaternion every frame would let rounding error
        /// build up over a long session and slowly tilt a rig that may only ever yaw.
        /// </summary>
        private float angle;

        private void Awake()
        {
            restRotation = transform.localRotation;
        }

        /// <summary>Degrees per second. Zero stops the orbit where it stands.</summary>
        public void SetSpeed(float value)
        {
            speed = value;
        }

        /// <summary>
        /// Puts the view back to the angle the scene was authored at, and stops it. Called
        /// between runs so every map is entered from the same viewpoint rather than from wherever
        /// the last run's final drag left the camera.
        /// </summary>
        public void ResetRotation()
        {
            speed = 0f;
            angle = 0f;
            transform.localRotation = restRotation;
        }

        private void Update()
        {
            if (Mathf.Approximately(speed, 0f))
            {
                return;
            }

            // Wrapped rather than left to grow, so the scalar stays small and precise however
            // long a session runs. Mathf.Repeat is one float op and allocates nothing.
            angle = Mathf.Repeat(angle + (speed * Time.deltaTime), 360f);

            // The pivot's parents are the scene's unrotated section headers, so turning about
            // Vector3.up in parent space is turning about world up. The authored pose is applied
            // first and the orbit on top of it, so a rig framed at an angle keeps its framing.
            transform.localRotation = Quaternion.AngleAxis(angle, Vector3.up) * restRotation;
        }
    }
}
