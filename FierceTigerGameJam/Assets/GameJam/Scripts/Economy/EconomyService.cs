using System;
using GameJam.Config;
using GameJam.Data;
using GameJam.Gameplay.Combat;
using UnityEngine;

namespace GameJam.Economy
{
    /// <summary>
    /// The only place gold is spent or granted.
    ///
    /// Prices live in configs and ownership lives in the save, so a shop button, a match reward
    /// and a debug panel would each have to reconcile the two on their own. Routing all of it
    /// through one asset means the rule that a charge and a grant always happen together is
    /// written once, and callers only have to ask whether the transaction succeeded.
    /// </summary>
    [CreateAssetMenu(menuName = "GameJam/Economy Service", fileName = "EconomyService")]
    public sealed class EconomyService : ScriptableObject
    {
        [Tooltip("What it costs to unlock each kind of ammunition. A bullet missing from it is "
                 + "not for sale, which is not the same as being free.")]
        [SerializeField] private PurchaseBulletConfig purchaseConfig;

        [Tooltip("What it costs to raise each kind of ammunition to a given level.")]
        [SerializeField] private UpgradeBulletConfig upgradeConfig;

        [Tooltip("The gold behind each reward id the rest of the game hands out.")]
        [SerializeField] private RewardConfig rewardConfig;

        [Tooltip("The catalogue this service unlocks and upgrades against.")]
        [SerializeField] private BulletLoadout loadout;

        [Tooltip("What it costs to unlock each vehicle. A vehicle missing from it is not for "
                 + "sale, which is not the same as being free.")]
        [SerializeField] private PurchaseVehicleConfig purchaseVehicleConfig;

        [Tooltip("What it costs to raise each vehicle to a given level.")]
        [SerializeField] private UpgradeVehicleConfig upgradeVehicleConfig;

        [Tooltip("The vehicle catalogue this service unlocks and upgrades against.")]
        [SerializeField] private VehicleLoadout vehicleLoadout;

        [Tooltip("What picking a failed run back up costs, and how many rounds it buys.")]
        [SerializeField] private LoseConfig loseConfig;

        /// <summary>
        /// Raised after gold changes, so a wallet display can redraw without polling. This asset
        /// outlives a scene, so subscribers must unsubscribe when they are disabled.
        /// </summary>
        public event Action GoldChanged;

        /// <summary>The catalogue this service works against, for UI that wants to list it.</summary>
        public BulletLoadout Loadout => loadout;

        /// <summary>The vehicle catalogue this service works against, for UI that wants to list it.</summary>
        public VehicleLoadout VehicleLoadout => vehicleLoadout;

        /// <summary>What the player can spend right now.</summary>
        public int Gold => UserData.Inventory.gold;

        /// <summary>Gold one continue costs. Zero with no config, which no caller may read as free.</summary>
        public int ContinuePrice => loseConfig != null ? loseConfig.continuePrice : 0;

        /// <summary>Rounds one continue buys, of whatever ammunition is loaded.</summary>
        public int ContinueAmmo => loseConfig != null ? loseConfig.continueAmmo : 0;

        /// <summary>
        /// Whether the player could pay for a continue this instant. False with no config: nothing
        /// is sold unpriced, and a missing config must read as "not for sale" rather than "free".
        /// </summary>
        public bool CanContinueRun()
        {
            return loseConfig != null && loseConfig.continueAmmo > 0 && Gold >= loseConfig.continuePrice;
        }

        /// <summary>
        /// Charges for a continue. Goes through <see cref="TrySpendGold"/> so the save and
        /// GoldChanged happen exactly as they do for any other spend.
        /// </summary>
        public bool TryPayContinue()
        {
            return CanContinueRun() && TrySpendGold(loseConfig.continuePrice);
        }

        /// <summary>
        /// Price of unlocking a bullet. False means it is not for sale at all, so a caller must
        /// not fall back to treating it as free.
        /// </summary>
        public bool TryGetPurchasePrice(BulletDefinition bullet, out int price)
        {
            price = 0;
            if (bullet == null || purchaseConfig == null)
            {
                return false;
            }

            return purchaseConfig.TryGetPrice(bullet.Id, out price);
        }

        /// <summary>
        /// Whether the player could buy this ammunition this instant. Every reason it might fail
        /// is checked here so that <see cref="TryPurchase"/> never has to undo a half-finished sale.
        /// </summary>
        public bool CanPurchase(BulletDefinition bullet)
        {
            if (bullet == null || IsUnlocked(bullet))
            {
                return false;
            }

            return TryGetPurchasePrice(bullet, out int price) && UserData.Inventory.CanAfford(price);
        }

