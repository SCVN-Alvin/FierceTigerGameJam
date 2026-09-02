using System;
using System.Collections;
using GameJam.Config;
using GameJam.Data;
using GameJam.Economy;
using GameJam.Gameplay.Cannon;
using GameJam.Gameplay.Combat;
using GameJam.Gameplay.Wall;
using UnityEngine;

namespace GameJam.Gameplay.Flow
{
    /// <summary>
    /// One attempt at a map, from choosing what ammunition to bring through to being paid for it.
    ///
    /// The run ends when the player has no bullets left, not when the last one is fired: the shot
    /// that empties the inventory may still be bringing half the structure down, and judging it
    /// immediately would rob the player of the collapse they just paid for.
    /// </summary>
    public sealed class LevelRunController : MonoBehaviour
    {
        public enum RunState
        {
            /// <summary>Before a map is chosen, or between runs.</summary>
            Idle,

            /// <summary>Choosing what ammunition to bring.</summary>
            Picking,

            Playing,

            /// <summary>Out of ammunition, waiting for the structure to stop moving.</summary>
            Settling,

            Finished,
        }

        [SerializeField] private MapSelection mapSelection;
        [SerializeField] private MapProgressionConfig progressionConfig;
        [SerializeField] private BulletInventory bulletInventory;
        [SerializeField] private LevelProgressTracker progressTracker;
        [SerializeField] private EconomyService economy;
        [SerializeField] private GridKnockdownCannonFireController fireController;

        [Header("Settling")]
        [Tooltip("Always wait at least this long after the last shot, so a collapse that has not "
                 + "started yet still gets its chance.")]
        [SerializeField] private float minimumSettleSeconds = 1.3f;

        [Tooltip("Then wait until nothing more has fallen for this long.")]
        [SerializeField] private float stillnessSeconds = 0.75f;

        [Tooltip("Give up waiting after this, so a block rolling forever cannot hang the run.")]
        [SerializeField] private float maximumSettleSeconds = 8f;

        [Tooltip("Pause after the last block goes before the result appears. Long enough to see "
                 + "the structure finish falling, short enough not to feel like a hang.")]
        [SerializeField] private float fullClearSettleSeconds = 0.6f;

        /// <summary>Raised with the run's result once it has been judged and paid.</summary>
        public event Action<RunResult> Finished;

        public event Action<RunState> StateChanged;

        public RunState State { get; private set; } = RunState.Idle;

        /// <summary>How much of this map the player must destroy to pass it.</summary>
        public float RequiredClearPercent { get; private set; } = 0.8f;

        /// <summary>How many bullets this map lets the player bring, across all types.</summary>
        public int BulletPickLimit { get; private set; }

        /// <summary>What one attempt came to.</summary>
        public struct RunResult
        {
            public string MapId;
            public float ClearPercent;
            public bool Passed;
            public bool FullyCleared;

            /// <summary>Gold actually handed over for this attempt, zero on a repeat.</summary>
            public int GoldAwarded;
        }

        private Coroutine settleRoutine;

        private void OnEnable()
        {
            if (bulletInventory != null)
            {
                bulletInventory.Emptied += HandleInventoryEmptied;
            }

            if (progressTracker != null)
            {
                progressTracker.ProgressChanged += HandleProgressChanged;
            }
        }

        private void OnDisable()
        {
            if (bulletInventory != null)
            {
                bulletInventory.Emptied -= HandleInventoryEmptied;
            }

            if (progressTracker != null)
            {
                progressTracker.ProgressChanged -= HandleProgressChanged;
            }
        }

        /// <summary>
        /// Ends the run the moment there is nothing left standing. Waiting for the player to
        /// spend the rest of their ammunition on rubble is not a decision, it is a chore, and it
        /// buries the best outcome in the game under a minute of firing at nothing.
        /// </summary>
        private void HandleProgressChanged(float clearPercent)
        {
            if (State != RunState.Playing || clearPercent < 1f)
            {
                return;
            }

            StopSettling();
            settleRoutine = StartCoroutine(FinishAfterFullClear());
        }

