using GameJam.Gameplay.Combat;
using GameJam.Gameplay.Flow;
using UnityEngine;

namespace GameJam.Gameplay.Cannon
{
    public sealed class GridKnockdownCannonFireController : MonoBehaviour
    {
        [SerializeField] private Camera targetCamera;
        [Tooltip("Optional. The ammunition the player brought into this run. Without one the "
                 + "cannon fires freely, which is what the scene does before a run is set up.")]
        [SerializeField] private BulletInventory bulletInventory;

        [Tooltip("Optional. Supplies the ammunition definitions the inventory holds counts of.")]
        [SerializeField] private BulletLoadout bulletLoadout;

        [SerializeField] private GridKnockdownCannonProjectile projectilePrefab;

        [Tooltip("Optional. Hands out warm cannon balls instead of instantiating one per shot. "
                 + "Without one the cannon still fires, it just pays for the ball at the moment "
                 + "the player taps.")]
        [SerializeField] private ProjectilePool projectilePool;

        [Tooltip("Optional. Only read for how many bullets the map lets the player bring, which "
                 + "is the most shots that can ever be in the air at once.")]
        [SerializeField] private LevelRunController runController;

        [SerializeField] private Transform projectileParent;
        [SerializeField] private Transform fireOrigin;
        [SerializeField] private CannonShotPresenter shotPresenter;
        [SerializeField] private CannonAimController aimController;
        [SerializeField] private float aimPlaneZ = 20f;
        [SerializeField] private float projectileSpeed = 105f;
        [SerializeField] private float projectileLifetime = 5f;
        [SerializeField] private float muzzleSpawnOffset = 0.28f;

        private const float MinFireDirectionSqrMagnitude = 0.0001f;
        private const int FallbackPoolSize = 10;

        private void Awake()
        {
            if (targetCamera == null)
            {
                targetCamera = Camera.main;
            }

            if (aimController == null)
            {
                aimController = GetComponent<CannonAimController>();
            }

            if (aimController != null)
            {
                aimController.Initialize(fireOrigin);
            }
        }

        /// <summary>
        /// Warms the ball queue for a run. Called when the run starts rather than in Awake,
        /// because how many balls are worth holding is a property of the map that was picked.
        /// </summary>
        public void PrepareForRun()
        {
            if (projectilePool == null)
            {
                return;
            }

            int budget = runController != null && runController.BulletPickLimit > 0
                ? runController.BulletPickLimit
                : FallbackPoolSize;

            projectilePool.Warm(budget);
        }

        /// <summary>Calls back any shot still in the air, so a run never leaves one behind.</summary>
        public void EndRun()
        {
            if (projectilePool != null)
            {
                projectilePool.ReturnAll();
            }
        }

        /// <summary>
        /// Raised when a shot was refused for want of ammunition, so the UI can say so rather
        /// than leaving the player tapping at a cannon that quietly does nothing.
        /// </summary>
        public event System.Action OutOfAmmunition;

        public bool TryFireAtScreenPoint(Vector2 screenPosition)
        {
            // Checked before aiming so an empty cannon refuses immediately, and spent only at the
            // moment a shot actually leaves: an aim that fails validation must not cost a bullet.
            if (!HasAmmunition())
            {
                OutOfAmmunition?.Invoke();
                return false;
            }

            if (targetCamera == null)
            {
#if UNITY_EDITOR
                Debug.LogWarning($"{nameof(GridKnockdownCannonFireController)} needs a camera.");
#endif
                return false;
            }

            if (aimController != null)
            {
                if (!aimController.TryAimAtScreenPoint(targetCamera, screenPosition, out AimRejectReason rejectReason))
                {
                    LogAimRejected(rejectReason);
                    return false;
                }

                Vector3 aimedMuzzlePosition = fireOrigin != null ? fireOrigin.position : transform.position;
                Vector3 aimedDirection = aimController.GetFireDirection(
                    aimedMuzzlePosition,
                    projectileSpeed,
                    muzzleSpawnOffset);

                if (aimedDirection.sqrMagnitude < MinFireDirectionSqrMagnitude)
                {
                    return false;
                }

                Fire(aimedMuzzlePosition, aimedDirection.normalized);
                return true;
            }

            Vector3 muzzlePosition = fireOrigin != null ? fireOrigin.position : transform.position;
            if (!TryGetAimWorldPoint(screenPosition, out Vector3 aimWorldPoint))
            {
                return false;
            }

            Vector3 fireDirection = aimWorldPoint - muzzlePosition;
            if (fireDirection.sqrMagnitude < MinFireDirectionSqrMagnitude)
            {
                return false;
            }

            Fire(muzzlePosition, fireDirection.normalized);
            return true;
        }

