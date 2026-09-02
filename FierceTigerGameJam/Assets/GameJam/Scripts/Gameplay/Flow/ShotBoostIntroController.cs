using System.Collections;
using GameJam.Data;
using GameJam.Gameplay.Cannon;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GameJam.Gameplay.Flow
{
    /// <summary>
    /// The one-time "1 FREE" Double/Triple Shoot popup.
    ///
    /// Multi-shot is no longer what an upgraded cannon does on every tap (that experiment is
    /// retired behind GridKnockdownCannonFireController.burstPerVehicleLevel) - it is a charge,
    /// destined for the ads/shop item packages. This popup is the player's first taste: the
    /// first level entered after a cannon first reaches level 2 opens it in mid-screen, softly
    /// (fade + scale), with "1 FREE" written big so there is no doubt it costs nothing and
    /// "DOUBLE SHOOT" beneath it. Tapping the panel arms the charge on the fire controller -
    /// the next shot fires two rounds, damage split, EACH round paying its own bullet - and the
    /// popup never returns. The same again, as TRIPLE, the first level after any cannon
    /// reaches level 3.
    ///
    /// While it is open the player can neither fire nor rotate the board: the full-screen dim
    /// swallows pointer raycasts and <see cref="BlockingInput"/> backs that up inside
    /// CannonInputShooter. It waits its turn behind the drag lesson - two overlays at once is
    /// how the 09-02 overlap bug happened.
    ///
    /// Built in code at show time - the same stopgap pattern as the drag hint; delete this
    /// file, its scene component and the CannonInputShooter checks to remove it. The panel is
    /// the drag hint's "fake UXUI" again: UI_Tutorial art, paper-coloured patch over its baked
    /// words, real labels on top.
    /// </summary>
    public sealed class ShotBoostIntroController : MonoBehaviour
    {
        /// <summary>True while the popup is open; CannonInputShooter refuses press and fire.</summary>
        public static bool BlockingInput { get; private set; }

        [SerializeField] private GameFlowController flow;
        [SerializeField] private GridKnockdownCannonFireController fireController;

        [Tooltip("The gameplay canvas the popup is built under.")]
        [SerializeField] private RectTransform hintParent;

        [Tooltip("The tutorial panel art; its baked words are covered, as in the drag hint.")]
        [SerializeField] private Sprite panelSprite;

        [SerializeField] private TMP_FontAsset labelFont;

        private RectTransform popupRoot;
        private CanvasGroup popupGroup;
        private RectTransform panelRect;
        private TMP_Text titleLabel;
        private TMP_Text nameLabel;
        private Coroutine appearRoutine;
        private Coroutine revealRoutine;
        private TMP_Text tapLabel;
        private int offeredRounds;
        private bool checkedThisRun;
        private float shownAt;

        /// <summary>Taps are ignored this long after opening, so the offer is actually read
        /// before a mid-aim tap can blow through it (Falcon: 0.5s lock).</summary>
        private const float MinViewSeconds = 0.5f;

        private void Update()
        {
            bool playing = flow != null && flow.State == GameFlowController.GameState.Playing;

            if (!playing)
            {
                checkedThisRun = false;
                if (BlockingInput)
                {
                    Hide();
                }

                return;
            }

            if (BlockingInput || checkedThisRun)
            {
                return;
            }

            // Behind the drag lesson in the queue: it keeps retrying until that overlay is
            // done, then decides once for this run.
            if (DragHintController.BlockingFire)
            {
                return;
            }

            checkedThisRun = true;
            int rounds = PendingIntroRounds();
            if (rounds > 1)
            {
                Show(rounds);
            }
        }

        private void OnDisable()
        {
            Hide();
        }

        /// <summary>
        /// 3 when a level-3 cannon exists and the triple intro is unseen, else 2 for a level-2
        /// cannon and unseen double intro, else 0. Highest first: a save that jumped straight
        /// to level 3 (the reset tools do) gets the triple and both flags, not two popups.
        /// </summary>
        private int PendingIntroRounds()
        {
            UserTutorialData tutorial = UserData.Tutorial;
            if (!tutorial.completed)
            {
                return 0;                                   // lesson one comes first
            }

            int maxLevel = 1;
            foreach (VehicleProgress progress in UserData.Vehicles.vehicles)
            {
                if (progress != null && progress.level > maxLevel)
                {
                    maxLevel = progress.level;
                }
            }

            if (maxLevel >= 3 && !tutorial.tripleShotIntroDone)
            {
                return 3;
            }

            return maxLevel >= 2 && !tutorial.doubleShotIntroDone ? 2 : 0;
        }

        private void Show(int rounds)
        {
            EnsurePopup();
            if (popupRoot == null)
            {
                return;
            }

            offeredRounds = rounds;
            titleLabel.text = "1 FREE";
            nameLabel.text = rounds >= 3 ? "TRIPLE SHOOT" : "DOUBLE SHOOT";

            popupRoot.gameObject.SetActive(true);
            BlockingInput = true;
            shownAt = Time.unscaledTime;
            tapLabel.gameObject.SetActive(false);

            if (appearRoutine != null)
            {
                StopCoroutine(appearRoutine);
            }

            if (revealRoutine != null)
            {
                StopCoroutine(revealRoutine);
            }

            appearRoutine = StartCoroutine(AppearSoftly());
            revealRoutine = StartCoroutine(RevealContinue());
        }

        private void Hide()
        {
            BlockingInput = false;

            if (appearRoutine != null)
            {
                StopCoroutine(appearRoutine);
                appearRoutine = null;
            }

            if (revealRoutine != null)
            {
                StopCoroutine(revealRoutine);
                revealRoutine = null;
            }

            if (popupRoot != null)
            {
                popupRoot.gameObject.SetActive(false);
            }
        }

        /// <summary>The panel tap: arm the free charge, remember it is spent, get out of the way.</summary>
        private void UseCharge()
        {
            // Inside the lock window the tap is swallowed whole - neither spending the charge
            // nor leaking through to the cannon.
            if (Time.unscaledTime - shownAt < MinViewSeconds)
            {
                return;
            }

            if (fireController != null)
            {
                // freeAmmo: this popup is the one-time gift, so the burst it arms costs nothing.
                fireController.ArmShotBoost(offeredRounds, freeAmmo: true);
            }

            UserTutorialData tutorial = UserData.Tutorial;
            if (offeredRounds >= 3)
            {
                tutorial.tripleShotIntroDone = true;
                tutorial.doubleShotIntroDone = true;        // a triple save skips the double
            }
            else
            {
                tutorial.doubleShotIntroDone = true;
            }

            UserData.Save();
            Hide();
        }

        private IEnumerator AppearSoftly()
        {
            const float seconds = 0.25f;

            float elapsed = 0f;
            while (elapsed < seconds)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / seconds));
                popupGroup.alpha = t;
                panelRect.localScale = Vector3.one * Mathf.Lerp(0.85f, 1f, t);
                yield return null;
            }

            popupGroup.alpha = 1f;
            panelRect.localScale = Vector3.one;
            appearRoutine = null;
        }

        /// <summary>
        /// After the lock window, "TAP TO CONTINUE" appears and pulses gently; from that moment
        /// a tap anywhere confirms. Before it, the popup just sits there being read.
        /// </summary>
        private IEnumerator RevealContinue()
        {
            yield return new WaitForSecondsRealtime(MinViewSeconds);

            tapLabel.gameObject.SetActive(true);
            RectTransform rect = tapLabel.rectTransform;
            float elapsed = 0f;
            while (true)
            {
                elapsed += Time.unscaledDeltaTime;
                rect.localScale = Vector3.one * (1f + 0.06f * Mathf.Sin(elapsed * 3.6f));
                yield return null;
            }
        }

        private void EnsurePopup()
        {
            if (popupRoot != null || hintParent == null)
            {
                return;
            }

            GameObject rootGo = new GameObject("ShotBoostIntro", typeof(RectTransform), typeof(CanvasGroup));
            popupRoot = (RectTransform)rootGo.transform;
            popupRoot.SetParent(hintParent, false);
            popupRoot.anchorMin = Vector2.zero;
            popupRoot.anchorMax = Vector2.one;
            popupRoot.offsetMin = Vector2.zero;
            popupRoot.offsetMax = Vector2.zero;
            popupRoot.SetAsLastSibling();
            popupGroup = rootGo.GetComponent<CanvasGroup>();

            // Full-screen soft dark: it is the "please look here" veil AND the raycast wall
            // that keeps taps from reaching the cannon underneath.
            GameObject dimGo = new GameObject("Dim", typeof(RectTransform));
            RectTransform dimRect = (RectTransform)dimGo.transform;
            dimRect.SetParent(popupRoot, false);
            dimRect.anchorMin = Vector2.zero;
            dimRect.anchorMax = Vector2.one;
            dimRect.offsetMin = Vector2.zero;
            dimRect.offsetMax = Vector2.zero;
            Image dim = dimGo.AddComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.55f);
            dim.raycastTarget = true;

            // A tap ANYWHERE confirms (after the lock window) - the dim is the button.
            Button anywhere = dimGo.AddComponent<Button>();
            anywhere.transition = Selectable.Transition.None;
            anywhere.onClick.AddListener(UseCharge);

            // Dead centre, per Falcon: "hien pop ngay giua man hinh".
            GameObject panelGo = new GameObject("Panel", typeof(RectTransform));
            panelRect = (RectTransform)panelGo.transform;
            panelRect.SetParent(popupRoot, false);
            panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0.6f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.anchoredPosition = Vector2.zero;
            panelRect.sizeDelta = new Vector2(560f, 246f);

            Image panel = panelGo.AddComponent<Image>();
            if (panelSprite != null)
            {
                panel.sprite = panelSprite;
                panel.preserveAspect = true;
            }

            panel.raycastTarget = false;                    // taps fall through to the dim

            if (panelSprite != null)
            {
                // Same paper patch as the drag hint, hiding the art's baked "Tap to shoot".
                GameObject coverGo = new GameObject("Cover", typeof(RectTransform));
                RectTransform coverRect = (RectTransform)coverGo.transform;
                coverRect.SetParent(panelRect, false);
                coverRect.anchorMin = Vector2.zero;
                coverRect.anchorMax = Vector2.one;
                coverRect.offsetMin = new Vector2(70f, 55f);
                coverRect.offsetMax = new Vector2(-70f, -55f);
                Image cover = coverGo.AddComponent<Image>();
                cover.color = new Color(0.984f, 0.973f, 0.937f);
                cover.raycastTarget = false;
            }

            // "1 FREE" big on the upper half - the free-ness is the message...
            titleLabel = MakeLabel("Title", new Vector2(0f, 0.42f), new Vector2(1f, 0.92f), 72f);

            // ...and what it buys sits beneath it.
            nameLabel = MakeLabel("Name", new Vector2(0f, 0.10f), new Vector2(1f, 0.44f), 40f);

            // The confirm line hangs under the panel, white on the dim, hidden until the lock
            // window has passed (RevealContinue shows and pulses it).
            GameObject tapGo = new GameObject("TapToContinue", typeof(RectTransform));
            RectTransform tapRect = (RectTransform)tapGo.transform;
            tapRect.SetParent(panelRect, false);
            tapRect.anchorMin = new Vector2(0f, 0f);
            tapRect.anchorMax = new Vector2(1f, 0f);
            tapRect.pivot = new Vector2(0.5f, 1f);
            tapRect.anchoredPosition = new Vector2(0f, -26f);
            tapRect.sizeDelta = new Vector2(0f, 64f);
            tapLabel = tapGo.AddComponent<TextMeshProUGUI>();
            if (labelFont != null)
            {
                tapLabel.font = labelFont;
            }

            tapLabel.text = "TAP TO CONTINUE";
            tapLabel.fontSize = 34f;
            tapLabel.fontStyle = FontStyles.Bold;
            tapLabel.color = Color.white;
            tapLabel.alignment = TextAlignmentOptions.Center;
            tapLabel.raycastTarget = false;
            tapGo.SetActive(false);
        }

        private TMP_Text MakeLabel(string name, Vector2 anchorMin, Vector2 anchorMax, float size)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            RectTransform rect = (RectTransform)go.transform;
            rect.SetParent(panelRect, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = new Vector2(40f, 0f);
            rect.offsetMax = new Vector2(-40f, 0f);

            TMP_Text label = go.AddComponent<TextMeshProUGUI>();
            if (labelFont != null)
            {
                label.font = labelFont;
            }

            label.fontSize = size;
            label.fontStyle = FontStyles.Bold;
            label.color = new Color(0.13f, 0.25f, 0.55f);
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;
            return label;
        }
    }
}
