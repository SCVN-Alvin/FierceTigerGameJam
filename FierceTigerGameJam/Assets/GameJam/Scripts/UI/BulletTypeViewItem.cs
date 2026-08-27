using GameJam.Gameplay.Combat;

namespace GameJam.UI
{
    /// <summary>
    /// The ammunition tab's row. Everything it draws is <see cref="ShopItemView"/>'s; this exists
    /// so the prefab and the shop can name the thing the row is about, and so the icon rule for
    /// ammunition lives beside the ammunition rather than in the shop's refresh loop.
    /// </summary>
    public sealed class BulletTypeViewItem : ShopItemView
    {
        /// <summary>
        /// Draws one kind of ammunition. A locked row shows the level-1 look rather than the look
        /// of a level the player has not bought: the row is an advertisement, and advertising the
        /// end of the upgrade path would be a lie about what the purchase gives them.
        /// </summary>
        public void Bind(
            BulletDefinition bullet,
            int level,
            int maxLevel,
            bool unlocked,
            bool equipped,
            string buyCaption,
            bool buyInteractable)
        {
            if (bullet == null)
            {
                return;
            }

            Bind(new State(
                bullet.ResolveIcon(unlocked ? level : 1),
                bullet.DisplayName,
                unlocked,
                level,
                maxLevel,
                equipped,
                buyCaption,
                buyInteractable));
        }
    }
}
