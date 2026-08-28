using UnityEngine;
using UnityEngine.UI;

namespace GameJam.UI
{
    /// <summary>
    /// Keeps the whole canvas readable at any aspect ratio by choosing which axis the
    /// <see cref="CanvasScaler"/> matches, per frame size.
    ///
    /// The UI is authored against a 9:16 portrait reference. On screens at least that tall -
    /// the reference itself, and the taller modern phones - matching the WIDTH is right: the
    /// layout keeps its authored width and the extra height becomes breathing room, which is
    /// exactly how it behaved before this component existed. On anything WIDER than the
    /// reference, matching width blows the layout up until it overflows the top and bottom, so
    /// there the scaler matches HEIGHT instead: the layout keeps its authored height, fits the
    /// screen vertically, and the extra width becomes margin.
    ///
    /// At the reference aspect both choices are identical, which is what makes this safe to add:
    /// a 9:16 screen renders pixel-for-pixel what it rendered before.
    ///
    /// Runs in edit mode too, so dragging the Game view between simulator sizes shows the same
    /// result a device would.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(CanvasScaler))]
    public sealed class AdaptiveCanvasScaler : MonoBehaviour
    {
        [Tooltip("The portrait resolution the screens were authored against.")]
        [SerializeField] private Vector2 referenceResolution = new Vector2(720f, 1280f);

        private CanvasScaler scaler;
        private int appliedWidth;
        private int appliedHeight;

        private void OnEnable()
        {
            Apply();
        }

        private void Update()
        {
            // Cheap enough to check every frame, and the only reliable way to catch the Game
            // view being resized in the editor as well as a device rotating in a build.
            if (Screen.width != appliedWidth || Screen.height != appliedHeight)
            {
                Apply();
            }
        }

        private void Apply()
        {
            if (Screen.width <= 0 || Screen.height <= 0 || referenceResolution.x <= 0f || referenceResolution.y <= 0f)
            {
                return;
            }

            if (scaler == null)
            {
                scaler = GetComponent<CanvasScaler>();
                if (scaler == null)
                {
                    return;
                }
            }

            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.referenceResolution = referenceResolution;

            float screenAspect = (float)Screen.width / Screen.height;
            float referenceAspect = referenceResolution.x / referenceResolution.y;

            // A hard switch rather than a blend: anywhere between 0 and 1 under-fits both axes
            // at once, which reads as "everything slightly wrong" instead of one axis fitting
            // exactly. The epsilon keeps the reference aspect itself on the width side.
            scaler.matchWidthOrHeight = screenAspect > referenceAspect + 0.0001f ? 1f : 0f;

            appliedWidth = Screen.width;
            appliedHeight = Screen.height;
        }
    }
}