        /// <summary>
        /// A short beat rather than the usual settle. Nothing is left to fall, so there is nothing
        /// to wait for, but cutting to the result on the same frame as the last block hides the
        /// moment the player just earned.
        /// </summary>
        private IEnumerator FinishAfterFullClear()
        {
            SetState(RunState.Settling);
            yield return new WaitForSeconds(Mathf.Max(0f, fullClearSettleSeconds));
            settleRoutine = null;
            Judge();
        }

        /// <summary>
        /// Opens the pick for the selected map, reading its budget and its pass bar. Called when
        /// the player commits to a map and before the structure is built.
        /// </summary>
        public void BeginPick()
        {
            ResolveMapRules();

            if (bulletInventory != null)
            {
                bulletInventory.BeginPick(BulletPickLimit);
            }

            SetState(RunState.Picking);
        }

        /// <summary>
        /// Starts the attempt. The structure must already be built, because what counts as a
        /// hundred percent is what actually got placed.
        /// </summary>
        public void BeginRun()
        {
            if (progressTracker != null)
            {
                progressTracker.BeginRun();
            }

            SetState(RunState.Playing);

            // A player who brought nothing has already lost, and nothing will raise Emptied for
            // them because nothing will ever be spent.
            if (bulletInventory != null && bulletInventory.IsEmpty)
            {
                HandleInventoryEmptied();
            }
        }

        /// <summary>Only a judged run can be continued; anything else has nothing to come back from.</summary>
        public bool CanContinueRun()
        {
            return State == RunState.Finished && bulletInventory != null;
        }

        /// <summary>
        /// Picks the run back up where it stopped. The structure is left as it is and the tracker
        /// keeps counting, so a continue is worth exactly the rounds it adds. The attempt Judge
        /// recorded stays recorded; when these rounds run out the run is judged again, on top of it.
        /// </summary>
        public bool ContinueRun(string bulletId, int amount)
        {
            if (!CanContinueRun() || string.IsNullOrEmpty(bulletId) || amount <= 0)
            {
                return false;
            }

            bulletInventory.Grant(bulletId, amount);

            // HandleInventoryEmptied and the full-clear path both refuse to act outside Playing, so
            // this is what re-arms the losing condition for the rounds just bought.
            SetState(RunState.Playing);
            return true;
        }

        /// <summary>Abandons the attempt without judging it, for a Back button mid-run.</summary>
        public void CancelRun()
        {
            StopSettling();
            SetState(RunState.Idle);
        }

        private void HandleInventoryEmptied()
        {
            if (State != RunState.Playing)
            {
                return;
            }

            StopSettling();
            settleRoutine = StartCoroutine(SettleThenJudge());
        }

        /// <summary>
        /// Waits for the structure to stop changing rather than for a fixed time. Progress not
        /// moving is exactly the question being asked, so it is a better test of "settled" than
        /// polling every rigidbody, and it costs nothing extra.
        /// </summary>
        private IEnumerator SettleThenJudge()
        {
            SetState(RunState.Settling);

            yield return new WaitForSeconds(minimumSettleSeconds);

            float deadline = Time.time + Mathf.Max(0f, maximumSettleSeconds - minimumSettleSeconds);
            float lastPercent = CurrentClearPercent();
            float stillSince = Time.time;

            while (Time.time < deadline)
            {
                yield return new WaitForSeconds(0.25f);

                float percent = CurrentClearPercent();
                if (!Mathf.Approximately(percent, lastPercent))
                {
                    lastPercent = percent;
                    stillSince = Time.time;
                    continue;
                }

                if (Time.time - stillSince >= stillnessSeconds)
                {
                    break;
                }
            }

            settleRoutine = null;
            Judge();
        }

        private void Judge()
        {
            string mapId = ResolveMapId();
            float clearPercent = CurrentClearPercent();

            RunResult result = new RunResult
            {
                MapId = mapId,
                ClearPercent = clearPercent,
                Passed = clearPercent >= RequiredClearPercent,
                FullyCleared = clearPercent >= UserMapProgressData.ClearRewardPercent,
                GoldAwarded = 0,
            };

            if (!string.IsNullOrEmpty(mapId))
            {
                MapAttemptResult attempt = UserData.Maps.RegisterAttempt(mapId, clearPercent, RequiredClearPercent);
                result.Passed = attempt.Passed;
                result.FullyCleared = attempt.FullyCleared;
                result.GoldAwarded = GrantRewards(mapId, attempt);
                UserData.Save();
            }

            SetState(RunState.Finished);
            Finished?.Invoke(result);
        }

