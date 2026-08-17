using UnityEngine;

namespace GameJam.Gameplay.Wall
{
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