        /// <summary>
        /// Unlocks the ammunition and charges for it, or does neither. Returns false when the
        /// purchase was refused, in which case nothing at all was written.
        /// </summary>
        public bool TryPurchase(BulletDefinition bullet)
        {
            if (purchaseConfig == null)
            {
                Debug.LogWarning($"{name} has no {nameof(PurchaseBulletConfig)}, so nothing can be bought.", this);
                return false;
            }

            if (!CanPurchase(bullet) || !TryGetPurchasePrice(bullet, out int price))
            {
                return false;
            }

            // The charge comes last of the checks and first of the writes: up to here nothing has
            // been granted, so a refused charge leaves the save exactly as it was.
            if (!UserData.Inventory.TrySpend(price))
            {
                return false;
            }

            // The record is unlocked directly rather than through BulletLoadout.Unlock, which
            // saves on its own: the whole transaction should commit exactly once, at the end.
            UserData.Bullets.Unlock(bullet.Id);
            UserData.Save();
            GoldChanged?.Invoke();
            return true;
        }

        /// <summary>
        /// Price of the next level for this ammunition, and which level that is. False means there
        /// is nothing to buy, either because the level is unpriced or because it does not exist.
        /// </summary>
        public bool TryGetUpgradePrice(BulletDefinition bullet, out int price, out int targetLevel)
        {
            price = 0;
            targetLevel = 0;
            if (bullet == null)
            {
                return false;
            }

            targetLevel = GetLevel(bullet) + 1;
            if (upgradeConfig == null || targetLevel > GetMaxLevel(bullet))
            {
                return false;
            }

            return upgradeConfig.TryGetUpgradePrice(bullet.Id, targetLevel, out price);
        }

        /// <summary>
        /// The highest level this ammunition can actually reach.
        ///
        /// Two assets have an opinion and they can disagree: BulletDefinition.LevelCount is how
        /// many levels are really defined (what the bullet will do in combat), while
        /// UpgradeBulletConfig only puts prices on levels and can happily price one that was never
        /// authored. The stricter of the two wins, so the player can never pay for a level that
        /// would silently clamp back down to the last defined one.
        /// </summary>
        public int GetMaxLevel(BulletDefinition bullet)
        {
            if (bullet == null)
            {
                return 1;
            }

            int defined = Mathf.Max(1, bullet.LevelCount);

            // A bullet the config does not list has no buyable levels, so it sits at its floor.
            int priced = 1;
            if (upgradeConfig != null && upgradeConfig.TryGetMaxLevel(bullet.Id, out int configured))
            {
                priced = Mathf.Max(1, configured);
            }

            return Mathf.Min(defined, priced);
        }

        /// <summary>
        /// Whether the player could upgrade this ammunition this instant. Locked ammunition cannot
        /// be levelled: it has to be bought first, so a level is never sold on something unowned.
        /// </summary>
        public bool CanUpgrade(BulletDefinition bullet)
        {
            if (bullet == null || !IsUnlocked(bullet))
            {
                return false;
            }

            if (GetLevel(bullet) >= GetMaxLevel(bullet))
            {
                return false;
            }

            return TryGetUpgradePrice(bullet, out int price, out int _) && UserData.Inventory.CanAfford(price);
        }

        /// <summary>
        /// Raises the ammunition one level and charges for it, or does neither. Returns false when
        /// the upgrade was refused, in which case nothing at all was written.
        /// </summary>
        public bool TryUpgrade(BulletDefinition bullet)
        {
            if (upgradeConfig == null)
            {
                Debug.LogWarning($"{name} has no {nameof(UpgradeBulletConfig)}, so nothing can be upgraded.", this);
                return false;
            }

            if (!CanUpgrade(bullet) || !TryGetUpgradePrice(bullet, out int price, out int targetLevel))
            {
                return false;
            }

            if (!UserData.Inventory.TrySpend(price))
            {
                return false;
            }

            // TryGetUpgradePrice has already refused any level past the ceiling, so this cannot
            // write a level the ammunition does not define.
            UserData.Bullets.SetLevel(bullet.Id, targetLevel);
            UserData.Save();
            GoldChanged?.Invoke();
            return true;
        }

        /// <summary>
        /// Price of unlocking a vehicle. False means it is not for sale at all, so a caller must
        /// not fall back to treating it as free.
        /// </summary>
        public bool TryGetVehiclePurchasePrice(VehicleDefinition vehicle, out int price)
        {
            price = 0;
            if (vehicle == null || purchaseVehicleConfig == null)
            {
                return false;
            }

            return purchaseVehicleConfig.TryGetPrice(vehicle.Id, out price);
        }

