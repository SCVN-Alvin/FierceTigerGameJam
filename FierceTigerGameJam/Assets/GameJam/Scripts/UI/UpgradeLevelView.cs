using UnityEngine;
using UnityEngine.UI;

namespace GameJam.UI
{
    /// <summary>
    /// One level of an item: lit when the player has reached it, dark when they have not.
    ///
    /// Two images stacked rather than one image whose sprite is swapped. A swap is cheaper, but
    /// it leaves nothing to animate: lighting a pip is the one moment of an upgrade the player
    /// actually sees, and with the lit half on its own object it can be faded or popped later
    /// without the prefab or this component changing shape.
    /// </summary>
    public sealed class UpgradeLevelView : MonoBehaviour
    {
        [Tooltip("The empty socket, always drawn. Child named Unfilled.")]
        [SerializeField] private Image unfilled;

        [Tooltip("Drawn over the socket while this level has been reached. Child named Fill.")]
        [SerializeField] private Image fill;

        public bool IsFilled => fill != null && fill.enabled;

        /// <summary>
        /// Lights the pip or puts it out. The lit image is disabled rather than deactivated, so
        /// the object it lives on keeps whatever a later animation put on it.
        /// </summary>
        public void SetFilled(bool filled)
        {
            if (fill != null)
            {
                fill.enabled = filled;
            }
        }

        /// <summary>
        /// Fills in whatever was left empty from the children, by the names the prefab uses.
        /// Anything set by hand is never overwritten, the same rule the shop rows follow.
        /// </summary>
        private void ResolveMissingReferences()
        {
            if (unfilled == null)
            {
                unfilled = FindImage("Unfilled");
            }

            if (fill == null)
            {
                fill = FindImage("Fill");
            }
        }

        /// <summary>
        /// Matched on the object's name rather than on the order the children are found in: the
        /// two images are the same component on the same kind of object, so taking the first hit
        /// would light the socket and hide the lit half on a prefab authored the other way round.
        /// </summary>
        private Image FindImage(string objectName)
        {
            Image[] candidates = GetComponentsInChildren<Image>(true);
            for (int i = 0; i < candidates.Length; i++)
            {
                if (string.Equals(candidates[i].gameObject.name, objectName, System.StringComparison.OrdinalIgnoreCase))
                {
                    return candidates[i];
                }
            }

            return null;
        }

        private void Reset()
        {
            ResolveMissingReferences();
        }

        private void OnValidate()
        {
            ResolveMissingReferences();
        }

        private void Awake()
        {
            // Also at runtime, so a pip spawned from a prefab that was never opened in the
            // inspector still knows its own parts.
            ResolveMissingReferences();
        }
    }
}
