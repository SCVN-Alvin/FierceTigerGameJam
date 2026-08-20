using System.Collections.Generic;
using GameJam.Data;
using GameJam.Gameplay.Combat;
using UnityEngine;

namespace GameJam.Economy
{
    /// <summary>
    /// A developer-only panel for driving the economy by hand while there is no shop UI.
    ///
    /// Every button goes through <see cref="EconomyService"/> rather than touching gold or the
    /// save itself, so what is exercised here is the same code the real shop will use, and a
    /// button that is greyed out is the service refusing rather than the panel guessing.
    /// </summary>
    public sealed class EconomyDebugPanel : MonoBehaviour
    {
        // The body is compiled out of release builds: this draws an IMGUI panel every frame and
        // hands out free gold, neither of which has any business shipping. The class itself stays
        // so that a scene referencing the component still loads.
#if UNITY_EDITOR || DEVELOPMENT_BUILD

        private const float PanelMargin = 12f;
        private const float PanelWidth = 340f;

        [Tooltip("The service every button here goes through.")]
        [SerializeField] private EconomyService economy;

        [Tooltip("The catalogue listed in the panel; every kind of ammunition in it gets a row.")]
        [SerializeField] private BulletLoadout loadout;

        [Tooltip("Draw the on-screen panel. Turn it off to use the context menu entries alone.")]
        [SerializeField] private bool showOnScreen = true;

        [Tooltip("Gold handed over by the grant button, for trying a price without earning it.")]
        [SerializeField] private int debugGoldGrant = 500;

        [Tooltip("Shows and hides the panel while the game view has focus.")]
        [SerializeField] private KeyCode toggleKey = KeyCode.F1;

        private Vector2 scroll;

        /// <summary>A missing reference is reported once; OnGUI runs several times a frame.</summary>
        private bool warnedMissingReferences;

        private void OnGUI()
        {
            // The key is read before the early return so the panel can be brought back once hidden.
            HandleToggleKey();

            if (!showOnScreen || !HasReferences())
            {
                return;
            }

            Rect area = new Rect(PanelMargin, PanelMargin, PanelWidth, Screen.height - (PanelMargin * 2f));
            GUILayout.BeginArea(area, GUI.skin.box);

            GUILayout.Label("Economy Debug");
            GUILayout.Label($"Gold: {economy.Gold}");

            if (GUILayout.Button($"Grant {debugGoldGrant} Gold"))
            {
                GrantDebugGold();
            }

            if (GUILayout.Button("Reset All Save Data"))
            {
                ResetAllSaveData();
            }

            GUILayout.Space(6f);

            scroll = GUILayout.BeginScrollView(scroll);
            IReadOnlyList<BulletDefinition> bullets = loadout.Bullets;
            if (bullets != null)
            {
                for (int i = 0; i < bullets.Count; i++)
                {
                    DrawBulletRow(bullets[i]);
                }
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        /// <summary>
        /// One row per kind of ammunition: what it is, whether it is owned, how far it has been
        /// taken, and the two things that can be bought for it.
        /// </summary>
        private void DrawBulletRow(BulletDefinition bullet)
        {
            if (bullet == null)
            {
                return;
            }

            bool unlocked = loadout.IsUnlocked(bullet);
            int level = loadout.GetLevel(bullet);

            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label(unlocked
                ? $"{bullet.DisplayName}  (owned, level {level}/{economy.GetMaxLevel(bullet)})"
                : $"{bullet.DisplayName}  (locked)");

            GUILayout.BeginHorizontal();

            // GUI.enabled is nested rather than set back to true, so this row cannot re-enable a
            // panel that something above it had switched off.
            bool wasEnabled = GUI.enabled;

            GUI.enabled = wasEnabled && economy.CanPurchase(bullet);
            string buyLabel = economy.TryGetPurchasePrice(bullet, out int price)
                ? $"Buy: {price}"
                : "Not for sale";
            if (GUILayout.Button(buyLabel))
            {
                economy.TryPurchase(bullet);
            }

            GUI.enabled = wasEnabled && economy.CanUpgrade(bullet);
            if (GUILayout.Button(UpgradeLabel(bullet, level)))
            {
                economy.TryUpgrade(bullet);
            }

            GUI.enabled = wasEnabled;

            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }

        /// <summary>
        /// Says why an upgrade is unavailable rather than just greying out, since "unpriced" and
        /// "already at the ceiling" are very different authoring mistakes to chase.
        /// </summary>
        private string UpgradeLabel(BulletDefinition bullet, int level)
        {
            if (economy.TryGetUpgradePrice(bullet, out int price, out int targetLevel))
            {
                return $"Upgrade to {targetLevel}: {price}";
            }

            return level >= economy.GetMaxLevel(bullet) ? "Max level" : "No upgrade priced";
        }

        /// <summary>
        /// The toggle is read from the IMGUI event rather than from UnityEngine.Input because this
        /// project runs on the Input System package, where the legacy Input class throws.
        /// </summary>
        private void HandleToggleKey()
        {
            if (toggleKey == KeyCode.None)
            {
                return;
            }

            Event current = Event.current;
            if (current == null || current.type != EventType.KeyDown || current.keyCode != toggleKey)
            {
                return;
            }

            showOnScreen = !showOnScreen;
            current.Use();
        }

        [ContextMenu("Toggle Panel")]
        private void TogglePanel()
        {
            showOnScreen = !showOnScreen;
        }

        [ContextMenu("Grant Debug Gold")]
        private void GrantDebugGold()
        {
            if (!HasReferences())
            {
                return;
            }

            economy.GrantGold(debugGoldGrant);
        }

        /// <summary>Wipes the save. Needs no references, so it works on an unwired panel too.</summary>
        [ContextMenu("Reset All Save Data")]
        private void ResetAllSaveData()
        {
            UserData.ResetAll();
        }

        /// <summary>The loadout equivalent of the row buttons, for when the panel is hidden.</summary>
        [ContextMenu("Upgrade Selected Bullet")]
        private void UpgradeSelectedBullet()
        {
            if (!HasReferences())
            {
                return;
            }

            BulletDefinition selected = loadout.Selected;
            if (selected == null)
            {
                Debug.LogWarning($"{nameof(EconomyDebugPanel)} has nothing selected to upgrade.", this);
                return;
            }

            if (!economy.TryUpgrade(selected))
            {
                Debug.LogWarning($"{selected.DisplayName} could not be upgraded: it is locked, at its "
                                 + "ceiling, unpriced, or unaffordable.", this);
            }
        }

        /// <summary>
        /// True when the panel is wired up. An unwired panel is a developer mistake, so it says so
        /// once and then stays quiet rather than throwing on every frame it is drawn.
        /// </summary>
        private bool HasReferences()
        {
            if (economy != null && loadout != null)
            {
                return true;
            }

            if (!warnedMissingReferences)
            {
                warnedMissingReferences = true;
                Debug.LogWarning($"{nameof(EconomyDebugPanel)} on \"{name}\" needs both an "
                                 + $"{nameof(EconomyService)} and a {nameof(BulletLoadout)}; it does nothing until both are set.", this);
            }

            return false;
        }
#endif
    }
}
