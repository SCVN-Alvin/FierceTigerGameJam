using System.Collections;
using GameJam.Gameplay.Flow;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GameJam.UI
{
    /// <summary>
    /// What the run came to: how much of the structure went down, whether that passed, and what
    /// it paid.
    ///
    /// A failed run is framed as running out of moves rather than out of ammunition. A move
    /// budget explains itself, where an empty cannon invites the question of why it cannot simply
    /// be reloaded.
    /// </summary>
    public sealed class RunResultView : MonoBehaviour
    {
        [SerializeField] private GameFlowController flow;

        [Header("Labels")]
        [SerializeField] private TMP_Text headlineLabel;
        [SerializeField] private TMP_Text clearPercentLabel;
        [SerializeField] private TMP_Text detailLabel;
        [SerializeField] private TMP_Text rewardLabel;

        [Header("Optional")]
        [Tooltip("Shown only when the run paid something, so a repeat clear does not display a "
                 + "reward of zero as though the player had been short changed.")]
        [SerializeField] private GameObject rewardRoot;

        [SerializeField] private Image clearProgressFill;

        [Header("Text")]
        [SerializeField] private string passedHeadline = "LEVEL CLEAR";
        [SerializeField] private string fullyClearedHeadline = "PERFECT CLEAR";
        [SerializeField] private string failedHeadline = "OUT OF MOVES";

        [Tooltip("Seconds the reward number takes to count up. Counting it up is what makes the "
                 + "reward land; zero shows it immediately.")]
        [SerializeField] private float rewardCountUpSeconds = 0.55f;

        private Coroutine countUpRoutine;

        private void OnEnable()
        {
            if (flow != null)
            {
                flow.RunFinished += Show;
            }
        }

        private void OnDisable()
        {
            if (flow != null)
            {
                flow.RunFinished -= Show;
            }

            StopCountUp();
        }

        /// <summary>Fills the panel in from a finished run.</summary>
        public void Show(LevelRunController.RunResult result)
        {
            SetText(headlineLabel, ResolveHeadline(result));

            // Floored rather than rounded: showing a hundred percent for a run that left a block
            // standing reads as the game losing track, not as generosity.
            int percent = Mathf.FloorToInt(Mathf.Clamp01(result.ClearPercent) * 100f);
            SetText(clearPercentLabel, $"{percent}%");
            SetText(detailLabel, ResolveDetail(result, percent));

            if (clearProgressFill != null)
            {
                clearProgressFill.fillAmount = Mathf.Clamp01(result.ClearPercent);
            }

            ShowReward(result.GoldAwarded);
        }

        private string ResolveHeadline(LevelRunController.RunResult result)
        {
            if (result.FullyCleared)
            {
                return fullyClearedHeadline;
            }

            return result.Passed ? passedHeadline : failedHeadline;
        }

        /// <summary>
        /// Tells the player what to do differently. A near miss is worth naming, and a pass short
        /// of a hundred percent is the hook to come back.
        /// </summary>
        private string ResolveDetail(LevelRunController.RunResult result, int percent)
        {
            if (result.FullyCleared)
            {
                return "Nothing left standing.";
            }

            if (result.Passed)
            {
                return "Clear it completely for a bigger reward.";
            }

            return "Not enough came down. Try a different mix of ammunition.";
        }

        private void ShowReward(int gold)
        {
            bool paid = gold > 0;
            if (rewardRoot != null)
            {
                rewardRoot.SetActive(paid);
            }

            if (rewardLabel == null)
            {
                return;
            }

            StopCountUp();

            if (!paid || rewardCountUpSeconds <= 0f)
            {
                rewardLabel.text = paid ? $"+{gold}" : string.Empty;
                return;
            }

            countUpRoutine = StartCoroutine(CountUp(gold));
        }

        /// <summary>
        /// Unscaled, so the count still runs if the game is paused behind the result panel.
        /// </summary>
        private IEnumerator CountUp(int gold)
        {
            float elapsed = 0f;
            while (elapsed < rewardCountUpSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                int shown = Mathf.FloorToInt(Mathf.Lerp(0f, gold, elapsed / rewardCountUpSeconds));
                rewardLabel.text = $"+{shown}";
                yield return null;
            }

            rewardLabel.text = $"+{gold}";
            countUpRoutine = null;
        }

        private void StopCountUp()
        {
            if (countUpRoutine != null)
            {
                StopCoroutine(countUpRoutine);
                countUpRoutine = null;
            }
        }

        private static void SetText(TMP_Text label, string value)
        {
            if (label != null)
            {
                label.text = value;
            }
        }
    }
}
