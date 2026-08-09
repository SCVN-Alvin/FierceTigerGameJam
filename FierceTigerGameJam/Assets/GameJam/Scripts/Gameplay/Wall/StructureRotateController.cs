using UnityEngine;

namespace GameJam.Gameplay.Wall
{
    public sealed class StructureRotateController : MonoBehaviour
    {
        [SerializeField] private SpinOnAxis structureSpinner;
        [SerializeField] private float dragRotationSpeed = 90f;

        private void Awake()
        {
            if (structureSpinner == null)
            {
                structureSpinner = FindFirstObjectByType<SpinOnAxis>();
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

        private void SetSpeed(float speed)
        {
            if (structureSpinner != null)
            {
                structureSpinner.SetSpeed(speed);
            }
        }
    }
}
