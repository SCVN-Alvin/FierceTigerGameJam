using System.Collections.Generic;
using UnityEngine;

namespace GameJam.Gameplay.Wall
{
    /// <summary>
    /// A ring of reusable instances per break effect. The ring's length is the hard cap on how
    /// many of that effect can be on screen: when it comes round again the oldest slot is
    /// restarted wherever the new break happened, even if it was still playing. A burst that
    /// gets cut short during a cascade is not something anyone can see; a hundred particle
    /// systems spawned in one frame is.
    /// </summary>
    /// <remarks>
    /// No block prefab has a break effect assigned yet, so at the time of writing this pool is
    /// never asked for anything. It is here so that assigning one later is a prefab change
    /// rather than a code change.
    /// TODO: revisit the ring size per effect once the VFX task picks the actual effects.
    /// </remarks>
    public static class BreakEffectPool
    {
        private const int RingSize = 6;

        private sealed class Ring
        {
            public GameObject[] Slots;
            public int Next;
        }

        private static readonly Dictionary<GameObject, Ring> ringsByPrefab = new Dictionary<GameObject, Ring>();
        private static Transform poolRoot;

        /// <summary>
        /// Plays the effect at a point in the world. Deliberately unparented, so a burst set off
        /// mid-spin stays where the hit happened rather than riding the structure round.
        /// </summary>
        public static void Play(GameObject prefab, Vector3 position)
        {
            if (prefab == null)
            {
                return;
            }

            GameObject instance = ResolveSlot(prefab);
            if (instance == null)
            {
                return;
            }

            instance.transform.SetPositionAndRotation(position, Quaternion.identity);

            // Switched off and on again rather than only moved: a system part way through its
            // life has to start over, and this is also what restarts one that had finished.
            if (instance.activeSelf)
            {
                instance.SetActive(false);
            }

            instance.SetActive(true);
        }

        /// <summary>Switches every effect off, for a run teardown.</summary>
        public static void StopAll()
        {
            foreach (KeyValuePair<GameObject, Ring> pair in ringsByPrefab)
            {
                GameObject[] slots = pair.Value.Slots;
                for (int i = 0; i < slots.Length; i++)
                {
                    if (slots[i] != null)
                    {
                        slots[i].SetActive(false);
                    }
                }
            }
        }

        private static GameObject ResolveSlot(GameObject prefab)
        {
            if (!ringsByPrefab.TryGetValue(prefab, out Ring ring))
            {
                ring = new Ring { Slots = new GameObject[RingSize] };
                ringsByPrefab[prefab] = ring;
            }

            int index = ring.Next;
            ring.Next = (ring.Next + 1) % ring.Slots.Length;

            if (ring.Slots[index] == null)
            {
                ring.Slots[index] = Object.Instantiate(prefab, ResolveRoot());
                ring.Slots[index].SetActive(false);
            }

            return ring.Slots[index];
        }

        private static Transform ResolveRoot()
        {
            if (poolRoot != null)
            {
                return poolRoot;
            }

            poolRoot = new GameObject("BreakEffectPool").transform;
            return poolRoot;
        }

#if UNITY_EDITOR
        /// <summary>Statics outlive play mode when domain reloading is switched off.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            ringsByPrefab.Clear();
            poolRoot = null;
        }
#endif
    }
}
