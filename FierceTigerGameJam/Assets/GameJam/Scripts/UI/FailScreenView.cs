using System.Globalization;
using GameJam.Audio;
using GameJam.Economy;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GameJam.UI
{
    /// <summary>
    /// The failed run's one question. It shows the price and whether the player can pay it; the
    /// paying, and everything that follows, is the flow's, so this view never touches the run.
    ///
    /// It does not listen for RunFinished the way the cleared screen does. The flow only switches
    /// this root on for a failed run, and it does so before raising the event, so being enabled is
    /// already the whole of "show" - and being enabled is also what a continue undoes, which makes
    /// OnEnable the one place the price has to be re-read.
    /// </summary>
    public sealed class FailScreenView : MonoBehaviour
    {
        [Tooltip("Where the price comes from and what says whether the player can cover it.")]
        [SerializeField] private EconomyService economy;

        [Tooltip("Dimmed rather than hidden when the player is short of gold: a price they cannot "
                 + "meet is still the thing the screen is telling them.")]
        [SerializeField] private Button continueButton;

        [SerializeField] private TMP_Text priceLabel;

        [Tooltip("SDF font for the runtime-built labels (+N, per-round, the step glyphs). The "
                 + "project's default is a 64px bitmap atlas that turns to mush at 190pt; an SDF "
                 + "stays razor sharp at any size.")]
        [SerializeField] private TMP_FontAsset labelFont;

        /// <summary>
        /// What this failure offers. Decided by the flow from how many times this map entry has
        /// already failed: the first failure sells rounds by the piece, the second the flat
        /// continue, and after that there is nothing left to buy.
        /// </summary>
        public enum FailMode
        {
            BuyBullets,
            ClassicContinue,
            Final,
        }

        public FailMode Mode { get; private set; } = FailMode.ClassicContinue;

        /// <summary>Rounds currently dialled in, BuyBullets mode only.</summary>
        public int PurchaseCount { get; private set; }

        private int maxPurchase = 20;
        private Coroutine pulseRoutine;
        private GameObject stepperRoot;
        private TMP_Text bigCountLabel;
        private TMP_Text perRoundLabel;
        private GameObject bannerArt;

        private void OnEnable()
        {
            if (economy != null)
            {
                economy.GoldChanged += Refresh;
            }

            // Being enabled is the whole of "show" for this screen, as the class comment above
            // explains, which makes OnEnable the moment the run is heard to have failed. A
            // continue switches the root off, so picking a run up and failing it again is heard
            // again - which is right: it failed again.
            AudioService.Play(AudioSlot.StageFailed);

            Refresh();
        }

        private void OnDisable()
        {
            if (pulseRoutine != null)
            {
                StopCoroutine(pulseRoutine);
                pulseRoutine = null;
                if (continueButton != null)
                {
                    continueButton.transform.localScale = Vector3.one;
                }
            }

            // The economy is an asset and outlives the scene, so a subscription left behind would
            // fire into a destroyed screen for the rest of the session.
            if (economy != null)
            {
                economy.GoldChanged -= Refresh;
            }
        }

        /// <summary>
        /// Configures which offer this failure makes. Called by the flow before the root shows
        /// (or right after - Refresh is cheap and idempotent).
        /// </summary>
        public void Present(FailMode mode, int suggestedCount, int maxCount)
        {
            Mode = mode;
            maxPurchase = Mathf.Max(1, maxCount);
            PurchaseCount = Mathf.Clamp(suggestedCount, 1, maxPurchase);

            if (mode == FailMode.BuyBullets && stepperRoot == null)
            {
                BuildStepper();
            }

            if (stepperRoot != null)
            {
                stepperRoot.SetActive(mode == FailMode.BuyBullets);
            }

            // The banner art has "+5 - Add 5 ammo to continue!" painted into it, which is a lie
            // in every mode but the flat continue. The purchase draws its own numbers instead.
            if (bannerArt == null)
            {
                Transform banner = transform.Find("Banner");
                bannerArt = banner != null ? banner.gameObject : null;
            }

            if (bannerArt != null)
            {
                bannerArt.SetActive(mode != FailMode.BuyBullets);
            }

            if (continueButton != null)
            {
                continueButton.gameObject.SetActive(mode != FailMode.Final);
            }

            Refresh();

            // A soft breathing pulse on the buy button, only while buying is the offer. It is
            // the one thing on the screen asking for money; it may as well raise its hand.
            if (pulseRoutine != null)
            {
                StopCoroutine(pulseRoutine);
                pulseRoutine = null;
            }

            if (continueButton != null)
            {
                continueButton.transform.localScale = Vector3.one;
                if (mode == FailMode.BuyBullets && isActiveAndEnabled)
                {
                    pulseRoutine = StartCoroutine(PulseBuyButton());
                }
            }
        }

        private System.Collections.IEnumerator PulseBuyButton()
        {
            Transform target = continueButton.transform;
            while (true)
            {
                float k = (Mathf.Sin(Time.unscaledTime * 4.4f) + 1f) * 0.5f;
                target.localScale = Vector3.one * Mathf.Lerp(1f, 1.07f, k);
                yield return null;
            }
        }

        /// <summary>
        /// Re-reads the price and what the player can afford. Called on every gold change as well
        /// as on show, because the charge for a continue happens while this screen is still up.
        /// </summary>
        private void Refresh()
        {
            if (economy == null)
            {
                return;
            }

            if (priceLabel != null)
            {
                // Invariant, so the thousands separator is the comma the art was drawn around
                // whatever locale the device is set to.
                priceLabel.text = Mode == FailMode.BuyBullets
                    ? (PurchaseCount * economy.LoseBulletPrice)
                        .ToString("N0", CultureInfo.InvariantCulture)
                    : economy.ContinuePrice.ToString("N0", CultureInfo.InvariantCulture);
            }

            if (bigCountLabel != null)
            {
                bigCountLabel.text = "+" + PurchaseCount;
            }

            if (perRoundLabel != null && economy != null)
            {
                perRoundLabel.text = economy.LoseBulletPrice
                    .ToString("N0", CultureInfo.InvariantCulture) + " / round";
            }

            if (continueButton != null)
            {
                continueButton.interactable = Mode == FailMode.BuyBullets
                    ? economy.Gold >= PurchaseCount * economy.LoseBulletPrice
                    : economy.CanContinueRun();
            }
        }

        private void AdjustCount(int delta)
        {
            PurchaseCount = Mathf.Clamp(PurchaseCount + delta, 1, maxPurchase);
            Refresh();
        }

        /// <summary>
        /// The minus / count / plus row, built out of the continue button's own visuals so it
        /// matches the screen without new art. Runtime-built on purpose: the fail screen's art
        /// is baked by an editor builder, and this keeps the first-failure shop out of its way
        /// until someone draws it properly.
        /// </summary>
        private void BuildStepper()
        {
            if (continueButton == null)
            {
                return;
            }

            stepperRoot = new GameObject("PurchaseStepper", typeof(RectTransform));
            RectTransform root = (RectTransform)stepperRoot.transform;

            // Laid over where the banner art sits, so the purchase reads as the same card:
            // big "+N" where the painted "+5" was, the per-round price under it, minus and
            // plus flanking. The banner's own rect is the guide when it exists.
            Transform banner = transform.Find("Banner");
            RectTransform reference = banner as RectTransform
                ?? continueButton.transform as RectTransform;
            root.SetParent(reference.parent, false);
            root.anchorMin = reference.anchorMin;
            root.anchorMax = reference.anchorMax;
            root.pivot = reference.pivot;
            root.anchoredPosition = reference.anchoredPosition;
            root.sizeDelta = reference.sizeDelta;
            root.SetSiblingIndex(reference.GetSiblingIndex() + 1);

            bigCountLabel = NewLabel(root, "BigCount", new Vector2(0f, 115f), 190f);
            bigCountLabel.fontStyle = FontStyles.Bold;
            bigCountLabel.rectTransform.sizeDelta = new Vector2(520f, 230f);

            // One row: minus, the per-round price, plus. The big count above already says how
            // many; repeating it in a second small label underneath only made two numbers to
            // read where one does.
            perRoundLabel = NewLabel(root, "PerRound", new Vector2(0f, -70f), 40f);
            MakeStepButton(root, "Minus", "-", new Vector2(-190f, -70f), -1);
            MakeStepButton(root, "Plus", "+", new Vector2(190f, -70f), +1);
        }

        private void MakeStepButton(RectTransform parent, string name, string glyph,
            Vector2 position, int delta)
        {
            Button template = continueButton;
            Button button = Instantiate(template, parent);
            button.gameObject.name = name;
            button.onClick.RemoveAllListeners();
            button.interactable = true;
            button.onClick.AddListener(() => AdjustCount(delta));

            // The clone brings the continue button's whole family with it - the coin, the price
            // label. A step button is a pill and a glyph and nothing else.
            for (int i = button.transform.childCount - 1; i >= 0; i--)
            {
                Destroy(button.transform.GetChild(i).gameObject);
            }

            RectTransform rect = (RectTransform)button.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(120f, 66f);

            // The background keeps its drawn proportions whatever rect it is given, so the pill
            // cannot come out squashed on another aspect.
            Image background = button.GetComponent<Image>();
            if (background != null)
            {
                background.preserveAspect = true;
            }

            TMP_Text label = NewLabel(rect, "Glyph", Vector2.zero, 54f);
            label.fontStyle = FontStyles.Bold;
            label.text = glyph;
        }

        private TMP_Text NewLabel(RectTransform parent, string name, Vector2 position, float size)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            RectTransform rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(220f, 100f);

            TMP_Text label = go.AddComponent<TextMeshProUGUI>();
            if (labelFont != null)
            {
                label.font = labelFont;
            }

            label.alignment = TextAlignmentOptions.Center;
            label.fontSize = size;
            label.raycastTarget = false;
            return label;
        }
    }
}
