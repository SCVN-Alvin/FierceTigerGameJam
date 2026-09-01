using System;
using System.Collections.Generic;
using System.Globalization;
using GameJam.Audio;
using GameJam.Data;
using GameJam.Economy;
using GameJam.Gameplay.Cannon;
using GameJam.Gameplay.Combat;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GameJam.UI
{
    /// <summary>
    /// The shop where ammunition is bought and levelled up, replacing what
    /// <see cref="EconomyDebugPanel"/> did by hand.
    ///
    /// Every row shows the same kind of ammunition in one of three states, because a bullet is
    /// only ever one of them: not owned (buy it), owned below its ceiling (level it up), or
    /// finished (nothing left to sell). One button carries whichever of those applies, so the row
    /// never offers the player two things at once and never has to explain why one of them is
    /// dead.
    ///
    /// Nothing here decides what anything costs or whether it can be afforded: prices and refusals
    /// both come from <see cref="EconomyService"/>, so this view and the service can never end up
    /// disagreeing about what a purchase would do.
    /// </summary>
    public sealed class BulletShopView : MonoBehaviour
    {
        [Tooltip("The service every button here goes through, and the source of all prices.")]
        [SerializeField] private EconomyService economy;

        [Tooltip("The catalogue listed in the shop. Left empty, the service's own catalogue is "
                 + "used, which is the one it unlocks and upgrades against anyway.")]
        [SerializeField] private BulletLoadout loadout;

        [Tooltip("Rows are spawned under here. Falls back to this object's transform.")]
        [SerializeField] private RectTransform container;

        [Tooltip("Any prefab holding one Button and a TMP_Text or Text for the label. Objects "
                 + "named Name and Level are used for those two readings, and text inside the "
                 + "button is treated as its caption; whatever is missing is folded into the name "
                 + "label. Left empty, a plain row is generated so the shop works before anyone "
                 + "designs one.")]
        [SerializeField] private GameObject rowPrefab;

        [Tooltip("Optional. The wallet, redrawn whenever gold changes. Put a GoldView on this "
                 + "object instead to get the count-up; this is the plain total.")]
        [SerializeField] private TMP_Text goldLabel;

        [Tooltip("Optional. The equipped ammunition, drawn large over the garage table. Disabled "
                 + "while the equipped ammunition has no icon, so the table is not covered by a "
                 + "white square.")]
        [SerializeField] private Image previewImage;

        [Tooltip("Optional. The equipped ammunition's level name under the preview, e.g. ROCK II.")]
        [SerializeField] private TMP_Text previewCaption;

        [Header("Layout")]
        [SerializeField] private bool useVerticalLayout = true;
        [SerializeField] private float rowSpacing = 12f;
        [SerializeField] private RectOffset layoutPadding;

        private const string RowNamePrefix = "ShopRow_";

        /// <summary>Nothing left to sell. Dimmed rather than hidden, so the row keeps its shape.</summary>
        private const string MaxCaption = "MAX";

        /// <summary>Not for sale, or priced nowhere: an authoring gap said out loud.</summary>
        private const string UnavailableCaption = "N/A";

        private readonly List<Row> spawnedRows = new List<Row>();

        private ShopModelShowcase showcase;

        private void OnEnable()
        {
            if (economy != null)
            {
                // A sale changes what every other row can afford, not just the one that was
                // clicked, so the whole screen listens rather than each row refreshing itself.
                economy.GoldChanged += Refresh;
            }

            BulletLoadout catalogue = ResolveLoadout();
            if (catalogue != null)
            {
                // The garage is where the choice is shown, as the one row carrying the EQUIPPED
                // chip and the item on the preview table, so it has to follow a choice made
                // anywhere else - the pre-run pick screen picks ammunition too.
                catalogue.SelectionChanged += HandleSelectionChanged;
            }

            // A level bought elsewhere - the debug panel, a reward - has to show here too.
            UserData.Changed += Refresh;

            // Rebuilt rather than refreshed: the catalogue may have been re-authored, and rows do
            // not survive an assembly reload.
            Rebuild();
        }

        private void OnDisable()
        {
            if (showcase != null)
            {
                showcase.Hide();
            }

            if (economy != null)
            {
                // The service is an asset and outlives this scene, so a subscription left behind
                // would keep a destroyed row alive and fire into it on the next run.
                economy.GoldChanged -= Refresh;
            }

            BulletLoadout catalogue = ResolveLoadout();
            if (catalogue != null)
            {
                catalogue.SelectionChanged -= HandleSelectionChanged;
            }

            UserData.Changed -= Refresh;
        }

        private void HandleSelectionChanged(BulletDefinition bullet)
        {
            Refresh();
        }

        /// <summary>
        /// Throws away the rows and builds them again from the catalogue. Safe to call at any
        /// time: it clears before it spawns, so calling it twice leaves one set of rows.
        /// </summary>
        [ContextMenu("Rebuild")]
        public void Rebuild()
        {
            ClearRows();

            BulletLoadout catalogue = ResolveLoadout();
            if (economy == null || catalogue == null)
            {
                Debug.LogWarning(
                    $"{nameof(BulletShopView)} on \"{name}\" needs an {nameof(EconomyService)} and a "
                    + $"{nameof(BulletLoadout)} (its own, or one on the service); it lists nothing until it has both.",
                    this);
                return;
            }

            Transform parent = container != null ? container : transform;
            // EnsureLayout(parent);

            IReadOnlyList<BulletDefinition> bullets = catalogue.Bullets;
            if (bullets == null)
            {
                return;
            }

            for (int i = 0; i < bullets.Count; i++)
            {
                BulletDefinition bullet = bullets[i];
                if (bullet == null)
                {
                    continue;
                }

                // Locked ammunition is listed here, unlike on the pick screen: being able to see
                // what is for sale is the whole point of a shop.
                Row row = CreateRow(parent, bullet);

                // Captured per iteration, otherwise every button on the screen would end up
                // spending on, or equipping, the last kind of ammunition in the catalogue.
                BulletDefinition clicked = bullet;

                if (row.Action != null)
                {
                    row.Action.onClick.AddListener(() => HandleRowClicked(clicked));
                }

                if (row.Item != null && row.Item.SelectButton != null)
                {
                    row.Item.SelectButton.onClick.AddListener(() => HandleRowSelected(clicked));
                }

                spawnedRows.Add(row);
            }

            Refresh();
        }

        /// <summary>Redraws every reading and every button state without rebuilding the rows.</summary>
        [ContextMenu("Refresh")]
        public void Refresh()
        {
            if (economy == null)
            {
                return;
            }

            for (int i = 0; i < spawnedRows.Count; i++)
            {
                RefreshRow(spawnedRows[i]);
            }

            if (goldLabel != null)
            {
                goldLabel.text = economy.Gold.ToString();
            }

            RefreshPreview();
        }

        /// <summary>
        /// Draws the equipped ammunition over the garage table. It is the same sprite the row
        /// shows, only larger: a second piece of art per item would be one more thing to draw
        /// before a new kind of ammunition could ship.
        /// </summary>
        private void RefreshPreview()
        {
            if (previewImage == null && previewCaption == null)
            {
                return;
            }

            BulletLoadout catalogue = ResolveLoadout();
            BulletDefinition selected = catalogue != null ? catalogue.Selected : null;
            int level = catalogue != null ? catalogue.SelectedLevel : 1;

            if (previewImage != null)
            {
                Sprite sprite = selected != null ? selected.ResolveIcon(level) : null;
                previewImage.sprite = sprite;

                // Disabled rather than left drawing: an Image with no sprite is a white block
                // over the table the frame art already draws.
                previewImage.enabled = sprite != null;
            }

            // The 3D prop on the table. A bullet with no prefab of its own is photographed as
            // the fire controller's default ball - the same fallback the shot itself takes.
            if (previewImage != null)
            {
                GameObject model = null;
                if (selected != null)
                {
                    if (selected.ProjectilePrefab != null)
                    {
                        model = selected.ProjectilePrefab.gameObject;
                    }
                    else
                    {
                        GridKnockdownCannonFireController fire =
                            FindFirstObjectByType<GridKnockdownCannonFireController>(
                                FindObjectsInactive.Include);
                        if (fire != null && fire.DefaultProjectilePrefab != null)
                        {
                            model = fire.DefaultProjectilePrefab.gameObject;
                        }
                    }
                }

                if (showcase == null && model != null)
                {
                    showcase = ShopModelShowcase.Create(previewImage.rectTransform);
                }

                if (showcase != null)
                {
                    showcase.Show(model);
                }
            }

            if (previewCaption != null)
            {
                previewCaption.text = ResolveCaption(selected, level);
            }
        }

        /// <summary>
        /// The level's own name, e.g. "ROCK II". An unnamed level falls back to the ammunition's
        /// name rather than to nothing: a blank caption under the preview reads as a screen that
        /// failed to load, which is a worse answer than one missing its roman numeral.
        /// </summary>
        private static string ResolveCaption(BulletDefinition bullet, int level)
        {
            if (bullet == null)
            {
                return string.Empty;
            }

            BulletDefinition.Level shown = bullet.GetLevel(level);
            string caption = shown != null && !string.IsNullOrEmpty(shown.displayName)
                ? shown.displayName
                : bullet.DisplayName;

            return string.IsNullOrEmpty(caption) ? string.Empty : caption.ToUpperInvariant();
        }

        /// <summary>
        /// Draws one row in whichever of the three states its ammunition is in, and lets the
        /// service decide whether the button is live. A price the player cannot cover leaves the
        /// button visibly dead, which reads as "not yet" rather than as a tap that did nothing.
        /// </summary>
        private void RefreshRow(Row row)
        {
            if (row == null || row.Root == null || row.Bullet == null)
            {
                return;
            }

            BulletLoadout catalogue = ResolveLoadout();
            if (catalogue == null)
            {
                return;
            }

            bool unlocked = catalogue.IsUnlocked(row.Bullet);
            int level = catalogue.GetLevel(row.Bullet);
            int maxLevel = economy.GetMaxLevel(row.Bullet);

            if (row.Item != null)
            {
                // Equipped is a reading, not a control: this only says which row is the loaded
                // one. Selected always resolves to something, falling back to the starter, so
                // exactly one row per tab carries the EQUIPPED chip and the rest carry SELECT.
                // The chip is the whole of the cue now - the other rows are no longer dimmed.
                string buyCaption = ResolveBuyCaption(row.Bullet, unlocked, level, maxLevel, out bool buyInteractable);
                row.Item.Bind(
                    row.Bullet,
                    level,
                    maxLevel,
                    unlocked,
                    catalogue.Selected == row.Bullet,
                    buyCaption,
                    buyInteractable);
                return;
            }

            string caption;
            bool interactable;

            if (!unlocked)
            {
                // False from TryGetPurchasePrice means the bullet is not for sale at all, which is
                // said outright: a blank button would read as a price that failed to load.
                caption = economy.TryGetPurchasePrice(row.Bullet, out int purchasePrice)
                    ? $"Buy  {purchasePrice}"
                    : "Not for sale";
                interactable = economy.CanPurchase(row.Bullet);
            }
            else if (level >= maxLevel)
            {
                caption = "MAX";
                interactable = false;
            }
            else
            {
                // Between the floor and the ceiling but unpriced is an authoring gap, so the row
                // names it rather than pretending the ammunition is finished.
                caption = economy.TryGetUpgradePrice(row.Bullet, out int upgradePrice, out int _)
                    ? $"Upgrade  {upgradePrice}"
                    : "No upgrade priced";
                interactable = economy.CanUpgrade(row.Bullet);
            }

            if (row.Typed != null)
            {
                row.Typed.Bind(row.Bullet.DisplayName, $"Lv {level} / {maxLevel}", caption, interactable);
                return;
            }

            // Readings the prefab has no home for are folded into the name, so a one-label row
            // still tells the player everything rather than quietly dropping half of it.
            string nameText = row.Bullet.DisplayName;

            if (row.LevelLabel.Exists)
            {
                row.LevelLabel.Set($"Lv {level} / {maxLevel}");
            }
            else
            {
                nameText += $"   Lv {level} / {maxLevel}";
            }

            if (row.CaptionLabel.Exists)
            {
                row.CaptionLabel.Set(caption);
            }
            else
            {
                nameText += $"   {caption}";
            }

            row.NameLabel.Set(nameText);

            if (row.Action != null)
            {
                row.Action.interactable = interactable;
            }
        }

        /// <summary>
        /// What the garage row's one button says, and whether it is live. The button is never
        /// hidden: it is positioned rather than laid out, so hiding it would gain nothing and
        /// cost the row its shape.
        ///
        /// The price carries no "Buy" or "Upgrade" in front of it, because the coin is painted
        /// into the button art and which of the two a tap does is already said by the row - a
        /// locked row buys, an owned one levels up.
        /// </summary>
        private string ResolveBuyCaption(
            BulletDefinition bullet,
            bool unlocked,
            int level,
            int maxLevel,
            out bool interactable)
        {
            if (!unlocked)
            {
                interactable = economy.CanPurchase(bullet);

                // False means the ammunition is not for sale at all, which is said outright: a
                // blank button would read as a price that failed to load.
                return economy.TryGetPurchasePrice(bullet, out int purchasePrice)
                    ? FormatPrice(purchasePrice)
                    : UnavailableCaption;
            }

            if (level >= maxLevel)
            {
                interactable = false;
                return MaxCaption;
            }

            interactable = economy.CanUpgrade(bullet);

            // Between the floor and the ceiling but unpriced is an authoring gap, so the row
            // names it rather than pretending the ammunition is finished.
            return economy.TryGetUpgradePrice(bullet, out int upgradePrice, out int _)
                ? FormatPrice(upgradePrice)
                : UnavailableCaption;
        }

        /// <summary>
        /// Grouped, and always with a comma rather than whatever the device's locale prefers:
        /// the button is 145 px of art wide, and a separator that changes width by locale is one
        /// the layout was never measured against.
        /// </summary>
        private static string FormatPrice(int price)
        {
            return price.ToString("N0", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// What the row's single button does is decided here rather than when it was wired, so a
        /// bullet bought in one click can be upgraded by the next one without the row being
        /// rebuilt underneath the player's finger.
        /// </summary>
        private void HandleRowClicked(BulletDefinition bullet)
        {
            if (economy == null || bullet == null)
            {
                return;
            }

            BulletLoadout catalogue = ResolveLoadout();
            if (catalogue == null)
            {
                return;
            }

            // The result was previously discarded. It is read now because a refusal is the one
            // outcome with nothing to show for it: a success announces itself through GoldChanged
            // and its coin, while a refusal would otherwise be a tap that did nothing at all.
            bool bought = catalogue.IsUnlocked(bullet)
                ? economy.TryUpgrade(bullet)
                : economy.TryPurchase(bullet);

            if (!bought)
            {
                AudioService.Play(AudioSlot.Denied);
            }

            // A successful transaction already announced itself through GoldChanged, so this is
            // for the refused one: nothing changed, and the row should still show why.
            Refresh();
        }

        /// <summary>
        /// Loads the ammunition the row describes. Equipping costs nothing, so unlike the Buy
        /// button this does not go through the economy - but it still goes through the loadout
        /// rather than the save, because that is what refuses ammunition the player does not own
        /// and what tells the rest of the game the choice moved.
        ///
        /// The garage is now the second place this can be done; the pre-run pick screen is still
        /// the first, and both call the same Select, so neither has an opinion the other does not
        /// hear about.
        /// </summary>
        private void HandleRowSelected(BulletDefinition bullet)
        {
            BulletLoadout catalogue = ResolveLoadout();
            if (catalogue == null || bullet == null)
            {
                return;
            }

            // Refused for locked ammunition and for the row already loaded, in which case nothing
            // was announced; the redraw below is what keeps the screen honest either way.
            catalogue.Select(bullet);
            Refresh();
        }

        /// <summary>
        /// The catalogue to list. The service holds one too, and using it when this view has none
        /// avoids a shop that lists ammunition the service would refuse to sell.
        /// </summary>
        private BulletLoadout ResolveLoadout()
        {
            if (loadout != null)
            {
                return loadout;
            }

            return economy != null ? economy.Loadout : null;
        }

        private Row CreateRow(Transform parent, BulletDefinition bullet)
        {
            GameObject rowObject = rowPrefab != null
                ? Instantiate(rowPrefab, parent)
                : CreateDefaultRow(parent);

            // The garage row, checked before anything else: it is the row this shop is designed
            // around, and it already knows every one of its own parts.
            BulletTypeViewItem item = rowObject.GetComponent<BulletTypeViewItem>();
            if (item != null)
            {
                Button buy = item.BuyButton;
                rowObject.name = RowNamePrefix + bullet.Id;

                if (buy == null)
                {
                    Debug.LogWarning(
                        $"{name}: the row for {bullet.DisplayName} has no Buy button, so it can be read but "
                        + "not bought from. The row prefab needs a child named Buy carrying one.",
                        this);
                }

                return new Row
                {
                    Bullet = bullet,
                    Root = rowObject,
                    Action = buy,
                    Item = item,
                };
            }

            // A row that describes itself is used as it is. The name and position matching below
            // is only for rows that were not authored for this shop.
            BulletTypeUpgradeView typedRow = rowObject.GetComponent<BulletTypeUpgradeView>();

            // Resolved before the rename so the search cannot be thrown by an id that happens to
            // contain one of the words being looked for.
            Button action = typedRow != null
                ? typedRow.ActionButton
                : rowObject.GetComponentInChildren<Button>(true);

            rowObject.name = RowNamePrefix + bullet.Id;

            if (action == null)
            {
                Debug.LogWarning(
                    $"{name}: the row for {bullet.DisplayName} has no Button, so it can be read but not "
                    + "bought from. Put one on the row prefab.",
                    this);
            }

            if (typedRow != null)
            {
                return new Row
                {
                    Bullet = bullet,
                    Root = rowObject,
                    Action = action,
                    Typed = typedRow,
                };
            }

            return new Row
            {
                Bullet = bullet,
                Root = rowObject,
                Action = action,
                NameLabel = FindLabel(rowObject, "Name", action, true),
                LevelLabel = FindLabel(rowObject, "Level", action, false),
                CaptionLabel = FindCaption(action),
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

                // Null when something blocked the add after all, which the caller checks: a
                // missing layout costs the shop its spacing, not its buttons.
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
        /// Finds a label by the name its object carries. Text living inside the button is never a
        /// candidate: that is the button's own caption, and writing the ammunition's name over the
        /// price would leave the row unreadable.
        /// </summary>
        private static Label FindLabel(GameObject rowObject, string nameHint, Button action, bool allowFallback)
        {
            TMP_Text[] tmpLabels = rowObject.GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < tmpLabels.Length; i++)
            {
                if (IsCandidate(tmpLabels[i].transform, action)
                    && tmpLabels[i].name.IndexOf(nameHint, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return new Label(tmpLabels[i], null);
                }
            }

            Text[] legacyLabels = rowObject.GetComponentsInChildren<Text>(true);
            for (int i = 0; i < legacyLabels.Length; i++)
            {
                if (IsCandidate(legacyLabels[i].transform, action)
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
                if (IsCandidate(tmpLabels[i].transform, action))
                {
                    return new Label(tmpLabels[i], null);
                }
            }

            for (int i = 0; i < legacyLabels.Length; i++)
            {
                if (IsCandidate(legacyLabels[i].transform, action))
                {
                    return new Label(null, legacyLabels[i]);
                }
            }

            return default;
        }

        /// <summary>The button's own text, which is where the price and the state are written.</summary>
        private static Label FindCaption(Button action)
        {
            if (action == null)
            {
                return default;
            }

            TMP_Text tmp = action.GetComponentInChildren<TMP_Text>(true);
            if (tmp != null)
            {
                return new Label(tmp, null);
            }

            Text legacy = action.GetComponentInChildren<Text>(true);
            return legacy != null ? new Label(null, legacy) : default;
        }

        private static bool IsCandidate(Transform label, Button action)
        {
            return action == null || !label.IsChildOf(action.transform);
        }

        /// <summary>
        /// Minimal stand-in so the shop is usable before anyone designs a row. Uses the built-in
        /// font rather than TMP, which needs its essentials imported to render anything.
        /// </summary>
        private static GameObject CreateDefaultRow(Transform parent)
        {
            GameObject rowObject = new GameObject(
                "ShopRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            RectTransform rowRect = (RectTransform)rowObject.transform;
            rowRect.SetParent(parent, false);
            rowRect.sizeDelta = new Vector2(520f, 56f);

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

            CreateDefaultLabel(rowRect, "Name", new Vector2(230f, 48f), TextAnchor.MiddleLeft);
            CreateDefaultLabel(rowRect, "Level", new Vector2(110f, 48f), TextAnchor.MiddleCenter);
            CreateDefaultButton(rowRect, "Action", new Vector2(160f, 48f));

            return rowObject;
        }

        private static void CreateDefaultButton(Transform parent, string objectName, Vector2 size)
        {
            GameObject buttonObject = new GameObject(objectName, typeof(RectTransform), typeof(Image), typeof(Button));
            RectTransform buttonRect = (RectTransform)buttonObject.transform;
            buttonRect.SetParent(parent, false);
            buttonRect.sizeDelta = size;

            Text label = CreateDefaultLabel(buttonRect, "Label", size, TextAnchor.MiddleCenter);

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
            public Button Action;

            /// <summary>Set for the garage row, which draws itself from one state.</summary>
            public BulletTypeViewItem Item;

            /// <summary>Set when the row prefab describes its own parts; null for a found row.</summary>
            public BulletTypeUpgradeView Typed;

            public Label NameLabel;
            public Label LevelLabel;
            public Label CaptionLabel;
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
