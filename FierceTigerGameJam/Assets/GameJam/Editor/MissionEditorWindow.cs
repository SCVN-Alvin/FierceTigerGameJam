using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GameJam.Config;
using GameJam.Gameplay.Wall;
using GameJam.UI;
using UnityEditor;
using UnityEngine;

namespace GameJam.EditorTools
{
    /// <summary>
    /// One window for the whole campaign layout.
    ///
    /// Adding a level used to mean editing three things by hand and keeping them in step:
    /// a MapConfig row, a MapProgressionConfig row, and the mission's slotMapIds on the
    /// MissionScreen prefab. Miss one and the slot silently points at a map that does not
    /// exist - no error, no warning, just a level that will not open.
    ///
    /// Here a level is one JSON file. Drop it in and all three are written together; clear the
    /// field and all three are cleaned up. The list on the right is the same data read back, so
    /// what the window shows is what the game will load.
    /// </summary>
    public sealed class MissionEditorWindow : EditorWindow
    {
        private const string MapsFolder = "Assets/GameJam/Maps";

        private MapConfig mapConfig;
        private MapProgressionConfig progression;
        private MissionPanelView missionView;

        private Vector2 scroll;
        private readonly Dictionary<string, MapFacts> factsCache = new Dictionary<string, MapFacts>();

        /// <summary>What the JSON itself says, read straight from the file so the window cannot
        /// disagree with what the game will build.</summary>
        private struct MapFacts
        {
            public bool Parsed;
            public string Id;
            public int Entries;
            public int Walls;
            public int Loose;
            public string Problem;
        }

        [MenuItem("Tools/Smashdown/Mission Editor")]
        public static void Open()
        {
            GetWindow<MissionEditorWindow>("Missions").minSize = new Vector2(760, 420);
        }

        private void OnEnable()
        {
            AutoFind();
        }

        private void AutoFind()
        {
            if (mapConfig == null) mapConfig = FindAsset<MapConfig>();
            if (progression == null) progression = FindAsset<MapProgressionConfig>();
            if (missionView == null) missionView = FindMissionView();
        }

        private static T FindAsset<T>() where T : ScriptableObject
        {
            string[] guids = AssetDatabase.FindAssets($"t:{typeof(T).Name}");
            return guids.Length == 0
                ? null
                : AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        private static MissionPanelView FindMissionView()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab MissionScreen"))
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                MissionPanelView view = prefab != null ? prefab.GetComponentInChildren<MissionPanelView>(true) : null;
                if (view != null)
                {
                    return view;
                }
            }

            return null;
        }

        private void OnGUI()
        {
            DrawHeader();
            if (mapConfig == null || progression == null || missionView == null)
            {
                EditorGUILayout.HelpBox(
                    "Point the three fields above at MapConfig, MapProgressionConfig and the "
                    + "MissionScreen prefab. They are found automatically when there is only one "
                    + "of each in the project.", MessageType.Info);
                return;
            }

            SerializedObject viewObject = new SerializedObject(missionView);
            SerializedProperty missions = viewObject.FindProperty("missions");

            scroll = EditorGUILayout.BeginScrollView(scroll);
            for (int m = 0; m < missions.arraySize; m++)
            {
                DrawMission(missions, m);
            }

            EditorGUILayout.Space(6);
            if (GUILayout.Button("+  Add mission", GUILayout.Height(24)))
            {
                missions.arraySize++;
                SerializedProperty added = missions.GetArrayElementAtIndex(missions.arraySize - 1);
                added.FindPropertyRelative("locked").boolValue = true;
                added.FindPropertyRelative("slotMapIds").arraySize = 0;
            }

            EditorGUILayout.EndScrollView();

            if (viewObject.hasModifiedProperties)
            {
                viewObject.ApplyModifiedProperties();
                MarkMissionViewDirty();
            }
        }

