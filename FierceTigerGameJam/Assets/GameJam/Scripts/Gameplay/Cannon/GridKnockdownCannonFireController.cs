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
        [Tooltip("Legacy fallback only. Used when there is no aim controller to ask for a tapped "
                 + "point, which is the bare demo wiring; an aimed shot solves its own speed from "
                 + "the arc below and ignores this.")]
        [SerializeField] private float projectileSpeed = 22f;

        [Tooltip("How far above the higher of the muzzle and the tapped point the shot crests. "
                 + "This is what makes every shot arc: raise it for a loopier lob, lower it for a "
                 + "flatter rocket. The impact point does not move either way. 0.8 is matched to "
                 + "the flight this replaced - the old fixed-speed solve crested about 1.2 above "
                 + "the muzzle on a mid-structure shot, and this lands near that while still "
                 + "guaranteeing a visible arc on the flat close shots that used to be dead level.")]
        [SerializeField] private float apexHeight = 0.8f;
        [SerializeField] private float projectileLifetime = 5f;
        [SerializeField] private float muzzleSpawnOffset = 0.28f;

        private const float MinFireDirectionSqrMagnitude = 0.0001f;
        private const int FallbackPoolSize = 10;

        /// <summary>
        /// How nearly straight up a launch has to be before the spawn rotation needs a different
        /// up axis to be defined at all.
        /// </summary>
        private const float NearlyVerticalHeading = 0.999f;

        /// <summary>Half a metre of crest: the flattest arc still worth calling one.</summary>
        private const float MinApexHeight = 0.5f;

        /// <summary>
        /// Keeps the crest off the floor. The arc is solved for the shape, so the flatter it is
        /// asked to be the faster the shot has to go: at zero the maths still answers, with a
        /// flight measured in a couple of frames and a speed in the hundreds. This is where that
        /// is refused, rather than leaving a designer to wonder why one field emptied the arc out
        /// of the game.
        /// </summary>
        private void OnValidate()
        {
            apexHeight = Mathf.Max(MinApexHeight, apexHeight);
            projectileSpeed = Mathf.Max(0f, projectileSpeed);
            projectileLifetime = Mathf.Max(0f, projectileLifetime);
            muzzleSpawnOffset = Mathf.Max(0f, muzzleSpawnOffset);
        }

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

                // The tapped point itself, not a heading. The arc is solved to land on it, so what
                // the shot needs is where the player pointed rather than which way the barrel is.
                if (!aimController.HasValidAimPoint)
                {
                    return false;
                }

                Vector3 aimedMuzzlePosition = fireOrigin != null ? fireOrigin.position : transform.position;
                FireAtAimPoint(aimedMuzzlePosition, aimController.LastAimWorldPoint);
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

            FireInDirection(muzzlePosition, fireDirection.normalized);
            return true;
        }

        /// <summary>
        /// Names a refused tap for whoever is building the level. Widened from editor-only to
        /// development builds so the reason travels with a test build. This is the only way a tap
        /// is refused now, and it is the aim plane's bounds check - a tap at empty sky. There is no
        /// ballistic refusal left to report: the fixed-apex solve reaches every point, however far
        /// or high, so nothing inside the arc maths can decline a shot.
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

        /// <summary>
        /// The aimed shot: a rocket arc that crests <see cref="apexHeight"/> above the higher of
        /// the muzzle and the tap and comes down on the tap itself. Solved twice, as the old
        /// fixed-speed path was, because the ball does not actually leave the muzzle point - it is
        /// pushed forward along its own heading first, and re-solving from where it really starts
        /// is what keeps that offset from becoming a miss.
        /// </summary>
        private void FireAtAimPoint(Vector3 muzzlePosition, Vector3 aimPoint)
        {
            BulletDefinition ammunition = ResolveShotAmmunition();
            GridKnockdownCannonProjectile prefab = ResolveProjectilePrefab(ammunition);
            float gravity = ResolveLaunchGravity(prefab);

            Vector3 launchVelocity = CannonBallisticAimMath.GetLobVelocity(
                muzzlePosition,
                aimPoint,
                apexHeight,
                gravity);

            Vector3 spawnPosition = muzzlePosition;
            if (muzzleSpawnOffset > 0f && launchVelocity.sqrMagnitude >= MinFireDirectionSqrMagnitude)
            {
                spawnPosition = muzzlePosition + (launchVelocity.normalized * muzzleSpawnOffset);
                launchVelocity = CannonBallisticAimMath.GetLobVelocity(
                    spawnPosition,
                    aimPoint,
                    apexHeight,
                    gravity);
            }

            Fire(ammunition, prefab, spawnPosition, launchVelocity);
        }

        /// <summary>
        /// The unaimed shot, kept for the wiring that has no aim controller at all: a straight
        /// push at <see cref="projectileSpeed"/> along the given heading, with no promise about
        /// where it comes down.
        /// </summary>
        private void FireInDirection(Vector3 muzzlePosition, Vector3 direction)
        {
            BulletDefinition ammunition = ResolveShotAmmunition();
            Fire(
                ammunition,
                ResolveProjectilePrefab(ammunition),
                muzzlePosition + (direction * muzzleSpawnOffset),
                direction * projectileSpeed);
        }

        /// <summary>
        /// The gravity the arc must be solved against: the world's, times what the shot will
        /// multiply it by in flight. Read off the prefab rather than the instance because the
        /// instance does not exist until the spawn point is known, and the spawn point depends on
        /// this answer - a pooled ball is a copy of that prefab, so the two agree. A shot with no
        /// prefab at all falls back to the same default the projectile's own field carries, which
        /// is what the built-in debugging sphere will come up with.
        /// </summary>
        private static float ResolveLaunchGravity(GridKnockdownCannonProjectile prefab)
        {
            float multiplier = prefab != null
                ? prefab.GravityMultiplier
                : GridKnockdownCannonProjectile.DefaultGravityMultiplier;

            // The magnitude, so the number matches what the projectile applies along
            // Physics.gravity itself. Both assume that vector points straight down; tilting it
            // project-wide would need the solve rewritten in gravity's own frame.
            return Physics.gravity.magnitude * multiplier;
        }

        private void Fire(
            BulletDefinition ammunition,
            GridKnockdownCannonProjectile prefab,
            Vector3 spawnPosition,
            Vector3 launchVelocity)
        {
            if (ammunition != null && !bulletInventory.TrySpend(ammunition.Id))
            {
                return;
            }

            Vector3 direction = launchVelocity.sqrMagnitude >= MinFireDirectionSqrMagnitude
                ? launchVelocity.normalized
                : Vector3.forward;

            // LookRotation is undefined for a heading parallel to its up axis, which only a shot
            // solved as straight up can produce.
            Quaternion spawnRotation = Mathf.Abs(direction.y) > NearlyVerticalHeading
                ? Quaternion.LookRotation(direction, Vector3.forward)
                : Quaternion.LookRotation(direction, Vector3.up);

            GridKnockdownCannonProjectile projectile = RentProjectile(
                prefab,
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

            // The velocity, not a heading and a speed: how fast this shot leaves is part of the
            // answer the arc was solved for, and rounding it back to a direction would throw that
            // away.
            projectile.Launch(launchVelocity, projectileLifetime);
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
