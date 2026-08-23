using System;
using System.Collections.Generic;
using GameJam.Gameplay.Combat;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GameJam.UI
{
    /// <summary>
    /// The screen where the player fills a run's ammunition budget before setting off.
    ///
    /// The map allows a total number of bullets across every type, and the player spends that
    /// budget however they like: ten may be ten rocks, or four rocks and six cannon balls. Only
    /// ammunition the player has unlocked is offered, because buying is a different screen's job.
    ///
    /// The view never starts the run itself. It raises <see cref="StartRequested"/> and lets the
    /// flow controller own the transition, so this stays a picker rather than a second place that
    /// knows how a run begins.
    /// </summary>
    public sealed class AmmoPickView : MonoBehaviour
    {
        [Tooltip("The run's picks. Someone else calls BeginPick with the map's limit before this "
                 + "screen opens; until then the budget is zero and every plus is disabled.")]
        [SerializeField] private BulletInventory inventory;

        [Tooltip("The catalogue, and the source of each kind's level and unlocked state.")]
        [SerializeField] private BulletLoadout loadout;

        [Tooltip("Rows are spawned under here. Falls back to this object's transform.")]
        [SerializeField] private RectTransform container;

        [Tooltip("Any prefab holding two Buttons (named plus and minus, or in that order: minus "
                 + "then plus) and a TMP_Text or Text for the label. Objects named Name, Level and "
                 + "Count are used for those three readings; whatever is missing is folded into "
                 + "the name label. Left empty, a plain row is generated so the screen works "
                 + "before anyone designs one.")]
        [SerializeField] private GameObject rowPrefab;

        [Tooltip("Optional. Shows the picked total against the map's limit, e.g. \"7 / 10\".")]
        [SerializeField] private TMP_Text totalLabel;

        [Tooltip("Optional. Raises StartRequested, and stays disabled while nothing is picked.")]
        [SerializeField] private Button startButton;

        [Header("Layout")]
        [SerializeField] private bool useVerticalLayout = true;
        [SerializeField] private float rowSpacing = 12f;
        [SerializeField] private RectOffset layoutPadding;

        private const string RowNamePrefix = "AmmoRow_";

        // Checked in order, so a button named "Row-Plus" is claimed by "plus" before the bare
        // symbol has a chance to read the separator as a minus.
        private static readonly string[] PlusHints = { "plus", "increase", "add", "+" };
        private static readonly string[] MinusHints = { "minus", "decrease", "remove", "-" };

        private readonly List<Row> spawnedRows = new List<Row>();

        /// <summary>
        /// Raised when the player asks to set off. The listener owns the transition: this view
        /// only knows that the pick is finished and legal.
        /// </summary>
        public event Action StartRequested;

        private void OnEnable()
        {
            if (inventory != null)
            {
                inventory.Changed += Refresh;
            }

            if (startButton != null)
            {
                startButton.onClick.AddListener(HandleStartClicked);
            }

            // Rebuilt rather than refreshed: what the player owns may have changed in the shop
            // while this screen was closed.
            Rebuild();
        }

        private void OnDisable()
        {
            if (inventory != null)
            {
                inventory.Changed -= Refresh;
            }

            if (startButton != null)
            {
                startButton.onClick.RemoveListener(HandleStartClicked);
            }
        }

        /// <summary>
        /// Throws away the rows and builds them again from the catalogue. Safe to call at any
        /// time: it clears before it spawns, so calling it twice leaves one set of rows.
        /// </summary>
        [ContextMenu("Rebuild")]
        public void Rebuild()
        {
            ClearRows();

            if (inventory == null || loadout == null)
            {
                Debug.LogWarning(
                    $"{nameof(AmmoPickView)} needs both a {nameof(BulletInventory)} and a {nameof(BulletLoadout)}.",
                    this);
                return;
            }

            Transform parent = container != null ? container : transform;
            EnsureLayout(parent);

            IReadOnlyList<BulletDefinition> bullets = loadout.Bullets;
            for (int i = 0; i < bullets.Count; i++)
            {
                BulletDefinition bullet = bullets[i];
                if (bullet == null || !loadout.IsUnlocked(bullet))
                {
                    // Locked ammunition is simply absent here. The player buys it in the shop.
                    continue;
                }

                Row row = CreateRow(parent, bullet);

                // Captured per iteration, otherwise every button on the screen would end up
                // acting on the last kind of ammunition in the catalogue.
                string bulletId = bullet.Id;

                if (row.Plus != null)
                {
                    row.Plus.onClick.AddListener(() => inventory.TryPick(bulletId));
                }

                if (row.Minus != null)
                {
                    row.Minus.onClick.AddListener(() => inventory.TryUnpick(bulletId));
                }

                spawnedRows.Add(row);
            }

            if (inventory.PickLimit <= 0)
            {
                Debug.LogWarning(
                    $"{name}: the pick budget is zero, so nothing can be taken along. "
                    + $"{nameof(BulletInventory)}.{nameof(BulletInventory.BeginPick)} has to run with the map's "
                    + "limit before this screen opens.",
                    this);
            }

            Refresh();
        }

        /// <summary>Redraws every reading without rebuilding the rows.</summary>
        public void Refresh()
        {
            if (inventory == null)
            {
                return;
            }

            for (int i = 0; i < spawnedRows.Count; i++)
            {
                RefreshRow(spawnedRows[i]);
            }

            if (totalLabel != null)
            {
                totalLabel.text = $"{inventory.TotalCount} / {inventory.PickLimit}";
            }

            if (startButton != null)
            {
                // Sending the player in with nothing is a guaranteed loss, so it is not something
                // a stray tap should be able to reach.
                startButton.interactable = inventory.TotalCount > 0;
            }
        }

        private void RefreshRow(Row row)
        {
            if (row == null || row.Root == null || row.Bullet == null)
            {
                return;
            }

            int count = inventory.GetCount(row.Bullet.Id);
            int level = loadout != null ? loadout.GetLevel(row.Bullet) : 1;

            // Readings the prefab has no home for are folded into the name, so a one-label row
            // still tells the player everything rather than quietly dropping half of it.
            string nameText = row.Bullet.DisplayName;

            if (row.LevelLabel.Exists)
            {
                row.LevelLabel.Set($"Lv {level}");
            }
            else
            {
                nameText += $"  Lv {level}";
            }

            if (row.CountLabel.Exists)
            {
                row.CountLabel.Set(count.ToString());
            }
            else
            {
                nameText += $"   x{count}";
            }

            row.NameLabel.Set(nameText);

            if (row.Plus != null)
            {
                row.Plus.interactable = inventory.RemainingPicks > 0;
            }

            if (row.Minus != null)
            {
                row.Minus.interactable = count > 0;
            }
        }

        private void HandleStartClicked()
        {
            if (inventory == null || inventory.IsEmpty)
            {
                return;
            }

            StartRequested?.Invoke();
        }

        private Row CreateRow(Transform parent, BulletDefinition bullet)
        {
            GameObject rowObject = rowPrefab != null
                ? Instantiate(rowPrefab, parent)
                : CreateDefaultRow(parent);

            // Resolved before the rename, so an id carrying a dash cannot be read as a minus
            // when the prefab's root is itself a button.
            ResolveStepButtons(rowObject, out Button minus, out Button plus);

            rowObject.name = RowNamePrefix + bullet.Id;

            if (minus == null || plus == null)
            {
                Debug.LogWarning(
                    $"{name}: the row for {bullet.DisplayName} is missing a "
                    + $"{(plus == null ? "plus" : "minus")} button. Name the prefab's buttons Plus and Minus, "
                    + "or leave exactly two on it in the order minus, plus.",
                    this);
            }

            return new Row
            {
                Bullet = bullet,
                Root = rowObject,
                Plus = plus,
                Minus = minus,
                NameLabel = FindLabel(rowObject, "Name", plus, minus, true),
                LevelLabel = FindLabel(rowObject, "Level", plus, minus, false),
                CountLabel = FindLabel(rowObject, "Count", plus, minus, false),
            };
        }

        /// <summary>
        /// Stacks the rows with real gaps between them. LayoutGroup is
        /// [DisallowMultipleComponent], so a group the container already carries is the one that
        /// has to be configured: adding a second one beside it silently returns null.
        /// </summary>
        private void EnsureLayout(Transform parent)
        {
            if (!useVerticalLayout)
            {
                return;
            }

            HorizontalOrVerticalLayoutGroup group = ResolveLayoutGroup(parent);
            if (group == null)
            {
                return;
            }

            group.enabled = true;
            group.spacing = rowSpacing;
            group.childAlignment = TextAnchor.UpperCenter;

            // Left alone when unset: Unity gives the group a zero RectOffset of its own, and
            // assigning null here only moves the failure into the layout pass.
            if (layoutPadding != null)
            {
                group.padding = layoutPadding;
            }

            // Rows keep the size their prefab defines rather than being stretched to fill.
            group.childForceExpandWidth = false;
            group.childForceExpandHeight = false;
            group.childControlWidth = false;
            group.childControlHeight = false;
        }

        private HorizontalOrVerticalLayoutGroup ResolveLayoutGroup(Transform parent)
        {
            HorizontalOrVerticalLayoutGroup group = parent.GetComponent<HorizontalOrVerticalLayoutGroup>();
            if (group == null)
            {
                // A grid group would block the add the same way, and swapping one out from under
                // whoever authored it is not this component's call.
                LayoutGroup blocking = parent.GetComponent<LayoutGroup>();
                if (blocking != null)
                {
                    Debug.LogWarning(
                        $"{name}: {parent.name} already has a {blocking.GetType().Name}, so the rows are laid "
                        + "out by that instead. Replace it with a Vertical Layout Group to get a list.",
                        this);
                    return null;
                }

                return parent.gameObject.AddComponent<VerticalLayoutGroup>();
            }

            if (!(group is VerticalLayoutGroup))
            {
                Debug.LogWarning(
                    $"{name}: {parent.name} has a {group.GetType().Name}, so the rows stay in a line and "
                    + $"{nameof(rowSpacing)} is applied to it. Replace it with a Vertical Layout Group on the "
                    + "prefab to stack them.",
                    this);
            }

            return group;
        }

        /// <summary>
        /// Picks the plus and minus out of whatever buttons the prefab carries: by name first,
        /// then by order, which is the convention a two button row already implies.
        /// </summary>
        private static void ResolveStepButtons(GameObject rowObject, out Button minus, out Button plus)
        {
            Button[] buttons = rowObject.GetComponentsInChildren<Button>(true);

            plus = FindButton(buttons, PlusHints, null);
            minus = FindButton(buttons, MinusHints, plus);

            if (plus != null && minus != null)
            {
                return;
            }

            // Nothing was named usefully. A row with two buttons reads left to right as minus
            // then plus, which is how the generated fallback lays itself out too.
            if (buttons.Length >= 2)
            {
                minus = buttons[0];
                plus = buttons[1];
            }
        }

        private static Button FindButton(Button[] buttons, string[] hints, Button exclude)
        {
            for (int h = 0; h < hints.Length; h++)
            {
                for (int i = 0; i < buttons.Length; i++)
                {
                    Button button = buttons[i];
                    if (button == null || button == exclude)
                    {
                        continue;
                    }

                    if (button.name.IndexOf(hints[h], StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return button;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Finds a label by the name its object carries. Text living inside the plus or minus
        /// button is never a candidate: that is the button's own caption, and writing the
        /// ammunition's name over a "+" would leave the row unreadable.
        /// </summary>
        private static Label FindLabel(GameObject rowObject, string nameHint, Button plus, Button minus, bool allowFallback)
        {
            TMP_Text[] tmpLabels = rowObject.GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < tmpLabels.Length; i++)
            {
                if (IsCandidate(tmpLabels[i].transform, plus, minus)
                    && tmpLabels[i].name.IndexOf(nameHint, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return new Label(tmpLabels[i], null);
                }
            }

            Text[] legacyLabels = rowObject.GetComponentsInChildren<Text>(true);
            for (int i = 0; i < legacyLabels.Length; i++)
            {
                if (IsCandidate(legacyLabels[i].transform, plus, minus)
                    && legacyLabels[i].name.IndexOf(nameHint, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return new Label(null, legacyLabels[i]);
                }
            }

            if (!allowFallback)
            {
                return default;
            }

            // The name has to land somewhere, so an unnamed prefab gets its first free label.
            for (int i = 0; i < tmpLabels.Length; i++)
            {
                if (IsCandidate(tmpLabels[i].transform, plus, minus))
                {
                    return new Label(tmpLabels[i], null);
                }
            }

            for (int i = 0; i < legacyLabels.Length; i++)
            {
                if (IsCandidate(legacyLabels[i].transform, plus, minus))
                {
                    return new Label(null, legacyLabels[i]);
                }
            }

            return default;
        }

        private static bool IsCandidate(Transform label, Button plus, Button minus)
        {
            if (plus != null && label.IsChildOf(plus.transform))
            {
                return false;
            }

            return minus == null || !label.IsChildOf(minus.transform);
        }

        /// <summary>
        /// Minimal stand-in so the screen is usable before anyone designs a row. Uses the built-in
        /// font rather than TMP, which needs its essentials imported to render anything.
        /// </summary>
        private static GameObject CreateDefaultRow(Transform parent)
        {
            GameObject rowObject = new GameObject(
                "AmmoRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            RectTransform rowRect = (RectTransform)rowObject.transform;
            rowRect.SetParent(parent, false);
            rowRect.sizeDelta = new Vector2(460f, 56f);

            // Freshly created, so nothing can be blocking the add here.
            HorizontalLayoutGroup group = rowObject.GetComponent<HorizontalLayoutGroup>();
            if (group != null)
            {
                group.spacing = 12f;
                group.childAlignment = TextAnchor.MiddleLeft;
                group.childForceExpandWidth = false;
                group.childForceExpandHeight = false;
                group.childControlWidth = false;
                group.childControlHeight = false;
            }

            // Minus first, plus last: the order the button resolution falls back on.
            CreateDefaultButton(rowRect, "Minus", "-", new Vector2(56f, 48f));
            CreateDefaultLabel(rowRect, "Name", new Vector2(260f, 48f), TextAnchor.MiddleLeft);
            CreateDefaultLabel(rowRect, "Count", new Vector2(56f, 48f), TextAnchor.MiddleCenter);
            CreateDefaultButton(rowRect, "Plus", "+", new Vector2(56f, 48f));

            return rowObject;
        }

        private static void CreateDefaultButton(Transform parent, string objectName, string caption, Vector2 size)
        {
            GameObject buttonObject = new GameObject(objectName, typeof(RectTransform), typeof(Image), typeof(Button));
            RectTransform buttonRect = (RectTransform)buttonObject.transform;
            buttonRect.SetParent(parent, false);
            buttonRect.sizeDelta = size;

            Text label = CreateDefaultLabel(buttonRect, "Label", size, TextAnchor.MiddleCenter);
            label.text = caption;

            // The caption stretches with its button, which the row's layout group may resize.
            RectTransform labelRect = (RectTransform)label.transform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
        }

        private static Text CreateDefaultLabel(Transform parent, string objectName, Vector2 size, TextAnchor alignment)
        {
            GameObject labelObject = new GameObject(objectName, typeof(RectTransform), typeof(Text));
            RectTransform labelRect = (RectTransform)labelObject.transform;
            labelRect.SetParent(parent, false);
            labelRect.sizeDelta = size;

            Text label = labelObject.GetComponent<Text>();
            label.alignment = alignment;
            label.color = Color.black;
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            return label;
        }

        /// <summary>
        /// Removes only what this view spawned. The container usually holds authored children too
        /// (a background, a title), and wiping every child would delete those the first time the
        /// list is rebuilt. Rows left over from an earlier rebuild are matched by name, since the
        /// tracking list does not survive an assembly reload.
        /// </summary>
        private void ClearRows()
        {
            for (int i = 0; i < spawnedRows.Count; i++)
            {
                DestroyRow(spawnedRows[i] != null ? spawnedRows[i].Root : null);
            }

            spawnedRows.Clear();

            Transform parent = container != null ? container : transform;
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                GameObject child = parent.GetChild(i).gameObject;
                if (child.name.StartsWith(RowNamePrefix, StringComparison.Ordinal))
                {
                    DestroyRow(child);
                }
            }
        }

        /// <summary>
        /// Unparented before being destroyed: Destroy only takes effect at the end of the frame,
        /// and until then the layout group would still lay out the old rows alongside the new ones
        /// spawned right after this.
        /// </summary>
        private static void DestroyRow(GameObject rowObject)
        {
            if (rowObject == null)
            {
                return;
            }

            rowObject.transform.SetParent(null, false);

            if (Application.isPlaying)
            {
                Destroy(rowObject);
            }
            else
            {
                DestroyImmediate(rowObject);
            }
        }

        /// <summary>One spawned row, and the pieces of it the view writes to.</summary>
        private sealed class Row
        {
            public BulletDefinition Bullet;
            public GameObject Root;
            public Button Plus;
            public Button Minus;
            public Label NameLabel;
            public Label LevelLabel;
            public Label CountLabel;
        }

        /// <summary>
        /// A label that is either TMP or the built-in UI text, whichever the prefab happened to
        /// use, so the rest of the view can write to it without asking which.
        /// </summary>
        private readonly struct Label
        {
            private readonly TMP_Text tmp;
            private readonly Text legacy;

            public Label(TMP_Text tmp, Text legacy)
            {
                this.tmp = tmp;
                this.legacy = legacy;
            }

            public bool Exists => tmp != null || legacy != null;

            public void Set(string text)
            {
                if (tmp != null)
                {
                    tmp.text = text;
                }
                else if (legacy != null)
                {
                    legacy.text = text;
                }
            }
        }
    }
}
