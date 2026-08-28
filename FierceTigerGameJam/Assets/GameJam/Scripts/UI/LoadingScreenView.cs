using System.Collections;
using GameJam.Config;
using GameJam.Gameplay.Flow;
using UnityEngine;
using UnityEngine.UI;

namespace GameJam.UI
{
    /// <summary>
    /// Fills the bar over the configured fake time and then hands the flow on. The time is
    /// unscaled: nothing that pauses the game should be able to freeze the splash.
    ///
    /// The view knows what comes after the splash only as "ask the flow" - it calls
    /// <see cref="GameFlowController.FinishLoading"/> and nothing else - so what the game does
    /// next can change without this screen learning about it.
    /// </summary>
    public sealed class LoadingScreenView : MonoBehaviour
    {
        [Tooltip("Told when the bar is full. Left empty - a test scene, the prefab opened on its "
                 + "own - the splash simply plays and stops.")]
        [SerializeField] private GameFlowController flow;

        [Tooltip("How long the bar takes to fill. Left empty the splash is over immediately, "
                 + "rather than hanging on a bar that never moves.")]
        [SerializeField] private LoadingConfig config;

        [Tooltip("The yellow fill, drawn Filled/Horizontal/Left so fillAmount is the progress.")]
        [SerializeField] private Image fill;

        private Coroutine fillRoutine;

        /// <summary>
        /// OnEnable owns the reset, so re-entering Loading later simply replays the bar rather
        /// than showing it already full.
        /// </summary>
        private void OnEnable()
        {
            SetFill(0f);
            fillRoutine = StartCoroutine(FillBar());
        }

        /// <summary>
        /// Paired with OnEnable and unconditional about it: entering play twice with the domain
        /// reload off must not leave a second coroutine racing the first to finish the splash.
        /// </summary>
        private void OnDisable()
        {
            if (fillRoutine != null)
            {
                StopCoroutine(fillRoutine);
                fillRoutine = null;
            }
        }

        private IEnumerator FillBar()
        {
            float seconds = config != null ? config.fakeLoadingSeconds : 0f;

            // A time of zero falls straight out of the loop with the bar full, which is what
            // makes it a legal setting rather than a divide by zero or a bar stuck at nothing.
            float elapsed = 0f;
            while (elapsed < seconds)
            {
                elapsed += Time.unscaledDeltaTime;
                SetFill(Mathf.Clamp01(elapsed / seconds));
                yield return null;
            }

            SetFill(1f);
            fillRoutine = null;

            if (flow == null)
            {
                // Once, at the end, rather than every frame: a scene with no flow is a scene
                // being used to look at the splash, and the splash is what it gets.
                Debug.LogWarning(
                    $"{name}: the loading bar is full but there is no {nameof(GameFlowController)} "
                    + "wired, so the game stays on the splash.",
                    this);
                yield break;
            }

            flow.FinishLoading();
        }

        private void SetFill(float amount)
        {
            if (fill != null)
            {
                fill.fillAmount = amount;
            }
        }
    }
}
