#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using GameJam.Config;
using GameJam.Gameplay.Wall;
using UnityEditor;
using UnityEngine;

namespace GameJam.EditorTools
{
    /// <summary>
    /// Renames every campaign map to mission{X}_map{Y}, so reading a map id tells you where in the
    /// campaign it comes.
    ///
    /// A map id is not one string in one file. It is written down in EIGHT places, and a rename
    /// that moves seven of them leaves a map that loads but silently stops paying its rewards, or
    /// stops recording that it was passed. All eight move together here:
    ///
    ///   1. MapConfig's entry id           - what everything else looks the map up by
    ///   2. the "id" inside the map JSON   - MapConfig.OnValidate warns when these two disagree
    ///   3. the JSON's filename            - through AssetDatabase, so the .meta and the guid ride
    ///                                       along; renaming the file on disk instead reads to
    ///                                       Unity as a delete plus an add and breaks every
    ///                                       reference to it
    ///   4. MapProgressionConfig's row     - the pass bar, the reward ids and the ammo budget
    ///   5. MissionConfig's mission        - which the board draws from, so a missed one empties it
    ///   6. MapSelection.defaultMapId      - the map used before the player has picked one
    ///   7. the baked prefab               - Map_{id}.prefab, which the baker names off the id
    ///   8. the baked prefab's meshes      - Map_{id}_Meshes.asset beside it, which the baker
    ///                                       deletes by that name on the next bake and would
    ///                                       otherwise strand forever
    ///
    /// The prefab's ROOT GameObject moves with 7: renaming a prefab asset renames its main object,
    /// which for a prefab is the root. The meshes asset's main mesh is renamed the same way, which
    /// is cosmetic - the prefab holds it by file id, not by name.
    ///
    /// Ids are normally never renamed once a save could refer to them. This is the deliberate
    /// one-time exception Brief 25 §3 calls for, and it costs every existing player their
    /// progress: UserMapProgressData is keyed by id, so the old records are orphaned rather than
    /// migrated. That was the decision; there is no migration on purpose.
    ///
    /// Safe to run twice: a map already at its mission-order id is skipped, and a run that would
    /// collide two maps on one id stops before it changes anything.
    /// </summary>
    public static class MapIdRenamer
    {
        private const string BakedMapFolder = "Assets/GameJam/Prefabs/Maps";

        /// <summary>
        /// Not a campaign map: it is the tutorial, it is not in any mission, and Brief 15's save
        /// flag keys off this exact string. Guarded by name as well as by not being in a mission,
        /// because being wrong about it is expensive and the check is free.
        /// </summary>
        private const string TutorialMapId = "tutorial";

