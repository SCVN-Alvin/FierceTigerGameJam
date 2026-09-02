using System.Collections;
using GameJam.Audio;
using GameJam.Config;
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

        [Header("Stars")]
        [Tooltip("Where the star thresholds live. The screen shows THIS run's stars - the board "
                 + "shows the best ever; both come from the same StarsFor so they can never "
                 + "disagree about what a star means.")]
        [SerializeField] private MissionConfig missionConfig;

        [SerializeField] private Sprite starOnSprite;

        [SerializeField] private Sprite starOffSprite;

        [Tooltip("Seconds the reward number takes to count up. Counting it up is what makes the "
                 + "reward land; zero shows it immediately.")]
        [SerializeField] private float rewardCountUpSeconds = 0.55f;

        private Coroutine countUpRoutine;
        private RectTransform starRow;
        private readonly Image[] starImages = new Image[3];
        private readonly Image[] goldImages = new Image[3];
        private Coroutine starDropRoutine;

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
            if (starDropRoutine != null)
            {
                StopCoroutine(starDropRoutine);
                starDropRoutine = null;
            }
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

            // Past the guard above, so a failed run that reached this listener stays silent. The
            // flow raises RunFinished exactly once per run, so this is heard exactly once.
            AudioService.Play(AudioSlot.StageClear);

            ShowMapPicture(result.MapId);
            ShowReward(result.GoldAwarded);
            ShowStars(result.ClearPercent);
        }

        /// <summary>
        /// The stars THIS run earned, under the banner. The three grey slots stand there from
        /// the first frame; each EARNED star then drops onto its slot one after another, slams
        /// to size, and knocks the whole screen - the juice is in the landing, not the layout.
        /// </summary>
        private void ShowStars(float clearPercent)
        {
            if (missionConfig == null)
            {
                return;
            }

            int stars = missionConfig.StarsFor(true, clearPercent);
            EnsureStarRow();
            if (starRow == null)
            {
                return;
            }

            for (int i = 0; i < starImages.Length; i++)
            {
                if (starImages[i] != null)
                {
                    starImages[i].gameObject.SetActive(true);
                }

                if (goldImages[i] != null)
                {
                    goldImages[i].gameObject.SetActive(false);
                }
            }

            if (starDropRoutine != null)
            {
                StopCoroutine(starDropRoutine);
            }

            starDropRoutine = StartCoroutine(DropStars(stars));
        }

        private System.Collections.IEnumerator DropStars(int stars)
        {
            yield return new WaitForSeconds(0.35f);   // let the banner land first

            for (int i = 0; i < stars && i < goldImages.Length; i++)
            {
                Image gold = goldImages[i];
                if (gold == null)
                {
                    continue;
                }

                RectTransform rect = gold.rectTransform;
                Vector2 target = SlotPosition(i);
                gold.gameObject.SetActive(true);

                // The fall: big and high to seated, fast, accelerating - a stamp, not a float.
                const float fall = 0.16f;
                for (float t = 0f; t < fall; t += Time.deltaTime)
                {
                    float k = t / fall;
                    k *= k;                            // ease in, gravity-like
                    rect.anchoredPosition = target + new Vector2(0f, (1f - k) * 170f);
                    rect.localScale = Vector3.one * Mathf.Lerp(2.4f, 1f, k);
                    gold.color = new Color(1f, 1f, 1f, Mathf.Clamp01(k * 3f));
                    yield return null;
                }

                rect.anchoredPosition = target;
                gold.color = Color.white;
                AudioService.Play(AudioSlot.StarLand);

                // The squash on landing. The shake was tried and retired - Falcon's call: at
                // card size it read as jank, and the slam carries the weight on its own.
                const float slam = 0.14f;
                for (float t = 0f; t < slam; t += Time.deltaTime)
                {
                    float k = 1f - t / slam;
                    rect.localScale = Vector3.one * (1f + 0.3f * k * k);
                    yield return null;
                }

                rect.localScale = Vector3.one;
                yield return new WaitForSeconds(0.12f);
            }

            starDropRoutine = null;
        }

        private static Vector2 SlotPosition(int index)
        {
            bool middle = index == 1;
            return new Vector2((index - 1) * 150f, middle ? 18f : 0f);
        }

        private void EnsureStarRow()
        {
            if (starRow != null || starOnSprite == null)
            {
                return;
            }

            GameObject go = new GameObject("Stars", typeof(RectTransform));
            starRow = (RectTransform)go.transform;
            starRow.SetParent(transform, false);
            starRow.anchorMin = starRow.anchorMax = new Vector2(0.5f, 0.695f);
            starRow.pivot = new Vector2(0.5f, 0.5f);
            starRow.anchoredPosition = Vector2.zero;
            starRow.sizeDelta = new Vector2(480f, 170f);

            for (int i = 0; i < starImages.Length; i++)
            {
                // The slot is the ON art tinted grey rather than the pack's Off sprite: the Off
                // art is a dark navy that all but disappears against a night backdrop, and a
                // slot the player cannot see fails at its one job - showing what is missing.
                starImages[i] = MakeStar(starRow, "Slot" + i, i, starOnSprite);
                starImages[i].color = new Color(0.42f, 0.47f, 0.58f, 0.95f);
                goldImages[i] = MakeStar(starRow, "Gold" + i, i, starOnSprite);
                goldImages[i].gameObject.SetActive(false);
            }
        }

        private static Image MakeStar(RectTransform parent, string name, int index, Sprite sprite)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            RectTransform rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = SlotPosition(index);
            bool middle = index == 1;
            rect.sizeDelta = middle ? new Vector2(156f, 156f) : new Vector2(128f, 128f);

            Image image = go.AddComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
            return image;
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