        private static void LogAimRejected(AimRejectReason rejectReason)
        {
#if UNITY_EDITOR
            if (rejectReason == AimRejectReason.TooLow
                || rejectReason == AimRejectReason.TooHigh
                || rejectReason == AimRejectReason.TooLeft
                || rejectReason == AimRejectReason.TooRight)
            {
                Debug.Log($"Aim at the structure. ({rejectReason})");
            }
#endif
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

        /// <summary>Whether a shot can be paid for. True when no inventory is wired at all.</summary>
        private bool HasAmmunition()
        {
            return bulletInventory == null || !bulletInventory.IsEmpty;
        }

        /// <summary>
        /// Which kind this shot spends: what the player has selected if they still have any,
        /// otherwise whatever is left. Running out of the chosen kind should fall through to the
        /// rest of the run's ammunition rather than stop the run dead.
        /// </summary>
        private BulletDefinition ResolveShotAmmunition()
        {
            if (bulletInventory == null || bulletLoadout == null)
            {
                return null;
            }

            BulletDefinition selected = bulletLoadout.Selected;
            if (selected != null && bulletInventory.GetCount(selected.Id) > 0)
            {
                return selected;
            }

            return bulletLoadout.Find(bulletInventory.FindFirstAvailable());
        }

        private void Fire(Vector3 muzzlePosition, Vector3 direction)
        {
            Vector3 spawnPosition = muzzlePosition + direction * muzzleSpawnOffset;
            BulletDefinition ammunition = ResolveShotAmmunition();
            if (ammunition != null && !bulletInventory.TrySpend(ammunition.Id))
            {
                return;
            }

            Quaternion spawnRotation = Quaternion.LookRotation(direction, Vector3.up);
            GridKnockdownCannonProjectile projectile = RentProjectile(spawnPosition, spawnRotation, direction);
            if (projectile == null)
            {
                return;
            }

            // Told what fired it before launching, since the damage it deals is looked up from
            // the ammunition and the very first collision can happen on the next physics step.
            if (ammunition != null)
            {
                projectile.SetAmmunition(ammunition, bulletLoadout.GetLevel(ammunition));
            }

            projectile.Launch(direction, projectileSpeed, projectileLifetime);
            if (shotPresenter != null)
            {
                shotPresenter.PlayShot();
            }
        }

        /// <summary>
        /// The pool first, then a plain instantiate of the prefab, then the built-in sphere. The
        /// last of those is the scene's own fallback for having no prefab at all, so it is never
        /// pooled: it is a debugging aid, not something a run is played with.
        /// </summary>
        private GridKnockdownCannonProjectile RentProjectile(
            Vector3 spawnPosition,
            Quaternion spawnRotation,
            Vector3 direction)
        {
            if (projectilePool != null && projectilePool.HasPrefab)
            {
                GridKnockdownCannonProjectile pooled = projectilePool.Rent(spawnPosition, spawnRotation, projectileParent);
                if (pooled != null)
                {
                    return pooled;
                }
            }

            if (projectilePrefab != null)
            {
                return Instantiate(projectilePrefab, spawnPosition, spawnRotation, projectileParent);
            }

            return CreateDefaultProjectile(spawnPosition, direction);
        }

        private GridKnockdownCannonProjectile CreateDefaultProjectile(Vector3 spawnPosition, Vector3 direction)
        {
            GameObject projectileObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            projectileObject.name = "Grid Knockdown Cannon Projectile";
            projectileObject.transform.SetParent(projectileParent);
            projectileObject.transform.position = spawnPosition;
            projectileObject.transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
            projectileObject.transform.localScale = Vector3.one * 0.275f;

            Rigidbody projectileRigidbody = projectileObject.AddComponent<Rigidbody>();
            projectileRigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            projectileRigidbody.interpolation = RigidbodyInterpolation.Interpolate;
            projectileRigidbody.useGravity = true;

            return projectileObject.AddComponent<GridKnockdownCannonProjectile>();
        }
    }
}
