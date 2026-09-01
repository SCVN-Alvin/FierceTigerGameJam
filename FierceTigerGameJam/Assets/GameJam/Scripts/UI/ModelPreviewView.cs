using GameJam.Gameplay.Tool;
using UnityEngine;
using UnityEngine.UI;

namespace GameJam.UI
{
    /// <summary>
    /// The garage's 3D preview window: a flat image showing what
    /// <see cref="ModelPreviewRig"/> is drawing.
    ///
    /// There is one of these per tab and one rig behind both of them, because only one tab is
    /// ever on screen. This end owns nothing but the picture - which model to show is the shop's
    /// decision and how to show it is the rig's - so a second preview somewhere else is this
    /// component and a rect, with nothing to duplicate.
    ///
    /// The rig is found rather than referenced: it stands in the scene and this lives in a
    /// prefab, and a prefab cannot hold a reference to a scene object. Looked up once and
    /// remembered, with the failure said out loud once rather than on every redraw.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ModelPreviewView : MonoBehaviour
    {
        [Tooltip("Draws the rig's texture. Left empty, a RawImage on this object is used or "
                 + "added, which is what the builder wires.")]
        [SerializeField] private RawImage image;

        [Tooltip("The rig's render texture, as the builder wired it. Kept only so the window is "
                 + "readable in the inspector: at runtime the texture is taken from the rig "
                 + "itself, so the two can never end up pointed at different targets.")]
        [SerializeField] private RenderTexture previewTexture;

        private ModelPreviewRig rig;

        private bool searchedForRig;

        /// <summary>
        /// The picture, resolved on demand rather than in Awake.
        ///
        /// Deliberately not in Awake: the shop view that drives this sits on the panel above it,
        /// and activating a panel runs both objects' callbacks with no order this may rely on. An
        /// Awake that switched the image off could land after the first <see cref="Show"/> and
        /// undo it. The prefab ships with the image switched off instead - blank until bound, the
        /// same rule the row icons follow - and only Show and <see cref="Clear"/> ever move it.
        /// </summary>
        private RawImage Image
        {
            get
            {
                if (image == null)
                {
                    image = GetComponent<RawImage>();
                }

                return image;
            }
        }

        /// <summary>
        /// Shows a model, and says whether it could. False means there is no art for this item or
        /// no rig in the scene, which is the shops' cue to fall back to the flat icon.
        /// </summary>
        public bool Show(GameObject modelPrefab, int level)
        {
            ModelPreviewRig resolved = ResolveRig();
            if (resolved == null || !resolved.Show(this, modelPrefab, level))
            {
                SetImageActive(false);
                return false;
            }

            RawImage picture = Image;
            if (picture != null)
            {
                // Taken from the rig every time rather than trusted from the inspector: a
                // serialised texture that no longer matches the one the camera renders into is a
                // blank window with nothing in the console to explain it.
                picture.texture = resolved.TargetTexture;
                picture.color = Color.white;
                picture.raycastTarget = false;
            }

            SetImageActive(true);
            return true;
        }

        /// <summary>
        /// Empties the window. Only takes the rig's model down if this view is the one that put
        /// it there, which the rig decides - see the tab-switching note on
        /// <see cref="ModelPreviewRig"/>.
        /// </summary>
        public void Clear()
        {
            SetImageActive(false);

            if (rig != null)
            {
                rig.Hide(this);
            }
        }

        /// <summary>
        /// Closing the tab or the whole garage is what switches the rig's camera off, and this is
        /// where that happens: the panel is deactivated, so this goes with it.
        /// </summary>
        private void OnDisable()
        {
            Clear();
        }

        private void SetImageActive(bool active)
        {
            RawImage picture = Image;
            if (picture != null && picture.enabled != active)
            {
                picture.enabled = active;
            }
        }

        private ModelPreviewRig ResolveRig()
        {
            if (rig != null)
            {
                return rig;
            }

            rig = ModelPreviewRig.Active;
            if (rig != null)
            {
                return rig;
            }

            if (searchedForRig)
            {
                return null;
            }

            // Once. A scene with no rig is a scene the garage builder has not been run in, and
            // searching again on every redraw would only add a cost to a screen that is already
            // showing the fallback icon.
            searchedForRig = true;
            rig = FindFirstObjectByType<ModelPreviewRig>();
            if (rig == null)
            {
                Debug.LogWarning(
                    $"{nameof(ModelPreviewView)} on \"{name}\" found no {nameof(ModelPreviewRig)} in the "
                    + "scene, so the preview falls back to the flat icon. Run Tools > Smashdown > Build "
                    + "Garage Screen and save the scene.",
                    this);
            }

            return rig;
        }
    }
}
