using System;
using System.Collections.Generic;
using GameJam.Data;
using GameJam.Gameplay.Wall;
using UnityEngine;

namespace GameJam.UI
{
    /// <summary>
    /// The mission board: one card per map in the config, each showing whether that level is
    /// locked, waiting to be played, or already passed.
    ///
    /// The panel owns the unlock rule and nothing else does, so a card never has to know what a
    /// save looks like. The rule is deliberately the simplest one that reads correctly on the
    /// board: level N is open once level N-1 has been passed, and level 1 is always open. Passing
    /// means reaching the map's required percentage, not clearing it outright - a player who is
    /// asked for a hundred percent before the next level opens is a player who stops.
    ///
    /// Tapping a card only selects the map. What happens next is the flow's business, reached
    /// through <see cref="MapSelection.SelectionChanged"/>, which is why nothing here holds a
    /// reference to the flow.
    /// </summary>
    public sealed class MissionPanelView : MonoBehaviour
    {
        [Tooltip("Where the choice is recorded, and the source of the map list.")]
        [SerializeField] private MapSelection mapSelection;

        [Tooltip("Cards are spawned under here. Falls back to this object's transform.")]
        [SerializeField] private RectTransform container;

        [SerializeField] private MissionProgressItemView itemPrefab;

        /// <summary>
        /// Spawned cards are named with this so a rebuild can find the ones it left behind. The
        /// tracking list does not survive an assembly reload; the names in the hierarchy do.
        /// </summary>
        private const string ItemNamePrefix = "Mission_";

        private readonly List<MissionProgressItemView> items = new List<MissionProgressItemView>();

        private void OnEnable()
        {
            // A finished run saves, and saving raises this, so the board is current the moment the
            // player is back on it without the screen having to be rebuilt.
            UserData.Changed += Refresh;

            // Rebuilt rather than refreshed: the map config may have been re-authored, and cards
            // do not survive an assembly reload.
            Rebuild();
        }

        private void OnDisable()
        {
            // UserData is static, so a handler left subscribed outlives this object and keeps a
            // destroyed view alive along with it.
            UserData.Changed -= Refresh;
        }

        /// <summary>Throws the cards away and makes one per map, then draws them.</summary>
        [ContextMenu("Rebuild")]
        public void Rebuild()
        {
            ClearItems();

            MapConfig config = ResolveConfig();
            if (config == null || itemPrefab == null)
            {
                return;
            }

            Transform parent = container != null ? container : transform;

            for (int i = 0; i < config.Count; i++)
            {
                MapInfo map = config.Get(i);
                if (map == null)
                {
                    continue;
                }

                MissionProgressItemView item = Instantiate(itemPrefab, parent);
                item.gameObject.name = ItemNamePrefix + map.Id;

                if (item.Action != null)
                {
                    // Captured per iteration so every card keeps its own index; the shared loop
                    // variable would leave them all opening the last map.
                    int index = i;
                    item.Action.onClick.AddListener(() => HandleItemClicked(index));
                }

                items.Add(item);
            }

            Refresh();
        }

        /// <summary>Redraws the cards in place, for a save that changed under a board already up.</summary>
        [ContextMenu("Refresh")]
        public void Refresh()
        {
            MapConfig config = ResolveConfig();
            if (config == null)
            {
                return;
            }

            for (int i = 0; i < items.Count; i++)
            {
                MissionProgressItemView item = items[i];
                if (item == null)
                {
                    continue;
                }

                // The number the player sees, not the map's id: ids are authoring data and need
                // not be the order the levels are played in.
                item.Bind($"LEVEL {i + 1}", ResolveState(config, i));
            }
        }

        /// <summary>
        /// Passed maps read as cleared whatever comes after them, so a player who goes back for a
        /// better score never sees a level they have beaten offered as new. With sequential play
        /// exactly one card is Current; a save with gaps in it would show several, which is
        /// harmless - each of them really is open.
        /// </summary>
        private MissionItemState ResolveState(MapConfig config, int index)
        {
            MapInfo map = config.Get(index);
            if (map == null)
            {
                return MissionItemState.Locked;
            }

            UserMapProgressData progress = UserData.Maps;
            if (progress.IsPassed(map.Id))
            {
                return MissionItemState.Cleared;
            }

            if (index == 0)
            {
                return MissionItemState.Current;
            }

            MapInfo previous = config.Get(index - 1);
            bool open = previous != null && progress.IsPassed(previous.Id);
            return open ? MissionItemState.Current : MissionItemState.Locked;
        }

        /// <summary>
        /// Selecting is the whole of it: <see cref="MapSelection"/> raises its change and the flow
        /// takes the player to the ammunition pick. A locked card's button is not interactable
        /// anyway; the guard is for a click that arrives between a refresh and a rebuild.
        /// </summary>
        private void HandleItemClicked(int index)
        {
            if (index < 0 || index >= items.Count)
            {
                return;
            }

            MissionProgressItemView item = items[index];
            if (item == null || item.State == MissionItemState.Locked)
            {
                return;
            }

            if (mapSelection == null)
            {
                Debug.LogWarning($"{name} has no {nameof(MapSelection)}, so tapping a level does nothing.", this);
                return;
            }

            mapSelection.SelectByIndex(index);
        }

        private MapConfig ResolveConfig()
        {
            if (mapSelection == null || mapSelection.Config == null)
            {
                Debug.LogWarning($"{name} needs a {nameof(MapSelection)} with a config to list the levels.", this);
                return null;
            }

            return mapSelection.Config;
        }

        /// <summary>
        /// Removes only what this view spawned. The container may hold authored children too, and
        /// wiping every child would delete those the first time the board is rebuilt. Cards left
        /// over from an earlier rebuild are matched by name, since the tracking list does not
        /// survive an assembly reload.
        /// </summary>
        private void ClearItems()
        {
            for (int i = 0; i < items.Count; i++)
            {
                DestroyItem(items[i] != null ? items[i].gameObject : null);
            }

            items.Clear();

            Transform parent = container != null ? container : transform;
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                GameObject child = parent.GetChild(i).gameObject;
                if (child.name.StartsWith(ItemNamePrefix, StringComparison.Ordinal))
                {
                    DestroyItem(child);
                }
            }
        }

        /// <summary>
        /// Unparented before being destroyed: Destroy only takes effect at the end of the frame,
        /// and until then the grid would still lay out the old cards alongside the new ones
        /// spawned right after this.
        /// </summary>
        private static void DestroyItem(GameObject itemObject)
        {
            if (itemObject == null)
            {
                return;
            }

            itemObject.transform.SetParent(null, false);

            if (Application.isPlaying)
            {
                Destroy(itemObject);
            }
            else
            {
                DestroyImmediate(itemObject);
            }
        }
    }
}
