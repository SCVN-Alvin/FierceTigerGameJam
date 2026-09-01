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

        /// <summary>The ball every bullet without its own prefab flies as. The shop's preview
        /// table reads this so an unconfigured bullet still has something to photograph.</summary>
        public GridKnockdownCannonProjectile DefaultProjectilePrefab => projectilePrefab;

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

        [Header("Burst Experiment")]
        [Tooltip("RETIRED EXPERIMENT (2026-09-02), kept as a quick switch: ON = one tap fires as "
                 + "many rounds as the equipped cannon's LEVEL, damage split evenly, and ONE "
                 + "bullet pays for the whole burst. OFF (shipping default) = one tap, one "
                 + "round; multi-shot now comes only from an armed Double/Triple Shoot charge "
                 + "(ArmShotBoost - free intro popup now, ads/shop items later), where EACH "
                 + "round costs its own bullet.")]
        [SerializeField] private bool burstPerVehicleLevel = false;

        [Tooltip("Seconds between the rounds of one burst. They leave the muzzle in order, "
                 + "never together.")]
        [Min(0.02f)] [SerializeField] private float burstInterval = 0.09f;

        [Tooltip("How far, in world units, the burst's later rounds spawn to the sides of the "
                 + "centre muzzle - round one flies from the middle, round two from one side "
                 + "barrel, round three from the other. Tune it to match where the equipped "
                 + "model's outer barrels sit.")]
        [Min(0f)] [SerializeField] private float burstMuzzleSpacing = 0.35f;

        private Coroutine burstRoutine;

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
            reloadReadyAt = 0f;                            // a fresh run never starts mid-reload
            armedBoostRounds = 0;                          // nor with a stale boost charge
            muzzleCycle = 0;                               // first shot leaves barrel one (top/left)

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

        // Reload gate: a paid shot arms it for the equipped vehicle level's reloadSeconds, and
        // taps land silently until it expires. A level authored at 0 never arms it, which is
        // exactly the pre-reload fire-on-every-tap behaviour.
        private float reloadReadyAt;
        private float reloadDuration;

        /// <summary>True while the reload timer is running; taps are refused.</summary>
        public bool IsReloading => Time.time < reloadReadyAt;

        /// <summary>Seconds until the next shot is allowed; 0 = ready.</summary>
        public float ReloadRemaining => Mathf.Max(0f, reloadReadyAt - Time.time);

        /// <summary>Length of the reload currently running (or the last one), for progress bars.</summary>
        public float ReloadDuration => reloadDuration;

        // A Double/Triple Shoot charge armed by UI (the one-time free intro popup today; the
        // ads/shop items later). Consumed by the next paid tap: that tap fires this many rounds
        // with the damage split evenly, and - unlike the retired per-level burst - EACH round
        // spends its own bullet (Falcon 2026-09-02: "moi vien ton 1 dan").
        private int armedBoostRounds;

        // Which authored barrel fires next. Single shots on a multi-barrel cannon walk the
        // authored muzzleOffsets in order - left then right, top then bottom, exactly as the
        // asset lists them - instead of always leaving barrel one (Falcon 2026-09-02: shots
        // looked like they had no fixed origin). A burst advances it by its round count so
        // the rotation stays continuous, and every run starts back at barrel one.
        private int muzzleCycle;

        /// <summary>Arm the NEXT shot to fire this many rounds (2 = Double, 3 = Triple).</summary>
        public void ArmShotBoost(int rounds)
        {
            armedBoostRounds = Mathf.Clamp(rounds, 0, 3);
        }

        /// <summary>Rounds the next shot will fire if a charge is armed; 0/1 = plain single.</summary>
        public int ArmedBoostRounds => armedBoostRounds;

        public bool TryFireAtScreenPoint(Vector2 screenPosition)
        {
            // Silent on purpose - mid-reload the player sees the bar filling, and a refusal
            // popup on every eager tap would punish exactly the tap rhythm we ask for.
            if (IsReloading)
            {
                return false;
            }

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
            // An armed Double/Triple charge outranks everything: its rounds each pay a bullet.
            // Otherwise the retired per-level burst (if switched back on) fires level rounds on
            // one bullet; the shipping default is a plain single.
            int rounds;
            bool spendPerRound;
            if (armedBoostRounds > 1)
            {
                rounds = armedBoostRounds;
                spendPerRound = true;
            }
            else
            {
                rounds = burstPerVehicleLevel && vehicleLoadout != null
                    ? Mathf.Max(1, vehicleLoadout.SelectedLevel)
                    : 1;
                spendPerRound = false;
            }

            armedBoostRounds = 0;                           // one tap consumes the charge
            float share = 1f / rounds;
            Vector3[] spawns = ResolveBurstSpawns(muzzlePosition, direction, rounds);
            Vector3[] directions = ResolveBurstDirections(spawns, muzzlePosition, direction);

            if (!FireSingle(spawns[0], directions[0], share, true))
            {
                return;
            }

            muzzleCycle += rounds;                          // the paid tap used these barrels

            // The tap is paid: start this level's reload. Burst rounds after the first belong
            // to the same shot and are never gated.
            GameJam.Gameplay.Combat.VehicleDefinition equipped =
                vehicleLoadout != null ? vehicleLoadout.Selected : null;
            float reload = equipped != null
                ? equipped.ResolveReloadSeconds(vehicleLoadout.SelectedLevel)
                : 0f;
            if (reload > 0f)
            {
                reloadDuration = reload;
                reloadReadyAt = Time.time + reload;
            }

            if (rounds > 1)
            {
                if (burstRoutine != null)
                {
                    StopCoroutine(burstRoutine);
                }

                burstRoutine = StartCoroutine(FireBurstRest(spawns, directions, share, spendPerRound));
            }
        }

        /// <summary>
        /// One launch direction per round, converging on the tapped point. The ballistic solve
        /// is done once, from the central muzzle; a round leaving an offset barrel with that
        /// same direction flies a parallel arc and lands exactly its barrel offset to the side
        /// of the tap - which is the drift Falcon felt on the 2- and 3-barrel cannons. Each
        /// offset round therefore has the solved direction YAWED so its horizontal heading runs
        /// from its own barrel to the aim point: speed and launch angle are untouched, the arc
        /// is the same shape in a rotated vertical plane, and at the tapped block every round
        /// sits on the tap's vertical line. (A stacked barrel's small height offset stays - it
        /// reads as the barrel's position, not as aim error.) Without a valid aim point (demo
        /// path) everything keeps the solved direction, exactly the old behaviour.
        /// </summary>
        private Vector3[] ResolveBurstDirections(Vector3[] spawns, Vector3 muzzlePosition, Vector3 direction)
        {
            Vector3[] directions = new Vector3[spawns.Length];
            bool hasTarget = aimController != null && aimController.HasValidAimPoint;
            Vector3 target = hasTarget ? aimController.LastAimWorldPoint : Vector3.zero;

            Vector3 solvedFlat = new Vector3(direction.x, 0f, direction.z);
            for (int i = 0; i < spawns.Length; i++)
            {
                directions[i] = direction;
                if (!hasTarget || solvedFlat.sqrMagnitude < 0.0001f)
                {
                    continue;
                }

                Vector3 wantedFlat = target - spawns[i];
                wantedFlat.y = 0f;
                if (wantedFlat.sqrMagnitude < 0.0001f)
                {
                    continue;
                }

                directions[i] = Quaternion.FromToRotation(solvedFlat.normalized, wantedFlat.normalized)
                    * direction;
            }

            return directions;
        }

        /// <summary>
        /// One spawn point per round, matched to the equipped model's barrels. Authored offsets
        /// on the vehicle level win outright - two stacked barrels are (0,+y)(0,-y), two
        /// side-by-side are (-x,0)(+x,0), in whatever order the rounds should leave. Without
        /// authoring, the spread is symmetric about the centre: a two-round burst takes half a
        /// spacing to each side (never centre-plus-one-side, which read as a three-barrel
        /// cannon missing one), a three-round burst takes centre, left, right.
        /// </summary>
        private Vector3[] ResolveBurstSpawns(Vector3 muzzlePosition, Vector3 direction, int rounds)
        {
            Vector3 right = Vector3.Cross(Vector3.up, direction).normalized;
            Vector3 up = Vector3.Cross(right, direction).normalized;

            GameJam.Gameplay.Combat.VehicleDefinition vehicle =
                vehicleLoadout != null ? vehicleLoadout.Selected : null;
            Vector2[] authored = vehicle != null
                ? vehicle.ResolveMuzzleOffsets(vehicleLoadout.SelectedLevel)
                : null;

            Vector3[] spawns = new Vector3[rounds];
            for (int i = 0; i < rounds; i++)
            {
                Vector2 offset;
                if (authored != null)
                {
                    // The cycle makes single taps alternate barrels; inside one burst the
                    // rounds still walk the barrels in authored order from wherever the
                    // cycle stands, wrapping if the burst outnumbers the barrels.
                    offset = authored[(muzzleCycle + i) % authored.Length];
                }
                else if (rounds == 2)
                {
                    offset = new Vector2(
                        (i == 0 ? -0.5f : 0.5f) * burstMuzzleSpacing, 0f);
                }
                else if (rounds >= 3)
                {
                    offset = new Vector2(
                        i == 0 ? 0f : (i == 1 ? -burstMuzzleSpacing : burstMuzzleSpacing), 0f);
                }
                else
                {
                    offset = Vector2.zero;
                }

                spawns[i] = muzzlePosition + right * offset.x + up * offset.y;
            }

            return spawns;
        }

        private System.Collections.IEnumerator FireBurstRest(
            Vector3[] spawns, Vector3[] directions, float share, bool spendPerRound)
        {
            for (int i = 1; i < spawns.Length; i++)
            {
                yield return new WaitForSeconds(burstInterval);

                // A boosted round pays for itself; when the pouch runs dry mid-burst,
                // FireSingle refuses and the remaining rounds simply never leave.
                FireSingle(spawns[i], directions[i], share, spendPerRound);
            }

            burstRoutine = null;
        }

        /// <summary>Stops a burst mid-flight with its owner; rounds not yet out stay unfired.</summary>
        private void OnDisable()
        {
            if (burstRoutine != null)
            {
                StopCoroutine(burstRoutine);
                burstRoutine = null;
            }
        }

        private bool FireSingle(Vector3 muzzlePosition, Vector3 direction, float damageShare, bool spendAmmo)
        {
            Vector3 spawnPosition = muzzlePosition + direction * muzzleSpawnOffset;
            BulletDefinition ammunition = ResolveShotAmmunition();
            if (spendAmmo && ammunition != null && !bulletInventory.TrySpend(ammunition.Id))
            {
                return false;
            }

            Quaternion spawnRotation = Quaternion.LookRotation(direction, Vector3.up);
            GridKnockdownCannonProjectile projectile = RentProjectile(
                ResolveProjectilePrefab(ammunition),
                spawnPosition,
                spawnRotation,
                direction);
            if (projectile == null)
            {
                return false;
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
                projectile.SetDamageMultiplier(vehicleLoadout.SelectedDamageMultiplier * damageShare);
            }
            else if (damageShare < 1f)
            {
                projectile.SetDamageMultiplier(damageShare);
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
            // answering has actually been launched and presented. Once per PAID tap: the
            // burst's follow-up rounds are the same logical shot, and a listener counting
            // shots (the tutorial) must not count one tap three times.
            if (spendAmmo)
            {
                Fired?.Invoke();
            }

            return true;
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