        [MenuItem("Tools/Smashdown/Rename Maps To Mission Order")]
        public static void RenameMapsToMissionOrder()
        {
            MissionConfig missions = LoadFirst<MissionConfig>();
            MapConfig maps = LoadFirst<MapConfig>();
            if (missions == null || maps == null)
            {
                Debug.LogError(
                    "Renaming needs a MissionConfig and a MapConfig in the project. Run "
                    + "Tools/Smashdown/Build Mission Screen first - it is what creates the "
                    + "MissionConfig, and the mission order is where the new ids come from.");
                return;
            }

            if (!TryPlanRenames(missions, maps, out List<Rename> plan, out string error))
            {
                Debug.LogError($"Nothing was renamed. {error}");
                return;
            }

            if (plan.Count == 0)
            {
                Debug.Log("Every campaign map is already at its mission-order id. Nothing to do.");
                return;
            }

            MapProgressionConfig progression = LoadFirst<MapProgressionConfig>();
            MapSelection selection = LoadFirst<MapSelection>();

            // Deliberately NOT wrapped in StartAssetEditing: this moves assets and then reads
            // paths back, and inside a batch the database's view of a path it has been told to
            // move is stale. A dozen extra imports cost a second; a rename that half-lands costs
            // a map that loads but no longer pays its rewards.
            for (int i = 0; i < plan.Count; i++)
            {
                Apply(plan[i], maps, missions, progression, selection);
            }

            EditorUtility.SetDirty(maps);
            EditorUtility.SetDirty(missions);
            if (progression != null)
            {
                EditorUtility.SetDirty(progression);
            }

            if (selection != null)
            {
                EditorUtility.SetDirty(selection);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log(Report(plan));
        }

        /// <summary>One map's move, worked out in full before any of it is carried out.</summary>
        private struct Rename
        {
            public string OldId;
            public string NewId;
            public string MissionId;
        }

        /// <summary>
        /// Works out every old to new pair from the mission order, and refuses the whole run if
        /// the result would put two maps on one id. Planning first is what makes a partial run
        /// impossible: a rename that half-happens is the one thing worse than not running it.
        /// </summary>
        private static bool TryPlanRenames(
            MissionConfig missions,
            MapConfig maps,
            out List<Rename> plan,
            out string error)
        {
            plan = new List<Rename>();
            HashSet<string> claimed = new HashSet<string>(StringComparer.Ordinal);

            for (int m = 0; m < missions.Count; m++)
            {
                Mission mission = missions.Get(m);
                if (mission == null || mission.mapIds == null)
                {
                    continue;
                }

                for (int i = 0; i < mission.mapIds.Length; i++)
                {
                    string oldId = mission.mapIds[i];
                    if (string.IsNullOrEmpty(oldId))
                    {
                        continue;
                    }

                    if (string.Equals(oldId, TutorialMapId, StringComparison.Ordinal))
                    {
                        Debug.LogWarning(
                            $"Mission \"{mission.id}\" lists the tutorial, which is not a campaign map. "
                            + "Left alone.");
                        continue;
                    }

                    string newId = $"mission{m + 1}_map{i + 1}";
                    if (!claimed.Add(newId))
                    {
                        error = $"Two maps would both become \"{newId}\".";
                        return false;
                    }

                    if (string.Equals(oldId, newId, StringComparison.Ordinal))
                    {
                        // Already moved by an earlier run.
                        continue;
                    }

                    if (!maps.TryGet(oldId, out _))
                    {
                        error = $"Mission \"{mission.id}\" names the map \"{oldId}\", which is not in {maps.name}.";
                        return false;
                    }

                    plan.Add(new Rename { OldId = oldId, NewId = newId, MissionId = mission.id });
                }
            }

            // A target that some OTHER map already answers to would collide the moment it is
            // written. It cannot happen from a clean run - mission{X}_map{Y} is a namespace no
            // authored id uses - but it can from a half-finished one, which is exactly when
            // stopping matters.
            for (int i = 0; i < plan.Count; i++)
            {
                if (maps.TryGet(plan[i].NewId, out _))
                {
                    error = $"\"{plan[i].NewId}\" is already the id of another map in {maps.name}.";
                    return false;
                }
            }

            error = null;
            return true;
        }

        private static void Apply(
            Rename rename,
            MapConfig maps,
            MissionConfig missions,
            MapProgressionConfig progression,
            MapSelection selection)
        {
            RenameJson(maps, rename);
            RenameInStringArray(new SerializedObject(maps), "maps", "id", rename);
            RenameInStringArray(new SerializedObject(missions), "missions", "mapIds", rename);

            if (progression != null)
            {
                RenameInStringArray(new SerializedObject(progression), "entries", "mapId", rename);
            }

            if (selection != null)
            {
                SerializedObject serialized = new SerializedObject(selection);
                SerializedProperty defaultMapId = serialized.FindProperty("defaultMapId");
                if (string.Equals(defaultMapId.stringValue, rename.OldId, StringComparison.Ordinal))
                {
                    defaultMapId.stringValue = rename.NewId;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
            }

            RenameBakedAsset($"{BakedMapFolder}/Map_{rename.OldId}.prefab", $"Map_{rename.NewId}");
            RenameBakedAsset($"{BakedMapFolder}/Map_{rename.OldId}_Meshes.asset", $"Map_{rename.NewId}_Meshes");
        }

        /// <summary>
        /// Rewrites the id inside the map JSON and then renames the file.
        ///
        /// The text is edited in place rather than parsed and written back out. The campaign maps
        /// are ONE-space indented (only the dev maps use two), so any reserialisation would rewrite
        /// every line of every map and collide head-on with Brief 23, which rewrites the same
        /// files. Replacing the one id value leaves every other byte exactly as it was.
        /// </summary>
        private static void RenameJson(MapConfig maps, Rename rename)
        {
            if (!maps.TryGet(rename.OldId, out MapInfo map) || map.MapJson == null)
            {
                Debug.LogWarning($"Map \"{rename.OldId}\" has no JSON assigned, so only its ids were moved.");
                return;
            }

            string path = AssetDatabase.GetAssetPath(map.MapJson);
            string text = File.ReadAllText(path);

            // The FIRST "id" whose value is the map's own id. Block ids ("f0_c0_r0") can never
            // match it, so this cannot land on one, and it does not care where in the file the
            // map's id was written or how the file is indented.
            string needle = $"\"{rename.OldId}\"";
            int idKey = text.IndexOf("\"id\"", StringComparison.Ordinal);
            int value = idKey >= 0 ? text.IndexOf(needle, idKey, StringComparison.Ordinal) : -1;
            if (value < 0)
            {
                Debug.LogWarning(
                    $"{Path.GetFileName(path)} does not declare the id \"{rename.OldId}\", so its text "
                    + "was left alone. Check it by hand.");
            }
            else
            {
                StringBuilder rewritten = new StringBuilder(text.Length);
                rewritten.Append(text, 0, value);
                rewritten.Append('"').Append(rename.NewId).Append('"');
                rewritten.Append(text, value + needle.Length, text.Length - value - needle.Length);

                // No BOM and no line-ending translation, so the diff is the one line.
                File.WriteAllText(path, rewritten.ToString(), new UTF8Encoding(false));
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            }

            // Through AssetDatabase so the .meta follows and the guid survives. Every reference to
            // this JSON is by guid, so they all keep pointing at it.
            string moved = AssetDatabase.RenameAsset(path, rename.NewId);
            if (!string.IsNullOrEmpty(moved))
            {
                Debug.LogWarning($"Could not rename {path} to {rename.NewId}.json: {moved}");
            }
        }

        /// <summary>
        /// Rewrites one map id wherever it appears in an array on an asset. The element property
        /// is either a string itself or an array of strings, which covers all four of the arrays
        /// that hold map ids.
        /// </summary>
        private static void RenameInStringArray(
            SerializedObject serialized,
            string arrayName,
            string fieldName,
            Rename rename)
        {
            SerializedProperty array = serialized.FindProperty(arrayName);
            if (array == null || !array.isArray)
            {
                return;
            }

            bool changed = false;
            for (int i = 0; i < array.arraySize; i++)
            {
                SerializedProperty field = array.GetArrayElementAtIndex(i).FindPropertyRelative(fieldName);
                if (field == null)
                {
                    continue;
                }

                if (field.isArray && field.propertyType != SerializedPropertyType.String)
                {
                    for (int e = 0; e < field.arraySize; e++)
                    {
                        SerializedProperty element = field.GetArrayElementAtIndex(e);
                        if (string.Equals(element.stringValue, rename.OldId, StringComparison.Ordinal))
                        {
                            element.stringValue = rename.NewId;
                            changed = true;
                        }
                    }
                }
                else if (string.Equals(field.stringValue, rename.OldId, StringComparison.Ordinal))
                {
                    field.stringValue = rename.NewId;
                    changed = true;
                }
            }

            if (changed)
            {
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        /// <summary>
        /// Moves a baked artefact if it is there. A map that has never been baked has neither, and
        /// that is not worth a warning: the baker makes them at the new name on the next run.
        /// </summary>
        private static void RenameBakedAsset(string path, string newName)
        {
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path) == null)
            {
                return;
            }

            string moved = AssetDatabase.RenameAsset(path, newName);
            if (!string.IsNullOrEmpty(moved))
            {
                Debug.LogWarning($"Could not rename {path} to {newName}: {moved}");
            }
        }

        private static string Report(List<Rename> plan)
        {
            StringBuilder report = new StringBuilder();
            report.Append("Renamed ").Append(plan.Count).AppendLine(" map(s) to mission order:");

            for (int i = 0; i < plan.Count; i++)
            {
                report.Append("  ").Append(plan[i].OldId)
                    .Append("  ->  ").Append(plan[i].NewId)
                    .Append("   (").Append(plan[i].MissionId).AppendLine(")");
            }

            report.AppendLine(
                "Every save is now orphaned: map progress is keyed by id, so players start the "
                + "campaign again. That was the call Brief 25 made; there is no migration.");
            report.Append(
                "The tutorial was not touched, and neither were the reward ids - pass_map_4 and "
                + "friends are RewardConfig's naming, not a map's.");
            return report.ToString();
        }

        private static T LoadFirst<T>() where T : UnityEngine.Object
        {
            string[] guids = AssetDatabase.FindAssets($"t:{typeof(T).Name}");
            for (int i = 0; i < guids.Length; i++)
            {
                T asset = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guids[i]));
                if (asset != null)
                {
                    return asset;
                }
            }

            return null;
        }
    }
}
#endif
