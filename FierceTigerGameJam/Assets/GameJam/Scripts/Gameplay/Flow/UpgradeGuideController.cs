using GameJam.Data;
using GameJam.Economy;
using GameJam.Gameplay.Combat;
using GameJam.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GameJam.Gameplay.Flow
{
    /// <summary>
    /// The third tutorial: after a failure, walk the player to their first upgrade.
    ///
    /// The screen goes dark except for a soft-edged hole over exactly one control at a time,
    /// with the tutorial hand pointing at it: the fail screen's close button, the menu's shop
    /// button, the VEHICLES tab, then Cannon A's Buy button. The dark part is four
    /// raycast-blocking strips built AROUND the hole - outside taps die, the control inside is
    /// the real, untouched button - and the tutorial Filter art is laid over the hole to
    /// feather the edge (v1's bare strips read as hard rectangles, Falcon 2026-09-03).
    ///
    /// Engages only when BOTH conditions hold: the fail screen is up AND the wallet already
    /// covers the starter cannon's level-2 price (either missing = silently skip that time).
    /// Completes on VehicleLoadout.LevelChanged reaching level 2, saved as
    /// UserTutorialData.upgradeGuideDone. Unknown states hide the overlay - no soft-locks.
    ///
    /// The hand's art points DOWN by nature: shown under a hole it is rotated to point up, and
    /// when the hole sits low on screen the whole hand+caption group flips ABOVE the hole
    /// unrotated - which is also what keeps the caption on screen at every size, together with
    /// a horizontal clamp for holes near the edges (the fail screen's corner X).
    /// </summary>
    public sealed class UpgradeGuideController : MonoBehaviour
    {
        [SerializeField] private GameFlowController flow;

        [Tooltip("The gameplay canvas the overlay is built under.")]
        [SerializeField] private RectTransform hintParent;

        [SerializeField] private Sprite handSprite;

        [Tooltip("The tutorial's feathered filter, laid over the hole to soften the edge.")]
        [SerializeField] private Sprite dimSprite;

        [SerializeField] private TMP_FontAsset labelFont;
        [SerializeField] private VehicleLoadout vehicleLoadout;
        [SerializeField] private EconomyService economy;

        [Tooltip("The cannon the lesson upgrades - the starter.")]
        [SerializeField] private string starterVehicleId = "cannon_a";

        private const float HoleMargin = 14f;

        private bool guideActive;
        private RectTransform overlayRoot;
        private readonly RectTransform[] strips = new RectTransform[4];
        private RectTransform feather;
        private RectTransform hand;
        private TMP_Text caption;
        private FailScreenView failView;
        private VehicleShopView garageView;
        private float handPulse;

        private void OnEnable()
        {
            if (vehicleLoadout != null)
            {
                vehicleLoadout.LevelChanged += HandleLevelChanged;
            }
        }

        private void OnDisable()
        {
            if (vehicleLoadout != null)
            {
                vehicleLoadout.LevelChanged -= HandleLevelChanged;
            }

            SetOverlayVisible(false);
        }

        private void Update()
        {
            if (UserData.Tutorial.upgradeGuideDone || flow == null)
            {
                SetOverlayVisible(false);
                return;
            }

            if (!guideActive)
            {
                TryEngage();
                if (!guideActive)
                {
                    return;
                }
            }

            RectTransform target = ResolveTarget(out string words);
            if (target == null)
            {
                SetOverlayVisible(false);
                return;
            }

            EnsureOverlay();
            SetOverlayVisible(true);
            caption.text = words;
            LayoutAroundTarget(target);
        }

        /// <summary>
        /// BOTH conditions, or nothing (Falcon): the fail screen is up AND the level-2 price is
        /// already affordable. A fail while too poor skips silently and a later fail retries.
        /// </summary>
        private void TryEngage()
        {
            if (!UserData.Tutorial.completed || flow.State != GameFlowController.GameState.Result)
            {
                return;
            }

            if (failView == null)
            {
                failView = FindFirstObjectByType<FailScreenView>();
            }

            if (failView == null || !failView.gameObject.activeInHierarchy)
            {
                return;
            }

            if (UserData.Vehicles.GetLevel(starterVehicleId) > 1)
            {
                UserData.Tutorial.upgradeGuideDone = true;  // already ahead of the lesson
                UserData.Save();
                return;
            }

            VehicleDefinition starter = vehicleLoadout != null ? vehicleLoadout.Find(starterVehicleId) : null;
            if (starter == null || economy == null
                || !economy.TryGetVehicleUpgradePrice(starter, out int price, out int targetLevel)
                || targetLevel != 2
                || !UserData.Inventory.CanAfford(price))
            {
                return;
            }

            guideActive = true;
        }

        /// <summary>What the light sits on in the current state; null = lights off.</summary>
        private RectTransform ResolveTarget(out string words)
        {
            words = string.Empty;
            switch (flow.State)
            {
                case GameFlowController.GameState.Result:
                    if (failView != null && failView.gameObject.activeInHierarchy)
                    {
                        Transform close = FindDeep(failView.transform, "CloseButton");
                        if (close != null)
                        {
                            words = "TAP TO GO BACK";
                            return close as RectTransform;
                        }
                    }

                    return null;

                case GameFlowController.GameState.MainMenu:
                    words = "OPEN THE SHOP";
                    return flow.ShopButtonRect;

                case GameFlowController.GameState.Shop:
                    return ResolveShopTarget(out words);

                default:
                    return null;                            // never dim gameplay or loading
            }
        }

        /// <summary>
        /// Inside the shop, two focused stops (Falcon: v1 lit the whole panel): the VEHICLES
        /// tab while the vehicle list is closed, then Cannon A's own Buy button - the starter
        /// is the catalogue's first row under "Rows". Fallbacks widen gracefully: row, then
        /// panel, so a renamed prefab dims the screen rather than soft-locking it.
        /// </summary>
        private RectTransform ResolveShopTarget(out string words)
        {
            words = string.Empty;
            if (garageView == null)
            {
                garageView = FindFirstObjectByType<VehicleShopView>(FindObjectsInactive.Include);
            }

            if (garageView == null)
            {
                return null;
            }

            if (!garageView.gameObject.activeInHierarchy)
            {
                Transform tab = FindDeep(hintParent, "VehicleTypeTab");
                if (tab != null && tab.gameObject.activeInHierarchy)
                {
                    words = "OPEN VEHICLES";
                    return tab as RectTransform;
                }

                return null;
            }

            Transform rows = FindDeep(garageView.transform, "Rows");
            Transform firstRow = null;
            if (rows != null)
            {
                for (int i = 0; i < rows.childCount; i++)
                {
                    if (rows.GetChild(i).gameObject.activeInHierarchy)
                    {
                        firstRow = rows.GetChild(i);
                        break;
                    }
                }
            }

            if (firstRow != null)
            {
                Transform buy = FindDeep(firstRow, "Buy");
                if (buy != null && buy.gameObject.activeInHierarchy)
                {
                    words = "UPGRADE YOUR CANNON";
                    return buy as RectTransform;
                }

                words = "UPGRADE YOUR CANNON";
                return firstRow as RectTransform;
            }

            words = "UPGRADE YOUR CANNON";
            return garageView.transform as RectTransform;
        }

        private void HandleLevelChanged(VehicleDefinition vehicle, int level)
        {
            if (!guideActive || vehicle == null || vehicle.Id != starterVehicleId || level < 2)
            {
                return;
            }

            guideActive = false;
            UserData.Tutorial.upgradeGuideDone = true;
            UserData.Save();
            SetOverlayVisible(false);
        }

        // ------------------------------------------------------------------ overlay building

        private void EnsureOverlay()
        {
            if (overlayRoot != null || hintParent == null)
            {
                return;
            }

            GameObject rootGo = new GameObject("UpgradeGuide", typeof(RectTransform));
            overlayRoot = (RectTransform)rootGo.transform;
            overlayRoot.SetParent(hintParent, false);
            overlayRoot.anchorMin = Vector2.zero;
            overlayRoot.anchorMax = Vector2.one;
            overlayRoot.offsetMin = Vector2.zero;
            overlayRoot.offsetMax = Vector2.zero;
            overlayRoot.SetAsLastSibling();

            for (int i = 0; i < 4; i++)
            {
                GameObject stripGo = new GameObject("Dim" + i, typeof(RectTransform));
                RectTransform strip = (RectTransform)stripGo.transform;
                strip.SetParent(overlayRoot, false);
                strip.anchorMin = strip.anchorMax = new Vector2(0.5f, 0.5f);
                strip.pivot = Vector2.zero;
                Image dim = stripGo.AddComponent<Image>();
                dim.color = new Color(0f, 0f, 0f, 0.78f);
                dim.raycastTarget = true;                   // the wall that makes the rest dead
                strips[i] = strip;
            }

            // The feathered filter over the hole: its transparent middle sits on the target and
            // its soft falloff hides the strips' hard edges. Visual only.
            if (dimSprite != null)
            {
                GameObject featherGo = new GameObject("Feather", typeof(RectTransform));
                feather = (RectTransform)featherGo.transform;
                feather.SetParent(overlayRoot, false);
                feather.anchorMin = feather.anchorMax = new Vector2(0.5f, 0.5f);
                feather.pivot = new Vector2(0.5f, 0.5f);
                Image featherImage = featherGo.AddComponent<Image>();
                featherImage.sprite = dimSprite;
                featherImage.raycastTarget = false;
            }

            GameObject handGo = new GameObject("Hand", typeof(RectTransform));
            hand = (RectTransform)handGo.transform;
            hand.SetParent(overlayRoot, false);
            hand.anchorMin = hand.anchorMax = new Vector2(0.5f, 0.5f);
            // Pivot ON the fingertip, measured from the art. The sprite is the pack's
            // gauntlet whose armored finger points DOWN-RIGHT; its tip sits at 95.4% across,
            // 32% up. Positioning the pivot = positioning the tip. The art is NEVER rotated -
            // the pack's own demo shows it above-left of the button, exactly as authored.
            hand.pivot = new Vector2(0.954f, 0.32f);
            hand.sizeDelta = new Vector2(110f, 110f);
            Image handImage = handGo.AddComponent<Image>();
            if (handSprite != null)
            {
                handImage.sprite = handSprite;
                handImage.preserveAspect = true;
            }

            handImage.raycastTarget = false;

            GameObject captionGo = new GameObject("Caption", typeof(RectTransform));
            RectTransform captionRect = (RectTransform)captionGo.transform;
            captionRect.SetParent(overlayRoot, false);
            captionRect.anchorMin = captionRect.anchorMax = new Vector2(0.5f, 0.5f);
            captionRect.pivot = new Vector2(0.5f, 0.5f);
            captionRect.sizeDelta = new Vector2(620f, 64f);
            caption = captionGo.AddComponent<TextMeshProUGUI>();
            if (labelFont != null)
            {
                caption.font = labelFont;
            }

            caption.fontSize = 38f;
            caption.fontStyle = FontStyles.Bold;
            caption.color = Color.white;
            caption.alignment = TextAlignmentOptions.Center;
            caption.raycastTarget = false;
        }

        /// <summary>
        /// Strips walled around the hole, feather over it, hand and caption beside it.
        ///
        /// v5 (Falcon round 4): every earlier version rotated the hand, assuming the art was
        /// a finger pointing up or down. It is not - it is the pack's gauntlet, finger
        /// pointing DOWN-RIGHT, and the pack's own preview uses it above-left of the target,
        /// unrotated, tip on the button. So: no rotation, ever. The tip kisses the hole's
        /// upper rim (slightly left of centre) and bobs along the finger's own axis; the body
        /// hangs up-left of the control, never over it.
        /// </summary>
        private void LayoutAroundTarget(RectTransform target)
        {
            Vector3[] corners = new Vector3[4];
            target.GetWorldCorners(corners);
            Vector2 min = hintParent.InverseTransformPoint(corners[0]);
            Vector2 max = hintParent.InverseTransformPoint(corners[2]);
            min -= new Vector2(HoleMargin, HoleMargin);
            max += new Vector2(HoleMargin, HoleMargin);

            Rect canvas = hintParent.rect;
            SetStrip(strips[0], new Vector2(canvas.xMin, max.y), new Vector2(canvas.xMax, canvas.yMax));
            SetStrip(strips[1], new Vector2(canvas.xMin, canvas.yMin), new Vector2(canvas.xMax, min.y));
            SetStrip(strips[2], new Vector2(canvas.xMin, min.y), new Vector2(min.x, max.y));
            SetStrip(strips[3], new Vector2(max.x, min.y), new Vector2(canvas.xMax, max.y));

            Vector2 middle = (min + max) * 0.5f;
            Vector2 holeSize = max - min;

            if (feather != null)
            {
                feather.anchoredPosition = middle;
                float span = Mathf.Max(holeSize.x, holeSize.y);
                feather.sizeDelta = new Vector2(span * 2.6f, span * 2.6f);
            }

            handPulse += Time.unscaledDeltaTime * 3.4f;
            float bob = Mathf.Abs(Mathf.Sin(handPulse)) * 14f;

            // The finger's axis in the art, measured: down-right at about -24 degrees. The tip
            // rests just above the hole's rim, a touch left of centre, and taps along that
            // axis - retreating up-left, returning to the rim. Never rotated (see above).
            Vector2 fingerDir = new Vector2(0.91f, -0.41f);
            Vector2 tip = new Vector2(Mathf.Lerp(min.x, middle.x, 0.55f), max.y + 4f)
                - fingerDir * bob;

            // Keep the whole sprite on screen: the body extends left and up of the tip
            // (pivot 0.954, 0.32).
            Vector2 size = hand.sizeDelta;
            tip.x = Mathf.Clamp(tip.x, canvas.xMin + size.x * 0.954f, canvas.xMax - size.x * 0.046f);
            tip.y = Mathf.Clamp(tip.y, canvas.yMin + size.y * 0.32f, canvas.yMax - size.y * 0.68f);
            hand.anchoredPosition = tip;
            hand.localEulerAngles = Vector3.zero;

            // Caption under the hole when there is room; above the hand's body otherwise
            // (the hand owns the space directly above the hole).
            RectTransform captionRect = caption.rectTransform;
            float halfCaption = captionRect.sizeDelta.x * 0.5f;
            float belowY = min.y - 70f;
            float captionY = belowY > canvas.yMin + 44f
                ? belowY
                : max.y + size.y * 0.68f + 56f;
            float x = Mathf.Clamp(middle.x, canvas.xMin + halfCaption + 8f, canvas.xMax - halfCaption - 8f);
            captionY = Mathf.Clamp(captionY, canvas.yMin + 44f, canvas.yMax - 44f);
            captionRect.anchoredPosition = new Vector2(x, captionY);
        }

        private static void SetStrip(RectTransform strip, Vector2 min, Vector2 max)
        {
            Vector2 size = Vector2.Max(Vector2.zero, max - min);
            strip.anchoredPosition = min;
            strip.sizeDelta = size;
            strip.gameObject.SetActive(size.x > 0.5f && size.y > 0.5f);
        }

        private void SetOverlayVisible(bool visible)
        {
            if (overlayRoot != null && overlayRoot.gameObject.activeSelf != visible)
            {
                overlayRoot.gameObject.SetActive(visible);
            }
        }

        private static Transform FindDeep(Transform root, string name)
        {
            if (root == null)
            {
                return null;
            }

            if (root.name == name)
            {
                return root;
            }

            for (int i = 0; i < root.childCount; i++)
            {
                Transform hit = FindDeep(root.GetChild(i), name);
                if (hit != null)
                {
                    return hit;
                }
            }

            return null;
        }
    }
}
