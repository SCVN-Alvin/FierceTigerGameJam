#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace GameJam.EditorTools
{
    /// <summary>
    /// Two menu items that make one font the game's font and then check the result fits.
    ///
    /// <b>Apply Game Font</b> puts <see cref="GameFonts.Default"/> and its material on every
    /// <see cref="TMP_Text"/> in the UI prefabs and the open scene, and makes it the project
    /// default so labels authored later are born right. It is written to be re-runnable: a second
    /// pass finds nothing to change and saves nothing, so it cannot churn prefabs or manufacture
    /// merge conflicts for anyone else's branch.
    ///
    /// Two things it refuses to do silently, because both are art decisions rather than mechanical
    /// ones. Clearing <see cref="FontStyles.Bold"/> changes how a label reads, so every place it is
    /// cleared is listed by name. And a coloured label tints the baked outline along with the
    /// glyph, so strongly coloured text will look different under the new font - those are listed
    /// too and left exactly as they are, for the art pass to judge.
    ///
    /// <b>Audit Text Layout</b> reports only. Lilita is wider than the LiberationSans it replaces,
    /// so labels that used to fit can clip; the audit finds them, and the fix - widen the rect,
    /// then auto-size, then ellipsis - is done by hand.
    /// </summary>
    public static class GameFontApplier
    {
        private const string PrefabRoot = "Assets/GameJam/Prefabs";
        private const string TmpSettingsPath = "Assets/TextMesh Pro/Resources/TMP Settings.asset";
        private const string UndoLabel = "Apply Game Font";

        /// <summary>A rect this small cannot show a glyph, whatever the font.</summary>
        private const float DegenerateRectSize = 2f;

        /// <summary>
        /// Sub-pixel slack, so a label authored flush with the canvas edge is not reported as
        /// hanging off it by a rounding error.
        /// </summary>
        private const float EdgeTolerance = 0.5f;

        /// <summary>
        /// The reference resolution every canvas in the game scales from. A prefab has no canvas of
        /// its own, so the audit borrows this one to give stretch-anchored labels a real size.
        /// </summary>
        private static readonly Vector2 AuditCanvasSize = new Vector2(720f, 1280f);

        // ---------------------------------------------------------------- font sweep

        [MenuItem("Tools/Smashdown/Apply Game Font")]
        public static void ApplyGameFont()
        {
            TMP_FontAsset font = GameFonts.Default;
            if (font == null)
            {
                Debug.LogError(
                    $"Apply Game Font found no TMP font asset at \"{GameFonts.DefaultFontPath}\". "
                    + "Nothing was changed - fix the path in GameFonts before running this again.");
                return;
            }

            Material material = GameFonts.DefaultMaterial;
            if (material == null)
            {
                Debug.LogError(
                    $"Apply Game Font found \"{font.name}\" but it carries no material, so labels "
                    + "would be left sampling the wrong atlas. Nothing was changed.");
                return;
            }

            SweepLog log = new SweepLog();

            // Which prefab assets the sweep has already rewritten. The scene pass consults this to
            // leave their instances alone; see IsCoveredBySweptPrefab.
            HashSet<string> sweptPrefabs = new HashSet<string>();

            ApplyToPrefabs(font, material, log, sweptPrefabs);
            ApplyToOpenScene(font, material, log, sweptPrefabs);
            bool settingsChanged = ApplyToTmpSettings(font);

            AssetDatabase.SaveAssets();
            Debug.Log(log.Summarise(font, settingsChanged));
        }

        private static void ApplyToPrefabs(TMP_FontAsset font, Material material, SweepLog log, HashSet<string> swept)
        {
            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { PrefabRoot });
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (string.IsNullOrEmpty(path))
                {
                    continue;
                }

                // Recorded whether or not it changed: an already-converted prefab is still one the
                // scene pass must not re-override.
                swept.Add(path);

                GameObject contents = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    TMP_Text[] labels = contents.GetComponentsInChildren<TMP_Text>(true);
                    if (labels.Length == 0)
                    {
                        continue;
                    }

                    log.prefabsWithLabels++;
                    int changes = 0;
                    for (int j = 0; j < labels.Length; j++)
                    {
                        changes += Convert(labels[j], font, material, path, log);
                    }

                    // Saving unconditionally would rewrite every prefab's serialised form on every
                    // run, which is how a re-runnable tool turns into a source of conflicts.
                    if (changes > 0)
                    {
                        PrefabUtility.SaveAsPrefabAsset(contents, path);
                        log.prefabsChanged++;
                        Debug.Log($"Apply Game Font: {path} - {changes} change(s) over {labels.Length} label(s).");
                    }
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(contents);
                }
            }
        }

        private static void ApplyToOpenScene(TMP_FontAsset font, Material material, SweepLog log, HashSet<string> swept)
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded)
            {
                Debug.Log("Apply Game Font: no scene is open, so only the prefabs were swept.");
                return;
            }

            Canvas[] canvases = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            int changes = 0;
            for (int i = 0; i < canvases.Length; i++)
            {
                // Root canvases only. A nested canvas's labels are already reached through its
                // root, and visiting them twice would double every line in the log.
                if (!canvases[i].isRootCanvas)
                {
                    continue;
                }

                TMP_Text[] labels = canvases[i].GetComponentsInChildren<TMP_Text>(true);
                for (int j = 0; j < labels.Length; j++)
                {
                    TMP_Text label = labels[j];
                    if (IsCoveredBySweptPrefab(label, swept))
                    {
                        log.skippedPrefabInstances++;
                        continue;
                    }

                    Undo.RecordObject(label, UndoLabel);
                    changes += Convert(label, font, material, scene.name, log);
                }
            }

            log.sceneChanges = changes;
            if (changes > 0)
            {
                EditorSceneManager.MarkSceneDirty(scene);
            }
        }

        /// <summary>
        /// The prefab pass has already rewritten the asset, so a label that comes from one of them
        /// is correct the moment the instance reloads; converting it here would bake a pointless
        /// override into the scene and, worse, pin the label to today's font if the prefab is ever
        /// re-pointed at another one.
        ///
        /// The test is on the source asset's path rather than on the instance, so a label from a
        /// prefab the sweep never saw - one living outside Prefabs/ - and a label added to an
        /// instance in the scene (which has no source object at all) are both still converted.
        /// </summary>
        private static bool IsCoveredBySweptPrefab(TMP_Text label, HashSet<string> swept)
        {
            if (!PrefabUtility.IsPartOfPrefabInstance(label))
            {
                return false;
            }

            TMP_Text source = PrefabUtility.GetCorrespondingObjectFromSource(label);
            if (source == null)
            {
                return false;
            }

            string path = AssetDatabase.GetAssetPath(source);
            return !string.IsNullOrEmpty(path) && swept.Contains(path);
        }

        /// <summary>
        /// Converts one label and returns how many distinct things it changed, so callers can tell
        /// a prefab worth saving from one that was already right. Colour is reported, never
        /// touched: see the class comment.
        /// </summary>
        private static int Convert(TMP_Text label, TMP_FontAsset font, Material material, string owner, SweepLog log)
        {
            int changes = 0;

            if (label.font != font)
            {
                label.font = font;
                changes++;
            }

            if (label.fontSharedMaterial != material)
            {
                label.fontSharedMaterial = material;
                changes++;
            }

            if ((label.fontStyle & FontStyles.Bold) != 0)
            {
                label.fontStyle &= ~FontStyles.Bold;
                log.boldCleared.Add(Describe(label, owner));
                changes++;
            }

            if (label.color != Color.white)
            {
                log.nonWhite.Add($"{Describe(label, owner)} #{ColorUtility.ToHtmlStringRGBA(label.color)}");
            }

            if (changes > 0)
            {
                EditorUtility.SetDirty(label);
            }

            return changes;
        }

        /// <summary>
        /// Points the project default at the game font so every label created from here on is born
        /// with it - the sweep is a one-off repair, this is the thing that stops the drift.
        ///
        /// Deliberately not SetIfEmpty: the field already holds LiberationSans, so filling only
        /// when empty would never do anything. It still writes only on a difference, which is what
        /// re-runnability actually needs here.
        /// </summary>
        private static bool ApplyToTmpSettings(TMP_FontAsset font)
        {
            Object settings = AssetDatabase.LoadAssetAtPath<Object>(TmpSettingsPath);
            if (settings == null)
            {
                Debug.LogWarning(
                    $"Apply Game Font found no TMP settings at \"{TmpSettingsPath}\", so new labels "
                    + "will still be born on TextMesh Pro's own default font.");
                return false;
            }

            SerializedObject serialized = new SerializedObject(settings);
            SerializedProperty property = serialized.FindProperty("m_defaultFontAsset");
            if (property == null)
            {
                Debug.LogWarning(
                    "Apply Game Font found no \"m_defaultFontAsset\" on the TMP settings asset - the "
                    + "field must have been renamed by a TextMesh Pro upgrade. The project default "
                    + "was left alone; set it by hand in Project Settings > TextMesh Pro.");
                return false;
            }

            if (property.objectReferenceValue == font)
            {
                return false;
            }

            property.objectReferenceValue = font;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(settings);
            return true;
        }

        // ---------------------------------------------------------------- layout audit

        [MenuItem("Tools/Smashdown/Audit Text Layout")]
        public static void AuditTextLayout()
        {
            List<string> findings = new List<string>();
            int inspected = AuditOpenScene(findings);
            inspected += AuditPrefabs(findings);

            if (findings.Count == 0)
            {
                Debug.Log($"Audit Text Layout: {inspected} label(s) inspected, nothing wrong.");
                return;
            }

            StringBuilder report = new StringBuilder();
            report.Append("Audit Text Layout: ").Append(findings.Count)
                .Append(" finding(s) over ").Append(inspected).Append(" label(s).");
            for (int i = 0; i < findings.Count; i++)
            {
                report.AppendLine().Append("  ").Append(findings[i]);
            }

            Debug.LogWarning(report.ToString());
        }

        private static int AuditOpenScene(List<string> findings)
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return 0;
            }

            int inspected = 0;
            Canvas[] canvases = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < canvases.Length; i++)
            {
                if (!canvases[i].isRootCanvas)
                {
                    continue;
                }

                inspected += AuditUnder(canvases[i].transform, (RectTransform)canvases[i].transform, scene.name, findings);
            }

            return inspected;
        }

        /// <summary>
        /// A prefab asset has no canvas, so every stretch-anchored label in it would measure at
        /// whatever size happened to be serialised - usually zero, which would report the whole
        /// project as broken. So each prefab is instantiated under a throwaway canvas sized to the
        /// game's reference resolution, in a preview scene that the open scene never sees.
        ///
        /// It measures the prefab at reference size only. A label that fits at 720x1280 but clips
        /// on a squatter screen is a thing only a Unity run in the Game view will show.
        /// </summary>
        private static int AuditPrefabs(List<string> findings)
        {
            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { PrefabRoot });
            if (guids.Length == 0)
            {
                return 0;
            }

            int inspected = 0;
            Scene preview = EditorSceneManager.NewPreviewScene();
            try
            {
                RectTransform canvas = CreateAuditCanvas(preview);
                for (int i = 0; i < guids.Length; i++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                    GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (asset == null || asset.GetComponentInChildren<TMP_Text>(true) == null)
                    {
                        continue;
                    }

                    GameObject instance = null;
                    try
                    {
                        instance = (GameObject)PrefabUtility.InstantiatePrefab(asset, preview);
                        if (instance.transform is RectTransform rect)
                        {
                            rect.SetParent(canvas, false);
                        }

                        LayoutRebuilder.ForceRebuildLayoutImmediate(canvas);
                        inspected += AuditUnder(instance.transform, canvas, path, findings);
                    }
                    catch (System.Exception failure)
                    {
                        // One prefab that will not instantiate must not cost the report for all the
                        // others, so it becomes a finding of its own.
                        findings.Add($"{path} :: could not be measured ({failure.GetType().Name}: {failure.Message})");
                    }
                    finally
                    {
                        if (instance != null)
                        {
                            Object.DestroyImmediate(instance);
                        }
                    }
                }
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(preview);
            }

            return inspected;
        }

        private static RectTransform CreateAuditCanvas(Scene preview)
        {
            GameObject holder = new GameObject("AuditCanvas", typeof(RectTransform), typeof(Canvas));
            SceneManager.MoveGameObjectToScene(holder, preview);

            // World space, because a preview scene has no screen to overlay and an overlay canvas
            // there sizes itself to nothing.
            Canvas canvas = holder.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            RectTransform rect = (RectTransform)holder.transform;
            rect.sizeDelta = AuditCanvasSize;
            return rect;
        }

        private static int AuditUnder(Transform root, RectTransform canvas, string owner, List<string> findings)
        {
            TMP_Text[] labels = root.GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < labels.Length; i++)
            {
                Inspect(labels[i], canvas, owner, findings);
            }

            return labels.Length;
        }

        private static void Inspect(TMP_Text label, RectTransform canvas, string owner, List<string> findings)
        {
            label.ForceMeshUpdate();

            RectTransform rect = label.rectTransform;
            Rect bounds = rect.rect;
            if (bounds.width < DegenerateRectSize || bounds.height < DegenerateRectSize)
            {
                findings.Add($"{Describe(label, owner)} degenerate rect {bounds.width:0.#}x{bounds.height:0.#}");

                // No point testing a zero-sized rect for truncation or for hanging off the canvas:
                // it fails both by construction, and three lines about one label buries the rest.
                return;
            }

            if (IsTruncated(label))
            {
                findings.Add($"{Describe(label, owner)} truncated (overflow {label.overflowMode}, rect {bounds.width:0.#}x{bounds.height:0.#})");
            }

            if (canvas != null && rect != canvas && IsOutside(rect, canvas))
            {
                findings.Add($"{Describe(label, owner)} extends outside its canvas");
            }
        }

        private static bool IsTruncated(TMP_Text label)
        {
            if (label.isTextTruncated)
            {
                return true;
            }

            // Overflow mode Overflow lets the text spill rather than drop characters, so a short
            // character count there means nothing. The parsed text is what to compare against:
            // label.text still holds the rich-text tags, which never become characters.
            if (label.overflowMode == TextOverflowModes.Overflow || label.textInfo == null)
            {
                return false;
            }

            string parsed = label.GetParsedText();
            return !string.IsNullOrEmpty(parsed) && label.textInfo.characterCount < parsed.Length;
        }

        private static bool IsOutside(RectTransform rect, RectTransform canvas)
        {
            Vector3[] corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            Rect bounds = canvas.rect;

            for (int i = 0; i < corners.Length; i++)
            {
                Vector3 local = canvas.InverseTransformPoint(corners[i]);
                if (local.x < bounds.xMin - EdgeTolerance || local.x > bounds.xMax + EdgeTolerance
                    || local.y < bounds.yMin - EdgeTolerance || local.y > bounds.yMax + EdgeTolerance)
                {
                    return true;
                }
            }

            return false;
        }

        // ---------------------------------------------------------------- shared

        private static string Describe(TMP_Text label, string owner)
        {
            return $"{owner} :: {HierarchyPath(label.transform)} \"{Excerpt(label.text)}\"";
        }

        private static string HierarchyPath(Transform transform)
        {
            StringBuilder path = new StringBuilder(transform.name);
            Transform parent = transform.parent;
            while (parent != null)
            {
                path.Insert(0, '/').Insert(0, parent.name);
                parent = parent.parent;
            }

            return path.ToString();
        }

        /// <summary>Enough of the string to recognise the label by, on one line.</summary>
        private static string Excerpt(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            string flat = text.Replace('\n', ' ').Replace('\r', ' ');
            return flat.Length <= 40 ? flat : flat.Substring(0, 40) + "...";
        }

        private sealed class SweepLog
        {
            internal readonly List<string> nonWhite = new List<string>();
            internal readonly List<string> boldCleared = new List<string>();
            internal int prefabsWithLabels;
            internal int prefabsChanged;
            internal int sceneChanges;
            internal int skippedPrefabInstances;

            /// <summary>
            /// One block rather than a line per label, because the two lists are meant to be read
            /// together by whoever does the art pass afterwards.
            /// </summary>
            internal string Summarise(TMP_FontAsset font, bool settingsChanged)
            {
                StringBuilder report = new StringBuilder();
                report.Append("Apply Game Font finished on \"").Append(font.name).Append("\": ")
                    .Append(prefabsChanged).Append(" of ").Append(prefabsWithLabels)
                    .Append(" prefab(s) with labels rewritten, ").Append(sceneChanges)
                    .Append(" scene change(s), ").Append(skippedPrefabInstances)
                    .Append(" label(s) left to their prefab, project default ")
                    .Append(settingsChanged ? "repointed." : "already correct.");

                Append(report, boldCleared, "Bold cleared (the outline is baked, faux-bold doubles it)");
                Append(report, nonWhite, "Left coloured, for the art pass to judge (the tint hits the outline too)");
                return report.ToString();
            }

            private static void Append(StringBuilder report, List<string> lines, string heading)
            {
                if (lines.Count == 0)
                {
                    return;
                }

                report.AppendLine().AppendLine().Append(heading).Append(" - ").Append(lines.Count).Append(':');
                for (int i = 0; i < lines.Count; i++)
                {
                    report.AppendLine().Append("  ").Append(lines[i]);
                }
            }
        }
    }
}
#endif
