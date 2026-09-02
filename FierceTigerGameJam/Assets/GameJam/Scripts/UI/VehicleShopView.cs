using System;
using System.Collections.Generic;
using System.Globalization;
using GameJam.Audio;
using GameJam.Data;
using GameJam.Economy;
using GameJam.Gameplay.Combat;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GameJam.UI
{
    /// <summary>
    /// The shop where vehicles are bought, levelled up and mounted.
    ///
    /// It lists the same way <see cref="BulletShopView"/> does - one row per catalogue entry,
    /// every price and every refusal coming from <see cref="EconomyService"/> - but each row
    /// carries two buttons rather than one. A bullet is only ever in one state at a time, so a
    /// single button can hold whatever applies; a vehicle can be worth upgrading and worth
    /// mounting at the same moment, and those are different purchases. Upgrading the vehicle the
    /// player is saving toward without driving it is the normal case, not an edge one.
    ///
    /// Buttons with nothing to do are disabled rather than hidden, so every row keeps the same
    /// shape and the list does not re-flow as the player buys their way down it.
    /// </summary>
    public sealed class VehicleShopView : MonoBehaviour
    {
        [Tooltip("The service every button here goes through, and the source of all prices.")]
        [SerializeField] private EconomyService economy;

        [Tooltip("The catalogue listed in the shop. Left empty, the service's own vehicle "
                 + "catalogue is used, which is the one it unlocks and upgrades against anyway.")]
        [SerializeField] private VehicleLoadout loadout;

        [Tooltip("Rows are spawned under here. Falls back to this object's transform.")]
        [SerializeField] private RectTransform container;

        [Tooltip("A row carrying a VehicleShopRowView, or children named Name, Level, Primary and "
                 + "Select for it to wire itself from. Left empty, a plain two-button row is "
                 + "generated so the shop works before anyone designs one.")]
        [SerializeField] private GameObject rowPrefab;

        [Tooltip("Optional. The wallet, redrawn whenever gold changes. Put a GoldView on this "
                 + "object instead to get the count-up; this is the plain total.")]
        [SerializeField] private TMP_Text goldLabel;

        [Tooltip("Optional. The equipped vehicle, drawn large over the garage table. Disabled "
                 + "while the equipped vehicle has no icon, so the table is not covered by a "
                 + "white square.")]
        [SerializeField] private Image previewImage;

        [Tooltip("Optional. The equipped vehicle's level and what that level is worth, under the "
                 + "preview, e.g. TRUCK II \u00b7 DAMAGE \u00d71.20.")]
        [SerializeField] private TMP_Text previewCaption;

        [Tooltip("Optional. The 3D window over the garage table. When it can show the equipped "
                 + "vehicle's model the flat icon above is hidden; when it cannot - no art for "
                 + "that level, or no rig in the scene - the icon is what the player sees.")]
        [SerializeField] private ModelPreviewView preview3D;

        private const string RowNamePrefix = "VehicleRow_";

        /// <summary>Nothing left to sell. Dimmed rather than hidden, so the row keeps its shape.</summary>
        private const string MaxCaption = "MAX";

        /// <summary>Not for sale, or priced nowhere: an authoring gap said out loud.</summary>
        private const string UnavailableCaption = "N/A";

        private readonly List<Row> spawnedRows = new List<Row>();

        private void OnEnable()
        {
            if (economy != null)
            {
                // A sale changes what every other row can afford, not just the one that was
                // clicked, so the whole screen listens rather than each row refreshing itself.
                economy.GoldChanged += Refresh;
            }

            VehicleLoadout catalogue = ResolveLoadout();
            if (catalogue != null)
            {
                catalogue.SelectionChanged += HandleSelectionChanged;
                catalogue.LevelChanged += HandleLevelChanged;
            }

            // A level bought elsewhere - the debug panel, a reward - has to show here too.
            UserData.Changed += Refresh;

            // Rebuilt rather than refreshed: the catalogue may have been re-authored, and rows do
            // not survive an assembly reload.
            Rebuild();
        }

        private void OnDisable()
        {
            if (economy != null)
            {
                // The service is an asset and outlives this scene, so a subscription left behind
                // would keep a destroyed row alive and fire into it on the next run.
                economy.GoldChanged -= Refresh;
            }

            VehicleLoadout catalogue = ResolveLoadout();
            if (catalogue != null)
            {
                catalogue.SelectionChanged -= HandleSelectionChanged;
                catalogue.LevelChanged -= HandleLevelChanged;
            }

            UserData.Changed -= Refresh;
        }

        /// <summary>
        /// Throws away the rows and builds them again from the catalogue. Safe to call at any
        /// time: it clears before it spawns, so calling it twice leaves one set of rows.
        /// </summary>
        [ContextMenu("Rebuild")]
        public void Rebuild()
        {
            ClearRows();

            VehicleLoadout catalogue = ResolveLoadout();
            if (economy == null || catalogue == null)
            {
                Debug.LogWarning(
                    $"{nameof(VehicleShopView)} on \"{name}\" needs an {nameof(EconomyService)} and a "
                    + $"{nameof(VehicleLoadout)} (its own, or one on the service); it lists nothing until it has both.",
                    this);
                return;
            }

            Transform parent = container != null ? container : transform;

            IReadOnlyList<VehicleDefinition> vehicles = catalogue.Vehicles;
            if (vehicles == null)
            {
                return;
            }

            for (int i = 0; i < vehicles.Count; i++)
            {
                VehicleDefinition vehicle = vehicles[i];
                if (vehicle == null)
                {
                    continue;
                }

                // Locked vehicles are listed: seeing what is for sale is the point of a shop.
                Row row = CreateRow(parent, vehicle);
                if (row == null)
                {
                    continue;
                }

                // Captured per iteration, otherwise every button on the screen would end up
                // spending on the last vehicle in the catalogue.
                VehicleDefinition clicked = vehicle;

                if (row.Item != null)
                {
                    // Buy spends; the rest of the row mounts. Buying still equips as well, which
                    // is not made redundant by this: a vehicle bought and left unmounted would be
                    // gold spent for no visible change.
                    if (row.Item.BuyButton != null)
                    {
                        row.Item.BuyButton.onClick.AddListener(() => HandlePrimaryClicked(clicked));
                    }

                    if (row.Item.SelectButton != null)
                    {
                        row.Item.SelectButton.onClick.AddListener(() => HandleSelectClicked(clicked));
                    }
                }
                else if (row.View != null)
                {
                    if (row.View.PrimaryButton != null)
                    {
                        row.View.PrimaryButton.onClick.AddListener(() => HandlePrimaryClicked(clicked));
                    }

                    if (row.View.SelectButton != null)
                    {
                        row.View.SelectButton.onClick.AddListener(() => HandleSelectClicked(clicked));
                    }
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
        /// Draws the equipped vehicle over the garage table, with what its level is worth written
        /// underneath. The multiplier lives here rather than on the row: the row has room for a
        /// name, a level and a price and nothing else, and a player asked to pay for level 2 has
        /// no other way to see what level 2 buys them.
        ///
        /// The vehicle is drawn twice over, and only ever one of the two at a time: the real model
        /// spinning in the 3D window when there is art for it, and the flat icon when there is
        /// not. That was once argued against here on the grounds that one of two ways of drawing
        /// a vehicle would always be the one nobody updated - which holds for two pieces of art
        /// and not for these two, because both read the same catalogue entry. The icon is now the
        /// fallback rather than a second description, and a vehicle with no model still has a
        /// picture.
        /// </summary>
        private void RefreshPreview()
        {
            if (previewImage == null && previewCaption == null && preview3D == null)
            {
                return;
            }

            VehicleLoadout catalogue = ResolveLoadout();
            VehicleDefinition selected = catalogue != null ? catalogue.Selected : null;
            int level = catalogue != null ? catalogue.SelectedLevel : 1;

            // First, because whether it worked is what decides if the icon is drawn at all.
            bool showingModel = preview3D != null
                && preview3D.Show(selected != null ? selected.ResolveModelPrefab(level) : null, level);

            if (previewImage != null)
            {
                Sprite sprite = selected != null ? selected.ResolveIcon(level) : null;
                previewImage.sprite = sprite;

                // Disabled rather than left drawing: an Image with no sprite is a white block
                // over the table the frame art already draws. Down as well while the model is up,
                // so the icon is not sitting on top of the thing it stands in for.
                previewImage.enabled = !showingModel && sprite != null;
            }

            if (previewCaption != null)
            {
                previewCaption.text = ResolveCaption(selected, level);
            }
        }

        /// <summary>
        /// The level's own name and its multiplier, e.g. "TRUCK II \u00b7 DAMAGE \u00d71.20". An unnamed
        /// level falls back to the vehicle's name rather than to nothing: a blank caption reads as
        /// a screen that failed to load, which is worse than one missing its roman numeral.
        /// </summary>
        private static string ResolveCaption(VehicleDefinition vehicle, int level)
        {
            if (vehicle == null)
            {
                return string.Empty;
            }

            VehicleDefinition.Level shown = vehicle.GetLevel(level);
            string displayName = shown != null && !string.IsNullOrEmpty(shown.displayName)
                ? shown.displayName
                : vehicle.DisplayName;

            if (string.IsNullOrEmpty(displayName))
            {
                displayName = string.Empty;
            }

            return string.Format(
                CultureInfo.InvariantCulture,
                "{0} · DAMAGE ×{1:0.00} · AMMO +{2}",
                displayName.ToUpperInvariant(),
                vehicle.GetDamageMultiplier(level),
                vehicle.ResolveAmmoBonus(level));
        }

        /// <summary>
        /// Draws one row in whichever state its vehicle is in, and lets the service decide whether
        /// the primary button is live. A price the player cannot cover leaves the button visibly
        /// dead, which reads as "not yet" rather than as a tap that did nothing.
        ///
        /// The level reading carries the multiplier, because that number is the whole product: a
        /// player asked to pay for level 2 has no other way to see what level 2 is worth.
        /// </summary>
        private void RefreshRow(Row row)
        {
            if (row == null || row.Root == null || row.Vehicle == null)
            {
                return;
            }

            VehicleLoadout catalogue = ResolveLoadout();
            if (catalogue == null)
            {
                return;
            }

            bool unlocked = catalogue.IsUnlocked(row.Vehicle);
            int level = catalogue.GetLevel(row.Vehicle);
            int maxLevel = economy.GetVehicleMaxLevel(row.Vehicle);
            bool selected = catalogue.Selected == row.Vehicle;

            if (row.Item != null)
            {
                // Equipped is a reading here, not a control: this only says which row is the
                // mounted one, and Selected always resolves to something, so exactly one row per
                // list carries the EQUIPPED chip and the rest carry SELECT. The chip is the whole
                // of the cue now - the rows are no longer dimmed against the equipped one.
                string buyCaption = ResolveBuyCaption(row.Vehicle, unlocked, level, maxLevel, out bool buyInteractable);
                row.Item.Bind(
                    row.Vehicle,
                    level,
                    maxLevel,
                    unlocked,
                    selected,
                    buyCaption,
                    buyInteractable);
                return;
            }

            if (row.View == null)
            {
                return;
            }

            string levelText;
            string primaryText;
            bool primaryInteractable;

            if (!unlocked)
            {
                levelText = "Locked";

                // False from TryGetVehiclePurchasePrice means the vehicle is not for sale at all,
                // which is said outright: a blank button would read as a price that failed to load.
                primaryText = economy.TryGetVehiclePurchasePrice(row.Vehicle, out int purchasePrice)
                    ? $"Buy  {purchasePrice}"
                    : "Not for sale";
                primaryInteractable = economy.CanPurchaseVehicle(row.Vehicle);
            }
            else
            {
                levelText = $"Lv {level}/{maxLevel}  x{row.Vehicle.GetDamageMultiplier(level):0.00}";

                if (level >= maxLevel)
                {
                    primaryText = "MAX";
                    primaryInteractable = false;
                }
                else
                {
                    // Between the floor and the ceiling but unpriced is an authoring gap, so the
                    // row names it rather than pretending the vehicle is finished.
                    primaryText = economy.TryGetVehicleUpgradePrice(row.Vehicle, out int upgradePrice, out int _)
                        ? $"Upgrade  {upgradePrice}"
                        : "No upgrade priced";
                    primaryInteractable = economy.CanUpgradeVehicle(row.Vehicle);
                }
            }

            // "Selected" rather than a hidden button, so the mounted vehicle is readable at a
            // glance instead of being the one row that looks unfinished.
            string selectText = selected ? "Selected" : "Select";
            bool selectInteractable = unlocked && !selected;

            row.View.Bind(
                row.Vehicle.DisplayName,
                levelText,
                primaryText,
                primaryInteractable,
                selectText,
                selectInteractable);
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
            VehicleDefinition vehicle,
            bool unlocked,
            int level,
            int maxLevel,
            out bool interactable)
        {
            if (!unlocked)
            {
                interactable = economy.CanPurchaseVehicle(vehicle);

                // False means the vehicle is not for sale at all, which is said outright: a blank
                // button would read as a price that failed to load.
                return economy.TryGetVehiclePurchasePrice(vehicle, out int purchasePrice)
                    ? FormatPrice(purchasePrice)
                    : UnavailableCaption;
            }

            if (level >= maxLevel)
            {
                interactable = false;
                return MaxCaption;
            }

            interactable = economy.CanUpgradeVehicle(vehicle);

            // Between the floor and the ceiling but unpriced is an authoring gap, so the row
            // names it rather than pretending the vehicle is finished.
            return economy.TryGetVehicleUpgradePrice(vehicle, out int upgradePrice, out int _)
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
        /// What the primary button does is decided here rather than when it was wired, so a
        /// vehicle bought in one click can be upgraded by the next one without the row being
        /// rebuilt underneath the player's finger.
        /// </summary>
        private void HandlePrimaryClicked(VehicleDefinition vehicle)
        {
            if (economy == null || vehicle == null)
            {
                return;
            }

            VehicleLoadout catalogue = ResolveLoadout();
            if (catalogue == null)
            {
                return;
            }

            // Read rather than discarded, for the same reason the ammunition shop reads it: a
            // refusal is the one outcome with nothing to show for it, so it is the one that has
            // to say so out loud.
            bool bought = catalogue.IsUnlocked(vehicle)
                ? economy.TryUpgradeVehicle(vehicle)
                : economy.TryPurchaseVehicle(vehicle);

            if (!bought)
            {
                AudioService.Play(AudioSlot.Denied);
            }

            // A successful transaction already announced itself through GoldChanged, so this is
            // for the refused one: nothing changed, and the row should still show why.
            Refresh();
        }

        /// <summary>
        /// Mounting costs nothing, so this does not go through the economy. It still goes through
        /// the loadout rather than the save, which is what refuses a locked vehicle and what tells
        /// the mount to swap the model.
        ///
        /// Reached from the garage row itself and from the fallback row's Select button, which are
        /// the same decision made on two different pictures.
        /// </summary>
        private void HandleSelectClicked(VehicleDefinition vehicle)
        {
            VehicleLoadout catalogue = ResolveLoadout();
            if (catalogue == null || vehicle == null)
            {
                return;
            }

            catalogue.Select(vehicle);
            Refresh();
        }

        private void HandleSelectionChanged(VehicleDefinition vehicle)
        {
            Refresh();
        }

        private void HandleLevelChanged(VehicleDefinition vehicle, int level)
        {
            Refresh();
        }

        /// <summary>
        /// The catalogue to list. The service holds one too, and using it when this view has none
        /// avoids a shop that lists vehicles the service would refuse to sell.
        /// </summary>
        private VehicleLoadout ResolveLoadout()
        {
            if (loadout != null)
            {
                return loadout;
            }

            return economy != null ? economy.VehicleLoadout : null;
        }

        /// <summary>
        /// Rows describe themselves here, unlike in the bullet shop. That shop has to find labels
        /// by name and the button by being the only one, which cannot tell two buttons apart, so
        /// a vehicle row without a <see cref="VehicleTypeViewItem"/> or a
        /// <see cref="VehicleShopRowView"/> gets the second one added rather than being guessed at.
        /// </summary>
        private Row CreateRow(Transform parent, VehicleDefinition vehicle)
        {
            GameObject rowObject = rowPrefab != null
                ? Instantiate(rowPrefab, parent)
                : CreateDefaultRow(parent);

            // The garage row, checked before anything else. No VehicleShopRowView is added
            // alongside it: that component would go looking for a Select button this row does not
            // have and warn about it every time the tab opens.
            VehicleTypeViewItem item = rowObject.GetComponent<VehicleTypeViewItem>();
            if (item != null)
            {
                Button buy = item.BuyButton;
                rowObject.name = RowNamePrefix + vehicle.Id;

                if (buy == null)
                {
                    Debug.LogWarning(
                        $"{name}: the row for {vehicle.DisplayName} has no Buy button, so it can be read but "
                        + "not bought from. The row prefab needs a child named Buy carrying one.",
                        this);
                }

                return new Row
                {
                    Vehicle = vehicle,
                    Root = rowObject,
                    Item = item,
                };
            }

            VehicleShopRowView view = rowObject.GetComponent<VehicleShopRowView>();
            if (view == null)
            {
                // Added rather than refused: the component wires itself from the children's names
                // in Awake, so a row prefab that was only ever designed still works.
                view = rowObject.AddComponent<VehicleShopRowView>();
            }

            rowObject.name = RowNamePrefix + vehicle.Id;

            if (view.PrimaryButton == null || view.SelectButton == null)
            {
                Debug.LogWarning(
                    $"{name}: the row for {vehicle.DisplayName} is missing one of its two buttons, so part of "
                    + "it can be read but not used. The row prefab needs children named Primary and Select.",
                    this);
            }

            return new Row
            {
                Vehicle = vehicle,
                Root = rowObject,
                View = view,
            };
        }

        /// <summary>
        /// Minimal stand-in so the shop is usable before anyone designs a row. Named the way
        /// <see cref="VehicleShopRowView"/> looks for its parts, so the component wires itself.
        /// </summary>
        private static GameObject CreateDefaultRow(Transform parent)
        {
            GameObject rowObject = new GameObject(
                "VehicleRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            RectTransform rowRect = (RectTransform)rowObject.transform;
            rowRect.SetParent(parent, false);
            rowRect.sizeDelta = new Vector2(680f, 56f);

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

            CreateDefaultLabel(rowRect, "Name", new Vector2(200f, 48f), TextAlignmentOptions.Left);
            CreateDefaultLabel(rowRect, "Level", new Vector2(200f, 48f), TextAlignmentOptions.Center);
            CreateDefaultButton(rowRect, "Primary", new Vector2(160f, 48f));
            CreateDefaultButton(rowRect, "Select", new Vector2(120f, 48f));

            return rowObject;
        }

        private static void CreateDefaultButton(Transform parent, string objectName, Vector2 size)
        {
            GameObject buttonObject = new GameObject(objectName, typeof(RectTransform), typeof(Image), typeof(Button));
            RectTransform buttonRect = (RectTransform)buttonObject.transform;
            buttonRect.SetParent(parent, false);
            buttonRect.sizeDelta = size;

            TMP_Text label = CreateDefaultLabel(buttonRect, "Label", size, TextAlignmentOptions.Center);

            // The caption stretches with its button, which the row's layout group may resize.
            RectTransform labelRect = (RectTransform)label.transform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
        }

        private static TMP_Text CreateDefaultLabel(
            Transform parent,
            string objectName,
            Vector2 size,
            TextAlignmentOptions alignment)
        {
            GameObject labelObject = new GameObject(objectName, typeof(RectTransform));
            RectTransform labelRect = (RectTransform)labelObject.transform;
            labelRect.SetParent(parent, false);
            labelRect.sizeDelta = size;

            TextMeshProUGUI label = labelObject.AddComponent<TextMeshProUGUI>();
            label.alignment = alignment;
            label.fontSize = 28f;
            label.color = Color.white;
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

        /// <summary>One spawned row, and the vehicle it stands for.</summary>
        private sealed class Row
        {
            public VehicleDefinition Vehicle;
            public GameObject Root;

            /// <summary>Set for the garage row, which draws itself from one state.</summary>
            public VehicleTypeViewItem Item;

            /// <summary>Set for the older two-button row, which is the fallback.</summary>
            public VehicleShopRowView View;
        }
    }
}