        /// <summary>
        /// Whether the player could buy this vehicle this instant. Every reason it might fail is
        /// checked here so that <see cref="TryPurchaseVehicle"/> never has to undo a half-finished
        /// sale.
        /// </summary>
        public bool CanPurchaseVehicle(VehicleDefinition vehicle)
        {
            if (vehicle == null || IsVehicleUnlocked(vehicle))
            {
                return false;
            }

            return TryGetVehiclePurchasePrice(vehicle, out int price) && UserData.Inventory.CanAfford(price);
        }

        /// <summary>
        /// Unlocks the vehicle, charges for it and mounts it, or does none of the three. Returns
        /// false when the purchase was refused, in which case nothing at all was written.
        ///
        /// Buying is the only way a vehicle is equipped: the garage that replaced the old shop
        /// has one button per row and no Select, so a vehicle nobody mounted here would be one
        /// the player paid for and never drove.
        /// </summary>
        public bool TryPurchaseVehicle(VehicleDefinition vehicle)
        {
            if (purchaseVehicleConfig == null)
            {
                Debug.LogWarning($"{name} has no {nameof(PurchaseVehicleConfig)}, so no vehicle can be bought.", this);
                return false;
            }

            if (!CanPurchaseVehicle(vehicle) || !TryGetVehiclePurchasePrice(vehicle, out int price))
            {
                return false;
            }

            // The charge comes last of the checks and first of the writes, exactly as the bullet
            // purchase does: a refused charge leaves the save as it was.
            if (!UserData.Inventory.TrySpend(price))
            {
                return false;
            }

            // Written straight to the record rather than through the loadout, which saves on its
            // own: the whole transaction should commit exactly once, at the end. It has to come
            // before the Select below, which refuses a vehicle that is not yet owned.
            UserData.Vehicles.Unlock(vehicle.Id);

            // Select saves, which commits the charge and the unlock above along with the choice,
            // and raises SelectionChanged so the mount swaps the model and the vehicle tab
            // re-dims its rows without either being told. False means the record already named
            // this vehicle, in which case nothing has saved yet and the plain write below does it.
            if (vehicleLoadout == null || !vehicleLoadout.Select(vehicle))
            {
                UserData.Save();
            }

            GoldChanged?.Invoke();
            return true;
        }

        /// <summary>
        /// Price of the next level for this vehicle, and which level that is. False means there is
        /// nothing to buy, either because the level is unpriced or because it does not exist.
        /// </summary>
        public bool TryGetVehicleUpgradePrice(VehicleDefinition vehicle, out int price, out int targetLevel)
        {
            price = 0;
            targetLevel = 0;
            if (vehicle == null)
            {
                return false;
            }

            targetLevel = GetVehicleLevel(vehicle) + 1;
            if (upgradeVehicleConfig == null || targetLevel > GetVehicleMaxLevel(vehicle))
            {
                return false;
            }

            return upgradeVehicleConfig.TryGetUpgradePrice(vehicle.Id, targetLevel, out price);
        }

        /// <summary>
        /// The highest level this vehicle can actually reach: the stricter of the levels the
        /// definition authored and the levels the config priced, for the same reason
        /// <see cref="GetMaxLevel"/> is. A level that was priced but never authored would clamp
        /// back down the moment it was bought, so it is never offered.
        /// </summary>
        public int GetVehicleMaxLevel(VehicleDefinition vehicle)
        {
            if (vehicle == null)
            {
                return 1;
            }

            int defined = Mathf.Max(1, vehicle.LevelCount);

            // A vehicle the config does not list has no buyable levels, so it sits at its floor.
            int priced = 1;
            if (upgradeVehicleConfig != null && upgradeVehicleConfig.TryGetMaxLevel(vehicle.Id, out int configured))
            {
                priced = Mathf.Max(1, configured);
            }

            return Mathf.Min(defined, priced);
        }

        /// <summary>
        /// Whether the player could upgrade this vehicle this instant. A locked vehicle cannot be
        /// levelled: it has to be bought first. It does not have to be the selected one, though -
        /// levelling the vehicle the player is saving toward is the point of the shop.
        /// </summary>
        public bool CanUpgradeVehicle(VehicleDefinition vehicle)
        {
            if (vehicle == null || !IsVehicleUnlocked(vehicle))
            {
                return false;
            }

            if (GetVehicleLevel(vehicle) >= GetVehicleMaxLevel(vehicle))
            {
                return false;
            }

            return TryGetVehicleUpgradePrice(vehicle, out int price, out int _) && UserData.Inventory.CanAfford(price);
        }

