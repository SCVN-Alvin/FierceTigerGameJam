using System;
using System.Collections;
using System.Collections.Generic;
using GameJam.Data;
using GameJam.Gameplay.Wall;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GameJam.UI
{
    /// <summary>
    /// The mission board, shown one mission at a time. A mission is nine authored slots; a slot
    /// either names a map in the config or is an empty placeholder for a level that has not been
    /// authored yet. Placeholders look locked but answer a tap with a shake and a notice, so the
    /// board can show the shape of the campaign before the campaign exists. A whole mission can
    /// also be locked, which greys its cards out and ignores them entirely until it is opened by
    /// hand in the inspector.
    ///
    /// Levels are numbered straight through the slots - mission 2 starts at LEVEL 10 - and the
    /// unlock rule runs over the REAL maps in slot order, skipping placeholders: the first real
    /// map is open, and each next one opens when the real map before it has been passed.
    ///
    /// Tapping a card only selects the map. What happens next is the flow's business, reached
    /// through <see cref="MapSelection.SelectionChanged"/>, which is why nothing here holds a
    /// reference to the flow.
    /// </summary>
    public sealed class MissionPanelView : MonoBehaviour
    {
        /// <summary>One mission's slots, and whether the whole mission is still shut.</summary>
        [Serializable]
        public struct MissionSlots
        {
            [Tooltip("A locked mission is shown greyed out and answers to nothing.")]
            public bool locked;

            [Tooltip("One entry per card: a map id from the map config, or empty for a "
                     + "placeholder level that is not authored yet.")]
            public string[] slotMapIds;
        }

        [Tooltip("Where the choice is recorded, and the source of the map list.")]
        [SerializeField] private MapSelection mapSelection;

        [Tooltip("Cards are spawned under here. Falls back to this object's transform.")]
        [SerializeField] private RectTransform container;

        [SerializeField] private MissionProgressItemView itemPrefab;

        [Tooltip("Mission 1 carries the maps that exist so far; mission 2 is reserved and locked.")]
        [SerializeField] private MissionSlots[] missions =
        {
            new MissionSlots
            {
                locked = false,
                slotMapIds = new[]
                {
                    "map_004_level_01", "map_005_level_02", "map_005_two_storey_hollow_courtyard",
                    "", "", "",
                    "2", "", "",
                },
            },
            new MissionSlots
            {
                locked = true,
                slotMapIds = new[] { "", "", "", "", "", "", "", "", "" },
            },
        };

        [Tooltip("Reads MISSION n. The word is no longer baked into the frame art.")]
        [SerializeField] private TMP_Text missionTitle;

        [Tooltip("Hidden on the first mission rather than disabled: an arrow with nowhere to go is noise.")]
        [SerializeField] private Button previousMissionButton;

        [Tooltip("Hidden on the last mission, the same way.")]
        [SerializeField] private Button nextMissionButton;

        [Tooltip("Answers a tap on a level that has no map yet. Kept inactive between notices.")]
        [SerializeField] private TMP_Text noticeLabel;

        [Tooltip("What that notice says.")]
        [SerializeField] private string missingMapNotice = "NO MAP YET!";

        [Tooltip("How grey a locked mission's cards are drawn.")]
        [Range(0f, 1f)]
        [SerializeField] private float lockedMissionAlpha = 0.5f;

        /// <summary>
        /// Spawned cards are named with this so a rebuild can find the ones it left behind. The
        /// tracking list does not survive an assembly reload; the names in the hierarchy do.
        /// </summary>
        private const string ItemNamePrefix = "Mission_";

        private readonly List<MissionProgressItemView> items = new List<MissionProgressItemView>();

        private int missionIndex;
        private Coroutine noticeRoutine;
        private Coroutine shakeRoutine;

        private void OnEnable()
        {
            // A finished run saves, and saving raises this, so the board is current the moment the
            // player is back on it without the screen having to be rebuilt.
            UserData.Changed += Refresh;

            if (previousMissionButton != null)
            {
                previousMissionButton.onClick.AddListener(SelectPreviousMission);
            }

            if (nextMissionButton != null)
            {
                nextMissionButton.onClick.AddListener(SelectNextMission);
            }

            // Always opens on mission 1, the campaign in progress. The rest is one tap away.
            missionIndex = 0;

            // Rebuilt rather than refreshed: the slots may have been re-authored, and cards do
            // not survive an assembly reload.
            Rebuild();
        }

        private void OnDisable()
        {
            // UserData is static, so a handler left subscribed outlives this object and keeps a
            // destroyed view alive along with it.
            UserData.Changed -= Refresh;

            if (previousMissionButton != null)
            {
                previousMissionButton.onClick.RemoveListener(SelectPreviousMission);
            }

            if (nextMissionButton != null)
            {
                nextMissionButton.onClick.RemoveListener(SelectNextMission);
            }

            if (noticeLabel != null)
            {
                noticeLabel.gameObject.SetActive(false);
            }

            noticeRoutine = null;
            shakeRoutine = null;
        }

        /// <summary>Wired to the right arrow. Public so a UI Button can call it directly.</summary>
        public void SelectNextMission()
        {
            SetMission(missionIndex + 1);
        }

        /// <summary>Wired to the left arrow.</summary>
        public void SelectPreviousMission()
        {
            SetMission(missionIndex - 1);
        }

        /// <summary>Throws the cards away and makes one per slot of this mission, then draws them.</summary>
        [ContextMenu("Rebuild")]
        public void Rebuild()
        {
            ClearItems();

            MapConfig config = ResolveConfig();
            if (config == null || itemPrefab == null)
            {
                return;
            }

            missionIndex = Mathf.Clamp(missionIndex, 0, MissionCount() - 1);

            Transform parent = container != null ? container : transform;
            string[] slots = ResolveSlots(missionIndex, config);

            for (int i = 0; i < slots.Length; i++)
            {
                MissionProgressItemView item = Instantiate(itemPrefab, parent);
                item.gameObject.name = ItemNamePrefix + "Slot" + (FirstSlotNumber(missionIndex) + i);

                if (item.Action != null)
                {
                    // Captured per iteration so every card keeps its own slot; the shared loop
                    // variable would leave them all answering for the last one.
                    int slot = i;
                    item.Action.onClick.AddListener(() => HandleItemClicked(slot));
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

            bool missionLocked = IsMissionLocked(missionIndex);
            string[] slots = ResolveSlots(missionIndex, config);
            int firstNumber = FirstSlotNumber(missionIndex);

            for (int i = 0; i < items.Count && i < slots.Length; i++)
            {
                MissionProgressItemView item = items[i];
                if (item == null)
                {
                    continue;
                }

                // The number the player sees, not the map's id: ids are authoring data and need
                // not be the order the levels are played in.
                item.Bind($"LEVEL {firstNumber + i}", ResolveState(config, slots[i], missionLocked));
                SetItemGreyed(item, missionLocked);
            }

            if (missionTitle != null)
            {
                missionTitle.text = $"MISSION {missionIndex + 1}";
            }

            if (previousMissionButton != null)
            {
                previousMissionButton.gameObject.SetActive(missionIndex > 0);
            }

            if (nextMissionButton != null)
            {
                nextMissionButton.gameObject.SetActive(missionIndex < MissionCount() - 1);
            }
        }

        private int MissionCount()
        {
            return missions != null && missions.Length > 0 ? missions.Length : 1;
        }

        private bool IsMissionLocked(int mission)
        {
            return missions != null && mission >= 0 && mission < missions.Length && missions[mission].locked;
        }

        /// <summary>LEVEL numbers run straight through the missions, so mission 2 starts at 10.</summary>
        private int FirstSlotNumber(int mission)
        {
            int number = 1;
            for (int m = 0; m < mission && missions != null && m < missions.Length; m++)
            {
                number += missions[m].slotMapIds != null ? missions[m].slotMapIds.Length : 0;
            }

            return number;
        }

        /// <summary>
        /// The slot list a mission shows. With nothing authored, the whole map config is one
        /// mission, which is how the board behaved before slots existed.
        /// </summary>
        private string[] ResolveSlots(int mission, MapConfig config)
        {
            if (missions != null && missions.Length > 0)
            {
                string[] authored = missions[Mathf.Clamp(mission, 0, missions.Length - 1)].slotMapIds;
                return authored ?? Array.Empty<string>();
            }

            string[] slots = new string[config.Count];
            for (int i = 0; i < config.Count; i++)
            {
                MapInfo map = config.Get(i);
                slots[i] = map != null ? map.Id : "";
            }

            return slots;
        }

        /// <summary>
        /// Every real map on the whole board, in slot order. The unlock rule walks this, so
        /// placeholders between two maps do not wall the later one off.
        /// </summary>
        private List<string> RealMapSequence(MapConfig config)
        {
            List<string> sequence = new List<string>();
            int count = MissionCount();
            for (int m = 0; m < count; m++)
            {
                string[] slots = ResolveSlots(m, config);
                for (int i = 0; i < slots.Length; i++)
                {
                    if (!string.IsNullOrEmpty(slots[i]) && config.TryGet(slots[i], out _))
                    {
                        sequence.Add(slots[i]);
                    }
                }
            }

            return sequence;
        }

        /// <summary>
        /// Passed maps read as cleared whatever comes after them, so a player who goes back for a
        /// better score never sees a level they have beaten offered as new.
        /// </summary>
        private MissionItemState ResolveState(MapConfig config, string slotMapId, bool missionLocked)
        {
            bool isReal = !string.IsNullOrEmpty(slotMapId) && config.TryGet(slotMapId, out _);
            if (!isReal)
            {
                // A locked mission's placeholders may as well be plain locks: nothing in a locked
                // mission answers taps anyway.
                return missionLocked ? MissionItemState.Locked : MissionItemState.Missing;
            }

            if (missionLocked)
            {
                return MissionItemState.Locked;
            }

            UserMapProgressData progress = UserData.Maps;
            if (progress.IsPassed(slotMapId))
            {
                return MissionItemState.Cleared;
            }

            List<string> sequence = RealMapSequence(config);
            int position = sequence.IndexOf(slotMapId);
            bool open = position <= 0 || progress.IsPassed(sequence[position - 1]);
            return open ? MissionItemState.Current : MissionItemState.Locked;
        }

        /// <summary>A locked mission's card is dimmed and deaf; everything else is drawn plainly.</summary>
        private void SetItemGreyed(MissionProgressItemView item, bool greyed)
        {
            CanvasGroup group = item.GetComponent<CanvasGroup>();
            if (group == null)
            {
                if (!greyed)
                {
                    return;
                }

                group = item.gameObject.AddComponent<CanvasGroup>();
            }

            group.alpha = greyed ? lockedMissionAlpha : 1f;
            group.interactable = !greyed;
            group.blocksRaycasts = !greyed;
        }

        /// <summary>
        /// Selecting is the whole of it: <see cref="MapSelection"/> raises its change and the flow
        /// takes the player to the ammunition pick. A slot with no map answers with a shake and a
        /// notice instead, so the tap is refused rather than ignored.
        /// </summary>
        private void HandleItemClicked(int slot)
        {
            if (IsMissionLocked(missionIndex))
            {
                return;
            }

            MapConfig config = ResolveConfig();
            if (config == null)
            {
                return;
            }

            string[] slots = ResolveSlots(missionIndex, config);
            if (slot < 0 || slot >= slots.Length || slot >= items.Count)
            {
                return;
            }

            MissionProgressItemView item = items[slot];
            if (item == null || item.State == MissionItemState.Locked)
            {
                return;
            }

            if (item.State == MissionItemState.Missing)
            {
                RefuseMissingLevel(item);
                return;
            }

            if (mapSelection == null)
            {
                Debug.LogWarning($"{name} has no {nameof(MapSelection)}, so tapping a level does nothing.", this);
                return;
            }

            mapSelection.SelectById(slots[slot]);
        }

        /// <summary>The whole answer to tapping an unauthored level: a shake and one line of notice.</summary>
        private void RefuseMissingLevel(MissionProgressItemView item)
        {
            if (shakeRoutine != null)
            {
                StopCoroutine(shakeRoutine);
                shakeRoutine = null;
            }

            shakeRoutine = StartCoroutine(ShakeCard((RectTransform)item.transform));

            if (noticeLabel != null)
            {
                if (noticeRoutine != null)
                {
                    StopCoroutine(noticeRoutine);
                }

                noticeRoutine = StartCoroutine(ShowNotice());
            }
        }

        /// <summary>
        /// A short damped wobble on the card itself. The grid only repositions its children on a
        /// layout pass, so nudging anchoredPosition between passes is safe as long as the original
        /// is put back, which the finally guarantees even if the card is torn down mid-shake.
        /// </summary>
        private IEnumerator ShakeCard(RectTransform card)
        {
            const float Duration = 0.35f;
            const float Amplitude = 9f;
            const float Cycles = 4f;

            Vector2 origin = card.anchoredPosition;
            float elapsed = 0f;

            try
            {
                while (elapsed < Duration && card != null)
                {
                    elapsed += Time.unscaledDeltaTime;
                    float fade = 1f - Mathf.Clamp01(elapsed / Duration);
                    float offset = Mathf.Sin(elapsed / Duration * Cycles * 2f * Mathf.PI) * Amplitude * fade;
                    card.anchoredPosition = origin + new Vector2(offset, 0f);
                    yield return null;
                }
            }
            finally
            {
                if (card != null)
                {
                    card.anchoredPosition = origin;
                }

                shakeRoutine = null;
            }
        }

        /// <summary>Shows the notice, holds it briefly, fades it, and puts it away.</summary>
        private IEnumerator ShowNotice()
        {
            const float HoldSeconds = 0.9f;
            const float FadeSeconds = 0.35f;

            noticeLabel.text = missingMapNotice;
            noticeLabel.gameObject.SetActive(true);

            Color shown = noticeLabel.color;
            shown.a = 1f;
            noticeLabel.color = shown;

            yield return new WaitForSecondsRealtime(HoldSeconds);

            float elapsed = 0f;
            while (elapsed < FadeSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                Color fading = shown;
                fading.a = 1f - Mathf.Clamp01(elapsed / FadeSeconds);
                noticeLabel.color = fading;
                yield return null;
            }

            noticeLabel.gameObject.SetActive(false);
            noticeLabel.color = shown;
            noticeRoutine = null;
        }

        private void SetMission(int index)
        {
            int clamped = Mathf.Clamp(index, 0, MissionCount() - 1);
            if (clamped == missionIndex)
            {
                return;
            }

            missionIndex = clamped;
            Rebuild();
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
