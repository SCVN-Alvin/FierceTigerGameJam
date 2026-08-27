using System.Collections;
using GameJam.Gameplay.Flow;
using GameJam.Gameplay.Wall;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GameJam.UI
{
    /// <summary>
    /// What a passed run came to: the map it cleared and what it paid. Only the passed half of the
    /// result lives here; a failed run has its own screen with a different question to ask.
    ///
    /// The screen asks the flow for nothing. Its three buttons are handed to
    /// <see cref="GameFlowController"/> as serialized references and driven from there, the way
    /// every other screen in the project is wired, so this view never has to know what REPLAY or
    /// CONTINUE mean.
    /// </summary>
    public sealed class ClearedScreenView : MonoBehaviour
    {
        [SerializeField] private GameFlowController flow;

        [Tooltip("Where the cleared map's picture is looked up, by the id the run reports.")]
        [SerializeField] private MapConfig mapConfig;

        [Header("Parts")]
        [Tooltip("The picture of the map just cleared. Hidden while a map has no art.")]
        [SerializeField] private Image mapImage;

        [Tooltip("Shown only when the run paid something, so a repeat clear does not display a "
                 + "reward of zero as though the player had been short changed.")]
        [SerializeField] private GameObject rewardRoot;

        [SerializeField] private TMP_Text rewardLabel;

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

        /// <summary>Fills the screen in from a finished run.</summary>
        public void Show(LevelRunController.RunResult result)
        {
            // The flow only raises this screen on a pass, but the event goes to every listener,
            // so a failure that reached this view is not this view's to draw.
            if (!result.Passed)
            {
                return;
            }

            ShowMapPicture(result.MapId);
            ShowReward(result.GoldAwarded);
        }

        /// <summary>
        /// The cleared map, when it has been drawn. An unassigned picture disables the image
        /// rather than leaving it enabled with no sprite, which Unity draws as a white block over
        /// the middle of the screen.
        /// </summary>
        private void ShowMapPicture(string mapId)
        {
            if (mapImage == null)
            {
                return;
            }

            Sprite picture = mapConfig != null && mapConfig.TryGet(mapId, out MapInfo map)
                ? map.ClearedImage
                : null;

            mapImage.sprite = picture;
            mapImage.enabled = picture != null;
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
        /// Unscaled, so the count still runs if the game is paused behind the cleared screen.
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
    }
}
