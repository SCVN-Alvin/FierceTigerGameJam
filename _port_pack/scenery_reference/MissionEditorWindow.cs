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

        /// <summary>
        /// Everything the window reads from the asset database, held between repaints.
        ///
        /// This window redraws on every mouse move, so anything it looks up while drawing is
        /// looked up dozens of times a second. Finding a level's JSON meant loading and parsing
        /// every file in the maps folder, once per row - twenty rows over twenty files, hundreds
        /// of asset loads a frame, which is what made the window crawl. All of it is now built
        /// once and thrown away when the project changes.
        /// </summary>
        private Dictionary<string, TextAsset> jsonById;
        private Dictionary<string, int> rowById;
        private readonly Dictionary<string, UnityEngine.Object[]> sceneryChoices =
            new Dictionary<string, UnityEngine.Object[]>();

        /// <summary>MapConfig read through one SerializedObject per repaint, not one per row.</summary>
        private SerializedObject configObject;
        private SerializedProperty configRows;

        private const string BackgroundPrefix = "BG";

        /// <summary>
        /// The floor slot takes the PNG itself, not a material. The ground plane keeps one
        /// material for the whole game and only the picture on it changes per level, so dressing
        /// a beach is dropping in a texture rather than authoring a material asset for it.
        /// </summary>
        private const string FloorPrefix = "Floor";

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
            GetWindow<MissionEditorWindow>("Missions").minSize = new Vector2(1080, 460);
        }

        private void OnEnable()
        {
            AutoFind();
        }

        /// <summary>Anything imported, moved or deleted invalidates what the window remembers.</summary>
        private void OnProjectChange()
        {
            InvalidateCaches();
            Repaint();
        }

        private void InvalidateCaches()
        {
            factsCache.Clear();
            sceneryChoices.Clear();
            jsonById = null;
            rowById = null;
        }

        /// <summary>id -> the JSON file that declares it, built in one pass over the maps folder.</summary>
        private void BuildJsonIndex()
        {
            jsonById = new Dictionary<string, TextAsset>(StringComparer.Ordinal);
            foreach (string guid in AssetDatabase.FindAssets("t:TextAsset", new[] { MapsFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                TextAsset asset = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
                string id = asset != null ? ReadId(asset.text) : null;
                if (!string.IsNullOrEmpty(id))
                {
                    jsonById[id] = asset;
                }
            }
        }

        /// <summary>id -> its row in MapConfig, so a row lookup is not a scan.</summary>
        private void BuildRowIndex()
        {
            rowById = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < configRows.arraySize; i++)
            {
                string id = configRows.GetArrayElementAtIndex(i).FindPropertyRelative("id").stringValue;
                if (!string.IsNullOrEmpty(id))
                {
                    rowById[id] = i;
                }
            }
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

            // Read-only for the whole pass. Every write goes through its own SerializedObject and
            // then drops the caches, so nothing drawn this frame can overwrite it.
            configObject = new SerializedObject(mapConfig);
            configRows = configObject.FindProperty("maps");
            if (jsonById == null)
            {
                BuildJsonIndex();
            }

            if (rowById == null)
            {
                BuildRowIndex();
            }

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
                    InvalidateCaches();
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
                    // The toggle is "Open", not "locked". Showing the locked flag under a
                    // label that reads Open means an unticked box sits beside the word
                    // Open, and ticking it - the natural thing to do to open a mission -
                    // locks it instead. That is how Mission 3 ended up shut.
                    locked.boolValue = !EditorGUILayout.ToggleLeft(
                        "Open", !locked.boolValue, GUILayout.Width(80));
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.LabelField($"{ids.arraySize} levels", EditorStyles.miniLabel, GUILayout.Width(70));
                    if (GUILayout.Button("Remove mission", GUILayout.Width(120)))
                    {
                        missions.DeleteArrayElementAtIndex(missionIndex);
                        return;
                    }
                }

                // Scenery belongs to the mission and only to the mission. Levels used to carry
                // their own copy with an "overridden" flag to protect it, which was three places
                // to set one thing and two of them silently drifting.
                SerializedProperty background = mission.FindPropertyRelative("background");
                SerializedProperty floor = mission.FindPropertyRelative("floorTexture");
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Scenery", GUILayout.Width(70));

                    SceneryField(background, typeof(Sprite), BackgroundPrefix, false, 210f);
                    SceneryField(floor, typeof(Texture2D), FloorPrefix, false, 180f);
                    TilingField(mission.FindPropertyRelative("floorTiling"), missionView);

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
                    InvalidateCaches();
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

        /// <summary>
        /// One scenery slot. Clicking it drops a list of the handful of assets whose name starts
        /// with the slot's prefix, rather than opening Unity's object picker on every sprite and
        /// texture in the project.
        ///
        /// The list is ours rather than Unity's on purpose: the picker is filtered by a search
        /// string, which is a suggestion the picker is free to ignore - and did, offering
        /// unrelated assets. A menu built from the assets we found can only offer those.
        ///
        /// The name prefix is a convention, not a guarantee, so anything already assigned that
        /// does not match is shown in red rather than silently replaced.
        /// </summary>
        private void SceneryField(SerializedProperty property, Type type, string prefix, bool onMapConfig, float width)
        {
            UnityEngine.Object current = property.objectReferenceValue;
            bool offConvention = current != null
                && !current.name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

            Color previous = GUI.color;
            if (offConvention)
            {
                GUI.color = new Color(1f, 0.7f, 0.65f);
            }

            string label = current != null ? current.name : $"{prefix}\u2026  (none)";
            if (GUILayout.Button(label, EditorStyles.popup, GUILayout.Width(width)))
            {
                ShowSceneryMenu(property, type, prefix, onMapConfig);
            }

            GUI.color = previous;

            if (current != null && GUILayout.Button("\u00d7", EditorStyles.miniButton, GUILayout.Width(20)))
            {
                AssignScenery(onMapConfig ? (UnityEngine.Object)mapConfig : missionView,
                    property.propertyPath, null, type, onMapConfig);
            }
        }

        /// <summary>
        /// Turns a plain texture into a sprite, in place, so a PNG can be chosen as a
        /// background without anyone remembering to change its import settings first.
        /// Unity gives a PNG the Default texture type, and a Default texture contains no
        /// sprite at all - the field would silently stay empty.
        /// </summary>
        private static UnityEngine.Object AsSprite(UnityEngine.Object picked)
        {
            if (picked == null || picked is Sprite)
            {
                return picked;
            }

            string assetPath = AssetDatabase.GetAssetPath(picked);
            if (AssetImporter.GetAtPath(assetPath) is TextureImporter importer
                && importer.textureType != TextureImporterType.Sprite)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.SaveAndReimport();
                Debug.Log($"Mission Editor: reimported \"{Path.GetFileName(assetPath)}\" as a Sprite "
                          + "so it can be used as a background.");
            }

            return AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
        }

        /// <summary>
        /// How many times the floor picture repeats across the ground plane. The plane is forty
        /// metres across, so one repeat stretches a deck plate to the width of a building. Grass
        /// hides that; anything with straight lines does not.
        /// </summary>
        private void TilingField(SerializedProperty tiling, UnityEngine.Object owner)
        {
            if (tiling == null)
            {
                return;
            }

            EditorGUI.BeginChangeCheck();
            float next = EditorGUILayout.FloatField(tiling.floatValue, GUILayout.Width(38));
            if (EditorGUI.EndChangeCheck())
            {
                SerializedObject serialized = new SerializedObject(owner);
                SerializedProperty target = serialized.FindProperty(tiling.propertyPath);
                if (target != null)
                {
                    target.floatValue = Mathf.Clamp(next, 0.25f, 32f);
                    serialized.ApplyModifiedProperties();
                    EditorUtility.SetDirty(owner);
                    InvalidateCaches();
                }
            }
        }

        /// <summary>The assets whose name starts with the prefix, found once and kept.</summary>
        private UnityEngine.Object[] SceneryChoices(Type type, string prefix)
        {
            string key = $"{prefix}:{type.Name}";
            if (sceneryChoices.TryGetValue(key, out UnityEngine.Object[] cached))
            {
                return cached;
            }

            // For the background slot the search is for TEXTURES, not sprites. A PNG
            // dropped into the project imports as a plain texture, has no sprite in it,
            // and so never appears in a t:Sprite search - which has now cost three
            // rounds of "why is my background missing". They are listed here and
            // converted on selection instead.
            List<UnityEngine.Object> found = new List<UnityEngine.Object>();
            HashSet<string> seen = new HashSet<string>();
            string searchType = type == typeof(Sprite) ? "Texture2D" : type.Name;
            foreach (string guid in AssetDatabase.FindAssets($"t:{searchType}"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!Path.GetFileNameWithoutExtension(path).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    || !seen.Add(path))
                {
                    continue;
                }

                UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath(path, type)
                                           ?? AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (asset != null)
                {
                    found.Add(asset);
                }
            }

            found.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            cached = found.ToArray();
            sceneryChoices[key] = cached;
            return cached;
        }

        private void ShowSceneryMenu(SerializedProperty property, Type type, string prefix, bool onMapConfig)
        {
            UnityEngine.Object owner = onMapConfig ? (UnityEngine.Object)mapConfig : missionView;
            string path = property.propertyPath;
            UnityEngine.Object current = property.objectReferenceValue;

            GenericMenu menu = new GenericMenu();
            menu.AddItem(new GUIContent("None"), current == null, () => AssignScenery(owner, path, null, type, onMapConfig));
            menu.AddSeparator(string.Empty);

            UnityEngine.Object[] choices = SceneryChoices(type, prefix);
            if (choices.Length == 0)
            {
                menu.AddDisabledItem(new GUIContent($"No {type.Name} named {prefix}\u2026 in the project"));
            }

            foreach (UnityEngine.Object choice in choices)
            {
                UnityEngine.Object picked = choice;
                menu.AddItem(new GUIContent(picked.name), picked == current,
                    () => AssignScenery(owner, path, picked, type, onMapConfig));
            }

            menu.ShowAsContext();
        }

        /// <summary>
        /// Writes one scenery slot. The menu answers after the window has finished drawing, so
        /// this opens its own SerializedObject rather than writing through one the repaint owns.
        /// </summary>
        private void AssignScenery(UnityEngine.Object owner, string path, UnityEngine.Object value, Type expected, bool onMapConfig)
        {
            if (expected == typeof(Sprite))
            {
                value = AsSprite(value);
            }

            SerializedObject serialized = new SerializedObject(owner);
            SerializedProperty target = serialized.FindProperty(path);
            if (target == null)
            {
                return;
            }

            target.objectReferenceValue = value;

            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(owner);
            InvalidateCaches();
            Repaint();
        }

        /// <summary>A row of the MapConfig this repaint opened. Read-only.</summary>
        private bool TryFindMapRow(string id, out SerializedProperty row)
        {
            row = null;
            if (rowById == null || configRows == null || !rowById.TryGetValue(id, out int index))
            {
                return false;
            }

            if (index >= configRows.arraySize)
            {
                return false;
            }

            row = configRows.GetArrayElementAtIndex(index);
            return true;
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

        private TextAsset FindJsonFor(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }

            if (jsonById == null)
            {
                BuildJsonIndex();
            }

            return jsonById.TryGetValue(id, out TextAsset asset) ? asset : null;
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
