using System;
using System.Collections.Generic;
using GameJam.Economy;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GameJam.UI
{
    /// <summary>
    /// A mock storefront for buying gold.
    ///
    /// PLACEHOLDER, NOT A REAL PURCHASE. A real in-app purchase needs a store configured on both
    /// platforms, product ids registered against it, and a receipt to validate, none of which
    /// exists yet. Rather than block the rest of the economy on that, every button here simply
    /// calls <see cref="EconomyService.GrantGold"/>: the player is charged nothing and given the
    /// pack. That makes gold free to anyone who can open this screen, so the click handler has to
    /// be rewritten against the real billing API (Unity IAP or the platform's own) before this
    /// ships, and the on-screen note exists so nobody mistakes it for a finished screen in the
    /// meantime.
    ///
    /// What is worth keeping is the shape: packs are authored data, and the gold they hand over
    /// goes through the one service that owns the wallet, so swapping the grant for a real
    /// purchase later touches this one method and nothing else.
    /// </summary>
    public sealed class IapShopView : MonoBehaviour
    {
        /// <summary>
        /// One thing on sale: what it is called, how much gold it hands over, and what it would
        /// cost if any of this charged money. The price is a plain string rather than a number
        /// because a real store returns a localised, currency-formatted label, and pretending it
        /// is a float now would only have to be undone later.
        /// </summary>
        [Serializable]
        public struct GoldPack
        {
            [Tooltip("Shown as the row's title, e.g. \"Pile of Gold\".")]
            public string displayName;

            [Tooltip("Gold handed over on click. Nothing is charged for it: see the class note.")]
            public int gold;

            [Tooltip("What it would cost, exactly as it should read on screen, e.g. \"$0.99\". A "
                     + "real store supplies this string itself, localised to the player's region.")]
            public string priceLabel;
        }

        [Tooltip("The service the gold is granted through. Everything else about the wallet is "
                 + "its business, including saving.")]
        [SerializeField] private EconomyService economy;

        [Tooltip("Rows are spawned under here. Falls back to this object's transform.")]
        [SerializeField] private RectTransform container;

        [Tooltip("Any prefab holding one Button and a TMP_Text or Text for the label. Objects "
                 + "named Name and Gold are used for those two readings, and text inside the "
                 + "button is treated as its price caption; whatever is missing is folded into "
                 + "the name label. Left empty, a plain row is generated so the screen works "
                 + "before anyone designs one.")]
        [SerializeField] private GameObject rowPrefab;

        [Tooltip("Optional. The wallet, redrawn whenever gold changes.")]
        [SerializeField] private TMP_Text goldLabel;

        [Tooltip("Optional. Where the placeholder note is shown. Left empty, a note row is "
                 + "generated at the top of the list, because this screen must never look like a "
                 + "real storefront to whoever is testing it.")]
        [SerializeField] private TMP_Text noteLabel;

        [TextArea]
        [Tooltip("The on-screen warning. Change the wording freely, but do not empty it while the "
                 + "buttons still hand out gold for nothing.")]
        [SerializeField] private string placeholderNote =
            "PLACEHOLDER STORE: these buttons grant gold immediately and charge nothing. "
            + "Replace them with real in-app purchases before shipping.";

        [Tooltip("What is on sale. Defaults to three packs, each worth a little more per unit of "
                 + "currency than the one below it, which is how a real store is priced.")]
        [SerializeField] private GoldPack[] packs =
        {
            new GoldPack { displayName = "Handful of Gold", gold = 500, priceLabel = "$0.99" },
            new GoldPack { displayName = "Sack of Gold", gold = 3000, priceLabel = "$4.99" },
            new GoldPack { displayName = "Chest of Gold", gold = 7500, priceLabel = "$9.99" },
        };

        [Header("Layout")]
        [SerializeField] private bool useVerticalLayout = true;
        [SerializeField] private float rowSpacing = 12f;
        [SerializeField] private RectOffset layoutPadding;

        private const string RowNamePrefix = "IapRow_";
        private const string NoteObjectName = RowNamePrefix + "Note";

        private readonly List<Row> spawnedRows = new List<Row>();

        private void OnEnable()
        {
            if (economy != null)
            {
                economy.GoldChanged += Refresh;
            }

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
        }

        /// <summary>
        /// Throws away the rows and builds them again from the authored packs. Safe to call at any
        /// time: it clears before it spawns, so calling it twice leaves one set of rows.
        /// </summary>
        [ContextMenu("Rebuild")]
        public void Rebuild()
        {
            ClearRows();

            if (economy == null)
            {
                Debug.LogWarning(
                    $"{nameof(IapShopView)} on \"{name}\" has no {nameof(EconomyService)}, so it lists nothing.",
                    this);
                return;
            }

            Transform parent = container != null ? container : transform;
            EnsureLayout(parent);
            ShowPlaceholderNote(parent);

            if (packs == null)
            {
                return;
            }

            for (int i = 0; i < packs.Length; i++)
            {
                GoldPack pack = packs[i];
                if (pack.gold <= 0)
                {
                    // A pack that hands over nothing is an authoring slip, not an offer.
                    Debug.LogWarning(
                        $"{name}: pack {i} (\"{pack.displayName}\") gives no gold, so it is not listed.",
                        this);
                    continue;
                }

                Row row = CreateRow(parent, pack, i);

                if (row.Action != null)
                {
                    // Captured per iteration, otherwise every button on the screen would end up
                    // granting the last pack in the list.
                    GoldPack clicked = pack;
                    row.Action.onClick.AddListener(() => HandlePackClicked(clicked));
                }

                spawnedRows.Add(row);
            }

            Refresh();
        }

        /// <summary>
        /// Redraws the wallet. The packs themselves are authored data and do not change while the
        /// screen is open, so the rows only need writing once, at build time.
        /// </summary>
        [ContextMenu("Refresh")]
        public void Refresh()
        {
            if (economy != null && goldLabel != null)
            {
                goldLabel.text = economy.Gold.ToString();
            }
        }

        /// <summary>
        /// Hands over the pack. THIS CHARGES NOTHING: it is a stand-in for a purchase flow, and
        /// the real one has to complete a store transaction and validate its receipt before any
        /// gold is granted. Everything after that point stays as it is here.
        /// </summary>
        private void HandlePackClicked(GoldPack pack)
        {
            if (economy == null || pack.gold <= 0)
            {
                return;
            }

            economy.GrantGold(pack.gold);

            Debug.Log(
                $"{name}: granted {pack.gold} gold for \"{pack.displayName}\" ({pack.priceLabel}) without "
                + "charging anything. This is the placeholder store, not a purchase.",
                this);
        }

        /// <summary>
        /// Writes the warning to the assigned label, or spawns one when there is none. The note is
        /// not optional in the way the label is: a screen that gives away gold has to say so.
        /// </summary>
        private void ShowPlaceholderNote(Transform parent)
        {
            if (string.IsNullOrEmpty(placeholderNote))
            {
                return;
            }

            if (noteLabel != null)
            {
                noteLabel.text = placeholderNote;
                return;
            }

            // Named with the row prefix so the next rebuild sweeps it away with everything else.
            Text generated = CreateDefaultLabel(parent, NoteObjectName, new Vector2(520f, 72f), TextAnchor.MiddleLeft);
            generated.text = placeholderNote;
            generated.color = new Color(0.55f, 0.1f, 0.1f);
        }

        private Row CreateRow(Transform parent, GoldPack pack, int index)
        {
            GameObject rowObject = rowPrefab != null
                ? Instantiate(rowPrefab, parent)
                : CreateDefaultRow(parent);

            Button action = rowObject.GetComponentInChildren<Button>(true);

            // The index keeps the name unique even when two packs are called the same thing.
            rowObject.name = RowNamePrefix + index;

            if (action == null)
            {
                Debug.LogWarning(
                    $"{name}: the row for \"{pack.displayName}\" has no Button, so it can be read but not "
                    + "bought from. Put one on the row prefab.",
                    this);
            }

            Label nameLabel = FindLabel(rowObject, "Name", action, true);
            Label goldRowLabel = FindLabel(rowObject, "Gold", action, false);
            Label captionLabel = FindCaption(action);

            // Written once, here: nothing about a pack changes while the screen is open. Readings
            // the prefab has no home for are folded into the name, so a one-label row still tells
            // the player everything rather than quietly dropping half of it.
            string nameText = string.IsNullOrEmpty(pack.displayName) ? "Gold Pack" : pack.displayName;
            string priceText = string.IsNullOrEmpty(pack.priceLabel) ? "Free (placeholder)" : pack.priceLabel;

            if (goldRowLabel.Exists)
            {
                goldRowLabel.Set($"{pack.gold} gold");
            }
            else
            {
                nameText += $"   {pack.gold} gold";
            }

            if (captionLabel.Exists)
            {
                captionLabel.Set(priceText);
            }
            else
            {
                nameText += $"   {priceText}";
            }

            nameLabel.Set(nameText);

            return new Row
            {
                Root = rowObject,
                Action = action,
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
                // missing layout costs the screen its spacing, not its buttons.
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
        /// candidate: that is the button's own caption, and writing the pack's name over the price
        /// would leave the row unreadable.
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

        /// <summary>The button's own text, which is where the price label is written.</summary>
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
        /// Minimal stand-in so the screen is usable before anyone designs a row. Uses the built-in
        /// font rather than TMP, which needs its essentials imported to render anything.
        /// </summary>
        private static GameObject CreateDefaultRow(Transform parent)
        {
            GameObject rowObject = new GameObject(
                "IapRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
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
            CreateDefaultLabel(rowRect, "Gold", new Vector2(130f, 48f), TextAnchor.MiddleCenter);
            CreateDefaultButton(rowRect, "Action", new Vector2(140f, 48f));

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
                    // Catches the generated note too, which carries the same prefix.
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

        /// <summary>One spawned row, and the button the view has to keep hold of.</summary>
        private sealed class Row
        {
            public GameObject Root;
            public Button Action;
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
