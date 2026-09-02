using GameJam.Data;
using UnityEditor;
using UnityEngine;

namespace GameJam.EditorTools
{
    /// <summary>
    /// The save-state test bench, all under Tools/Smashdown/Reset: every reset a tester reaches
    /// for lives in one fold-out, each cutting exactly one thing and saying what it left alone.
    /// The unlock helpers sit beside it at the Smashdown level.
    /// </summary>
    public static class GarageSaveReset
    {
        [MenuItem("Tools/Smashdown/Reset/All (whole save, back to first launch)")]
        private static void ResetAll()
        {
            foreach (string key in new[]
                     { "user.bullets", "user.vehicles", "user.inventory", "user.maps", "user.tutorial" })
            {
                PlayerPrefs.DeleteKey("gamejam." + key);
            }

            PlayerPrefs.Save();
            UserData.Reload();
            Debug.Log("Save wiped: garage, gold, missions and tutorial all back to first launch.");
        }

        [MenuItem("Tools/Smashdown/Reset/Tutorial (plays again on next run)")]
        private static void ResetTutorial()
        {
            PlayerPrefs.DeleteKey("gamejam.user.tutorial");
            PlayerPrefs.Save();
            UserData.Reload();
            Debug.Log("Tutorial flag cleared: it plays again on the next entry. Nothing else touched.");
        }

        [MenuItem("Tools/Smashdown/Reset/Garage (bullets + cannons, keep missions)")]
        private static void ResetGarage()
        {
            PlayerPrefs.DeleteKey("gamejam.user.bullets");
            PlayerPrefs.DeleteKey("gamejam.user.vehicles");
            PlayerPrefs.Save();

            // The statics cache the old records; a reload makes every open system re-read the
            // now-fresh state instead of writing the cached one back over the reset on exit.
            UserData.Reload();

            Debug.Log("Garage save reset: bullets and vehicles back to their starting state. "
                      + "Missions, gold and tutorial untouched.");
        }

        [MenuItem("Tools/Smashdown/Reset/Mission 1 Progress")]
        private static void ResetMissionOne()
        {
            ResetMission(1);
        }

        [MenuItem("Tools/Smashdown/Reset/Mission 2 Progress")]
        private static void ResetMissionTwo()
        {
            ResetMission(2);
        }

        [MenuItem("Tools/Smashdown/Reset/Mission 3 Progress")]
        private static void ResetMissionThree()
        {
            ResetMission(3);
        }

        private static void ResetMission(int mission)
        {
            string prefix = "mission" + mission + "_";
            int removed = UserData.Maps.maps.RemoveAll(
                entry => entry != null && entry.mapId != null && entry.mapId.StartsWith(prefix));
            UserData.Save();
            UserData.Reload();
            Debug.Log("Mission " + mission + " progress reset: " + removed + " record(s) removed. "
                      + "Other missions, garage and gold untouched.");
        }

        [MenuItem("Tools/Smashdown/Unlock/All Missions (mark every map passed)")]
        private static void UnlockAllMissions()
        {
            for (int mission = 1; mission <= 3; mission++)
            {
                for (int map = 1; map <= 9; map++)
                {
                    MapProgress progress = UserData.Maps.GetOrCreate($"mission{mission}_map{map}");
                    progress.passed = true;
                    if (progress.bestClearPercent < 0.5f)
                    {
                        progress.bestClearPercent = 0.5f;
                    }
                }
            }

            UserData.Save();
            UserData.Reload();
            Debug.Log("All 27 campaign maps marked passed - every mission is open. Rewards stay unclaimed.");
        }

        [MenuItem("Tools/Smashdown/Unlock/Full Garage (all bullets + cannons, max level)")]
        private static void UnlockFullGarage()
        {
            // Bullets author two levels, vehicles three - the caps come from the shipped
            // configs, not from one shared number.
            string[] bulletIds = { "rock_type", "cannon_type" };
            string[] vehicleIds = { "cannon_a", "cannon_b", "cannon_c" };

            foreach (string id in bulletIds)
            {
                UserData.Bullets.Unlock(id);
                UserData.Bullets.SetLevel(id, 2);
            }

            foreach (string id in vehicleIds)
            {
                UserData.Vehicles.Unlock(id);
                UserData.Vehicles.SetLevel(id, 3);
            }

            UserData.Save();
            Debug.Log("Garage fully unlocked: bullets at II, cannons at III. Missions and gold untouched.");
        }

        [MenuItem("Tools/Smashdown/Give 999,999 Gold")]
        private static void GiveTestGold()
        {
            UserData.Inventory.gold = 999999;
            UserData.Save();
            UserData.Reload();
            Debug.Log("Gold set to 999,999. Everything else untouched.");
        }

        [MenuItem("Tools/Smashdown/Set All Cannons To Level II")]
        private static void SetCannonsLevelTwo()
        {
            foreach (string id in new[] { "cannon_a", "cannon_b", "cannon_c" })
            {
                UserData.Vehicles.Unlock(id);
                UserData.Vehicles.SetLevel(id, 2);
            }

            UserData.Save();
            Debug.Log("Every cannon set to level II, for checking the two-barrel models.");
        }
    }
}
