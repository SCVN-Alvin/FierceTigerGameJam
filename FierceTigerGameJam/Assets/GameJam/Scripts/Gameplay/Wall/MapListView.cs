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
        /// Lays the buttons out in a row with real gaps between them. Any vertical group already
        /// on the container is switched off first: two layout groups on one object fight over the
        /// same children and the result depends on component order.
        /// </summary>
        private void EnsureLayout(Transform parent)
        {
            if (!useHorizontalLayout)
            {
                return;
            }

            VerticalLayoutGroup vertical = parent.GetComponent<VerticalLayoutGroup>();
            if (vertical != null && vertical.enabled)
            {
                vertical.enabled = false;
            }

            HorizontalLayoutGroup horizontal = parent.GetComponent<HorizontalLayoutGroup>();
            if (horizontal == null)
            {
                horizontal = parent.gameObject.AddComponent<HorizontalLayoutGroup>();
            }

            horizontal.spacing = buttonSpacing;
            horizontal.padding = layoutPadding;
            horizontal.childAlignment = TextAnchor.MiddleCenter;

            // Buttons keep the size their prefab defines rather than being stretched to fill.
            horizontal.childForceExpandWidth = false;
            horizontal.childForceExpandHeight = false;
            horizontal.childControlWidth = false;
            horizontal.childControlHeight = false;
        }

        private Button CreateButton(Transform parent, MapInfo map)
        {
            Button button = buttonPrefab != null
                ? Instantiate(buttonPrefab, parent)
                : CreateDefaultButton(parent);

            button.gameObject.name = $"MapButton_{map.Id}";
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

        private void ClearButtons()
        {
            Transform parent = container != null ? container : transform;
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                GameObject child = parent.GetChild(i).gameObject;
                if (Application.isPlaying)
                {
                    Destroy(child);
                }
                else
                {
                    DestroyImmediate(child);
                }
            }

            spawnedButtons.Clear();
        }
    }
}
