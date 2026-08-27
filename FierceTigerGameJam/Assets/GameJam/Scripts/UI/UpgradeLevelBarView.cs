using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameJam.UI
{
    /// <summary>
    /// The strip of pips along a shop row: one per level the item can actually reach, lit up to
    /// the level it has been taken to.
    ///
    /// The count belongs here rather than in each row component, because both kinds of row draw
    /// the same strip from the same two numbers. A row that spawned its own pips would be two
    /// copies of this loop that could drift apart, and the ammunition row and the vehicle row
    /// showing a level differently is exactly the kind of bug nobody reports.
    /// </summary>
    public sealed class UpgradeLevelBarView : MonoBehaviour
    {
        [Tooltip("One pip. Without it the strip stays empty rather than inventing art.")]
        [SerializeField] private UpgradeLevelView pipPrefab;

        [Tooltip("Pips are spawned under here. Left empty, this transform.")]
        [SerializeField] private RectTransform container;

        private const string PipNamePrefix = "Pip_";

        private readonly List<UpgradeLevelView> pips = new List<UpgradeLevelView>();

        /// <summary>
        /// Draws a strip of <paramref name="total"/> pips with the first
        /// <paramref name="current"/> of them lit.
        ///
        /// Pips already on the strip are re-used and only the difference is spawned or destroyed,
        /// so a refresh from a price change does not rebuild art that did not move. A zero total
        /// is tolerated rather than refused: an item with no priced levels has no strip, which is
        /// a readable answer, not an error.
        /// </summary>
        public void Bind(int total, int current)
        {
            Transform parent = ResolveContainer();

            AdoptStrays(parent);

            int wanted = Mathf.Max(0, total);

            for (int i = pips.Count - 1; i >= wanted; i--)
            {
                DestroyPip(pips[i] != null ? pips[i].gameObject : null);
                pips.RemoveAt(i);
            }

            while (pips.Count < wanted && pipPrefab != null)
            {
                UpgradeLevelView pip = Instantiate(pipPrefab, parent);
                pip.name = PipNamePrefix + (pips.Count + 1);
                pips.Add(pip);
            }

            int lit = Mathf.Clamp(current, 0, wanted);
            for (int i = 0; i < pips.Count; i++)
            {
                if (pips[i] != null)
                {
                    pips[i].SetFilled(i < lit);
                }
            }
        }

        /// <summary>
        /// Picks up pips left under the container by an earlier bind. The tracking list does not
        /// survive an assembly reload, and without this every reload would leave the old strip in
        /// place and spawn a second one beside it - the same name-prefix trick the shop views use
        /// for their rows.
        /// </summary>
        private void AdoptStrays(Transform parent)
        {
            if (pips.Count > 0)
            {
                return;
            }

            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                if (!child.name.StartsWith(PipNamePrefix, StringComparison.Ordinal))
                {
                    continue;
                }

                UpgradeLevelView pip = child.GetComponent<UpgradeLevelView>();
                if (pip != null)
                {
                    pips.Add(pip);
                }
            }
        }

        private Transform ResolveContainer()
        {
            return container != null ? container : transform;
        }

        /// <summary>
        /// Unparented before being destroyed: Destroy only takes effect at the end of the frame,
        /// and until then the layout group would lay out the pips this strip has just dropped
        /// alongside the ones it kept.
        /// </summary>
        private static void DestroyPip(GameObject pipObject)
        {
            if (pipObject == null)
            {
                return;
            }

            pipObject.transform.SetParent(null, false);

            if (Application.isPlaying)
            {
                Destroy(pipObject);
            }
            else
            {
                DestroyImmediate(pipObject);
            }
        }
    }
}
