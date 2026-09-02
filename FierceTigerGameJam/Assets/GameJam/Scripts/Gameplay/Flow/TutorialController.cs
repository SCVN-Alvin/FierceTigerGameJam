using GameJam.Data;
using GameJam.Gameplay.Cannon;
using GameJam.Gameplay.Combat;
using GameJam.Gameplay.Wall;
using UnityEngine;

namespace GameJam.Gameplay.Flow
{
    /// <summary>
    /// The first-launch tutorial: one brick block, a spotlight on it and a "Tap to shoot" prompt.
    ///
    /// Everything extra the tutorial needs lives here rather than in the flow, because the run
    /// itself is an ordinary run - same build, same judge, same cleared and fail screens. This
    /// only scripts the way in (select a standalone map, take the starter rounds, skip the
    /// ammunition pick) and owns the overlay drawn over it, so the loop keeps one road into play.
    /// </summary>
    public sealed class TutorialController : MonoBehaviour
    {
        [SerializeField] private GameFlowController flow;
        [SerializeField] private LevelRunController runController;
        [SerializeField] private MapSelection mapSelection;
        [SerializeField] private BulletInventory bulletInventory;

        [Tooltip("Only read for the starter bullet's id, which is what the tutorial hands out.")]
        [SerializeField] private BulletLoadout bulletLoadout;

        [SerializeField] private GridKnockdownCannonFireController fireController;

        [Tooltip("Standalone entry, not listed in MapConfig; filled by the builder (id, name, Maps/tutorial.json).")]
        [SerializeField] private MapInfo tutorialMap;

        [SerializeField, Min(1)] private int tutorialAmmo = 3;

        [Header("Overlay")]
        [Tooltip("The whole overlay: switched off once the tutorial run is over, however it ended.")]
        [SerializeField] private GameObject overlayRoot;

        [Tooltip("The Tap to shoot panel. Dismissed by the first shot.")]
        [SerializeField] private GameObject panel;

        [Tooltip("The dim with the spotlight hole. Dismissed by the first shot, with the panel.")]
        [SerializeField] private GameObject hole;

        /// <summary>True until the player has destroyed the tutorial block at least once.</summary>
        public bool ShouldRun => !UserData.Tutorial.completed;

        /// <summary>
        /// True only while <see cref="TryStartTutorial"/> is steering the flow, so
        /// HandleMapSelected knows to stand still instead of opening the ammunition pick it would
        /// normally answer a selection with.
        /// </summary>
        public bool IsStarting { get; private set; }

        /// <summary>
        /// True while the tutorial's own run is the one in progress.
        ///
        /// Read by <see cref="GameFlowController.HandleRunFinished"/> at the TOP of that method,
        /// before it raises RunFinished - which is what clears this again, one frame's worth of
        /// call stack later, in <see cref="HandleRunFinished"/> below. Reading it any later would
        /// depend on the order the two subscribers happen to be called in.
        /// </summary>
        public bool IsRunning => running;

        /// <summary>Set for the length of the tutorial run, so a normal run cannot claim its result.</summary>
        private bool running;

        private void OnEnable()
        {
            if (fireController != null)
            {
                fireController.Fired += HandleFired;
            }

            if (flow != null)
            {
                flow.RunFinished += HandleRunFinished;
                flow.StateChanged += HandleStateChanged;
            }
        }

        private void OnDisable()
        {
            if (fireController != null)
            {
                fireController.Fired -= HandleFired;
            }

            if (flow != null)
            {
                flow.RunFinished -= HandleRunFinished;
                flow.StateChanged -= HandleStateChanged;
            }
        }

