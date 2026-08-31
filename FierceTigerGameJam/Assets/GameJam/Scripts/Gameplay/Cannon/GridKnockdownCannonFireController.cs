using System.Collections.Generic;
using GameJam.Audio;
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

        [Tooltip("Optional. The vehicle the cannon is mounted on, which multiplies the damage of "
                 + "whatever is loaded. Without one every shot does the bullet's authored damage.")]
        [SerializeField] private VehicleLoadout vehicleLoadout;

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
        [Tooltip("How fast a ball leaves the muzzle. Low on purpose: the aim already solves a "
                 + "ballistic arc, and above roughly 40 units per second the solution is so flat "
                 + "and the flight so short that the parabola cannot be seen at all.")]
        [SerializeField] private float projectileSpeed = 22f;
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
        /// Warms the ball queues for a run. Called when the run starts rather than in Awake,
        /// because what is worth holding is a property of the pick the player just made: warming
        /// the whole budget of every kind would build far more balls than a run can ever fire.
        /// </summary>
        public void PrepareForRun()
        {
            if (projectilePool == null)
            {
                return;
            }

            if (bulletInventory != null && bulletLoadout != null && !bulletInventory.IsEmpty)
            {
                foreach (KeyValuePair<string, int> entry in bulletInventory.Counts)
                {
                    if (entry.Value <= 0)
                    {
                        continue;
                    }

                    BulletDefinition bullet = bulletLoadout.Find(entry.Key);
                    projectilePool.Warm(ResolveProjectilePrefab(bullet), entry.Value);
                }

                return;
            }

            // No pick to read - the scene's demo path, where the cannon fires freely. Falls back
            // to the old budget-sized warm of the one serialized prefab so that path still gets
            // a warm queue rather than paying for each ball at the moment the player taps.
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

        /// <summary>
        /// Raised once per shot actually fired, after it is paid for and after it has left the
        /// muzzle, for one-shot UI like the tutorial's prompt. A shot refused for want of
        /// ammunition raises <see cref="OutOfAmmunition"/> instead, and one refused by the aim or
        /// by an empty projectile pool raises neither: nothing left the cannon, so there is
        /// nothing for a listener to answer.
        /// </summary>
        public event System.Action Fired;

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

        /// <summary>
        /// Names a refused tap for whoever is building the level. Widened from editor-only to
        /// development builds so the reason travels with a test build. Note that this path is the
        /// aim plane's bounds check - a tap at empty sky - and never a ballistic one: the solver
        /// always answers with its best direction, so a target out of the cannon's reach is
        /// reported by <see cref="CannonBallisticAimMath"/> itself rather than here.
        /// </summary>
        private static void LogAimRejected(AimRejectReason rejectReason)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
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
            GridKnockdownCannonProjectile projectile = RentProjectile(
                ResolveProjectilePrefab(ammunition),
                spawnPosition,
                spawnRotation,
                direction);
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

            // Read at the moment of firing rather than cached in Awake: the player changes
            // vehicle in the shop between runs, and the cannon is not re-enabled in between.
            if (vehicleLoadout != null)
            {
                projectile.SetDamageMultiplier(vehicleLoadout.SelectedDamageMultiplier);
            }

            projectile.Launch(direction, projectileSpeed, projectileLifetime);
            if (shotPresenter != null)
            {
                shotPresenter.PlayShot();
            }

            // Beside the muzzle flash rather than inside the presenter: the presenter is optional
            // and a scene without one should still be heard firing. Past every early return above,
            // so a shot that was refused for want of ammunition stays silent.
            AudioService.Play(AudioSlot.Fire);

            // Last, so a listener that tears something down cannot run before the shot it is
            // answering has actually been launched and presented.
            Fired?.Invoke();
        }

        /// <summary>
        /// The ball this ammunition flies as. An ammunition with none configured falls back to
        /// this cannon's own prefab, so a bullet added to the config before the artist has made
        /// its projectile still shoots something.
        /// </summary>
        private GridKnockdownCannonProjectile ResolveProjectilePrefab(BulletDefinition ammunition)
        {
            return ammunition != null && ammunition.ProjectilePrefab != null
                ? ammunition.ProjectilePrefab
                : projectilePrefab;
        }

        /// <summary>
        /// The pool first, then a plain instantiate of the prefab, then the built-in sphere. The
        /// last of those is the scene's own fallback for having no prefab at all, so it is never
        /// pooled: it is a debugging aid, not something a run is played with.
        /// </summary>
        private GridKnockdownCannonProjectile RentProjectile(
            GridKnockdownCannonProjectile prefab,
            Vector3 spawnPosition,
            Quaternion spawnRotation,
            Vector3 direction)
        {
            // The HasPrefab gate the pool used to be asked for is gone: the pool is keyed now and
            // answers null for a kind it cannot make, which is the same refusal one call earlier.
            if (projectilePool != null)
            {
                GridKnockdownCannonProjectile pooled = projectilePool.Rent(
                    prefab,
                    spawnPosition,
                    spawnRotation,
                    projectileParent);
                if (pooled != null)
                {
                    return pooled;
                }
            }

            if (prefab != null)
            {
                return Instantiate(prefab, spawnPosition, spawnRotation, projectileParent);
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
