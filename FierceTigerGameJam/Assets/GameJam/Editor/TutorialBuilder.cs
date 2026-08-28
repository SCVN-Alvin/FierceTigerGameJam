#if UNITY_EDITOR
using System.IO;
using GameJam.Config;
using GameJam.Gameplay.Cannon;
using GameJam.Gameplay.Combat;
using GameJam.Gameplay.Flow;
using GameJam.Gameplay.Wall;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace GameJam.EditorTools
{
    /// <summary>
    /// Authors everything the first-launch tutorial needs: its one-block map, its rules row, the
    /// overlay drawn over it, and the controller in the scene that ties the three to the flow.
    ///
    /// Built the way the loading, cleared and fail screens are, and for the same reasons: the
    /// prefab is the one description of the overlay, the scene holds nothing but an instance of
    /// it, and every number below is written only when the thing it belongs to did not already
    /// exist, so re-running never costs a tuning pass. Deleting the prefab is how you ask for the
    /// numbers below back.
    /// </summary>
    public static class TutorialBuilder
    {
        private const string ConfigFolder = "Assets/GameJam/Config";
        private const string MapsFolder = "Assets/GameJam/Maps";
        private const string TutorialFolder = "Assets/GameJam/Prefabs/UI/Tutorial";
        private const string TutorialTextures = "Assets/GameJam/Textures/UI/Tutorial";

        private const string MapJsonPath = MapsFolder + "/tutorial.json";
        private const string ProgressionPath = ConfigFolder + "/MapProgressionConfig.asset";

        private const string PanelSprite = TutorialTextures + "/UI_Tutorial.png";

        /// <summary>
        /// The artist's dim-with-a-hole, when it lands. Until then the fallback below is used, and
        /// swapping to this is a matter of dropping the file in and re-running.
        /// </summary>
        private const string HoleSprite = TutorialTextures + "/UI_Tutorial_Hole.png";

        /// <summary>
        /// What stands in for it. Contrary to the brief, this is not a plain dim: Filter.png is
        /// already a 1216x1920 black field at alpha 0.6 with a soft transparent ellipse punched
        /// through its middle, which is exactly the spotlight the brief describes. It is used at
        /// full strength rather than "at reduced alpha" for that reason - the dimming is painted
        /// into the file, and tinting it down again would only wash the effect out.
        /// </summary>
        private const string HoleFallbackSprite = TutorialTextures + "/Filter.png";

        public const string OverlayPrefabPath = TutorialFolder + "/TutorialOverlay.prefab";
        public const string OverlayName = "TutorialOverlay";

        /// <summary>
        /// Where the overlay sits under the Canvas: straight after the in-run HUD, so the bullet
        /// counter and the gear stay readable through it and the result screens still cover it.
        /// </summary>
        private const string HudName = "RunHud";

        private const string MapId = "tutorial";
        private const string MapDisplayName = "Tutorial";

        /// <summary>
        /// One block means any hit that destroys it is 100 percent, so the usual pass bar is met
        /// the moment the block goes. Three rounds, and no reward ids at all: the tutorial pays
        /// nothing, which is what keeps it out of the player's gold and off the mission board.
        /// </summary>
        private const float RequiredClearPercent = 0.8f;
        private const int BulletPickLimit = 3;

        /// <summary>The map JSON written when there is none, matching the canonical schema.</summary>
        private const string MapJsonBody = @"{
  ""schemaVersion"": 1,
  ""id"": ""tutorial"",
  ""grid"": { ""width"": 1, ""height"": 1, ""cellSize"": 0.25, ""layerDepth"": 0.25 },
  ""layers"": [
    {
      ""level"": 0,
      ""blocks"": [
        { ""id"": ""f0_c0_r0"", ""type"": ""brick_1x1"", ""position"": { ""x"": 0, ""y"": 0 }, ""rotation"": 0 }
      ]
    }
  ]
}
";

        [MenuItem("Tools/Smashdown/Build Tutorial")]
        public static void BuildTutorial()
        {
            TextAsset mapJson = EnsureMapJson();
            EnsureRulesRow();

            GameObject overlay = BuildOverlayPrefab();
            EnsureSceneInstance(overlay, mapJson);

            AssetDatabase.SaveAssets();
            Debug.Log(
                "Built the tutorial: " + MapJsonPath + ", its rules row, " + OverlayPrefabPath
                + ", and an instance of the overlay under the Canvas. Save the scene to keep the wiring.");
        }

        // ------------------------------------------------------------------ map

        /// <summary>
        /// The tutorial's own map, deliberately not listed in MapConfig: the mission board counts
        /// what that asset holds, so a map outside it is one the player is never shown and never
        /// counted against. It rides the normal pipeline anyway, because MapSelection takes any
        /// MapInfo rather than only ones the config knows.
        /// </summary>
        private static TextAsset EnsureMapJson()
        {
            TextAsset existing = AssetDatabase.LoadAssetAtPath<TextAsset>(MapJsonPath);
            if (existing != null)
            {
                return existing;
            }

            EnsureFolder(MapsFolder);
            File.WriteAllText(MapJsonPath, MapJsonBody);
            AssetDatabase.ImportAsset(MapJsonPath);
            Debug.Log($"{nameof(TutorialBuilder)} wrote {MapJsonPath}.");
            return AssetDatabase.LoadAssetAtPath<TextAsset>(MapJsonPath);
        }

        /// <summary>
        /// Appends the tutorial's rules row, and only if no row already claims the id.
        ///
        /// Appending rather than filling an empty table, which is how GameConfigBuilder writes
        /// this asset: it only writes when the table is empty, so a tutorial row added before it
        /// has run would stop the campaign maps ever getting theirs. Run Build Game Config first
        /// on a fresh project.
        /// </summary>
        private static void EnsureRulesRow()
        {
            MapProgressionConfig progression = AssetDatabase.LoadAssetAtPath<MapProgressionConfig>(ProgressionPath);
            if (progression == null)
            {
                Debug.LogWarning(
                    $"{nameof(TutorialBuilder)} found no {ProgressionPath}, so the tutorial has no rules "
                    + "row and would be played with the default budget. Run Tools > Smashdown > Build Game Config.");
                return;
            }

            SerializedObject serialized = new SerializedObject(progression);
            SerializedProperty entries = serialized.FindProperty("entries");

            for (int i = 0; i < entries.arraySize; i++)
            {
                SerializedProperty mapId = entries.GetArrayElementAtIndex(i).FindPropertyRelative("mapId");
                if (mapId != null && mapId.stringValue == MapId)
                {
                    return;
                }
            }

            entries.InsertArrayElementAtIndex(entries.arraySize);
            SerializedProperty entry = entries.GetArrayElementAtIndex(entries.arraySize - 1);
            entry.FindPropertyRelative("mapId").stringValue = MapId;
            entry.FindPropertyRelative("requiredClearPercent").floatValue = RequiredClearPercent;
            entry.FindPropertyRelative("passMapRewardId").stringValue = string.Empty;
            entry.FindPropertyRelative("clearMapRewardId").stringValue = string.Empty;
            entry.FindPropertyRelative("bulletPickLimit").intValue = BulletPickLimit;

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(progression);
            Debug.Log($"{nameof(TutorialBuilder)} added the \"{MapId}\" rules row to {ProgressionPath}.");
        }

        // ------------------------------------------------------------------ overlay

        /// <summary>
        /// The dim with the spotlight, and the prompt above it. Nothing here takes input: the
        /// player shoots straight through the overlay, which is what makes the first tap both
        /// dismiss the prompt and fire the shot the prompt asked for.
        /// </summary>
        private static GameObject BuildOverlayPrefab()
        {
            EnsureFolder(TutorialFolder);

            // Resolved before the prefab is opened, not inside the callback: picking the fallback
            // can trigger a re-import, and re-importing an asset while prefab contents are loaded
            // is asking for the edit to be made against a stale copy.
            string holeSprite = ResolveHoleSpritePath();

            return EnsurePrefab(OverlayPrefabPath, OverlayName, (root, created) =>
            {
                RectTransform rect = (RectTransform)root.transform;
                if (created)
                {
                    Place(rect, Vector2.zero, Vector2.one);
                }

                // Full screen: where the hole falls is painted into the art, so the sprite is
                // stretched over the screen rather than fitted to the block.
                EnsureImage("Hole", rect, holeSprite, Vector2.zero, Vector2.one,
                    Image.Type.Simple, false);

                // The ref's panel at x 340-890, y 300-520 of a 1216x2160 mock-up, read as
                // fractions of the screen so it holds its place on an aspect the mock-up is not.
                EnsureImage("Panel", rect, PanelSprite,
                    new Vector2(0.28f, 0.759f), new Vector2(0.73f, 0.861f),
                    Image.Type.Simple, true);

                if (created)
                {
                    // Inactive in the prefab, not only on the instance: the controller switches it
                    // on for the tutorial run, and an overlay that shipped active would be an
                    // override on every instance of it.
                    root.SetActive(false);
                }
            });
        }

        /// <summary>
        /// The artist's hole if it has landed, otherwise Filter.png, which already carries the
        /// same dim and ellipse. Says which was used, because the two do not look the same and a
        /// silent fallback is how you end up shipping the stand-in.
        /// </summary>
        private static string ResolveHoleSpritePath()
        {
            if (AssetDatabase.LoadAssetAtPath<Sprite>(HoleSprite) != null)
            {
                return HoleSprite;
            }

            if (LoadSpriteRepairingImport(HoleFallbackSprite) != null)
            {
                Debug.Log(
                    $"{nameof(TutorialBuilder)} used {HoleFallbackSprite} for the spotlight, because "
                    + $"{HoleSprite} has not been supplied yet. Drop that file in and re-run to swap it.");
                return HoleFallbackSprite;
            }

            Debug.LogWarning(
                $"{nameof(TutorialBuilder)} could not load a spotlight sprite from {HoleSprite} or "
                + $"{HoleFallbackSprite}, so the tutorial has a prompt but no dim.");
            return HoleFallbackSprite;
        }

        /// <summary>
        /// Loads a sprite, correcting the one import setting that stops a texture being one.
        ///
        /// Needed because Filter.png ships as Sprite (Multiple) with no sub-sprites defined, which
        /// yields no Sprite sub-asset at all: LoadAssetAtPath returns null, and the two result
        /// screens that already point at this file are sitting on empty, disabled images because
        /// of it. Repaired rather than worked around, since the flow's dim is not optional and an
        /// artist re-exporting the file could reintroduce it.
        /// </summary>
        private static Sprite LoadSpriteRepairingImport(string path)
        {
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite != null)
            {
                return sprite;
            }

            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null || importer.spriteImportMode == SpriteImportMode.Single)
            {
                return null;
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.SaveAndReimport();
            Debug.Log(
                $"{nameof(TutorialBuilder)} re-imported {path} as a single sprite; it was set to "
                + "Multiple with no sprites defined, so nothing could reference it.");

            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        // ------------------------------------------------------------------ scene

        /// <summary>
        /// Puts the overlay in the scene and wires the controller that drives it. Placed straight
        /// after the HUD every run: a screen added by another builder later would otherwise land
        /// between them, and the counter and gear are meant to read through this, not under it.
        /// </summary>
        private static void EnsureSceneInstance(GameObject prefab, TextAsset mapJson)
        {
            if (prefab == null)
            {
                return;
            }

            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded)
            {
                Debug.LogWarning("The tutorial overlay was built, but there is no loaded scene to put it in.");
                return;
            }

            Canvas canvas = Object.FindFirstObjectByType<Canvas>(FindObjectsInactive.Include);
            if (canvas == null)
            {
                Debug.LogWarning("The tutorial overlay was built, but the scene has no Canvas to put it under.");
                return;
            }

            GameObject instance = EnsureInstance(prefab, canvas.transform);
            PlaceAfterHud(canvas.transform, instance.transform);

            GameFlowController flow = Object.FindFirstObjectByType<GameFlowController>(FindObjectsInactive.Include);
            if (flow == null)
            {
                Debug.LogWarning(
                    "The tutorial overlay is in the scene but there is no GameFlowController to hang the "
                    + "tutorial off, so it will never be shown.");
                EditorSceneManager.MarkSceneDirty(scene);
                return;
            }

            TutorialController tutorial = UiBuilder.Ensure<TutorialController>(flow.gameObject);
            WireTutorial(tutorial, flow, instance, mapJson);

            SerializedObject serializedFlow = new SerializedObject(flow);
            UiBuilder.SetIfEmpty(serializedFlow, "tutorial", tutorial);
            serializedFlow.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
        }

        private static GameObject EnsureInstance(GameObject prefab, Transform parent)
        {
            Transform existing = parent.Find(OverlayName);
            if (existing != null)
            {
                return existing.gameObject;
            }

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            instance.name = OverlayName;

            // Restates what the instance inherited from an already stretched, already inactive
            // prefab root, so it is left with no overrides of its own.
            Place((RectTransform)instance.transform, Vector2.zero, Vector2.one);
            instance.SetActive(false);
            return instance;
        }

        /// <summary>
        /// Straight after the HUD, or left where it is when there is no HUD to sit behind. The
        /// index is worked out after the move rather than before it: taking the overlay out from
        /// in front of the HUD shifts the HUD down one, and inserting at the index read first
        /// would land a slot too far.
        /// </summary>
        private static void PlaceAfterHud(Transform canvas, Transform overlay)
        {
            Transform hud = canvas.Find(HudName);
            if (hud == null)
            {
                return;
            }

            int hudIndex = hud.GetSiblingIndex();
            int target = overlay.GetSiblingIndex() < hudIndex ? hudIndex : hudIndex + 1;
            if (overlay.GetSiblingIndex() != target)
            {
                overlay.SetSiblingIndex(target);
            }
        }

        /// <summary>
        /// Points the controller at everything it needs. The map is described here rather than in
        /// MapConfig on purpose - see EnsureMapJson - so its three fields are written straight
        /// onto the component.
        /// </summary>
        private static void WireTutorial(
            TutorialController tutorial,
            GameFlowController flow,
            GameObject overlay,
            TextAsset mapJson)
        {
            SerializedObject serialized = new SerializedObject(tutorial);

            UiBuilder.SetIfEmpty(serialized, "flow", flow);
            UiBuilder.SetIfEmpty(serialized, "runController",
                Object.FindFirstObjectByType<LevelRunController>(FindObjectsInactive.Include));
            UiBuilder.SetIfEmpty(serialized, "fireController",
                Object.FindFirstObjectByType<GridKnockdownCannonFireController>(FindObjectsInactive.Include));

            UiBuilder.SetIfEmpty(serialized, "mapSelection", UiBuilder.LoadFirstAsset<MapSelection>());
            UiBuilder.SetIfEmpty(serialized, "bulletInventory", UiBuilder.LoadFirstAsset<BulletInventory>());
            UiBuilder.SetIfEmpty(serialized, "bulletLoadout", UiBuilder.LoadFirstAsset<BulletLoadout>());

            UiBuilder.SetIfEmpty(serialized, "overlayRoot", overlay);
            UiBuilder.SetIfEmpty(serialized, "panel", FindChild(overlay, "Panel"));
            UiBuilder.SetIfEmpty(serialized, "hole", FindChild(overlay, "Hole"));

            SetIfEmptyString(serialized, "tutorialMap.id", MapId);
            SetIfEmptyString(serialized, "tutorialMap.displayName", MapDisplayName);
            UiBuilder.SetIfEmpty(serialized, "tutorialMap.mapJson", mapJson);

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static GameObject FindChild(GameObject parent, string name)
        {
            Transform child = parent != null ? parent.transform.Find(name) : null;
            return child != null ? child.gameObject : null;
        }

        /// <summary>
        /// The string counterpart of SetIfEmpty, for the same reason: a name somebody changed by
        /// hand must survive the next run.
        /// </summary>
        private static void SetIfEmptyString(SerializedObject serialized, string propertyPath, string value)
        {
            SerializedProperty property = serialized.FindProperty(propertyPath);
            if (property == null)
            {
                Debug.LogWarning(
                    $"{nameof(TutorialBuilder)} found no field \"{propertyPath}\" on "
                    + serialized.targetObject.GetType().Name + ".");
                return;
            }

            if (string.IsNullOrEmpty(property.stringValue))
            {
                property.stringValue = value;
            }
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>
        /// Builds a prefab, or runs the same steps over the one that is already there. Editing the
        /// existing asset rather than replacing it is what keeps a reference somebody dragged in by
        /// hand, and what keeps the guid the scene points at.
        /// </summary>
        private static GameObject EnsurePrefab(string path, string rootName, System.Action<GameObject, bool> build)
        {
            bool exists = AssetDatabase.LoadAssetAtPath<GameObject>(path) != null;

            GameObject root = exists
                ? PrefabUtility.LoadPrefabContents(path)
                : new GameObject(rootName, typeof(RectTransform));

            try
            {
                build(root, !exists);
                return PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                if (exists)
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
                else
                {
                    Object.DestroyImmediate(root);
                }
            }
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            int split = path.LastIndexOf('/');
            string parent = path.Substring(0, split);
            string leaf = path.Substring(split + 1);

            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        /// <summary>
        /// Finds or creates an image and, only if it had to make one, gives it the look this
        /// layout wants. An image somebody re-sliced or nudged is handed straight back.
        /// </summary>
        private static void EnsureImage(
            string name,
            Transform parent,
            string spritePath,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Image.Type type,
            bool preserveAspect)
        {
            Transform found = parent.Find(name);
            bool created = found == null || found.GetComponent<Image>() == null;

            RectTransform rect = UiBuilder.EnsureSpriteImage(name, parent, spritePath, anchorMin, anchorMax);
            if (!created)
            {
                return;
            }

            Place(rect, anchorMin, anchorMax);

            Image image = rect.GetComponent<Image>();
            if (image == null)
            {
                return;
            }

            image.type = type;
            image.preserveAspect = preserveAspect;

            // Never: the whole point of this overlay is that the player shoots through it, so the
            // first tap is both the answer to the prompt and the shot it asked for.
            image.raycastTarget = false;

            // An image with no sprite is a white block over the middle of the screen, which is what
            // a missing texture would look like rather than like a missing texture.
            image.enabled = image.sprite != null;
        }

        /// <summary>Anchors with no offsets: the whole layout is fractions of its parent.</summary>
        private static void Place(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
#endif
