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

        /// <summary>
        /// Raised after gold changes, so a wallet display can redraw without polling. This asset
        /// outlives a scene, so subscribers must unsubscribe when they are disabled.
        /// </summary>
        public event Action GoldChanged;

        /// <summary>The catalogue this service works against, for UI that wants to list it.</summary>
        public BulletLoadout Loadout => loadout;

        /// <summary>What the player can spend right now.</summary>
        public int Gold => UserData.Inventory.gold;

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
    }
}
