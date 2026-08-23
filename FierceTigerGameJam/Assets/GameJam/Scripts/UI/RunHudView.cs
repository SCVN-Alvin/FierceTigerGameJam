using System;
using System.Collections.Generic;
using GameJam.Gameplay.Combat;
using GameJam.Gameplay.Flow;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GameJam.UI
{
    /// <summary>
    /// The heads-up display during a run: how much of the structure is down, and what is left to
    /// bring the rest of it down with.
    ///
    /// Every reference is optional. A scene may want only the percent, only the bullet count, or
    /// the whole thing, and a view that throws in a half-wired scene takes the run with it.
    /// </summary>
    public sealed class RunHudView : MonoBehaviour
    {
        [Tooltip("Optional. The pass bar is read from the run when it starts, so the HUD shows "
                 + "the bar for the map actually being played rather than a value set by hand.")]
        [SerializeField] private LevelRunController runController;

        [SerializeField] private LevelProgressTracker progressTracker;
        [SerializeField] private BulletInventory inventory;

        [Tooltip("Catalogue the bullet ids in the inventory are named from. Without it the "
                 + "breakdown falls back to showing raw ids.")]
        [SerializeField] private BulletLoadout loadout;

        [Header("Clear progress")]
        [Tooltip("Whole-number percent destroyed, e.g. \"72%\".")]
        [SerializeField] private TMP_Text clearPercentLabel;

        [Tooltip("A Filled Image whose fillAmount follows progress from 0 to 1.")]
        [SerializeField] private Image clearProgressFill;

        [Tooltip("The bar the player has to beat, pushed in by the run flow.")]
        [SerializeField] private TMP_Text requiredPercentLabel;

        [Header("Ammunition")]
        [Tooltip("Total bullets left, across every type.")]
        [SerializeField] private TMP_Text remainingBulletsLabel;

        [Tooltip("One row per remaining type is spawned under here. Left empty, no breakdown is "
                 + "built at all, because rows dumped on the HUD root would land on top of it.")]
        [SerializeField] private RectTransform bulletBreakdownContainer;

        [Tooltip("Any prefab with a TMP_Text or Text in it. Two or more labels are filled as name "
                 + "and count; a single one gets both. Left empty, a plain row is generated.")]
        [SerializeField] private RectTransform rowPrefab;

        [Header("Breakdown layout")]
        [SerializeField] private bool useVerticalLayout = true;
        [SerializeField] private float rowSpacing = 4f;
        [SerializeField] private RectOffset layoutPadding;

        [Header("Pass bar")]
        [Tooltip("Fraction of the structure this map asks for. The flow overwrites it per map.")]
        [Range(0f, 1f)]
        [SerializeField] private float requiredClearPercent = 0.8f;

        private const string RowNamePrefix = "BulletRow_";

        private readonly List<RectTransform> spawnedRows = new List<RectTransform>();

        /// <summary>Reused between rebuilds so a shot does not allocate a list per row.</summary>
        private readonly List<KeyValuePair<string, int>> orderedCounts = new List<KeyValuePair<string, int>>();

        /// <summary>How much of this map the player must destroy to pass it, from 0 to 1.</summary>
        public float RequiredClearPercent => requiredClearPercent;

        private void OnEnable()
        {
            // Taken here rather than pushed in by the flow: every view in this screen set reads
            // its own data, which keeps the flow controller from knowing what any of them draw.
            if (runController != null)
            {
                requiredClearPercent = runController.RequiredClearPercent;
            }

            if (progressTracker != null)
            {
                progressTracker.ProgressChanged += HandleProgressChanged;
            }

            if (inventory != null)
            {
                inventory.Changed += HandleInventoryChanged;
            }

            // The tracker only raises on a change and the inventory only on a pick or a shot, so
            // without this the HUD would sit blank until the player did something.
            Refresh();
        }

        private void OnDisable()
        {
            if (progressTracker != null)
            {
                progressTracker.ProgressChanged -= HandleProgressChanged;
            }

            if (inventory != null)
            {
                inventory.Changed -= HandleInventoryChanged;
            }
        }

        /// <summary>
        /// Takes the map's pass bar. The map that is being played is the flow's business, not the
        /// HUD's, so the number is pushed in rather than looked up here.
        /// </summary>
        public void SetRequiredClearPercent(float value)
        {
            requiredClearPercent = Mathf.Clamp01(value);
            Refresh();
        }

        /// <summary>Redraws every part of the HUD from the current state.</summary>
        [ContextMenu("Refresh")]
        public void Refresh()
        {
            float clearPercent = progressTracker != null ? Mathf.Clamp01(progressTracker.ClearPercent) : 0f;

            if (clearPercentLabel != null)
            {
                clearPercentLabel.text = FormatPercent(clearPercent);
            }

            if (clearProgressFill != null)
            {
                clearProgressFill.fillAmount = clearPercent;
            }

            if (requiredPercentLabel != null)
            {
                requiredPercentLabel.text = FormatPercent(requiredClearPercent);
            }

            if (remainingBulletsLabel != null)
            {
                remainingBulletsLabel.text = (inventory != null ? inventory.TotalCount : 0).ToString();
            }

            RebuildBreakdown();
        }

        /// <summary>
        /// Percent as the player should read it. Floored rather than rounded, because 99.6% shown
        /// as "100%" tells the player they got a full clear that the run is about to deny them,
        /// and being told 99% when the truth is 99.6% costs nothing.
        /// </summary>
        private static string FormatPercent(float fraction)
        {
            int percent = Mathf.FloorToInt(Mathf.Clamp01(fraction) * 100f);
            return percent + "%";
        }

        private void HandleProgressChanged(float clearPercent)
        {
            Refresh();
        }

        private void HandleInventoryChanged()
        {
            Refresh();
        }

        private void RebuildBreakdown()
        {
            if (bulletBreakdownContainer == null)
            {
                return;
            }

            ClearRows();

            if (inventory == null)
            {
                return;
            }

            CollectRemaining();
            if (orderedCounts.Count == 0)
            {
                return;
            }

            EnsureLayout(bulletBreakdownContainer);

            for (int i = 0; i < orderedCounts.Count; i++)
            {
                KeyValuePair<string, int> entry = orderedCounts[i];
                RectTransform row = CreateRow(bulletBreakdownContainer, entry.Key, entry.Value);
                if (row != null)
                {
                    spawnedRows.Add(row);
                }
            }
        }

        /// <summary>
        /// The types with any bullets left, in catalogue order.
        ///
        /// Ordering by the catalogue rather than by the inventory's dictionary keeps the rows in
        /// one place while the player shoots: the breakdown is rebuilt after every shot, and rows
        /// that reshuffle whenever a type runs out are unreadable. Ids the catalogue does not know
        /// are still listed, at the end, so a mis-set id shows up rather than quietly vanishing.
        /// </summary>
        private void CollectRemaining()
        {
            orderedCounts.Clear();

            IReadOnlyList<BulletDefinition> catalogue = loadout != null ? loadout.Bullets : null;
            if (catalogue != null)
            {
                for (int i = 0; i < catalogue.Count; i++)
                {
                    BulletDefinition bullet = catalogue[i];
                    if (bullet == null)
                    {
                        continue;
                    }

                    int count = inventory.GetCount(bullet.Id);
                    if (count > 0)
                    {
                        orderedCounts.Add(new KeyValuePair<string, int>(bullet.Id, count));
                    }
                }
            }

            foreach (KeyValuePair<string, int> entry in inventory.Counts)
            {
                if (entry.Value <= 0)
                {
                    continue;
                }

                if (loadout != null && loadout.Find(entry.Key) != null)
                {
                    // Already listed above, in its catalogue position.
                    continue;
                }

                orderedCounts.Add(entry);
            }
        }

        /// <summary>
        /// Stacks the rows. LayoutGroup is [DisallowMultipleComponent], so a group the container
        /// already carries is the one to configure: adding a second one silently returns null
        /// rather than throwing, and every line after it would be a null reference.
        /// </summary>
        private void EnsureLayout(RectTransform parent)
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
            group.childAlignment = TextAnchor.UpperLeft;

            // Left alone when unset: Unity gives the group a zero RectOffset of its own, and
            // assigning null here only moves the failure into the layout pass.
            if (layoutPadding != null)
            {
                group.padding = layoutPadding;
            }

            // Rows stay the width the container gives them but keep their authored height, so a
            // short list does not stretch into a tall one.
            group.childForceExpandWidth = true;
            group.childForceExpandHeight = false;
            group.childControlWidth = true;
            group.childControlHeight = false;
        }

        private HorizontalOrVerticalLayoutGroup ResolveLayoutGroup(RectTransform parent)
        {
            HorizontalOrVerticalLayoutGroup group = parent.GetComponent<HorizontalOrVerticalLayoutGroup>();
            if (group == null)
            {
                // A grid group blocks the add the same way, and swapping one out from under
                // whoever authored it is not this component's call.
                LayoutGroup blocking = parent.GetComponent<LayoutGroup>();
                if (blocking != null)
                {
                    Debug.LogWarning(
                        $"{name}: {parent.name} already has a {blocking.GetType().Name}, so the bullet rows are "
                        + "laid out by that instead. Replace it with a Vertical Layout Group to get a column.",
                        this);
                    return null;
                }

                return parent.gameObject.AddComponent<VerticalLayoutGroup>();
            }

            if (!(group is VerticalLayoutGroup))
            {
                Debug.LogWarning(
                    $"{name}: {parent.name} has a {group.GetType().Name}, so the bullet rows stay in a row and "
                    + $"{nameof(rowSpacing)} is applied to it. Replace it with a Vertical Layout Group on the "
                    + "prefab to lay them out in a column.",
                    this);
            }

            return group;
        }

        private RectTransform CreateRow(RectTransform parent, string bulletId, int count)
        {
            RectTransform row = rowPrefab != null
                ? Instantiate(rowPrefab, parent)
                : CreateDefaultRow(parent);

            if (row == null)
            {
                return null;
            }

            row.gameObject.name = RowNamePrefix + bulletId;
            SetRowText(row, ResolveDisplayName(bulletId), count);
            return row;
        }

        private string ResolveDisplayName(string bulletId)
        {
            BulletDefinition bullet = loadout != null ? loadout.Find(bulletId) : null;
            return bullet != null ? bullet.DisplayName : bulletId;
        }

        /// <summary>
        /// Fills the row's labels. A prefab with two of them reads as a name column and a count
        /// column; one label has to carry both, which is why the count is formatted the same way
        /// in either case.
        /// </summary>
        private static void SetRowText(RectTransform row, string displayName, int count)
        {
            string countText = "x" + count;

            TMP_Text[] tmpLabels = row.GetComponentsInChildren<TMP_Text>(true);
            if (tmpLabels.Length >= 2)
            {
                tmpLabels[0].text = displayName;
                tmpLabels[1].text = countText;
                return;
            }

            if (tmpLabels.Length == 1)
            {
                tmpLabels[0].text = displayName + "  " + countText;
                return;
            }

            Text[] legacyLabels = row.GetComponentsInChildren<Text>(true);
            if (legacyLabels.Length >= 2)
            {
                legacyLabels[0].text = displayName;
                legacyLabels[1].text = countText;
                return;
            }

            if (legacyLabels.Length == 1)
            {
                legacyLabels[0].text = displayName + "  " + countText;
            }
        }

        /// <summary>
        /// Minimal stand-in so the breakdown is usable before anyone designs a row prefab. Uses
        /// the built-in font rather than TMP, which needs its essentials imported to render.
        /// </summary>
        private static RectTransform CreateDefaultRow(Transform parent)
        {
            GameObject rowObject = new GameObject("BulletRow", typeof(RectTransform), typeof(Text));
            RectTransform rowRect = (RectTransform)rowObject.transform;
            rowRect.SetParent(parent, false);
            rowRect.sizeDelta = new Vector2(220f, 28f);

            Text label = rowObject.GetComponent<Text>();
            label.alignment = TextAnchor.MiddleLeft;
            label.color = Color.white;
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            return rowRect;
        }

        /// <summary>
        /// Removes only what this view spawned. The container may hold authored children too, a
        /// background or a heading, and wiping every child would delete those the first time the
        /// breakdown is rebuilt. Rows left from an earlier rebuild are matched by name, since the
        /// tracking list does not survive an assembly reload.
        /// </summary>
        private void ClearRows()
        {
            for (int i = 0; i < spawnedRows.Count; i++)
            {
                DestroyRow(spawnedRows[i] != null ? spawnedRows[i].gameObject : null);
            }

            spawnedRows.Clear();

            if (bulletBreakdownContainer == null)
            {
                return;
            }

            for (int i = bulletBreakdownContainer.childCount - 1; i >= 0; i--)
            {
                GameObject child = bulletBreakdownContainer.GetChild(i).gameObject;
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
    }
}
