using GameJam.Gameplay.Cameras;
using UnityEngine;

namespace GameJam.Gameplay.Wall
{
    /// <summary>
    /// Turns the player's view of the structure while they drag. It used to drive
    /// <see cref="SpinOnAxis"/> and turn the map itself; it now drives a <see cref="CameraOrbit"/>
    /// and turns the camera rig around the map, which looks the same and leaves the physics scene
    /// alone. The name is kept because it is what the drag means to the player.
    /// </summary>
    public sealed class StructureRotateController : MonoBehaviour
    {
        [SerializeField] private CameraOrbit cameraOrbit;
        [SerializeField] private float dragRotationSpeed = 90f;

        private void Awake()
        {
            if (cameraOrbit == null)
            {
                cameraOrbit = FindFirstObjectByType<CameraOrbit>();
            }
        }

        public void RotateFromScreenDelta(float screenDeltaX)
        {
            if (Mathf.Approximately(screenDeltaX, 0f))
            {
                Stop();
                return;
            }

            SetSpeed(Mathf.Sign(screenDeltaX) * dragRotationSpeed);
        }

        public void Stop()
        {
            SetSpeed(0f);
        }

        /// <summary>
        /// Takes the speed the structure used to be turned at and gives the orbit its negative.
        ///
        /// The two are the same gesture seen from opposite ends. SpinOnAxis turned the structure
        /// about world up through the structure centre; the orbit turns the camera about world up
        /// through the same point. Turning the camera by -x leaves the camera-relative pose of
        /// the structure exactly as turning the structure by +x did, so the sign has to flip here
        /// or every drag would send the view the wrong way. dragRotationSpeed keeps its old
        /// meaning - how fast the world appears to turn - so the drag feels unchanged.
        /// </summary>
        private void SetSpeed(float speed)
        {
            if (cameraOrbit != null)
            {
                cameraOrbit.SetSpeed(-speed);
            }
        }
    }
}