        /// <summary>
        /// Raises the vehicle one level and charges for it, or does neither. Returns false when
        /// the upgrade was refused, in which case nothing at all was written.
        /// </summary>
        public bool TryUpgradeVehicle(VehicleDefinition vehicle)
        {
            if (upgradeVehicleConfig == null)
            {
                Debug.LogWarning($"{name} has no {nameof(UpgradeVehicleConfig)}, so no vehicle can be upgraded.", this);
                return false;
            }

            if (!CanUpgradeVehicle(vehicle) || !TryGetVehicleUpgradePrice(vehicle, out int price, out int targetLevel))
            {
                return false;
            }

            if (!UserData.Inventory.TrySpend(price))
            {
                return false;
            }

            // Unlike the bullet upgrade above, this goes through the loadout rather than writing
            // UserData directly. A vehicle level is not only a number: the model standing under
            // the cannon is spawned from it, and the mount is told to swap it by the loadout's
            // LevelChanged. Writing the record here would leave the player looking at the level
            // they just paid to replace until they left the scene and came back.
            //
            // The loadout saves as part of that, which commits the charge made just above, so the
            // transaction still lands in one write.
            if (vehicleLoadout != null)
            {
                vehicleLoadout.SetLevel(vehicle, targetLevel);
            }
            else
            {
                UserData.Vehicles.SetLevel(vehicle.Id, targetLevel);
                UserData.Save();
            }

            GoldChanged?.Invoke();
            return true;
        }

        /// <summary>
        /// Pays out an authored reward. False means the id is unknown, which is how a typo in a
        /// config shows up as "nothing was granted" rather than as a crash at the end of a run.
        /// </summary>
        private void OnDisable()
        {
            // Subscribers are cleared with the play session. A ScriptableObject outlives play
            // mode in the editor, so an event left holding scene objects keeps them alive and
            // fires into the wreckage of the last run when the next one starts.
            GoldChanged = null;
        }

        public bool TryGrantReward(string rewardId, out int gold)
        {
            gold = 0;
            if (rewardConfig == null)
            {
                Debug.LogWarning($"{name} has no {nameof(RewardConfig)}, so \"{rewardId}\" cannot be granted.", this);
                return false;
            }

            if (!rewardConfig.TryGetReward(rewardId, out gold))
            {
                return false;
            }

            // A reward authored as nothing is still a reward that exists, so this reports success
            // while leaving the save alone: there is no change to write and none to announce.
            if (gold <= 0)
            {
                return true;
            }

            UserData.Inventory.Add(gold);
            UserData.Save();
            GoldChanged?.Invoke();
            return true;
        }

        /// <summary>Hands over gold that the caller has already worked out, for example a score bonus.</summary>
        public void GrantGold(int amount)
        {
            if (amount <= 0)
            {
                return;
            }

            UserData.Inventory.Add(amount);
            UserData.Save();
            GoldChanged?.Invoke();
        }

        /// <summary>
        /// Takes gold for something this service does not price itself. Returns false, and takes
        /// nothing, when the player cannot cover it in full.
        /// </summary>
        public bool TrySpendGold(int amount)
        {
            if (amount < 0)
            {
                return false;
            }

            // Charging nothing always succeeds, and writing a save for it would be noise.
            if (amount == 0)
            {
                return true;
            }

            if (!UserData.Inventory.TrySpend(amount))
            {
                return false;
            }

            UserData.Save();
            GoldChanged?.Invoke();
            return true;
        }

        /// <summary>
        /// Ownership goes through the loadout when there is one, because the starter bullet counts
        /// as owned without ever appearing in the save.
        /// </summary>
        private bool IsUnlocked(BulletDefinition bullet)
        {
            if (bullet == null)
            {
                return false;
            }

            return loadout != null ? loadout.IsUnlocked(bullet) : UserData.Bullets.IsUnlocked(bullet.Id);
        }

        private int GetLevel(BulletDefinition bullet)
        {
            if (bullet == null)
            {
                return 1;
            }

            return loadout != null ? loadout.GetLevel(bullet) : UserData.Bullets.GetLevel(bullet.Id);
        }

        /// <summary>
        /// Ownership goes through the loadout when there is one, because the starter vehicle
        /// counts as owned without ever appearing in the save.
        /// </summary>
        private bool IsVehicleUnlocked(VehicleDefinition vehicle)
        {
            if (vehicle == null)
            {
                return false;
            }

            return vehicleLoadout != null
                ? vehicleLoadout.IsUnlocked(vehicle)
                : UserData.Vehicles.IsUnlocked(vehicle.Id);
        }

        private int GetVehicleLevel(VehicleDefinition vehicle)
        {
            if (vehicle == null)
            {
                return 1;
            }

            return vehicleLoadout != null
                ? vehicleLoadout.GetLevel(vehicle)
                : UserData.Vehicles.GetLevel(vehicle.Id);
        }
    }
}
