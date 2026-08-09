using UnityEngine;

namespace GameJam.Gameplay.Wall
{
    public class SpinOnAxis : MonoBehaviour
    {
        [SerializeField] private Transform rotationCenter;
        [SerializeField] private float speed;

        public void SetSpeed(float value)
        {
            speed = value;
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
