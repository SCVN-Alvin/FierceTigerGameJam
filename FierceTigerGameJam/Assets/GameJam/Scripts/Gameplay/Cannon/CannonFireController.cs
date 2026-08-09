using UnityEngine;

namespace GameJam.Gameplay.Cannon
{
    public sealed class CannonFireController : MonoBehaviour
    {
        [SerializeField] private Camera targetCamera;
        [SerializeField] private CannonProjectile projectilePrefab;
        [SerializeField] private Transform projectileParent;
        [SerializeField] private Transform fireOrigin;
        [SerializeField] private CannonShotPresenter shotPresenter;
        [SerializeField] private float aimPlaneZ = 20f;
        [SerializeField] private float projectileSpeed = 35f;
        [SerializeField] private float projectileLifetime = 5f;
        [SerializeField] private float muzzleSpawnOffset = 0.28f;

        private const float MinFireDirectionSqrMagnitude = 0.0001f;

        private void Awake()
        {
            if (targetCamera == null)
            {
                targetCamera = Camera.main;
            }
        }

        public bool TryFireAtScreenPoint(Vector2 screenPosition)
        {
            if (targetCamera == null)
            {
                Debug.LogWarning($"{nameof(CannonFireController)} needs a camera.");
                return false;
            }

            if (!TryGetAimWorldPoint(screenPosition, out Vector3 aimWorldPoint))
            {
                return false;
            }

            Vector3 muzzlePosition = fireOrigin != null ? fireOrigin.position : transform.position;
            Vector3 fireDirection = aimWorldPoint - muzzlePosition;
            if (fireDirection.sqrMagnitude < MinFireDirectionSqrMagnitude)
            {
                return false;
            }

            Fire(muzzlePosition, fireDirection.normalized);
            return true;
        }

        private bool TryGetAimWorldPoint(Vector2 screenPosition, out Vector3 aimWorldPoint)
        {
            Ray ray = targetCamera.ScreenPointToRay(screenPosition);
            Plane aimPlane = new Plane(Vector3.forward, new Vector3(0f, 0f, aimPlaneZ));
            if (aimPlane.Raycast(ray, out float distance))
            {
                aimWorldPoint = ray.GetPoint(distance);
                return true;
            }

            aimWorldPoint = Vector3.zero;
            return false;
        }

        private void Fire(Vector3 muzzlePosition, Vector3 direction)
        {
            Vector3 spawnPosition = muzzlePosition + direction * muzzleSpawnOffset;
            CannonProjectile projectile = projectilePrefab != null
                ? Instantiate(projectilePrefab, spawnPosition, Quaternion.LookRotation(direction, Vector3.up), projectileParent)
                : CreateDefaultProjectile(spawnPosition, direction);

            projectile.Launch(direction, projectileSpeed, projectileLifetime);
            if (shotPresenter != null)
            {
                shotPresenter.PlayShot();
            }
        }

        private CannonProjectile CreateDefaultProjectile(Vector3 spawnPosition, Vector3 direction)
        {
            GameObject projectileObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            projectileObject.name = "Cannon Projectile";
            projectileObject.transform.SetParent(projectileParent);
            projectileObject.transform.position = spawnPosition;
            projectileObject.transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
            projectileObject.transform.localScale = Vector3.one * 0.55f;

            Rigidbody projectileRigidbody = projectileObject.AddComponent<Rigidbody>();
            projectileRigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            projectileRigidbody.interpolation = RigidbodyInterpolation.Interpolate;
            projectileRigidbody.useGravity = false;

            return projectileObject.AddComponent<CannonProjectile>();
        }
    }
}
