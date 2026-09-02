using GameJam.Gameplay.Cannon;
using GameJam.Gameplay.Flow;
using UnityEngine;
using UnityEngine.UI;

namespace GameJam.UI
{
    /// <summary>
    /// The HUD face of the fire controller's reload gate: a slim bar low on the screen that
    /// fills while the cannon reloads and disappears the moment it is ready.
    ///
    /// It only appears at all when the equipped vehicle level authors a reloadSeconds above 0
    /// (VehicleDefinition.Level.reloadSeconds), because a 0-second reload never arms the gate.
    /// The bar draws nothing while ready on purpose - a permanently full bar is one more HUD
    /// element to ignore, while an appearing one reads as "wait".
    ///
    /// Built in code at first show - same stopgap pattern as the fail-screen stepper and the
    /// drag hint; delete this file and its scene component to remove the feature. The fill is
    /// anchor-driven (anchorMax.x), not Image.Filled, so it needs no sprite.
    /// </summary>
    public sealed class ReloadBarController : MonoBehaviour
    {
        [SerializeField] private GameFlowController flow;
        [SerializeField] private GridKnockdownCannonFireController fireController;

        [Tooltip("The gameplay canvas the bar is built under.")]
        [SerializeField] private RectTransform barParent;

        [Tooltip("Screen-height fraction the bar sits at, from the bottom. Tune to taste.")]
        [SerializeField] private float screenHeightAnchor = 0.075f;

        private RectTransform barRoot;
        private RectTransform fillRect;

        private void Update()
        {
            bool playing = flow != null && flow.State == GameFlowController.GameState.Playing;
            bool reloading = playing
                && fireController != null
                && fireController.IsReloading
                && fireController.ReloadDuration > 0f;

            if (!reloading)
            {
                if (barRoot != null && barRoot.gameObject.activeSelf)
                {
                    barRoot.gameObject.SetActive(false);
                }

                return;
            }

            EnsureBar();
            if (barRoot == null)
            {
                return;
            }

            if (!barRoot.gameObject.activeSelf)
            {
                barRoot.gameObject.SetActive(true);
            }

            float progress =
                Mathf.Clamp01(1f - fireController.ReloadRemaining / fireController.ReloadDuration);

            // Anchor-driven fill; the floor keeps the rounded ends from inverting at 0.
            fillRect.anchorMax = new Vector2(Mathf.Lerp(0.03f, 1f, progress), 1f);
        }

        private void EnsureBar()
        {
            if (barRoot != null || barParent == null)
            {
                return;
            }

            GameObject rootGo = new GameObject("ReloadBar", typeof(RectTransform));
            barRoot = (RectTransform)rootGo.transform;
            barRoot.SetParent(barParent, false);
            barRoot.anchorMin = barRoot.anchorMax = new Vector2(0.5f, screenHeightAnchor);
            barRoot.pivot = new Vector2(0.5f, 0.5f);
            barRoot.anchoredPosition = Vector2.zero;
            barRoot.sizeDelta = new Vector2(340f, 16f);

            Image back = rootGo.AddComponent<Image>();
            back.color = new Color(0f, 0f, 0f, 0.45f);
            back.raycastTarget = false;

            GameObject fillGo = new GameObject("Fill", typeof(RectTransform));
            fillRect = (RectTransform)fillGo.transform;
            fillRect.SetParent(barRoot, false);
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = new Vector2(0.03f, 1f);
            fillRect.offsetMin = new Vector2(3f, 3f);
            fillRect.offsetMax = new Vector2(-3f, -3f);

            Image fill = fillGo.AddComponent<Image>();
            fill.color = new Color(1f, 0.78f, 0.25f);      // HUD gold, matches the coin accents
            fill.raycastTarget = false;
        }
    }
}