        /// <summary>
        /// Drops straight into the tutorial run, skipping the mission board and the ammunition
        /// pick. Returns false when it is not wired up enough to do that, which is the caller's
        /// cue to open the main menu instead: a launch that neither shows a tutorial nor a menu is
        /// a black screen with no way out.
        ///
        /// Returning a bool rather than the brief's void <c>StartTutorial</c> for that reason, and
        /// because a Try* that can refuse is how the rest of this codebase says so.
        /// </summary>
        public bool TryStartTutorial()
        {
            if (flow == null || runController == null || mapSelection == null || tutorialMap == null
                || tutorialMap.MapJson == null)
            {
                Debug.LogError(
                    $"{name}: the tutorial is missing its flow, run controller, map selection or map "
                    + "JSON, so it cannot be started. Run Tools > Smashdown > Build Tutorial.",
                    this);
                return false;
            }

            // Set before the flow is steered rather than after it, so a run that manages to finish
            // inside ConfirmAmmoPick is still recognised as the tutorial's rather than leaving the
            // overlay up over a result screen that has already been and gone.
            running = true;
            IsStarting = true;

            try
            {
                // The guard in HandleMapSelected keeps the flow quiet through this.
                mapSelection.Select(tutorialMap);

                // Reads the tutorial's rules row: pick limit 3, pass at 80 percent, no rewards.
                runController.BeginPick();

                string bulletId = StarterBulletId();
                if (bulletInventory != null && !bulletInventory.TryPick(bulletId, tutorialAmmo))
                {
                    // Not fatal: the run starts empty and BeginRun judges it immediately, which is
                    // the same thing a misconfigured normal run does. Said out loud because an
                    // unwinnable tutorial is otherwise a mystery.
                    Debug.LogWarning(
                        $"{name}: could not hand the player {tutorialAmmo} of \"{bulletId}\", so the "
                        + "tutorial run starts with nothing to fire.",
                        this);
                }

                // Reset, warm, build the one block, begin the run.
                flow.ConfirmAmmoPick();
            }
            finally
            {
                // In a finally because an exception on the way in would otherwise leave this true
                // for good, and every map the player ever chose afterwards would be ignored.
                IsStarting = false;
            }

            SetActive(overlayRoot, true);
            SetActive(panel, true);
            SetActive(hole, true);
            return true;
        }

        /// <summary>The first shot answers the prompt; later shots find it already gone.</summary>
        private void HandleFired()
        {
            if (!running)
            {
                return;
            }

            SetActive(panel, false);
            SetActive(hole, false);
        }

        private void HandleRunFinished(LevelRunController.RunResult result)
        {
            if (!running)
            {
                return;
            }

            // Completion is the block coming down, not the first shot: a player who quits or
            // misses every round should meet the tutorial again next launch.
            if (result.Passed)
            {
                // The 100-gold completion prize is NOT paid here: the tutorial map authors
                // passMapRewardId "tutorial_complete", so the normal reward pipeline pays it
                // claim-once and the cleared screen shows it like any map reward.
                UserData.Tutorial.completed = true;
                UserData.Save();
            }

            running = false;
            SetActive(overlayRoot, false);
        }

        /// <summary>
        /// The safety net for leaving the run any other way - abandoning it from settings, or the
        /// back button - since those never raise a result for HandleRunFinished to hear.
        /// </summary>
        private void HandleStateChanged(GameFlowController.GameState state)
        {
            if (!running
                || state == GameFlowController.GameState.Playing
                || state == GameFlowController.GameState.Result)
            {
                return;
            }

            running = false;
            SetActive(overlayRoot, false);
        }

        /// <summary>
        /// The starter bullet, which is the one kind a brand new player is guaranteed to own.
        /// Null with no loadout wired, which TryPick refuses rather than acting on.
        /// </summary>
        private string StarterBulletId()
        {
            BulletDefinition starter = bulletLoadout != null ? bulletLoadout.DefaultBullet : null;
            return starter != null ? starter.Id : null;
        }

        private static void SetActive(GameObject target, bool active)
        {
            if (target != null && target.activeSelf != active)
            {
                target.SetActive(active);
            }
        }
    }
}
