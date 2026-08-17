using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GameJam.Gameplay.Wall
{
    /// <summary>
    /// Builds one button per map in the config and keeps the selected one highlighted.
    /// </summary>
    public sealed class MapListView : MonoBehaviour
    {
        [SerializeField] private MapSelection mapSelection;

        [Tooltip("Buttons are spawned under here. Falls back to this object's transform.")]
        [SerializeField] private RectTransform container;

        [Tooltip("Any prefab with a Button, and a TMP_Text or Text somewhere in it for the label. "
                 + "Left empty, a plain button is generated so the list still works.")]
        [SerializeField] private Button buttonPrefab;

        [SerializeField] private Color normalColor = Color.white;
        [SerializeField] private Color selectedColor = new Color(0.55f, 0.85f, 1f);

        [Header("Layout")]
        [SerializeField] private bool useHorizontalLayout = true;
        [SerializeField] private float buttonSpacing = 16f;
        [SerializeField] private RectOffset layoutPadding;

        private const string ButtonNamePrefix = "MapButton_";

        private readonly List<Button> spawnedButtons = new List<Button>();

        private void OnEnable()
        {
            if (mapSelection != null)
            {
                mapSelection.SelectionChanged += HandleSelectionChanged;
            }

            Rebuild();
        }

        private void OnDisable()
        {
            if (mapSelection != null)
            {
                mapSelection.SelectionChanged -= HandleSelectionChanged;
            }
        }

        [ContextMenu("Rebuild")]
        public void Rebuild()
        {
            ClearButtons();

            if (mapSelection == null || mapSelection.Config == null)
            {
                Debug.LogWarning($"{nameof(MapListView)} needs a {nameof(MapSelection)} with a config.", this);
                return;
            }

            Transform parent = container != null ? container : transform;
            EnsureLayout(parent);

            MapConfig config = mapSelection.Config;

            for (int i = 0; i < config.Count; i++)
            {
                MapInfo map = config.Get(i);
                if (map == null)
                {
                    continue;
                }

                Button button = CreateButton(parent, map);

                // Captured per iteration so every button keeps its own index.
                int index = i;
                button.onClick.AddListener(() => mapSelection.SelectByIndex(index));
                spawnedButtons.Add(button);
            }

            RefreshHighlight();
        }

        /// <summary>
        /// Puts real gaps between the buttons. LayoutGroup is [DisallowMultipleComponent], so a
        /// group the container already carries is the one that has to be configured - adding a
        /// second one next to it silently returns null rather than throwing.
        /// </summary>
        private void EnsureLayout(Transform parent)
        {
            if (!useHorizontalLayout)
            {
                return;
            }

            HorizontalOrVerticalLayoutGroup group = ResolveLayoutGroup(parent);
            if (group == null)
            {
                return;
            }

            group.enabled = true;
            group.spacing = buttonSpacing;
            group.childAlignment = TextAnchor.MiddleCenter;

            // Left alone when unset: Unity gives the group a zero RectOffset of its own, and
            // assigning null here only moves the failure into the layout pass.
            if (layoutPadding != null)
            {
                group.padding = layoutPadding;
            }

            // Buttons keep the size their prefab defines rather than being stretched to fill.
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
                        $"{name}: {parent.name} already has a {blocking.GetType().Name}, so the buttons are "
                        + "laid out by that instead. Replace it with a Horizontal Layout Group to get a row.",
                        this);
                    return null;
                }

                return parent.gameObject.AddComponent<HorizontalLayoutGroup>();
            }

            if (!(group is HorizontalLayoutGroup))
            {
                Debug.LogWarning(
                    $"{name}: {parent.name} has a {group.GetType().Name}, so the buttons stay in a column and "
                    + $"{nameof(buttonSpacing)} is applied to it. Replace it with a Horizontal Layout Group on "
                    + "the prefab to lay them out in a row.",
                    this);
            }

            return group;
        }

        private Button CreateButton(Transform parent, MapInfo map)
        {
            Button button = buttonPrefab != null
                ? Instantiate(buttonPrefab, parent)
                : CreateDefaultButton(parent);

            button.gameObject.name = ButtonNamePrefix + map.Id;
            SetLabel(button, map.DisplayName);
            return button;
        }

        private static void SetLabel(Button button, string text)
        {
            TMP_Text tmpLabel = button.GetComponentInChildren<TMP_Text>(true);
            if (tmpLabel != null)
            {
                tmpLabel.text = text;
                return;
            }

            Text legacyLabel = button.GetComponentInChildren<Text>(true);
            if (legacyLabel != null)
            {
                legacyLabel.text = text;
            }
        }

        /// <summary>
        /// Minimal stand-in so the list is usable before anyone designs a button prefab. Uses the
        /// built-in font rather than TMP, which needs its essentials imported to render anything.
        /// </summary>
        private static Button CreateDefaultButton(Transform parent)
        {
            GameObject buttonObject = new GameObject("MapButton", typeof(RectTransform), typeof(Image), typeof(Button));
            RectTransform buttonRect = (RectTransform)buttonObject.transform;
            buttonRect.SetParent(parent, false);
            buttonRect.sizeDelta = new Vector2(220f, 48f);

            GameObject labelObject = new GameObject("Label", typeof(RectTransform), typeof(Text));
            RectTransform labelRect = (RectTransform)labelObject.transform;
            labelRect.SetParent(buttonRect, false);
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            Text label = labelObject.GetComponent<Text>();
            label.alignment = TextAnchor.MiddleCenter;
            label.color = Color.black;
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            return buttonObject.GetComponent<Button>();
        }

        private void HandleSelectionChanged(MapInfo map)
        {
            RefreshHighlight();
        }

        /// <summary>
        /// Tints through the Button's own ColorBlock so the highlight survives its hover and
        /// press transitions instead of being overwritten by them.
        /// </summary>
        private void RefreshHighlight()
        {
            if (mapSelection == null || mapSelection.Config == null)
            {
                return;
            }

            int selectedIndex = mapSelection.HasSelection
                ? mapSelection.Config.IndexOf(mapSelection.Selected)
                : -1;
            for (int i = 0; i < spawnedButtons.Count; i++)
            {
                Button button = spawnedButtons[i];
                if (button == null)
                {
                    continue;
                }

                ColorBlock colors = button.colors;
                colors.normalColor = i == selectedIndex ? selectedColor : normalColor;
                colors.selectedColor = colors.normalColor;
                button.colors = colors;
            }
        }

        /// <summary>
        /// Removes only what this view spawned. The container usually holds authored children too
        /// - a background, a title - and wiping every child would delete those the first time the
        /// list is rebuilt. Buttons left over from an earlier rebuild are matched by name, since
        /// the tracking list does not survive an assembly reload.
        /// </summary>
        private void ClearButtons()
        {
            for (int i = 0; i < spawnedButtons.Count; i++)
            {
                DestroyButton(spawnedButtons[i] != null ? spawnedButtons[i].gameObject : null);
            }

            spawnedButtons.Clear();

            Transform parent = container != null ? container : transform;
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                GameObject child = parent.GetChild(i).gameObject;
                if (child.name.StartsWith(ButtonNamePrefix, StringComparison.Ordinal))
                {
                    DestroyButton(child);
                }
            }
        }

        /// <summary>
        /// Unparented before being destroyed: Destroy only takes effect at the end of the frame,
        /// and until then the layout group would still lay out the old buttons alongside the new
        /// ones spawned right after this.
        /// </summary>
        private static void DestroyButton(GameObject buttonObject)
        {
            if (buttonObject == null)
            {
                return;
            }

            buttonObject.transform.SetParent(null, false);

            if (Application.isPlaying)
            {
                Destroy(buttonObject);
            }
            else
            {
                DestroyImmediate(buttonObject);
            }
        }
    }
}
