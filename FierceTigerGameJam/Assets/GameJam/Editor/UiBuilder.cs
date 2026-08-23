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
    public static class UiBuilder
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
            GameObject result = BuildResult(canvas.transform, flow, out Button retryButton);
            BuildGoldPanel(canvas.transform, economy);

            WireFlow(flow, run, ammoPick, hud, result, startButton, retryButton);

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

            TMP_Text percent = EnsureLabel("ClearPercent", root, "0%", 56, TextAlignmentOptions.Left,
                new Vector2(0.04f, 0.88f), new Vector2(0.4f, 0.97f));
            TMP_Text required = EnsureLabel("RequiredPercent", root, "target 80%", 26, TextAlignmentOptions.Left,
                new Vector2(0.04f, 0.83f), new Vector2(0.4f, 0.88f));
            Image fill = EnsureProgressBar("ProgressBar", root);
            TMP_Text remaining = EnsureLabel("RemainingBullets", root, "0", 48, TextAlignmentOptions.Right,
                new Vector2(0.6f, 0.88f), new Vector2(0.96f, 0.97f));
            RectTransform breakdown = EnsureRect("Breakdown", root, new Vector2(0.7f, 0.7f), new Vector2(0.96f, 0.87f));

            RunHudView view = Ensure<RunHudView>(root.gameObject);
            SerializedObject serialized = new SerializedObject(view);
            SetIfEmpty(serialized, "runController", run);
            SetIfEmpty(serialized, "progressTracker", tracker);
            SetIfEmpty(serialized, "inventory", inventory);
            SetIfEmpty(serialized, "loadout", loadout);
            SetIfEmpty(serialized, "clearPercentLabel", percent);
            SetIfEmpty(serialized, "requiredPercentLabel", required);
            SetIfEmpty(serialized, "clearProgressFill", fill);
            SetIfEmpty(serialized, "remainingBulletsLabel", remaining);
            SetIfEmpty(serialized, "bulletBreakdownContainer", breakdown);
            serialized.ApplyModifiedPropertiesWithoutUndo();

            return root.gameObject;
        }

        private static GameObject BuildResult(Transform canvas, GameFlowController flow, out Button retryButton)
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

            retryButton = EnsureButton("RetryButton", root, "RETRY", new Vector2(0.35f, 0.14f), new Vector2(0.65f, 0.24f));

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

        private static void BuildGoldPanel(Transform canvas, EconomyService economy)
        {
            RectTransform root = EnsureRect(GoldName, canvas, new Vector2(0.62f, 0.9f), new Vector2(0.97f, 0.99f));
            TMP_Text label = EnsureLabel("GoldLabel", root, "0", 40, TextAlignmentOptions.Right, Vector2.zero, Vector2.one);

            GoldView view = Ensure<GoldView>(root.gameObject);
            SerializedObject serialized = new SerializedObject(view);
            SetIfEmpty(serialized, "economy", economy);
            SetIfEmpty(serialized, "goldLabel", label);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void WireFlow(
            GameFlowController flow,
            LevelRunController run,
            GameObject ammoPick,
            GameObject hud,
            GameObject result,
            Button startButton,
            Button retryButton)
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
            SetIfEmpty(serialized, "resultRoot", result);
            SetIfEmpty(serialized, "startRunButton", startButton);
            SetIfEmpty(serialized, "retryButton", retryButton);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// Only fills a reference that is empty. Re-running the builder must not overwrite
        /// something deliberately pointed somewhere else.
        /// </summary>
        private static void SetIfEmpty(SerializedObject serialized, string propertyName, Object value)
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

        private static T Ensure<T>(GameObject target) where T : Component
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

        private static RectTransform EnsureRect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax)
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

        private static TMP_Text EnsureLabel(
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

        private static Button EnsureButton(string name, Transform parent, string caption, Vector2 anchorMin, Vector2 anchorMax)
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