        /// <summary>
        /// Pays for what this attempt newly achieved. The claim is recorded only once the gold has
        /// actually been handed over, so a run interrupted between earning and being paid can
        /// still be paid next time rather than losing the reward silently.
        /// </summary>
        /// <summary>One shared RewardConfig entry pays every map's 2-star bonus.</summary>
        private const string TwoStarRewardId = "two_star_bonus";

        /// <summary>Paid on EVERY pass after the map's own pass reward was claimed - the small
        /// "worth replaying" trickle (Falcon 2026-09-02: 25 gold). Not claim-once by design.</summary>
        private const string ReplayPassRewardId = "replay_pass_bonus";

        private int GrantRewards(string mapId, MapAttemptResult attempt)
        {
            if (economy == null || !TryGetRules(mapId, out MapProgressionConfig.Entry rules))
            {
                return 0;
            }

            int awarded = 0;

            if (attempt.NewlyPassed && economy.TryGrantReward(rules.passMapRewardId, out int passGold))
            {
                awarded += passGold;
                UserData.Maps.MarkPassRewardClaimed(mapId);
            }
            else if (attempt.Passed
                     && !string.IsNullOrEmpty(rules.clearMapRewardId)
                     && economy.TryGrantReward(ReplayPassRewardId, out int replayGold))
            {
                // A replayed pass is never worth 0: the big pass reward stays claim-once, but
                // each repeat pays the small trickle. Maps outside the economy (tutorial) skip
                // it, same gate as the 2-star bonus.
                awarded += replayGold;
            }

            if (attempt.NewlyCleared && economy.TryGrantReward(rules.clearMapRewardId, out int clearGold))
            {
                awarded += clearGold;
                UserData.Maps.MarkClearRewardClaimed(mapId);
            }

            // The 2-star milestone pays a flat bonus from one shared reward entry; the once-only
            // is per map (twoStarRewardClaimed), like the others. The gate is clearMapRewardId:
            // the tutorial authors a pass reward (its 100-gold completion prize, shown on the
            // cleared screen like any other) but no clear reward, so it stays out of the star
            // and replay trickles.
            if (attempt.NewlyTwoStar
                && !string.IsNullOrEmpty(rules.clearMapRewardId)
                && economy.TryGrantReward(TwoStarRewardId, out int starGold))
            {
                awarded += starGold;
                UserData.Maps.MarkTwoStarRewardClaimed(mapId);
            }

            return awarded;
        }

        private void ResolveMapRules()
        {
            RequiredClearPercent = 0.8f;
            BulletPickLimit = 10;

            if (TryGetRules(ResolveMapId(), out MapProgressionConfig.Entry rules))
            {
                RequiredClearPercent = rules.requiredClearPercent;
                BulletPickLimit = rules.bulletPickLimit;
            }
        }

        private bool TryGetRules(string mapId, out MapProgressionConfig.Entry rules)
        {
            rules = null;
            return progressionConfig != null
                && !string.IsNullOrEmpty(mapId)
                && progressionConfig.TryGetMapRules(mapId, out rules);
        }

        private string ResolveMapId()
        {
            MapInfo map = mapSelection != null ? mapSelection.Selected : null;
            return map != null ? map.Id : null;
        }

        private float CurrentClearPercent()
        {
            return progressTracker != null ? progressTracker.CalculateClearPercent() : 0f;
        }

        private void StopSettling()
        {
            if (settleRoutine != null)
            {
                StopCoroutine(settleRoutine);
                settleRoutine = null;
            }
        }

        private void SetState(RunState next)
        {
            if (State == next)
            {
                return;
            }

            State = next;
            StateChanged?.Invoke(next);
        }
    }
}
