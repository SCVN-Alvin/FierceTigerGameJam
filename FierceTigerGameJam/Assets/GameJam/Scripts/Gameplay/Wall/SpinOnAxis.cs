using UnityEngine;

namespace GameJam.Gameplay.Wall
{
    /// <summary>
    /// Rotates this transform about a vertical axis through a centre object.
    ///
    /// Restored to the plain RotateAround it was before brief 22. That brief made it sweep every
    /// block's kinematic Rigidbody with MovePosition so a drag would not teleport bodies into
    /// their loose neighbours and set off an activation cascade. The sweep worked, but it could
    /// only carry blocks that were still kinematic, so after a shot the intact part of the
    /// structure turned and the damaged part did not and the whole thing read as stuck.
    ///
    /// Brief 24 removed the need for any of it: the drag now orbits a CameraOrbit pivot carrying
    /// the camera, the cannon and the backdrop, and the structure is never moved at all. Nothing
    /// drives this component in the gameplay scene any more - it is left in place, and undriven,
    /// because the demo scenes still spin their models with it from an authored speed. Anything
    /// that does drive it should know it is a transform write, and so is only safe on a structure
    /// whose bodies are not simulating.
    /// </summary>
    public class SpinOnAxis : MonoBehaviour
    {
        [SerializeField] private Transform rotationCenter;
        [SerializeField] private float speed;

        private Vector3 restPosition;
        private Quaternion restRotation;

        private void Awake()
        {
            restPosition = transform.localPosition;
            restRotation = transform.localRotation;
        }

        public void SetSpeed(float value)
        {
            speed = value;
        }

        /// <summary>
        /// Puts the transform back where it started and stops it. RotateAround moves position as
        /// well as rotation, so both have to be restored or the next map is built around an
        /// offset root.
        /// </summary>
        public void ResetRotation()
        {
            speed = 0f;
            transform.localPosition = restPosition;
            transform.localRotation = restRotation;
        }

        public void SetRotationCenter(Transform center)
        {
            rotationCenter = center;
        }

        private void Update()
        {
            if (rotationCenter == null || Mathf.Approximately(speed, 0f))
            {
                return;
            }

            transform.RotateAround(
                rotationCenter.position,
                Vector3.up,
                speed * Time.deltaTime
            );
        }
    }
}
