using System;
using System.Collections.Generic;
using GameJam.Data;
using UnityEngine;

namespace GameJam.Gameplay.Combat
{
    /// <summary>
    /// Which vehicle the cannon is mounted on and what level each one has reached.
    ///
    /// Like <see cref="BulletLoadout"/> this asset owns no state: everything it reports comes from
    /// the player's saved record, and what it does own is the catalogue, which is design-time data.
    ///
    /// It raises one event a bullet loadout does not need. A bullet level only changes numbers, so
    /// nothing has to be told about it; a vehicle level changes the model standing under the
    /// cannon, so <see cref="LevelChanged"/> exists for the mount to listen to.
    /// </summary>
    [CreateAssetMenu(menuName = "GameJam/Vehicle Loadout", fileName = "VehicleLoadout")]
    public sealed class VehicleLoadout : ScriptableObject
    {
        [Tooltip("Every vehicle in the game, unlocked or not.")]
        [SerializeField] private VehicleDefinition[] vehicles = Array.Empty<VehicleDefinition>();

        [Tooltip("Owned and selected before the player has bought anything.")]
        [SerializeField] private VehicleDefinition defaultVehicle;

        public event Action<VehicleDefinition> SelectionChanged;

        /// <summary>Raised when a vehicle's level changes, so the mount can re-spawn the model.</summary>
        public event Action<VehicleDefinition, int> LevelChanged;

        public IReadOnlyList<VehicleDefinition> Vehicles => vehicles;

        public VehicleDefinition DefaultVehicle => defaultVehicle;

        /// <summary>
        /// The mounted vehicle, falling back to the starter when nothing has been chosen or the
        /// saved choice names something that no longer exists or is no longer owned.
        /// </summary>
        public VehicleDefinition Selected
        {
            get
            {
                VehicleDefinition saved = Find(UserData.Vehicles.selectedVehicleId);
                return saved != null && IsUnlocked(saved) ? saved : defaultVehicle;
            }
        }

        public int SelectedLevel => GetLevel(Selected);

        /// <summary>The number the projectile multiplies by. 1 when nothing is configured.</summary>
        public float SelectedDamageMultiplier
        {
            get
            {
                VehicleDefinition selected = Selected;
                return selected != null ? selected.GetDamageMultiplier(SelectedLevel) : 1f;
            }
        }

        public bool Select(VehicleDefinition vehicle)
        {
            if (vehicle == null || !IsUnlocked(vehicle))
            {
                return false;
            }

            if (string.Equals(UserData.Vehicles.selectedVehicleId, vehicle.Id, StringComparison.Ordinal))
            {
                return false;
            }

            UserData.Vehicles.selectedVehicleId = vehicle.Id;
            UserData.Save();
            SelectionChanged?.Invoke(vehicle);
            return true;
        }

        public bool SelectById(string vehicleId)
        {
            return Select(Find(vehicleId));
        }

        public int GetLevel(VehicleDefinition vehicle)
        {
            return vehicle == null ? 1 : UserData.Vehicles.GetLevel(vehicle.Id);
        }

        /// <summary>
        /// The starter is always owned, the same reason the starter bullet is: a fresh save must
        /// have something under the cannon, and nothing in the shop is reachable without playing.
        /// </summary>
        public bool IsUnlocked(VehicleDefinition vehicle)
        {
            if (vehicle == null)
            {
                return false;
            }

            return vehicle == defaultVehicle || UserData.Vehicles.IsUnlocked(vehicle.Id);
        }

        public void Unlock(VehicleDefinition vehicle)
        {
            if (vehicle == null || IsUnlocked(vehicle))
            {
                return;
            }

            UserData.Vehicles.Unlock(vehicle.Id);
            UserData.Save();
        }

        /// <summary>
        /// Raises a level, capped at however many the vehicle actually defines. Whether the player
        /// may afford it is the economy's business, not this asset's.
        ///
        /// Announced rather than written quietly: the model on the cannon is spawned from this
        /// level, so an upgrade nobody hears about leaves the player looking at what they paid to
        /// replace.
        /// </summary>
        public int SetLevel(VehicleDefinition vehicle, int level)
        {
            if (vehicle == null)
            {
                return 1;
            }

            int previous = GetLevel(vehicle);
            int clamped = Mathf.Clamp(level, 1, Mathf.Max(1, vehicle.LevelCount));
            UserData.Vehicles.SetLevel(vehicle.Id, clamped);
            UserData.Save();

            if (clamped != previous)
            {
                LevelChanged?.Invoke(vehicle, clamped);
            }

            return clamped;
        }

        public int Upgrade(VehicleDefinition vehicle)
        {
            return vehicle == null ? 1 : SetLevel(vehicle, GetLevel(vehicle) + 1);
        }

        public bool IsMaxLevel(VehicleDefinition vehicle)
        {
            return vehicle != null && GetLevel(vehicle) >= Mathf.Max(1, vehicle.LevelCount);
        }

        public VehicleDefinition Find(string vehicleId)
        {
            if (string.IsNullOrEmpty(vehicleId))
            {
                return null;
            }

            for (int i = 0; i < vehicles.Length; i++)
            {
                if (vehicles[i] != null && string.Equals(vehicles[i].Id, vehicleId, StringComparison.Ordinal))
                {
                    return vehicles[i];
                }
            }

            return null;
        }

        private void OnDisable()
        {
            // Subscribers are cleared with the play session. This asset outlives play mode in the
            // editor, so an event left holding a mount from the last run keeps it alive and fires
            // into the wreckage of it when the next one starts.
            SelectionChanged = null;
            LevelChanged = null;
        }
    }
}