        private void DrawHeader()
        {
            EditorGUILayout.Space(4);
            using (new EditorGUILayout.HorizontalScope())
            {
                mapConfig = (MapConfig)EditorGUILayout.ObjectField(mapConfig, typeof(MapConfig), false);
                progression = (MapProgressionConfig)EditorGUILayout.ObjectField(progression, typeof(MapProgressionConfig), false);
                missionView = (MissionPanelView)EditorGUILayout.ObjectField(missionView, typeof(MissionPanelView), false);
                if (GUILayout.Button("Refresh", GUILayout.Width(70)))
                {
                    factsCache.Clear();
                    AutoFind();
                }
            }

            EditorGUILayout.Space(4);
        }

        private void DrawMission(SerializedProperty missions, int missionIndex)
        {
            SerializedProperty mission = missions.GetArrayElementAtIndex(missionIndex);
            SerializedProperty locked = mission.FindPropertyRelative("locked");
            SerializedProperty ids = mission.FindPropertyRelative("slotMapIds");

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField($"MISSION {missionIndex + 1}", EditorStyles.boldLabel, GUILayout.Width(110));
                    locked.boolValue = EditorGUILayout.ToggleLeft(
                        locked.boolValue ? "Locked" : "Open", locked.boolValue, GUILayout.Width(80));
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.LabelField($"{ids.arraySize} levels", EditorStyles.miniLabel, GUILayout.Width(70));
                    if (GUILayout.Button("Remove mission", GUILayout.Width(120)))
                    {
                        missions.DeleteArrayElementAtIndex(missionIndex);
                        return;
                    }
                }

                for (int slot = 0; slot < ids.arraySize; slot++)
                {
                    DrawSlot(missions, missionIndex, ids, slot);
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("+  Add level", GUILayout.Height(22)))
                    {
                        ids.arraySize++;
                        ids.GetArrayElementAtIndex(ids.arraySize - 1).stringValue = string.Empty;
                    }

