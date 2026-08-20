using System;
using UnityEngine;

namespace GameJam.Data
{
    /// <summary>
    /// What the player owns that is spent rather than equipped. Only gold for now, but it is its
    /// own record so adding a second currency does not disturb bullet progress or map progress.
    /// </summary>
    [Serializable]
    public sealed class UserInventoryData
    {
        /// <summary>Bumped when the shape of this record changes, so old saves can be migrated.</summary>
        public int version = 1;

        public int gold;

        public bool CanAfford(int price)
        {
            return price <= 0 || gold >= price;
        }

        public void Add(int amount)
        {
            if (amount <= 0)
            {
                return;
            }

            gold += amount;
        }

        /// <summary>
        /// Takes the price only if it can be paid in full, so a caller cannot leave the player
        /// with a partial charge for something they did not get.
        /// </summary>
        public bool TrySpend(int price)
        {
            if (price < 0 || !CanAfford(price))
            {
                return false;
            }

            gold = Mathf.Max(0, gold - price);
            return true;
        }
    }
}
