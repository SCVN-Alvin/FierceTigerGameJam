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
            reloadReadyAt = 0f;                            // a fresh run never starts mid-reload
            armedBoostRounds = 0;                          // nor with a stale boost charge
            armedBoostFree = false;
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
        /// Raised once per tap that actually fired, after it has left the muzzle, for one-shot UI
        /// like the tutorial's prompt. Once per tap rather than per round, so a burst counts as
        /// the single shot it reads as; and not conditional on payment, so a gifted burst is
        /// announced like any other. A shot refused for want of
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

        // Whether that charge is a gift. The intro popup hands out its Double/Triple for nothing
        // (Falcon 2026-09-03: "vi la free nen phat do ko ton dan"); a charge bought from an ad or
        // the shop later leaves this false and every round pays its own bullet as before.
        private bool armedBoostFree;

        // Which authored barrel fires next. Single shots on a multi-barrel cannon walk the
        // authored muzzleOffsets in order - left then right, top then bottom, exactly as the
        // asset lists them - instead of always leaving barrel one (Falcon 2026-09-02: shots
        // looked like they had no fixed origin). A burst advances it by its round count so
        // the rotation stays continuous, and every run starts back at barrel one.
        private int muzzleCycle;

        /// <summary>
        /// Arm the NEXT shot to fire this many rounds (2 = Double, 3 = Triple). Pass
        /// <paramref name="freeAmmo"/> for a charge that was given away rather than bought: the
        /// whole burst then costs nothing. It only applies to a real burst, so a "free single" is
        /// still an ordinary paid shot rather than a way to fire with an empty pouch.
        /// </summary>
        public void ArmShotBoost(int rounds, bool freeAmmo = false)
        {
            armedBoostRounds = Mathf.Clamp(rounds, 0, 3);
            armedBoostFree = freeAmmo && armedBoostRounds > 1;
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
        /// The aimed tap, whole: how many rounds it fires, what each of them costs, and the
        /// reload it starts. One round is the shipping default; a Double/Triple charge makes it
        /// two or three, spread over time rather than fired together.
        ///
        /// Falcon's burst structure and the fixed-apex arc compose rather than compete - the
        /// burst decides how many shots and what they cost, the arc decides the velocity of each
        /// one - so both are kept.
        /// </summary>
        private void FireAtAimPoint(Vector3 muzzlePosition, Vector3 aimPoint)
        {
            // An armed Double/Triple charge outranks everything: its rounds each pay a bullet,
            // unless the charge was a gift, in which case the whole burst is free.
            // Otherwise the retired per-level burst (if switched back on) fires level rounds on
            // one bullet; the shipping default is a plain single.
            int rounds;
            bool spendPerRound;
            bool freeCharge = false;
            if (armedBoostRounds > 1)
            {
                rounds = armedBoostRounds;
                freeCharge = armedBoostFree;
                spendPerRound = !freeCharge;
            }
            else
            {
                rounds = burstPerVehicleLevel && vehicleLoadout != null
                    ? Mathf.Max(1, vehicleLoadout.SelectedLevel)
                    : 1;
                spendPerRound = false;
            }

            armedBoostRounds = 0;                           // one tap consumes the charge
            armedBoostFree = false;
            float share = 1f / rounds;
            Vector3[] spawns = ResolveBurstSpawns(muzzlePosition, aimPoint, rounds);

            // Announced on the first round whether or not it was paid for. Announcing and paying
            // used to be the same flag, which would have left a gifted burst firing silently -
            // and the tutorial, which counts taps off this event, never seeing the shot.
            if (!FireRound(spawns[0], aimPoint, share, !freeCharge, true))
            {
                return;
            }

            muzzleCycle += rounds;                          // the paid tap used these barrels

            // The tap is paid: start this level's reload. Burst rounds after the first belong
            // to the same shot and are never gated.
            VehicleDefinition equipped = vehicleLoadout != null ? vehicleLoadout.Selected : null;
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

                burstRoutine = StartCoroutine(FireBurstRest(spawns, aimPoint, share, spendPerRound));
            }
        }

        /// <summary>
        /// One round of a tap, solved and launched at the moment it leaves.
        ///
        /// The arc is solved per round from THAT round's barrel, not once from the centre. The
        /// branch this came from solved once and yawed each offset round's heading to converge on
        /// the tap; that fixed the sideways drift but, as its own comment admitted, left a stacked
        /// barrel's height offset in. Solving from the barrel removes both at once, because the
        /// solve's whole promise is that the shot lands on the point it was given, wherever it
        /// started. That is why the yaw helper is gone rather than merged.
        ///
        /// Solved twice, as the single shot always was: the ball does not leave the barrel point
        /// itself, it is pushed forward along its own heading first, and re-solving from where it
        /// really starts is what keeps that offset from becoming a miss.
        /// </summary>
        private bool FireRound(
            Vector3 barrelPosition,
            Vector3 aimPoint,
            float damageShare,
            bool spendAmmo,
            bool announceShot)
        {
            BulletDefinition ammunition = ResolveShotAmmunition();
            GridKnockdownCannonProjectile prefab = ResolveProjectilePrefab(ammunition);
            float gravity = ResolveLaunchGravity(prefab);

            Vector3 launchVelocity = CannonBallisticAimMath.GetLobVelocity(
                barrelPosition,
                aimPoint,
                apexHeight,
                gravity);

            Vector3 spawnPosition = barrelPosition;
            if (muzzleSpawnOffset > 0f && launchVelocity.sqrMagnitude >= MinFireDirectionSqrMagnitude)
            {
                spawnPosition = barrelPosition + (launchVelocity.normalized * muzzleSpawnOffset);
                launchVelocity = CannonBallisticAimMath.GetLobVelocity(
                    spawnPosition,
                    aimPoint,
                    apexHeight,
                    gravity);
            }

            return Fire(
                ammunition, prefab, spawnPosition, launchVelocity, damageShare, spendAmmo, announceShot);
        }

        /// <summary>
        /// The rounds after the first, one every <see cref="burstInterval"/> seconds - they leave
        /// in order, never together. Each solves its own arc as it fires, so the burst walks the
        /// barrels instead of copying one velocity across all three.
        /// </summary>
        private System.Collections.IEnumerator FireBurstRest(
            Vector3[] spawns, Vector3 aimPoint, float share, bool spendPerRound)
        {
            for (int i = 1; i < spawns.Length; i++)
            {
                yield return new WaitForSeconds(burstInterval);

                // A boosted round pays for itself; when the pouch runs dry mid-burst, FireRound
                // refuses and the remaining rounds simply never leave.
                FireRound(spawns[i], aimPoint, share, spendPerRound, false);
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

        /// <summary>
        /// One spawn point per round, matched to the equipped model's barrels. Authored offsets on
        /// the vehicle level win outright - two stacked barrels are (0,+y)(0,-y), two side-by-side
        /// are (-x,0)(+x,0), in whatever order the rounds should leave. Without authoring the
        /// spread is symmetric about the centre: a two-round burst takes half a spacing to each
        /// side (never centre-plus-one-side, which read as a three-barrel cannon missing one), a
        /// three-round burst takes centre, left, right.
        ///
        /// The sideways axis is taken from the horizontal line to the tap rather than from a
        /// launch heading. The lob leaves steeply, and hanging the barrel spread off that heading
        /// would roll a side-by-side pair further towards vertical the steeper the shot got.
        /// </summary>
        private Vector3[] ResolveBurstSpawns(Vector3 muzzlePosition, Vector3 aimPoint, int rounds)
        {
            Vector3 heading = aimPoint - muzzlePosition;
            heading.y = 0f;
            heading = heading.sqrMagnitude >= MinFireDirectionSqrMagnitude
                ? heading.normalized
                : Vector3.forward;

            Vector3 right = Vector3.Cross(Vector3.up, heading).normalized;

            VehicleDefinition vehicle = vehicleLoadout != null ? vehicleLoadout.Selected : null;
            Vector2[] authored = vehicle != null
                ? vehicle.ResolveMuzzleOffsets(vehicleLoadout.SelectedLevel)
                : null;

            Vector3[] spawns = new Vector3[rounds];
            for (int i = 0; i < rounds; i++)
            {
                Vector2 offset;
                if (authored != null && authored.Length > 0)
                {
                    // The cycle makes single taps alternate barrels; inside one burst the rounds
                    // still walk the barrels in authored order from wherever the cycle stands,
                    // wrapping if the burst outnumbers the barrels.
                    offset = authored[(muzzleCycle + i) % authored.Length];
                }
                else if (rounds == 2)
                {
                    offset = new Vector2((i == 0 ? -0.5f : 0.5f) * burstMuzzleSpacing, 0f);
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

                spawns[i] = muzzlePosition + (right * offset.x) + (Vector3.up * offset.y);
            }

            return spawns;
        }

        /// <summary>
        /// The unaimed shot, kept for the wiring that has no aim controller at all: a straight
        /// push at <see cref="projectileSpeed"/> along the given heading, with no promise about
        /// where it comes down. Single by design - a burst needs an aim point to solve each round
        /// against, and this path has none.
        /// </summary>
        private void FireInDirection(Vector3 muzzlePosition, Vector3 direction)
        {
            BulletDefinition ammunition = ResolveShotAmmunition();
            Fire(
                ammunition,
                ResolveProjectilePrefab(ammunition),
                muzzlePosition + (direction * muzzleSpawnOffset),
                direction * projectileSpeed,
                1f,
                true,
                true);
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

        /// <summary>
        /// Spawns and launches one ball. Returns false when the shot was refused - no bullet to
        /// pay with, or nothing to rent - which is what stops a burst that outruns the pouch.
        /// </summary>
        private bool Fire(
            BulletDefinition ammunition,
            GridKnockdownCannonProjectile prefab,
            Vector3 spawnPosition,
            Vector3 launchVelocity,
            float damageShare,
            bool spendAmmo,
            bool announceShot)
        {
            if (spendAmmo && ammunition != null && !bulletInventory.TrySpend(ammunition.Id))
            {
                return false;
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
            // answering has actually been launched and presented. Once per TAP: the burst's
            // follow-up rounds are the same logical shot, and a listener counting shots (the
            // tutorial) must not count one tap three times. Deliberately not tied to whether the
            // round was paid for - a gifted burst is still a shot that happened.
            if (announceShot)
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
