using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameJam.Config
{
    /// <summary>
    /// What it costs to unlock each vehicle. Prices live here rather than on the
    /// VehicleDefinition itself so the shop can be retuned without touching the assets that
    /// describe what the vehicle does to a shot.
    /// </summary>
    [CreateAssetMenu(menuName = "GameJam/Purchase Vehicle Config", fileName = "PurchaseVehicleConfig")]
    public sealed class PurchaseVehicleConfig : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            [Tooltip("Vehicle id, matching VehicleDefinition.Id, for example vehicle_tank.")]
            public string vehicleId;

            [Tooltip("Gold the player pays once to unlock this vehicle. Zero means it is free, "
                     + "which is how a starting vehicle would be expressed.")]
            public int goldPrice;
        }

        [SerializeField] private Entry[] entries = Array.Empty<Entry>();

        private Dictionary<string, int> lookup;

        public IReadOnlyList<Entry> Entries => entries;

        /// <summary>
        /// Price of unlocking a vehicle. Returns false when the vehicle is not listed, which the
        /// caller should read as "not for sale" rather than "free".
        /// </summary>
        public bool TryGetPrice(string vehicleId, out int goldPrice)
        {
            goldPrice = 0;
            if (string.IsNullOrEmpty(vehicleId))
            {
                return false;
            }

            EnsureLookup();
            return lookup.TryGetValue(vehicleId, out goldPrice);
        }

        private void EnsureLookup()
        {
            if (lookup != null)
            {
                return;
            }

            lookup = new Dictionary<string, int>(entries.Length, StringComparer.Ordinal);
            for (int i = 0; i < entries.Length; i++)
            {
                string vehicleId = entries[i].vehicleId;
                if (string.IsNullOrEmpty(vehicleId))
                {
                    continue;
                }

                lookup[vehicleId] = entries[i].goldPrice;
            }
        }

        private void OnValidate()
        {
            // Entries may have been edited in the inspector, so the cached lookup is stale.
            lookup = null;

            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < entries.Length; i++)
            {
                Entry entry = entries[i];
                entry.goldPrice = Mathf.Max(0, entry.goldPrice);
                entries[i] = entry;

                if (string.IsNullOrEmpty(entry.vehicleId))
                {
                    continue;
                }

                if (!seen.Add(entry.vehicleId))
                {
                    Debug.LogWarning($"{name} lists the vehicle id \"{entry.vehicleId}\" more than once; the last entry wins.", this);
                }
            }
        }
    }
}
