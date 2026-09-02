using System;
using System.Collections;
using System.Collections.Generic;
using GameJam.Config;
using GameJam.Data;
using GameJam.Gameplay.Wall;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GameJam.UI
{
    /// <summary>
    /// The mission board, shown one mission at a time. What a mission is and which maps it holds
    /// is <see cref="MissionConfig"/>'s business now, not this screen's: the board draws one card
    /// per map the mission actually has, so a short mission is a short row rather than a row
    /// padded out with blanks.
    ///
    /// A mission the player has not earned yet is drawn greyed and answers to nothing. Whether it
    /// is earned is asked of <see cref="MissionConfig.IsUnlocked"/> rather than decided here,
    /// because the cleared screen needs the same answer when it works out what comes next.
    ///
    /// Levels are numbered straight through the missions - mission 2 starts at the level after
    /// mission 1's last - and inside an open mission each map opens when the one before it in
    /// board order has been passed.
    ///
    /// Tapping a card only selects the map. What happens next is the flow's business, reached
    /// through <see cref="MapSelection.SelectionChanged"/>, which is why nothing here holds a
    /// reference to the flow.
    /// </summary>
    public sealed class MissionPanelView : MonoBehaviour
    {
        [Tooltip("Where the choice is recorded, and the source of the map list.")]
        [SerializeField] private MapSelection mapSelection;

        [Tooltip("Which missions there are, which maps are in them, and when the next one opens.")]
        [SerializeField] private MissionConfig missionConfig;

        [Tooltip("Cards are spawned under here. Falls back to this object's transform.")]
        [SerializeField] private RectTransform container;

        [SerializeField] private MissionProgressItemView itemPrefab;

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

        /// <summary>
        /// Every real map on the whole board, in board order, rebuilt at the top of each refresh
        /// and reused rather than reallocated. It used to be built once per card, which made a
        /// refresh quadratic in the number of maps and threw away a list each time.
        /// </summary>
        private readonly List<string> realMapSequence = new List<string>();

        private int missionIndex;
        private Coroutine noticeRoutine;
        private Coroutine shakeRoutine;

        private void OnEnable()
        {
            EnsureScroll();

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

            // Opens on the campaign frontier: the furthest mission the player has earned.
            // Opening on mission 1 forever meant a player two missions in paged across the
            // whole campaign every time the board came up.
            missionIndex = FurthestUnlockedMission();

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

            if (MissionCount() == 0)
            {
                Debug.LogWarning($"{name} has no {nameof(MissionConfig)} with missions in it, so the board is empty.", this);
                return;
            }

            missionIndex = Mathf.Clamp(missionIndex, 0, MissionCount() - 1);

            Transform parent = container != null ? container : transform;
            string[] slots = ResolveSlots(missionIndex);

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

            bool missionLocked = !IsMissionUnlocked(missionIndex);
            ApplyBackgroundMood(missionLocked);
            if (boardScroller != null)
            {
                boardScroller.ResetToTop();
            }
            string[] slots = ResolveSlots(missionIndex);
            int firstNumber = FirstSlotNumber(missionIndex);

            // Once per refresh rather than once per card: every card asks the same question of it.
            RebuildRealMapSequence(config);

            for (int i = 0; i < items.Count && i < slots.Length; i++)
            {
                MissionProgressItemView item = items[i];
                if (item == null)
                {
                    continue;
                }

                // The number the player sees, not the map's id: ids are authoring data and need
                // not be the order the levels are played in.
                item.Bind($"LEVEL {firstNumber + i}", ResolveState(config, slots[i], missionLocked),
                    ResolveStars(slots[i]));
                SetItemGreyed(item, missionLocked);
            }

            if (missionTitle != null)
            {
                missionTitle.text = ResolveMissionTitle(missionIndex);
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

        /// <summary>
        /// The last mission in order the unlock rule lets the player into. Walks forward and
        /// stops at the first locked one, so a gap can never be jumped.
        /// </summary>
        private int FurthestUnlockedMission()
        {
            int furthest = 0;
            for (int m = 0; m < MissionCount(); m++)
            {
                if (!IsMissionUnlocked(m))
                {
                    break;
                }

                furthest = m;
            }

            return furthest;
        }

        private int MissionCount()
        {
            return missionConfig != null ? missionConfig.Count : 0;
        }

        /// <summary>
        /// Asked of the config rather than answered here. The rule is about progress, not about
        /// this screen, and the cleared screen has to be able to ask it too.
        /// </summary>
        private bool IsMissionUnlocked(int mission)
        {
            return missionConfig == null || missionConfig.IsUnlocked(mission);
        }

        /// <summary>The mission's own name, or MISSION n when it was not given one.</summary>
        private string ResolveMissionTitle(int mission)
        {
            Mission entry = missionConfig != null ? missionConfig.Get(mission) : null;
            return entry != null && !string.IsNullOrEmpty(entry.displayName)
                ? entry.displayName
                : $"MISSION {mission + 1}";
        }

        /// <summary>LEVEL numbers run straight through the missions, so mission 2 starts after
        /// mission 1's last map rather than at a fixed number: missions may be any length now.</summary>
        private int FirstSlotNumber(int mission)
        {
            int number = 1;
            for (int m = 0; m < mission; m++)
            {
                number += ResolveSlots(m).Length;
            }

            return number;
        }

        /// <summary>
        /// The maps a mission shows, in order. Straight from the config: there is no fallback that
        /// turns the whole map registry into one mission any more, because a board with no mission
        /// config is a wiring mistake and drawing something plausible would hide it.
        /// </summary>
        private string[] ResolveSlots(int mission)
        {
            Mission entry = missionConfig != null ? missionConfig.Get(mission) : null;
            return entry != null && entry.mapIds != null ? entry.mapIds : Array.Empty<string>();
        }

        /// <summary>
        /// Every real map on the whole board, in board order, into the reused list. The per-map
        /// unlock rule walks this, so a mis-authored id between two maps does not wall the later
        /// one off.
        /// </summary>
        private void RebuildRealMapSequence(MapConfig config)
        {
            realMapSequence.Clear();

            int count = MissionCount();
            for (int m = 0; m < count; m++)
            {
                string[] slots = ResolveSlots(m);
                for (int i = 0; i < slots.Length; i++)
                {
                    if (!string.IsNullOrEmpty(slots[i]) && config.TryGet(slots[i], out _))
                    {
                        realMapSequence.Add(slots[i]);
                    }
                }
            }
        }

        /// <summary>
        /// Passed maps read as cleared whatever comes after them, so a player who goes back for a
        /// better score never sees a level they have beaten offered as new.
        /// </summary>
        /// <summary>The best star count this level has ever earned, straight off the save.</summary>
        private int ResolveStars(string slotMapId)
        {
            if (string.IsNullOrEmpty(slotMapId) || missionConfig == null)
            {
                return 0;
            }

            UserMapProgressData progress = UserData.Maps;
            return missionConfig.StarsFor(
                progress.IsPassed(slotMapId),
                progress.GetBestClearPercent(slotMapId));
        }

        private MissionItemState ResolveState(MapConfig config, string slotMapId, bool missionLocked)
        {
            bool isReal = !string.IsNullOrEmpty(slotMapId) && config.TryGet(slotMapId, out _);
            if (!isReal)
            {
                // Missions carry only real maps now, so this is no longer an unauthored slot - it
                // is an id in the mission config that the map registry does not have. Kept as
                // Missing rather than removed: the shake and the notice are the only thing that
                // tells a player why a card did nothing, and MissionConfig.OnValidate has already
                // named the bad id in the console for whoever authored it.
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

            int position = realMapSequence.IndexOf(slotMapId);
            bool open = position <= 0 || progress.IsPassed(realMapSequence[position - 1]);
            return open ? MissionItemState.Current : MissionItemState.Locked;
        }

        /// <summary>
        /// The board scrolls via MissionBoardScroller (see that file's header for why the
        /// prefab's own horizontal-only ScrollRect is left completely alone). Here we only
        /// prepare the geometry: top-anchor the grid without moving its first row, give it a
        /// real height (ContentSizeFitter over the GridLayoutGroup), make sure the viewport
        /// clips and has a raycast surface, then hand the grid to the scroller.
        /// </summary>
        private MissionBoardScroller boardScroller;

        private void EnsureScroll()
        {
            if (boardScroller != null || container == null
                || container.parent is not RectTransform viewport)
            {
                return;
            }

            // Fitted height must grow DOWNWARD from where the prefab drew the first row.
            Vector3[] corners = new Vector3[4];
            container.GetWorldCorners(corners);
            float topWorldY = corners[1].y;
            container.anchorMin = new Vector2(container.anchorMin.x, 1f);
            container.anchorMax = new Vector2(container.anchorMax.x, 1f);
            container.pivot = new Vector2(container.pivot.x, 1f);
            container.GetWorldCorners(corners);
            container.position += new Vector3(0f, topWorldY - corners[1].y, 0f);

            // The prefab SHIPS a ContentSizeFitter on the grid - horizontal Preferred,
            // vertical UNCONSTRAINED. Left like that the grid's rect height stays 0, the
            // scroll range computes to 0 and every drag clamps dead (the "do luon" freeze:
            // checking merely for the component's existence skipped this). Vertical must be
            // Preferred so the GridLayoutGroup's real 4-row height becomes the content size.
            ContentSizeFitter fitter = container.GetComponent<ContentSizeFitter>();
            if (fitter == null)
            {
                fitter = container.gameObject.AddComponent<ContentSizeFitter>();
            }

            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            if (viewport.GetComponent<RectMask2D>() == null)
            {
                viewport.gameObject.AddComponent<RectMask2D>();
            }

            // Drags on the blue between cards must still reach the scroller.
            if (viewport.GetComponent<Graphic>() == null)
            {
                Image surface = viewport.gameObject.AddComponent<Image>();
                surface.color = Color.clear;
                surface.raycastTarget = true;
            }

            boardScroller = viewport.gameObject.AddComponent<MissionBoardScroller>();
            boardScroller.Init(container);
        }

        private Image moodBackground;
        private Color moodNormalColor = Color.white;

        /// <summary>
        /// A locked mission darkens the board art itself, not only the cards, so paging onto
        /// mission 2/3 before they open reads as "not yet" at a glance (Falcon 2026-09-02; a
        /// real blur needs a shader pass, so the mood is carried by the tint). The Image is
        /// found by name under the panel - the prefab's "Background" - and its authored colour
        /// is restored the moment the mission is open.
        /// </summary>
        private void ApplyBackgroundMood(bool locked)
        {
            if (moodBackground == null && container != null && container.parent != null)
            {
                Transform background = container.parent.Find("Background");
                if (background != null)
                {
                    moodBackground = background.GetComponent<Image>();
                    if (moodBackground != null)
                    {
                        moodNormalColor = moodBackground.color;
                    }
                }
            }

            if (moodBackground != null)
            {
                moodBackground.color = locked
                    ? moodNormalColor * new Color(0.45f, 0.45f, 0.52f, 1f)
                    : moodNormalColor;
            }
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
            if (!IsMissionUnlocked(missionIndex))
            {
                return;
            }

            MapConfig config = ResolveConfig();
            if (config == null)
            {
                return;
            }

            string[] slots = ResolveSlots(missionIndex);
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
            if (MissionCount() == 0)
            {
                return;
            }

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
