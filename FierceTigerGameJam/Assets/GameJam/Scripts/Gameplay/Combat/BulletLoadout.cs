using System;
using System.Collections.Generic;
using GameJam.Data;
using UnityEngine;

namespace GameJam.Gameplay.Combat
{
    /// <summary>
    /// Which ammunition is loaded and what level each kind has reached.
    ///
    /// This asset owns no state of its own: everything it reports comes from the player's saved
    /// record, so an upgrade survives closing the game. What the asset does own is the catalogue,
    /// the list of ammunition that exists at all, which is design-time data and belongs here
    /// rather than in a save file.
    /// </summary>
    [CreateAssetMenu(menuName = "GameJam/Bullet Loadout", fileName = "BulletLoadout")]
    public sealed class BulletLoadout : ScriptableObject
    {
        [Tooltip("Every kind of ammunition in the game, unlocked or not.")]
        [SerializeField] private BulletDefinition[] bullets = Array.Empty<BulletDefinition>();

        [Tooltip("Loaded, and unlocked, before the player has bought anything.")]
        [SerializeField] private BulletDefinition defaultBullet;

        public event Action<BulletDefinition> SelectionChanged;

        public IReadOnlyList<BulletDefinition> Bullets => bullets;

        public BulletDefinition DefaultBullet => defaultBullet;

        /// <summary>
        /// The loaded ammunition, falling back to the starter when nothing has been chosen or the
        /// saved choice names something that no longer exists in the catalogue.
        /// </summary>
        public BulletDefinition Selected
        {
            get
            {
                BulletDefinition saved = Find(UserData.Bullets.selectedBulletId);
                return saved != null && IsUnlocked(saved) ? saved : defaultBullet;
            }
        }

        public int SelectedLevel => GetLevel(Selected);

        public bool Select(BulletDefinition bullet)
        {
            if (bullet == null || !IsUnlocked(bullet))
            {
                return false;
            }

            if (string.Equals(UserData.Bullets.selectedBulletId, bullet.Id, StringComparison.Ordinal))
            {
                return false;
            }

            UserData.Bullets.selectedBulletId = bullet.Id;
            UserData.Save();
            SelectionChanged?.Invoke(bullet);
            return true;
        }

        public bool SelectById(string bulletId)
        {
            return Select(Find(bulletId));
        }

        public int GetLevel(BulletDefinition bullet)
        {
            return bullet == null ? 1 : UserData.Bullets.GetLevel(bullet.Id);
        }

        /// <summary>
        /// The starter is always owned. Without that the player would open a fresh game unable to
        /// fire anything, and nothing to earn gold with to buy their way out of it.
        /// </summary>
        public bool IsUnlocked(BulletDefinition bullet)
        {
            if (bullet == null)
            {
                return false;
            }

            return bullet == defaultBullet || UserData.Bullets.IsUnlocked(bullet.Id);
        }

        public void Unlock(BulletDefinition bullet)
        {
            if (bullet == null || IsUnlocked(bullet))
            {
                return;
            }

            UserData.Bullets.Unlock(bullet.Id);
            UserData.Save();
        }

        /// <summary>
        /// Raises a level, capped at however many the ammunition actually defines. Whether the
        /// player may afford it is the economy's business, not this asset's.
        /// </summary>
        public int SetLevel(BulletDefinition bullet, int level)
        {
            if (bullet == null)
            {
                return 1;
            }

            int clamped = Mathf.Clamp(level, 1, Mathf.Max(1, bullet.LevelCount));
            UserData.Bullets.SetLevel(bullet.Id, clamped);
            UserData.Save();
            return clamped;
        }

        public int Upgrade(BulletDefinition bullet)
        {
            return bullet == null ? 1 : SetLevel(bullet, GetLevel(bullet) + 1);
        }

        public bool IsMaxLevel(BulletDefinition bullet)
        {
            return bullet != null && GetLevel(bullet) >= Mathf.Max(1, bullet.LevelCount);
        }

        public BulletDefinition Find(string bulletId)
        {
            if (string.IsNullOrEmpty(bulletId))
            {
                return null;
            }

            for (int i = 0; i < bullets.Length; i++)
            {
                if (bullets[i] != null && string.Equals(bullets[i].Id, bulletId, StringComparison.Ordinal))
                {
                    return bullets[i];
                }
            }

            return null;
        }
    }
}
