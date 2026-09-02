using GameJam.Gameplay.Flow;
using TMPro;
using UnityEngine;

namespace GameJam.UI
{
    /// <summary>
    /// How much of the structure has been destroyed, shown during a run.
    ///
    /// Only the number. The bar and the target readout were dropped: the run already ends by
    /// itself on a total clear, and the pass mark matters when the result is judged rather than
    /// while the player is aiming. Bullets are the counter's job.
    /// </summary>
    /// <summary>
    /// Obsolete. This existed only to write the clear percentage into the HUD, and that readout has
    /// been retired along with the remaining-bullet text and the Breakdown holder.
    ///
    /// Kept until every RunHud prefab has been rebuilt. Deleting the class while a prefab still
    /// carries the component would leave a missing-script husk on it - the same trap the wall
    /// removal hit, where deleting BreakableWall stranded twelve baked maps. UiBuilder strips the
    /// component on its next run; once that has happened everywhere, this file can go.
    /// </summary>
    public sealed class RunHudView : MonoBehaviour
    {
        [SerializeField] private LevelProgressTracker progressTracker;

        [Tooltip("Shows a whole-number percent, for example 72%.")]
        [SerializeField] private TMP_Text clearPercentLabel;

        private void OnEnable()
        {
            if (progressTracker != null)
            {
                progressTracker.ProgressChanged += HandleProgressChanged;
            }

            // Drawn once on enable so the readout is right before the first event arrives.
            Refresh();
        }

        private void OnDisable()
        {
            if (progressTracker != null)
            {
                progressTracker.ProgressChanged -= HandleProgressChanged;
            }
        }

        [ContextMenu("Refresh")]
        public void Refresh()
        {
            HandleProgressChanged(progressTracker != null ? progressTracker.ClearPercent : 0f);
        }

        private void HandleProgressChanged(float clearPercent)
        {
            if (clearPercentLabel == null)
            {
                return;
            }

            // Floored rather than rounded: reading a hundred percent while a block is still
            // standing looks like the game has lost track of the structure.
            int percent = Mathf.FloorToInt(Mathf.Clamp01(clearPercent) * 100f);
            clearPercentLabel.text = $"{percent}%";
        }
    }
}
