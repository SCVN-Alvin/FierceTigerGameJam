using UnityEngine;

namespace GameJam.Gameplay.Cannon
{
    /// <summary>
    /// Copies the aimed barrel's local rotation onto this transform, every frame.
    ///
    /// The barrel the aim actually turns is <c>CannonA</c>, which is switched off: an inactive
    /// transform still receives and holds rotations, but nothing parented under it is ever drawn.
    /// So anything that has to both aim and be seen - the muzzle flash - cannot simply hang off
    /// it. This mirrors the rotation onto a live object instead.
    ///
    /// The mirror and its source must share a parent, so the local rotation transfers as it
    /// stands with no rebasing between two different parent orientations. The builder places this
    /// object as a sibling of the barrel for exactly that reason; pointed at a transform under
    /// some other parent it would aim in the wrong plane.
    /// </summary>
    public sealed class CannonAimMirror : MonoBehaviour
    {
        [Tooltip("The transform the aim rotates - CannonA. Switched off, but its rotation is "
               + "still the one the shot leaves along, so it is the one worth copying.")]
        [SerializeField] private Transform source;

        /// <summary>
        /// LateUpdate, not Update: the aim is applied straight from the drag callbacks, and
        /// reading it after everything else has moved keeps the flash from trailing the barrel
        /// by a frame on the shot that matters most - the one fired mid-drag.
        /// </summary>
        private void LateUpdate()
        {
            if (source == null)
            {
                return;
            }

            transform.localRotation = source.localRotation;
        }
    }
}
