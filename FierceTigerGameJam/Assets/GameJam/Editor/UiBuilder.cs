#if UNITY_EDITOR
using GameJam.Economy;
using GameJam.Gameplay.Combat;
using GameJam.Gameplay.Flow;
using GameJam.Gameplay.Wall;
using GameJam.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace GameJam.EditorTools
{
    /// <summary>
    /// Builds the screens the game loop needs and wires them to each other: the ammunition pick,
    /// the in-run readouts, the result panel and the gold readout.
    ///
    /// The layout is deliberately plain. This exists so the loop is playable and testable without
    /// anyone hand-authoring four screens first, not to be the final art. Re-running it keeps
    /// whatever is already there and only fills in what is missing, so tuning a panel by hand is
    /// not undone by the next run.
    /// </summary>
    public static partial class UiBuilder
    {
        private const string AmmoPickName = "AmmoPickScreen";
        private const string HudName = "RunHud";
        private const string ResultName = "ResultScreen";
        private const string GoldName = "GoldPanel";

        private static readonly Color PanelColor = new Color(0.06f, 0.08f, 0.12f, 0.85f);
        private static readonly Color AccentColor = new Color(0.35f, 0.75f, 1f);

        [MenuItem("Tools/Smashdown/Build Game UI")]
        public static void BuildGameUi()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded)
            {
                Debug.LogWarning($"{nameof(UiBuilder)} needs a loaded scene.");
                return;
            }

            Canvas canvas = Object.FindFirstObjectByType<Canvas>(FindObjectsInactive.Include);
            if (canvas == null)
            {
                Debug.LogError($"{nameof(UiBuilder)} needs a Canvas in the scene to build into.");
                return;
            }

            GameFlowController flow = Object.FindFirstObjectByType<GameFlowController>(FindObjectsInactive.Include);
            LevelRunController run = Object.FindFirstObjectByType<LevelRunController>(FindObjectsInactive.Include);
            LevelProgressTracker tracker = Object.FindFirstObjectByType<LevelProgressTracker>(FindObjectsInactive.Include);

            BulletInventory inventory = LoadAsset<BulletInventory>();
            BulletLoadout loadout = LoadAsset<BulletLoadout>();
            EconomyService economy = LoadAsset<EconomyService>();

            GameObject ammoPick = BuildAmmoPick(canvas.transform, inventory, loadout, out Button startButton);
            GameObject hud = BuildHud(canvas.transform, run, tracker, inventory, loadout);
            GameObject result = BuildResult(canvas.transform, flow, out Button retryButton, out Button resultMainMenuButton);

            WireFlow(flow, run, ammoPick, hud, result, startButton, retryButton, resultMainMenuButton);

            // The sprite chrome comes last: it fills the readouts the plain screens left empty,
            // and it needs the roots above to already exist.
            BuildSpriteScreens(canvas.transform, flow, hud, economy);

            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log(
                $"{nameof(UiBuilder)} built the ammunition pick, the run HUD, the result panel and the gold readout. "
                + "Anything it could not find in the scene or the project is left unassigned on the components.");
        }

        private static GameObject BuildAmmoPick(Transform canvas, BulletInventory inventory, BulletLoadout loadout, out Button startButton)
        {
            RectTransform root = EnsurePanel(AmmoPickName, canvas);
            CreateTitle(root, "CHOOSE YOUR AMMUNITION");

            RectTransform rows = EnsureRect("Rows", root, new Vector2(0.1f, 0.28f), new Vector2(0.9f, 0.72f));
            TMP_Text total = EnsureLabel("TotalLabel", root, "0 / 0", 42, TextAlignmentOptions.Center,
                new Vector2(0.3f, 0.76f), new Vector2(0.7f, 0.84f));

            startButton = EnsureButton("StartButton", root, "START", new Vector2(0.35f, 0.1f), new Vector2(0.65f, 0.2f));

            AmmoPickView view = Ensure<AmmoPickView>(root.gameObject);
            SerializedObject serialized = new SerializedObject(view);
            SetIfEmpty(serialized, "inventory", inventory);
            SetIfEmpty(serialized, "loadout", loadout);
            SetIfEmpty(serialized, "container", rows);
            SetIfEmpty(serialized, "totalLabel", total);
            SetIfEmpty(serialized, "startButton", startButton);
            serialized.ApplyModifiedPropertiesWithoutUndo();

            return root.gameObject;
        }

        private static GameObject BuildHud(
            Transform canvas,
            LevelRunController run,
            LevelProgressTracker tracker,
            BulletInventory inventory,
            BulletLoadout loadout)
        {
            // Transparent: the HUD sits over live gameplay, so a panel background would hide the
            // thing the player is aiming at.
            RectTransform root = EnsureRect(HudName, canvas, Vector2.zero, Vector2.one);

            TMP_Text percent = EnsureLabel("ClearPercent", root, "0%", 56, TextAlignmentOptions.Center,
                new Vector2(0.32f, 0.885f), new Vector2(0.68f, 0.962f));

            RunHudView view = Ensure<RunHudView>(root.gameObject);
            SerializedObject serialized = new SerializedObject(view);
            SetIfEmpty(serialized, "progressTracker", tracker);
            SetIfEmpty(serialized, "clearPercentLabel", percent);
            serialized.ApplyModifiedPropertiesWithoutUndo();

            return root.gameObject;
        }

        private static GameObject BuildResult(
            Transform canvas,
            GameFlowController flow,
            out Button retryButton,
            out Button mainMenuButton)
        {
            RectTransform root = EnsurePanel(ResultName, canvas);

            TMP_Text headline = EnsureLabel("Headline", root, "LEVEL CLEAR", 64, TextAlignmentOptions.Center,
                new Vector2(0.1f, 0.66f), new Vector2(0.9f, 0.8f));
            TMP_Text percent = EnsureLabel("ClearPercent", root, "0%", 96, TextAlignmentOptions.Center,
                new Vector2(0.1f, 0.5f), new Vector2(0.9f, 0.66f));
            TMP_Text detail = EnsureLabel("Detail", root, string.Empty, 28, TextAlignmentOptions.Center,
                new Vector2(0.1f, 0.42f), new Vector2(0.9f, 0.5f));

            RectTransform rewardRoot = EnsureRect("Reward", root, new Vector2(0.3f, 0.3f), new Vector2(0.7f, 0.4f));
            TMP_Text reward = EnsureLabel("RewardLabel", rewardRoot, "+0", 48, TextAlignmentOptions.Center,
                Vector2.zero, Vector2.one);

            // Stacked rather than side by side: full width gives each a bigger tap target, and
            // reading down the screen puts the likelier choice first. Retry is on top because a
            // player who just failed is more often going again than leaving.
            retryButton = EnsureButton("RetryButton", root, "RETRY",
                new Vector2(0.22f, 0.20f), new Vector2(0.78f, 0.29f));
            mainMenuButton = EnsureButton("MainMenuButton", root, "MAIN MENU",
                new Vector2(0.22f, 0.09f), new Vector2(0.78f, 0.18f));

            RunResultView view = Ensure<RunResultView>(root.gameObject);
            SerializedObject serialized = new SerializedObject(view);
            SetIfEmpty(serialized, "flow", flow);
            SetIfEmpty(serialized, "headlineLabel", headline);
            SetIfEmpty(serialized, "clearPercentLabel", percent);
            SetIfEmpty(serialized, "detailLabel", detail);
            SetIfEmpty(serialized, "rewardLabel", reward);
            SetIfEmpty(serialized, "rewardRoot", rewardRoot.gameObject);
            serialized.ApplyModifiedPropertiesWithoutUndo();

            return root.gameObject;
        }

        private static void WireFlow(
            GameFlowController flow,
            LevelRunController run,
            GameObject ammoPick,
            GameObject hud,
            GameObject result,
            Button startButton,
            Button retryButton,
            Button resultMainMenuButton)
        {
            if (flow == null)
            {
                Debug.LogWarning($"{nameof(UiBuilder)} found no {nameof(GameFlowController)}, so the screens are built but not wired to it.");
                return;
            }

            SerializedObject serialized = new SerializedObject(flow);
            SetIfEmpty(serialized, "runController", run);
            SetIfEmpty(serialized, "ammoPickRoot", ammoPick);
            SetIfEmpty(serialized, "hudRoot", hud);
            // "failRoot" rather than "resultRoot": the flow now has a screen per outcome, and the
            // plain panel this builder makes is the failing one. SerializedObject looks the field
            // up by its current name and FormerlySerializedAs does not help it, so leaving the old
            // string here would only log that the field is missing.
            SetIfEmpty(serialized, "failRoot", result);
            SetIfEmpty(serialized, "startRunButton", startButton);
            SetIfEmpty(serialized, "retryButton", retryButton);
            SetIfEmpty(serialized, "resultContinueButton", resultMainMenuButton);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// Only fills a reference that is empty. Re-running the builder must not overwrite
        /// something deliberately pointed somewhere else.
        /// </summary>
        internal static void SetIfEmpty(SerializedObject serialized, string propertyName, Object value)
        {
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property == null)
            {
                Debug.LogWarning($"{nameof(UiBuilder)} found no field \"{propertyName}\" on {serialized.targetObject.GetType().Name}.");
                return;
            }

            if (property.objectReferenceValue == null && value != null)
            {
                property.objectReferenceValue = value;
            }
        }

        internal static T Ensure<T>(GameObject target) where T : Component
        {
            T existing = target.GetComponent<T>();
            return existing != null ? existing : Undo.AddComponent<T>(target);
        }

        private static RectTransform EnsurePanel(string name, Transform parent)
        {
            RectTransform root = EnsureRect(name, parent, Vector2.zero, Vector2.one);
            if (root.GetComponent<Image>() == null)
            {
                Image background = Undo.AddComponent<Image>(root.gameObject);
                background.color = PanelColor;
            }

            return root;
        }

        internal static RectTransform EnsureRect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax)
        {
            Transform existing = parent.Find(name);
            RectTransform rect;
            if (existing != null)
            {
                rect = existing as RectTransform;
                if (rect != null)
                {
                    return rect;
                }

                Object.DestroyImmediate(existing.gameObject);
            }

            GameObject created = new GameObject(name, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(created, $"Create {name}");
            rect = (RectTransform)created.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return rect;
        }

        internal static TMP_Text EnsureLabel(
            string name,
            Transform parent,
            string text,
            int size,
            TextAlignmentOptions alignment,
            Vector2 anchorMin,
            Vector2 anchorMax)
        {
            RectTransform rect = EnsureRect(name, parent, anchorMin, anchorMax);
            TextMeshProUGUI label = rect.GetComponent<TextMeshProUGUI>();
            if (label == null)
            {
                label = Undo.AddComponent<TextMeshProUGUI>(rect.gameObject);
                label.text = text;
                label.fontSize = size;
                label.alignment = alignment;
                label.color = Color.white;
            }

            return label;
        }

        private static Image EnsureProgressBar(string name, Transform parent)
        {
            RectTransform rect = EnsureRect(name, parent, new Vector2(0.04f, 0.79f), new Vector2(0.5f, 0.82f));
            Image fill = rect.GetComponent<Image>();
            if (fill == null)
            {
                fill = Undo.AddComponent<Image>(rect.gameObject);
                fill.color = AccentColor;
                fill.type = Image.Type.Filled;
                fill.fillMethod = Image.FillMethod.Horizontal;
                fill.fillAmount = 0f;
            }

            return fill;
        }

        internal static Button EnsureButton(string name, Transform parent, string caption, Vector2 anchorMin, Vector2 anchorMax)
        {
            RectTransform rect = EnsureRect(name, parent, anchorMin, anchorMax);
            Button button = rect.GetComponent<Button>();
            if (button == null)
            {
                Image background = Undo.AddComponent<Image>(rect.gameObject);
                background.color = AccentColor;
                button = Undo.AddComponent<Button>(rect.gameObject);
                EnsureLabel("Label", rect, caption, 36, TextAlignmentOptions.Center, Vector2.zero, Vector2.one);
            }

            return button;
        }

        private static void CreateTitle(RectTransform root, string caption)
        {
            EnsureLabel("Title", root, caption, 44, TextAlignmentOptions.Center,
                new Vector2(0.1f, 0.85f), new Vector2(0.9f, 0.95f));
        }

        /// <summary>The first asset of a type in the project, since there is only ever one of each.</summary>
        private static T LoadAsset<T>() where T : Object
        {
            string[] guids = AssetDatabase.FindAssets($"t:{typeof(T).Name}");
            if (guids.Length == 0)
            {
                Debug.LogWarning($"{nameof(UiBuilder)} found no {typeof(T).Name} asset, so that reference is left empty.");
                return null;
            }

            return AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }
    }
}
#endif
