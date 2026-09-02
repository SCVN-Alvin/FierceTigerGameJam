using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GameJam.UI
{
    /// <summary>What the player can do with one level, which is the only thing its card shows.</summary>
    public enum MissionItemState
    {
        /// <summary>Not open yet: the level before it has not been passed.</summary>
        Locked,

        /// <summary>Open and not passed - the one the player is on.</summary>
        Current,

        /// <summary>Passed. Still playable, for a better percentage.</summary>
        Cleared,

        /// <summary>
        /// No map authored for this slot yet. Looks locked, but stays clickable so the panel can
        /// answer the tap with a notice instead of silence.
        /// </summary>
        Missing,
    }

    /// <summary>
    /// One level on the mission board: its name and the one thing the player can do with it.
    ///
    /// The card only shows a state; deciding it - what counts as cleared, what is open - is the
    /// panel's, which is what lets the unlock rule change without a card knowing anything about
    /// maps or saves. It does not even wire its own button: the panel does, because only the
    /// panel knows which level this card was built for.
    /// </summary>
    public sealed class MissionProgressItemView : MonoBehaviour
    {
        [SerializeField] private TMP_Text title;

        [Tooltip("The card's one button. Retry, play or a dead lock, depending on the state.")]
        [SerializeField] private Button action;

        [Tooltip("The button's graphic, whose sprite is swapped per state. Falls back to the "
                 + "button's own targetGraphic.")]
        [SerializeField] private Image actionImage;

        [Header("State Sprites")]
        [SerializeField] private Sprite retrySprite;
        [SerializeField] private Sprite playSprite;
        [SerializeField] private Sprite lockedSprite;

        [Header("Star Sprites")]
        [Tooltip("A lit star. Left empty the card draws its own plain star.")]
        [SerializeField] private Sprite starOnSprite;

        [Tooltip("An empty star slot, shown for the stars not yet earned so the player can see "
                 + "what is missing. Left empty only the earned stars show.")]
        [SerializeField] private Sprite starOffSprite;

        /// <summary>The panel wires the click, since what it opens depends on which level this is.</summary>
        public Button Action => action;

        /// <summary>What the card was last bound to, so a click can be refused before it is acted on.</summary>
        public MissionItemState State { get; private set; }

        private RectTransform starRow;
        private readonly Image[] starImages = new Image[3];
        private static Sprite starSprite;

        /// <summary>Kept for callers that know nothing of stars.</summary>
        public void Bind(string titleText, MissionItemState state)
        {
            Bind(titleText, state, 0);
        }

        /// <summary>
        /// Draws the card for one level. Called on every refresh, so it sets everything.
        /// A passed level with stars shows THEM where the retry arrow sat - the best count ever
        /// earned, which the save keeps monotonic. A level without a star yet (unplayed, or
        /// failed) keeps its plain button.
        /// </summary>
        public void Bind(string titleText, MissionItemState state, int stars)
        {
            State = state;
            bool showStars = state == MissionItemState.Cleared && stars > 0;
            EnsureStarRow();
            if (starRow != null)
            {
                starRow.gameObject.SetActive(showStars);
                for (int i = 0; i < starImages.Length; i++)
                {
                    if (starImages[i] == null)
                    {
                        continue;
                    }

                    bool lit = i < stars;
                    bool visible = showStars && (lit || starOffSprite != null);
                    starImages[i].gameObject.SetActive(visible);
                    if (!visible)
                    {
                        continue;
                    }

                    if (starOnSprite != null)
                    {
                        starImages[i].sprite = lit ? starOnSprite : starOffSprite;
                        starImages[i].color = Color.white;   // the pack art carries its own colour
                    }
                    else
                    {
                        starImages[i].sprite = ResolveStarSprite();
                        starImages[i].color = new Color(1f, 0.8f, 0.1f);
                    }
                }
            }

            if (title != null)
            {
                title.text = titleText;
            }

            Image image = ResolveActionImage();
            if (image != null)
            {
                image.enabled = !showStars;           // the stars stand where the arrow sat
                Sprite sprite = ResolveSprite(state);

                // A state with no art assigned keeps whatever the prefab had rather than blanking
                // the card: an empty Image draws a white block over the badge.
                if (sprite != null)
                {
                    image.sprite = sprite;
                }
            }

            if (action != null)
            {
                // The button's transition is None on purpose (see the builder): a locked card must
                // not look greyed out on top of already being a lock. This still stops the click.
                action.interactable = state != MissionItemState.Locked;
            }
        }

        /// <summary>
        /// The star row, built once over the action button's own rect: up to three Images of a
        /// procedurally drawn star. Drawn, not typed: the game's font is a baked bitmap with no
        /// star glyph in it, so any text star renders as nothing on this project's UI.
        /// </summary>
        private void EnsureStarRow()
        {
            if (starRow != null || action == null)
            {
                return;
            }

            GameObject go = new GameObject("Stars", typeof(RectTransform));
            starRow = (RectTransform)go.transform;
            starRow.SetParent(action.transform.parent, false);
            RectTransform reference = (RectTransform)action.transform;
            starRow.anchorMin = reference.anchorMin;
            starRow.anchorMax = reference.anchorMax;
            starRow.pivot = reference.pivot;
            starRow.anchoredPosition = reference.anchoredPosition;
            starRow.sizeDelta = reference.sizeDelta;

            for (int i = 0; i < starImages.Length; i++)
            {
                GameObject starGo = new GameObject("Star" + i, typeof(RectTransform));
                RectTransform rect = (RectTransform)starGo.transform;
                rect.SetParent(starRow, false);
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = new Vector2((i - 1) * 34f, 0f);
                rect.sizeDelta = new Vector2(32f, 32f);

                Image image = starGo.AddComponent<Image>();
                image.preserveAspect = true;
                image.raycastTarget = false;         // the tap still belongs to the button below
                starImages[i] = image;
            }

            starRow.gameObject.SetActive(false);
        }

        /// <summary>A five-point star drawn into a texture once and shared by every card.</summary>
        private static Sprite ResolveStarSprite()
        {
            if (starSprite != null)
            {
                return starSprite;
            }

            const int size = 64;
            Vector2[] points = new Vector2[10];
            for (int i = 0; i < 10; i++)
            {
                float radius = i % 2 == 0 ? 0.95f : 0.42f;
                float angle = Mathf.PI * 0.5f + i * Mathf.PI / 5f;
                points[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
            }

            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // 2x2 supersampling keeps the points from looking chewed at card size.
                    float coverage = 0f;
                    for (int sy = 0; sy < 2; sy++)
                    {
                        for (int sx = 0; sx < 2; sx++)
                        {
                            Vector2 p = new Vector2(
                                ((x + 0.25f + sx * 0.5f) / size) * 2f - 1f,
                                ((y + 0.25f + sy * 0.5f) / size) * 2f - 1f);
                            if (InsidePolygon(p, points))
                            {
                                coverage += 0.25f;
                            }
                        }
                    }

                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, coverage));
                }
            }

            texture.Apply();
            starSprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
            return starSprite;
        }

        private static bool InsidePolygon(Vector2 p, Vector2[] polygon)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
            {
                Vector2 a = polygon[i];
                Vector2 b = polygon[j];
                if (a.y > p.y != b.y > p.y
                    && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x)
                {
                    inside = !inside;
                }
            }

            return inside;
        }

        private Sprite ResolveSprite(MissionItemState state)
        {
            switch (state)
            {
                case MissionItemState.Cleared:
                    return retrySprite;
                case MissionItemState.Current:
                    return playSprite;
                default:
                    return lockedSprite;
            }
        }

        private Image ResolveActionImage()
        {
            if (actionImage != null)
            {
                return actionImage;
            }

            return action != null ? action.targetGraphic as Image : null;
        }

        /// <summary>
        /// Fills in whatever was left empty from the children, by the names the prefab uses, so
        /// adding this component to an authored card wires itself rather than leaving references
        /// to be dragged in. Anything set by hand is never overwritten.
        /// </summary>
        private void ResolveMissingReferences()
        {
            if (action == null)
            {
                action = FindByName<Button>("Action");
            }

            if (actionImage == null && action != null)
            {
                actionImage = action.targetGraphic as Image;
                if (actionImage == null)
                {
                    actionImage = action.GetComponent<Image>();
                }
            }

            if (title == null)
            {
                // Never the button's own caption: the card's button is a picture, but a card
                // authored with a label under it would otherwise have that label taken for the
                // level's name and overwritten on the first bind.
                title = FindLabel("Title");
            }
        }

        private T FindByName<T>(string objectName) where T : Component
        {
            T[] candidates = GetComponentsInChildren<T>(true);
            for (int i = 0; i < candidates.Length; i++)
            {
                if (string.Equals(candidates[i].gameObject.name, objectName, StringComparison.OrdinalIgnoreCase))
                {
                    return candidates[i];
                }
            }

            return null;
        }

        private TMP_Text FindLabel(string objectName)
        {
            TMP_Text[] candidates = GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < candidates.Length; i++)
            {
                if (action != null && candidates[i].transform.IsChildOf(action.transform))
                {
                    continue;
                }

                if (string.Equals(candidates[i].gameObject.name, objectName, StringComparison.OrdinalIgnoreCase))
                {
                    return candidates[i];
                }
            }

            return null;
        }

        /// <summary>
        /// The whole card is the tap target, not just the button art. An invisible image behind
        /// everything catches the touch and hands it to the same click the panel wired; the
        /// interactable check keeps a locked card as dead as its button.
        /// </summary>
        private void EnsureCardTap()
        {
            if (action == null || transform.Find("CardTap") != null)
            {
                return;
            }

            GameObject go = new GameObject("CardTap", typeof(RectTransform));
            RectTransform rect = (RectTransform)go.transform;
            rect.SetParent(transform, false);
            rect.SetAsFirstSibling();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            Image catcher = go.AddComponent<Image>();
            catcher.color = new Color(0f, 0f, 0f, 0f);

            Button tap = go.AddComponent<Button>();
            tap.transition = Selectable.Transition.None;
            tap.targetGraphic = catcher;
            tap.onClick.AddListener(() =>
            {
                if (action != null && action.interactable)
                {
                    action.onClick.Invoke();
                }
            });
        }

        private void Reset()
        {
            ResolveMissingReferences();
        }

        private void OnValidate()
        {
            ResolveMissingReferences();
        }

        private void Awake()
        {
            // Also at runtime, so a card instantiated from a prefab that was never opened in the
            // inspector still knows its own parts.
            ResolveMissingReferences();
            if (Application.isPlaying)
            {
                EnsureCardTap();
            }
        }
    }
}
