using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameJam.Data
{
    /// <summary>What the player owns of one vehicle and how far they have taken it.</summary>
    [Serializable]
    public sealed class VehicleProgress
    {
        public string vehicleId;
        public bool unlocked;

        /// <summary>One-based, matching how levels read to the player. Never below 1.</summary>
        public int level = 1;
    }

    /// <summary>
    /// The player's vehicles: what is owned, what level each is at, and which one is mounted.
    /// Its own record with its own key, so a save written before vehicles existed simply has
    /// nothing here and starts the player on the default vehicle at level 1.
    /// </summary>
    [Serializable]
    public sealed class UserVehicleData
    {
        /// <summary>Bumped when the shape of this record changes, so old saves can be migrated.</summary>
        public int version = 1;

        public string selectedVehicleId;

        public List<VehicleProgress> vehicles = new List<VehicleProgress>();

        public bool IsUnlocked(string vehicleId)
        {
            return TryGet(vehicleId, out VehicleProgress progress) && progress.unlocked;
        }

        public int GetLevel(string vehicleId)
        {
            return TryGet(vehicleId, out VehicleProgress progress) ? Mathf.Max(1, progress.level) : 1;
        }

        public void Unlock(string vehicleId)
        {
            GetOrCreate(vehicleId).unlocked = true;
        }

        public void SetLevel(string vehicleId, int level)
        {
            GetOrCreate(vehicleId).level = Mathf.Max(1, level);
        }

        public bool TryGet(string vehicleId, out VehicleProgress progress)
        {
            progress = null;
            if (string.IsNullOrEmpty(vehicleId))
            {
                return false;
            }

            for (int i = 0; i < vehicles.Count; i++)
            {
                if (vehicles[i] != null && string.Equals(vehicles[i].vehicleId, vehicleId, StringComparison.Ordinal))
                {
                    progress = vehicles[i];
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// A record is only written for a vehicle the player has actually touched, so the save
        /// does not grow a row for every vehicle the game will ever define.
        /// </summary>
        public VehicleProgress GetOrCreate(string vehicleId)
        {
            if (TryGet(vehicleId, out VehicleProgress existing))
            {
                return existing;
            }

            VehicleProgress created = new VehicleProgress
            {
                vehicleId = vehicleId,
                unlocked = false,
                level = 1,
            };
            vehicles.Add(created);
            return created;
        }
    }
}
