#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using GameJam.Audio;
using GameJam.Gameplay.Flow;
using GameJam.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace GameJam.EditorTools
{
    /// <summary>
    /// Brings the WhackStack sound pack into the project and wires the whole game to it: the clips
    /// and their import settings, the <see cref="AudioConfig"/> that says which clip means what,
    /// the scene object that plays them, and a sweep that gives every existing button its click.
    ///
    /// Idempotent, like every builder here. Nothing is overwritten: a slot somebody re-authored,
    /// an import setting somebody tuned and a component somebody already added are all left alone,
    /// so re-running this is a no-op on a project that has already had it run. Deleting the config
    /// asset is how you ask for the default wiring back.
    /// </summary>
    public static class AudioSetup
    {
        /// <summary>
        /// Where the pack lives on the machine it was authored on. A path outside the project on
        /// purpose: the mp3 are the source material, and the copy under Assets is what the game
        /// ships. Missing, the import step is skipped and said so - the rest of the builder still
        /// runs against whatever is already imported.
        /// </summary>
        private const string SourceFolder = "/Users/duongtrinh/Desktop/WhackStack";

        private const string AudioFolder = "Assets/GameJam/Audio";
        private const string ConfigFolder = "Assets/GameJam/Config";
        private const string ConfigPath = ConfigFolder + "/AudioConfig.asset";

        /// <summary>Where the button sweep looks for prefabs. Every UI prefab in the game is under it.</summary>
        private const string UiPrefabFolder = "Assets/GameJam/Prefabs/UI";

        private const string ServiceObjectName = "AudioService";

        /// <summary>
        /// Which clip fills which slot on a config that has none. The clip names are the pack's,
        /// unchanged: renaming them on the way in would only make the pack harder to re-import.
        /// </summary>
        private static readonly (string Property, string Clip)[] SlotWiring =
        {
            ("fire", "sfx_canonexplose"),
            ("ballImpact", "sfx_ballimpact"),
            ("ballFall", "sfx_ballfall"),
            ("hitBrick", "sfx_hitbrick"),
            ("hitConcrete", "sfx_hitcement"),

            // The pack has no glass hit. Ice is the closest clink in it, and this is the line to
            // change when a real one arrives.
            ("hitGlass", "sfx_hitice"),

            ("breakBrick", "sfx_crackedbrick"),
            ("breakConcrete", "sfx_crackedcement"),
            ("breakGlass", "sfx_crackedglass"),
            ("uiClick", "sfx_buttonclick"),
            ("denied", "sfx_denied"),
            ("coin", "sfx_coinreward"),
            ("stageClear", "sfx_stageclear"),
            ("stageFailed", "sfx_stagefailed"),
            ("musicTitle", "bgm_title"),
            ("musicGame", "bgm_game"),
        };

        [MenuItem("Tools/Smashdown/Set Up Audio")]
        public static void SetUpAudio()
        {
            ImportPack();

            AudioConfig config = EnsureConfig();
            WireConfig(config);
            EnsureSceneService(config);

            int swept = SweepButtons();

            AssetDatabase.SaveAssets();
            Debug.Log(
                $"{nameof(AudioSetup)} wired {ConfigPath}, ensured the {ServiceObjectName} object and "
                + $"gave {swept} button(s) a click. Save the scene to keep the wiring.");
        }

        // ------------------------------------------------------------------ import

        /// <summary>
        /// Copies the pack in and gives every clip the settings it wants. Files already in the
        /// project are not re-copied, so a clip somebody replaced by hand stays replaced.
        /// </summary>
        private static void ImportPack()
        {
            EnsureFolder(AudioFolder);

            if (!Directory.Exists(SourceFolder))
            {
                Debug.Log(
                    $"{nameof(AudioSetup)} found no source pack at {SourceFolder}, so nothing was "
                    + "copied. Whatever is already under " + AudioFolder + " is used as it is.");
                ApplyImportSettings();
                return;
            }

            string[] sources = Directory.GetFiles(SourceFolder, "*.mp3");
            int copied = 0;
            for (int i = 0; i < sources.Length; i++)
            {
                // Only the mp3 are taken. The .meta files sitting beside them belong to the
                // project the pack was exported from, and importing one would hand our clip that
                // project's guid - which is how an asset ends up referenced by nothing.
                string destination = $"{AudioFolder}/{Path.GetFileName(sources[i])}";
                if (File.Exists(destination))
                {
                    continue;
                }

                File.Copy(sources[i], destination);
                copied++;
            }

            if (copied > 0)
            {
                AssetDatabase.Refresh();
                Debug.Log($"{nameof(AudioSetup)} copied {copied} clip(s) into {AudioFolder}.");
            }

            ApplyImportSettings();
        }

        /// <summary>
        /// Force to mono and Vorbis for everything; the two bgm tracks stream, the effects are
        /// decompressed up front.
        ///
        /// Mono because nothing here is positional and a stereo file is twice the memory for a
        /// sound that plays flat anyway. Streaming for the music because a three-minute track
        /// decompressed into memory is the single largest thing the game would hold; decompress on
        /// load for the effects for the opposite reason - they are short, and a collapse cannot
        /// afford to decode a dozen clips inside one physics step.
        ///
        /// Only written when it is not already what the file says, so re-running this reimports
        /// nothing and a clip somebody deliberately retuned keeps its settings the moment they
        /// differ from these.
        /// </summary>
        private static void ApplyImportSettings()
        {
            string[] guids = AssetDatabase.FindAssets("t:AudioClip", new[] { AudioFolder });
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (AssetImporter.GetAtPath(path) is not AudioImporter importer)
                {
                    continue;
                }

                // Anything named bgm_* is a track rather than an effect. Read off the name because
                // that is the pack's own convention and it survives a clip being added later.
                bool isMusic = Path.GetFileNameWithoutExtension(path).StartsWith("bgm_");

                AudioImporterSampleSettings settings = importer.defaultSampleSettings;
                AudioClipLoadType wanted = isMusic
                    ? AudioClipLoadType.Streaming
                    : AudioClipLoadType.DecompressOnLoad;

                bool changed = false;
                if (!importer.forceToMono)
                {
                    importer.forceToMono = true;
                    changed = true;
                }

                if (settings.loadType != wanted || settings.compressionFormat != AudioCompressionFormat.Vorbis)
                {
                    settings.loadType = wanted;
                    settings.compressionFormat = AudioCompressionFormat.Vorbis;
                    importer.defaultSampleSettings = settings;
                    changed = true;
                }

                if (changed)
                {
                    importer.SaveAndReimport();
                }
            }
        }

        // ------------------------------------------------------------------ config

        private static AudioConfig EnsureConfig()
        {
            EnsureFolder(ConfigFolder);

            AudioConfig existing = AssetDatabase.LoadAssetAtPath<AudioConfig>(ConfigPath);
            if (existing != null)
            {
                return existing;
            }

            AudioConfig created = ScriptableObject.CreateInstance<AudioConfig>();
            AssetDatabase.CreateAsset(created, ConfigPath);
            Debug.Log($"{nameof(AudioSetup)} created {ConfigPath}.");
            return created;
        }

        /// <summary>
        /// Fills in the slots that are still empty. A slot with anything in it is left alone, so a
        /// second take dropped in beside the default survives a re-run - that is what makes this
        /// builder safe to run at any time.
        /// </summary>
        private static void WireConfig(AudioConfig config)
        {
            if (config == null)
            {
                return;
            }

            SerializedObject serialized = new SerializedObject(config);
            int missing = 0;

            for (int i = 0; i < SlotWiring.Length; i++)
            {
                if (!SetClipIfEmpty(serialized, SlotWiring[i].Property, SlotWiring[i].Clip))
                {
                    missing++;
                }
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();

            if (missing > 0)
            {
                Debug.LogWarning(
                    $"{nameof(AudioSetup)} could not fill {missing} slot(s) because the clip was not "
                    + $"in {AudioFolder}. Those events are silent until it is.");
            }
        }

        /// <summary>
        /// Puts one clip in a slot that has none, the array equivalent of
        /// <see cref="UiBuilder.SetIfEmpty"/>. False means the clip itself was missing, which is an
        /// import problem rather than an authoring one and is worth counting.
        /// </summary>
        private static bool SetClipIfEmpty(SerializedObject serialized, string propertyName, string clipName)
        {
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property == null)
            {
                Debug.LogWarning($"{nameof(AudioSetup)} found no field \"{propertyName}\" on {nameof(AudioConfig)}.");
                return true;
            }

            // Anything already authored wins, including a deliberately emptied-then-refilled slot.
            if (property.arraySize > 0)
            {
                return true;
            }

            AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>($"{AudioFolder}/{clipName}.mp3");
            if (clip == null)
            {
                return false;
            }

            property.arraySize = 1;
            property.GetArrayElementAtIndex(0).objectReferenceValue = clip;
            return true;
        }

        // ------------------------------------------------------------------ scene

        /// <summary>
        /// The object that plays everything, under =====SYSTEM=====, carrying the service and the
        /// music director. One object found by name, so running this twice does not make a second.
        /// </summary>
        private static void EnsureSceneService(AudioConfig config)
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded)
            {
                Debug.LogWarning(
                    $"{nameof(AudioSetup)} wired the config, but there is no loaded scene to put the "
                    + $"{ServiceObjectName} in, so nothing will play.");
                return;
            }

            Transform section = EnsureSystemSection(scene);

            Transform found = section.Find(ServiceObjectName);
            GameObject serviceObject;
            if (found != null)
            {
                serviceObject = found.gameObject;
            }
            else
            {
                serviceObject = new GameObject(ServiceObjectName);
                Undo.RegisterCreatedObjectUndo(serviceObject, "Create Audio Service");
                SceneManager.MoveGameObjectToScene(serviceObject, scene);
                Undo.SetTransformParent(serviceObject.transform, section, "Create Audio Service");
                serviceObject.transform.localPosition = Vector3.zero;
            }

            // No AudioSources are added here. The service builds exactly one music source and
            // exactly its configured number of effect sources in Awake, which is what makes a
            // double-run - or a domain reload with reload switched off - unable to leave two.
            AudioService service = UiBuilder.Ensure<AudioService>(serviceObject);
            SerializedObject serializedService = new SerializedObject(service);
            UiBuilder.SetIfEmpty(serializedService, "config", config);
            serializedService.ApplyModifiedPropertiesWithoutUndo();

            MusicDirector director = UiBuilder.Ensure<MusicDirector>(serviceObject);
            GameFlowController flow = Object.FindFirstObjectByType<GameFlowController>(FindObjectsInactive.Include);
            if (flow == null)
            {
                Debug.LogWarning(
                    $"{nameof(AudioSetup)} put the {ServiceObjectName} in the scene but found no "
                    + $"{nameof(GameFlowController)} to wire it to, so there will be no music.");
            }
            else
            {
                SerializedObject serializedDirector = new SerializedObject(director);
                UiBuilder.SetIfEmpty(serializedDirector, "flow", flow);
                serializedDirector.ApplyModifiedPropertiesWithoutUndo();
            }

            EditorSceneManager.MarkSceneDirty(scene);
        }

        /// <summary>
        /// The =====SYSTEM===== header, made if the scene has not been organised yet. Named from
        /// <see cref="SceneHierarchyOrganizer"/> rather than written out again, so the two cannot
        /// disagree about what the section is called.
        /// </summary>
        private static Transform EnsureSystemSection(Scene scene)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i].name == SceneHierarchyOrganizer.SystemHeader)
                {
                    return roots[i].transform;
                }
            }

            GameObject section = new GameObject(SceneHierarchyOrganizer.SystemHeader);
            Undo.RegisterCreatedObjectUndo(section, "Create Scene Section");
            SceneManager.MoveGameObjectToScene(section, scene);
            section.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            return section.transform;
        }

        // ------------------------------------------------------------------ button sweep

        /// <summary>
        /// Gives every button in the project its click, in the prefabs first and then the scene.
        ///
        /// That order matters and is the whole trick: a button living in a prefab gets the
        /// component on the asset, so every instance of it inherits one rather than carrying an
        /// override. By the time the scene is walked, those instances already have theirs and are
        /// skipped, and only genuinely scene-only buttons are touched.
        /// </summary>
        private static int SweepButtons()
        {
            int added = SweepPrefabs();

            // So the open scene's prefab instances see the components just saved onto their assets
            // and are not given a second, overriding copy below.
            if (added > 0)
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            return added + SweepScene();
        }

        private static int SweepPrefabs()
        {
            if (!AssetDatabase.IsValidFolder(UiPrefabFolder))
            {
                return 0;
            }

            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { UiPrefabFolder });
            int added = 0;

            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    int addedHere = AddToButtons(root.GetComponentsInChildren<Button>(true));

                    // Saved only when something changed, so a re-run rewrites no prefab and shows
                    // up as no diff at all.
                    if (addedHere > 0)
                    {
                        PrefabUtility.SaveAsPrefabAsset(root, path);
                        added += addedHere;
                    }
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }

            return added;
        }

        private static int SweepScene()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return 0;
            }

            Button[] buttons = Object.FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            int added = AddToButtons(buttons);
            if (added > 0)
            {
                EditorSceneManager.MarkSceneDirty(scene);
            }

            return added;
        }

        /// <summary>
        /// One component per button and never a second. ButtonClickSound is
        /// [DisallowMultipleComponent], so a double would not merely be untidy - it would be
        /// refused - which is why the check comes first rather than being left to AddComponent.
        /// </summary>
        private static int AddToButtons(IReadOnlyList<Button> buttons)
        {
            int added = 0;
            for (int i = 0; i < buttons.Count; i++)
            {
                Button button = buttons[i];
                if (button == null || button.GetComponent<ButtonClickSound>() != null)
                {
                    continue;
                }

                button.gameObject.AddComponent<ButtonClickSound>();
                added++;
            }

            return added;
        }

        // ------------------------------------------------------------------ helpers

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
    }
}
#endif
