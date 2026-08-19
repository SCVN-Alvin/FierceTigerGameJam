using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameJam.Gameplay.Combat
{
    /// <summary>
    /// Which ammunition is loaded and what level each kind has reached. Held on an asset so the
    /// cannon, the UI and anything else can read it without knowing about each other, the same
    /// way the map selection works.
    /// </summary>
    [CreateAssetMenu(menuName = "GameJam/Bullet Loadout", fileName = "BulletLoadout")]
    public sealed class BulletLoadout : ScriptableObject
    {
        [Tooltip("Every kind of ammunition in the game, unlocked or not.")]
        [SerializeField] private BulletDefinition[] bullets = Array.Empty<BulletDefinition>();

        [Tooltip("Loaded, and unlocked, when nothing has been chosen yet.")]
        [SerializeField] private BulletDefinition defaultBullet;

        /// <summary>
        /// Not serialized: progress made while playing should not be baked into the asset and
        /// leak into the next session. Persistence, when it arrives, belongs in a save file.
        /// </summary>
        [NonSerialized] private BulletDefinition selected;
        [NonSerialized] private Dictionary<string, int> levelsById;
        [NonSerialized] private HashSet<string> unlocked;

        public event Action<BulletDefinition> SelectionChanged;

        public IReadOnlyList<BulletDefinition> Bullets => bullets;

        public BulletDefinition Selected => selected != null ? selected : defaultBullet;

        public int SelectedLevel => GetLevel(Selected);

        public bool Select(BulletDefinition bullet)
        {
            if (bullet == null || selected == bullet)
            {
                return false;
            }

            if (!IsUnlocked(bullet))
            {
                Debug.LogWarning($"{name}: {bullet.DisplayName} is not unlocked yet.", this);
                return false;
            }

            selected = bullet;
            SelectionChanged?.Invoke(selected);
            return true;
        }

        public int GetLevel(BulletDefinition bullet)
        {
            if (bullet == null)
            {
                return 1;
            }

            EnsureState();
            return levelsById.TryGetValue(bullet.Id, out int level) ? level : 1;
        }

        /// <summary>Raises a bullet's level, capped at however many levels it actually defines.</summary>
        public int Upgrade(BulletDefinition bullet)
        {
            if (bullet == null)
            {
                return 1;
            }

            EnsureState();
            int next = Mathf.Clamp(GetLevel(bullet) + 1, 1, Mathf.Max(1, bullet.LevelCount));
            levelsById[bullet.Id] = next;
            return next;
        }

        public bool IsUnlocked(BulletDefinition bullet)
        {
            if (bullet == null)
            {
                return false;
            }

            EnsureState();
            return unlocked.Contains(bullet.Id);
        }

        public void Unlock(BulletDefinition bullet)
        {
            if (bullet == null)
            {
                return;
            }

            EnsureState();
            unlocked.Add(bullet.Id);
        }

        /// <summary>
        /// The starting bullet begins unlocked, so a fresh project can fire something before any
        /// progression is wired up.
        /// </summary>
        private void EnsureState()
        {
            if (levelsById != null)
            {
                return;
            }

            levelsById = new Dictionary<string, int>(StringComparer.Ordinal);
            unlocked = new HashSet<string>(StringComparer.Ordinal);

            BulletDefinition starter = defaultBullet != null
                ? defaultBullet
                : (bullets.Length > 0 ? bullets[0] : null);
            if (starter != null)
            {
                unlocked.Add(starter.Id);
            }
        }

        private void OnDisable()
        {
            // Domain reload does this anyway, but not when the editor is set to skip it.
            selected = null;
            levelsById = null;
            unlocked = null;
        }
    }
}
