using System;
using UnityEngine;

namespace GameJam.Data
{
    /// <summary>
    /// The player's saved state, and the one place anything reads or writes it.
    ///
    /// Static rather than a ScriptableObject service, which is how the design-time state in this
    /// project is done: a bullet loadout, an economy and a map result all need the same records,
    /// and threading an asset reference through every one of them buys nothing when there can
    /// only ever be one player. The store underneath is swappable, so this is still testable.
    /// </summary>
    public static class UserData
    {
        private const string BulletsKey = "user.bullets";
        private const string VehiclesKey = "user.vehicles";
        private const string InventoryKey = "user.inventory";
        private const string MapProgressKey = "user.maps";
        private const string TutorialKey = "user.tutorial";

        private static ISaveStore store = new PlayerPrefsSaveStore();
        private static UserBulletData bullets;
        private static UserVehicleData vehicles;
        private static UserInventoryData inventory;
        private static UserMapProgressData maps;
        private static UserTutorialData tutorial;

        /// <summary>Raised after any change, so UI can redraw without polling.</summary>
        public static event Action Changed;

        /// <summary>Swap before first access to save somewhere other than PlayerPrefs.</summary>
        public static ISaveStore Store
        {
            get => store;
            set
            {
                store = value ?? new PlayerPrefsSaveStore();
                Reload();
            }
        }

        public static UserBulletData Bullets => bullets ??= Load<UserBulletData>(BulletsKey);

        public static UserVehicleData Vehicles => vehicles ??= Load<UserVehicleData>(VehiclesKey);

        public static UserInventoryData Inventory => inventory ??= Load<UserInventoryData>(InventoryKey);

        public static UserMapProgressData Maps => maps ??= Load<UserMapProgressData>(MapProgressKey);

        public static UserTutorialData Tutorial => tutorial ??= Load<UserTutorialData>(TutorialKey);

        /// <summary>
        /// Writes everything and commits it. Called after a change rather than on a timer: these
        /// records are small and change rarely, and a player who closes the game straight after
        /// an upgrade must not lose it.
        /// </summary>
        public static void Save()
        {
            if (bullets != null)
            {
                store.Save(BulletsKey, JsonUtility.ToJson(bullets));
            }

            if (vehicles != null)
            {
                store.Save(VehiclesKey, JsonUtility.ToJson(vehicles));
            }

            if (inventory != null)
            {
                store.Save(InventoryKey, JsonUtility.ToJson(inventory));
            }

            if (maps != null)
            {
                store.Save(MapProgressKey, JsonUtility.ToJson(maps));
            }

            if (tutorial != null)
            {
                store.Save(TutorialKey, JsonUtility.ToJson(tutorial));
            }

            store.Flush();
            Changed?.Invoke();
        }

        /// <summary>Drops the in-memory copies so the next read comes from storage again.</summary>
        public static void Reload()
        {
            bullets = null;
            vehicles = null;
            inventory = null;
            maps = null;
            tutorial = null;
            Changed?.Invoke();
        }

        /// <summary>Wipes the save. For the debug menu, and for starting a fresh test run.</summary>
        public static void ResetAll()
        {
            store.Delete(BulletsKey);
            store.Delete(VehiclesKey);
            store.Delete(InventoryKey);
            store.Delete(MapProgressKey);
            store.Delete(TutorialKey);
            store.Flush();
            Reload();
        }

        /// <summary>
        /// A record that fails to parse is replaced rather than allowed to throw. A corrupt save
        /// costing the player their progress is bad; a corrupt save bricking the game is worse,
        /// and one bad record cannot touch the others because each has its own key.
        /// </summary>
        private static T Load<T>(string key) where T : class, new()
        {
            if (!store.TryLoad(key, out string json))
            {
                return new T();
            }

            try
            {
                return JsonUtility.FromJson<T>(json) ?? new T();
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"{nameof(UserData)} could not read \"{key}\", starting it fresh: {exception.Message}");
                return new T();
            }
        }

        /// <summary>
        /// Entering play mode must read from storage, not from whatever the last session left in
        /// memory. Unity only clears statics on domain reload, which the editor can be told to
        /// skip, so this is done explicitly.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnEnterPlayMode()
        {
            bullets = null;
            vehicles = null;
            inventory = null;
            maps = null;
            tutorial = null;
        }
    }
}
