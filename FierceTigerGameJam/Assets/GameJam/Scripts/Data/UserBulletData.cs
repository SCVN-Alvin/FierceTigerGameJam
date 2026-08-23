using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameJam.Data
{
    /// <summary>What the player has unlocked and how far they have taken it.</summary>
    [Serializable]
    public sealed class BulletProgress
    {
        public string bulletId;
        public bool unlocked;

        /// <summary>One-based, matching how levels read to the player. Never below 1.</summary>
        public int level = 1;
    }

    /// <summary>
    /// The player's ammunition: what is owned, what level each kind is at, and which is loaded.
    /// Survives between sessions, so it is the record that an upgrade actually changes; the
    /// runtime loadout reads from here rather than keeping its own copy.
    /// </summary>
    [Serializable]
    public sealed class UserBulletData
    {
        /// <summary>Bumped when the shape of this record changes, so old saves can be migrated.</summary>
        public int version = 1;

        public string selectedBulletId;

        public List<BulletProgress> bullets = new List<BulletProgress>();

        public bool IsUnlocked(string bulletId)
        {
            return TryGet(bulletId, out BulletProgress progress) && progress.unlocked;
        }

        public int GetLevel(string bulletId)
        {
            return TryGet(bulletId, out BulletProgress progress) ? Mathf.Max(1, progress.level) : 1;
        }

        public void Unlock(string bulletId)
        {
            GetOrCreate(bulletId).unlocked = true;
        }

        public void SetLevel(string bulletId, int level)
        {
            GetOrCreate(bulletId).level = Mathf.Max(1, level);
        }

        public bool TryGet(string bulletId, out BulletProgress progress)
        {
            progress = null;
            if (string.IsNullOrEmpty(bulletId))
            {
                return false;
            }

            for (int i = 0; i < bullets.Count; i++)
            {
                if (bullets[i] != null && string.Equals(bullets[i].bulletId, bulletId, StringComparison.Ordinal))
                {
                    progress = bullets[i];
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// A record is only written for ammunition the player has actually touched, so the save
        /// does not grow a row for every bullet the game will ever define.
        /// </summary>
        public BulletProgress GetOrCreate(string bulletId)
        {
            if (TryGet(bulletId, out BulletProgress existing))
            {
                return existing;
            }

            BulletProgress created = new BulletProgress
            {
                bulletId = bulletId,
                unlocked = false,
                level = 1,
            };
            bullets.Add(created);
            return created;
        }
    }
}