                    using (new EditorGUI.DisabledScope(ids.arraySize == 0))
                    {
                        if (GUILayout.Button("−  Remove last", GUILayout.Height(22), GUILayout.Width(130)))
                        {
                            ids.arraySize--;
                        }
                    }
                }
            }

            EditorGUILayout.Space(4);
        }

        private void DrawSlot(SerializedProperty missions, int missionIndex, SerializedProperty ids, int slot)
        {
            SerializedProperty idProperty = ids.GetArrayElementAtIndex(slot);
            string id = idProperty.stringValue;
            int levelNumber = GlobalLevelNumber(missions, missionIndex, slot);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField($"LEVEL {levelNumber}", GUILayout.Width(70));

                // The JSON is the level. Drop a different one here and the map is replaced.
                TextAsset current = FindJsonFor(id);
                TextAsset next = (TextAsset)EditorGUILayout.ObjectField(current, typeof(TextAsset), false, GUILayout.Width(210));
                if (next != current)
                {
                    idProperty.stringValue = next == null ? string.Empty : ApplyJson(next);
                    factsCache.Clear();
                }

                DrawFacts(id, current);

                if (GUILayout.Button("↑", GUILayout.Width(24)) && slot > 0)
                {
                    ids.MoveArrayElement(slot, slot - 1);
                }

                if (GUILayout.Button("↓", GUILayout.Width(24)) && slot < ids.arraySize - 1)
                {
                    ids.MoveArrayElement(slot, slot + 1);
                }

                if (GUILayout.Button("×", GUILayout.Width(24)))
                {
                    ids.DeleteArrayElementAtIndex(slot);
                }
            }
        }

        private void DrawFacts(string id, TextAsset json)
        {
            if (string.IsNullOrEmpty(id))
            {
                EditorGUILayout.LabelField("empty slot — shows NO MAP YET", EditorStyles.miniLabel);
                return;
            }

            if (json == null)
            {
                GUI.color = new Color(1f, 0.55f, 0.5f);
                EditorGUILayout.LabelField($"\"{id}\" has no JSON in {MapsFolder}", EditorStyles.miniLabel);
                GUI.color = Color.white;
                return;
            }

            MapFacts facts = Facts(json);
            if (!facts.Parsed)
            {
                GUI.color = new Color(1f, 0.55f, 0.5f);
                EditorGUILayout.LabelField(facts.Problem, EditorStyles.miniLabel);
                GUI.color = Color.white;
                return;
            }

            int ammo = AmmoFor(id);
            string line = $"{facts.Entries} entries · {facts.Walls} walls · {facts.Loose} loose · {ammo} ammo";
            if (!string.Equals(facts.Id, id, StringComparison.Ordinal))
            {
                GUI.color = new Color(1f, 0.8f, 0.4f);
                line += $"   ⚠ json id is \"{facts.Id}\"";
            }

            EditorGUILayout.LabelField(line, EditorStyles.miniLabel);
            GUI.color = Color.white;
        }

        /// <summary>Level numbers run straight through the missions, the way the panel shows them.</summary>
        private static int GlobalLevelNumber(SerializedProperty missions, int missionIndex, int slot)
        {
            int number = 1;
            for (int m = 0; m < missionIndex; m++)
            {
                number += missions.GetArrayElementAtIndex(m).FindPropertyRelative("slotMapIds").arraySize;
            }

            return number + slot;
        }

        // ------------------------------------------------------------------ writing

        /// <summary>
        /// Makes a dropped JSON into a playable level: the id comes from inside the file, and a
        /// MapConfig row and a MapProgressionConfig row are created for it if they are missing.
        /// Returns the id to store in the mission slot.
        /// </summary>
        private string ApplyJson(TextAsset json)
        {
            MapFacts facts = Facts(json);
            if (!facts.Parsed)
            {
                EditorUtility.DisplayDialog("Not a map", facts.Problem, "OK");
                return string.Empty;
            }

            EnsureMapConfigRow(facts.Id, json);
            EnsureProgressionRow(facts.Id, facts.Entries);
            return facts.Id;
        }

        private void EnsureMapConfigRow(string id, TextAsset json)
        {
            SerializedObject config = new SerializedObject(mapConfig);
            SerializedProperty maps = config.FindProperty("maps");

            for (int i = 0; i < maps.arraySize; i++)
            {
                SerializedProperty row = maps.GetArrayElementAtIndex(i);
                if (row.FindPropertyRelative("id").stringValue != id)
                {
                    continue;
                }

                // Already listed: point it at this file, and drop the baked prefab because it
                // was built from whatever the JSON used to say.
                row.FindPropertyRelative("mapJson").objectReferenceValue = json;
                row.FindPropertyRelative("mapPrefab").objectReferenceValue = null;
                config.ApplyModifiedProperties();
                EditorUtility.SetDirty(mapConfig);
                return;
            }

            maps.arraySize++;
            SerializedProperty added = maps.GetArrayElementAtIndex(maps.arraySize - 1);
            added.FindPropertyRelative("id").stringValue = id;
            added.FindPropertyRelative("displayName").stringValue = Prettify(id);
            added.FindPropertyRelative("mapJson").objectReferenceValue = json;
            added.FindPropertyRelative("clearedImage").objectReferenceValue = null;
            added.FindPropertyRelative("mapPrefab").objectReferenceValue = null;
            config.ApplyModifiedProperties();
            EditorUtility.SetDirty(mapConfig);
        }

        private void EnsureProgressionRow(string id, int entries)
        {
            SerializedObject config = new SerializedObject(progression);
            SerializedProperty entriesProperty = config.FindProperty("entries");

            for (int i = 0; i < entriesProperty.arraySize; i++)
            {
                if (entriesProperty.GetArrayElementAtIndex(i).FindPropertyRelative("mapId").stringValue == id)
                {
                    return;
                }
            }

            entriesProperty.arraySize++;
            SerializedProperty added = entriesProperty.GetArrayElementAtIndex(entriesProperty.arraySize - 1);
            added.FindPropertyRelative("mapId").stringValue = id;
            added.FindPropertyRelative("requiredClearPercent").floatValue = 0.8f;
            added.FindPropertyRelative("passMapRewardId").stringValue = string.Empty;
            added.FindPropertyRelative("clearMapRewardId").stringValue = string.Empty;
            added.FindPropertyRelative("bulletPickLimit").intValue = SuggestedAmmo(entries);
            config.ApplyModifiedProperties();
            EditorUtility.SetDirty(progression);
        }

        /// <summary>A starting point, not a tuning pass: bigger maps come in with more ammunition.</summary>
        private static int SuggestedAmmo(int entries)
        {
            if (entries <= 120) return 10;
            if (entries <= 220) return 14;
            if (entries <= 350) return 18;
            return 20;
        }

        private void MarkMissionViewDirty()
        {
            EditorUtility.SetDirty(missionView);
            GameObject root = PrefabUtility.GetOutermostPrefabInstanceRoot(missionView.gameObject);
            if (root != null)
            {
                PrefabUtility.RecordPrefabInstancePropertyModifications(missionView);
            }

            AssetDatabase.SaveAssets();
        }

        // ------------------------------------------------------------------ reading

        private int AmmoFor(string id)
        {
            foreach (MapProgressionConfig.Entry entry in progression.Entries)
            {
                if (entry.mapId == id)
                {
                    return entry.bulletPickLimit;
                }
            }

            return 0;
        }

        private static TextAsset FindJsonFor(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }

            foreach (string guid in AssetDatabase.FindAssets("t:TextAsset", new[] { MapsFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                TextAsset asset = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
                if (asset != null && ReadId(asset.text) == id)
                {
                    return asset;
                }
            }

            return null;
        }

        private MapFacts Facts(TextAsset json)
        {
            if (json == null)
            {
                return new MapFacts { Problem = "no file" };
            }

            string key = AssetDatabase.GetAssetPath(json);
            if (factsCache.TryGetValue(key, out MapFacts cached))
            {
                return cached;
            }

            MapFacts facts = Parse(json.text);
            factsCache[key] = facts;
            return facts;
        }

        /// <summary>
        /// Counts what the JSON contains without going through JsonUtility, so a file that is
        /// broken in a way the importer would reject still reports something useful instead of
        /// throwing.
        /// </summary>
        private static MapFacts Parse(string text)
        {
            MapFacts facts = new MapFacts();
            try
            {
                KnockdownMapDefinition map = JsonUtility.FromJson<KnockdownMapDefinition>(text);
                if (map == null || map.layers == null || map.layers.Length == 0)
                {
                    facts.Problem = "no layers - not a map JSON";
                    return facts;
                }

                HashSet<string> walls = new HashSet<string>();
                int entries = 0;
                int loose = 0;
                foreach (KnockdownMapLayer layer in map.layers)
                {
                    if (layer?.blocks == null)
                    {
                        continue;
                    }

                    foreach (KnockdownMapBlock block in layer.blocks)
                    {
                        entries++;
                        string wall = block.WallId;
                        if (string.IsNullOrEmpty(wall))
                        {
                            loose++;
                        }
                        else
                        {
                            walls.Add(wall);
                        }
                    }
                }

                facts.Parsed = true;
                facts.Id = map.id;
                facts.Entries = entries;
                facts.Walls = walls.Count;
                facts.Loose = loose;
                return facts;
            }
            catch (Exception error)
            {
                facts.Problem = $"unreadable: {error.Message}";
                return facts;
            }
        }

        private static string ReadId(string text)
        {
            try
            {
                KnockdownMapDefinition map = JsonUtility.FromJson<KnockdownMapDefinition>(text);
                return map?.id;
            }
            catch
            {
                return null;
            }
        }

        private static string Prettify(string id)
        {
            string name = id.Replace('_', ' ').Trim();
            return name.Length == 0 ? id : char.ToUpperInvariant(name[0]) + name.Substring(1);
        }
    }
}
