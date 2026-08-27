using GameJam.Gameplay.Combat;

namespace GameJam.UI
{
    /// <summary>
    /// The vehicle tab's row, the same picture as <see cref="BulletTypeViewItem"/> with a vehicle
    /// behind it. Vehicles are listed as rows rather than as the card grid the mock-ups drew:
    /// a card can show what a vehicle looks like, but not the level it is at, the pips it has
    /// left and its price, and those are what the player is here to compare.
    /// </summary>
    public sealed class VehicleTypeViewItem : ShopItemView
    {
        /// <summary>
        /// Draws one vehicle. A locked row shows the level-1 look, for the same reason the
        /// ammunition row does.
        /// </summary>
        public void Bind(
            VehicleDefinition vehicle,
            int level,
            int maxLevel,
            bool unlocked,
            bool equipped,
            string buyCaption,
            bool buyInteractable)
        {
            if (vehicle == null)
            {
                return;
            }

            Bind(new State(
                vehicle.ResolveIcon(unlocked ? level : 1),
                vehicle.DisplayName,
                unlocked,
                level,
                maxLevel,
                equipped,
                buyCaption,
                buyInteractable));
        }
    }
}
