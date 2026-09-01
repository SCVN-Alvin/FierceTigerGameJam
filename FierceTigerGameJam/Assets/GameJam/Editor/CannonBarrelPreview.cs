#if UNITY_EDITOR
using System.Collections.Generic;
using GameJam.Gameplay.Cannon;
using GameJam.Gameplay.Combat;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GameJam.EditorTools
{
    /// <summary>
    /// Lays every cannon in the catalogue out in a row in the open scene, drawn the way the game
    /// draws them, so they can be looked at side by side without entering play mode.
    ///
    /// The point is comparison. A barrel is only judgeable against the others - whether they agree
    /// on size, whether they sit at the same height, whether one of them is facing the wrong way -
    /// and doing that by equipping nine vehicles one at a time in play mode is slow enough that in
    /// practice nobody does it.
    ///
    /// Everything it makes is parented under one object and is editor-only. It is not a builder:
    /// nothing here is part of the game, nothing is saved, and <see cref="ClearPreview"/> or a plain
    /// delete removes every trace of it.
    /// </summary>
    public static class CannonBarrelPreview
    {
        private const string PreviewRootName = "__CannonBarrelPreview";

        private const string LoadoutPath = "Assets/GameJam/Config/Vehicles/VehicleLoadout.asset";

        private const string SlingshotPrefabPath =
            "Assets/GameJam/Imported/LunaSmashdown/Prefabs/SmashFest/Slingshot.prefab";

        /// <summary>Gap between models, in metres. Wide enough that outlines do not touch.</summary>
        private const float ColumnSpacing = 1.2f;

        /// <summary>Gap between one vehicle's levels and the next vehicle's.</summary>
        private const float RowSpacing = 1.6f;

        [MenuItem("Tools/Smashdown/Debug/Show All Cannon Barrels")]
        public static void ShowPreview()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded)
            {
                Debug.LogWarning($"{nameof(CannonBarrelPreview)} needs a loaded scene to lay the cannons out in.");
                return;
            }

            VehicleLoadout loadout = AssetDatabase.LoadAssetAtPath<VehicleLoadout>(LoadoutPath);
            if (loadout == null || loadout.Vehicles == null || loadout.Vehicles.Count == 0)
            {
                Debug.LogWarning(
                    $"{nameof(CannonBarrelPreview)} found no vehicles at {LoadoutPath}. Run "
                    + "Tools > Smashdown > Create Default Vehicle Definitions first.");
                return;
            }

            // Read from the real mount rather than duplicated here, so the preview cannot drift out
            // of step with what the game actually draws - the whole value of the preview is that it
            // is not a second opinion.
            if (!TryReadMountSettings(out bool barrelOnly, out string[] hiddenPrefixes, out Vector3 scale))
            {
                return;
            }

            ClearPreview();

            GameObject root = new GameObject(PreviewRootName);
            Undo.RegisterCreatedObjectUndo(root, "Show Cannon Barrels");

            int spawned = 0;
            int row = 0;
            for (int v = 0; v < loadout.Vehicles.Count; v++)
            {
                VehicleDefinition vehicle = loadout.Vehicles[v];
                if (vehicle == null)
                {
                    continue;
                }

                int levels = vehicle.LevelCount;
                for (int level = 1; level <= levels; level++)
                {
                    GameObject prefab = vehicle.ResolveModelPrefab(level);
                    if (prefab == null)
                    {
                        continue;
                    }

                    GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform);
                    if (model == null)
                    {
                        continue;
                    }

                    model.name = $"{vehicle.Id}_L{level}";
                    model.transform.localPosition = new Vector3((level - 1) * ColumnSpacing, 0f, -row * RowSpacing);
                    model.transform.localRotation = Quaternion.identity;
                    model.transform.localScale = scale;

                    if (barrelOnly)
                    {
                        HideNonBarrelParts(model, hiddenPrefixes);
                    }

                    spawned++;
                }

                row++;
            }

            Selection.activeGameObject = root;
            EditorSceneManager.MarkSceneDirty(scene);

            Debug.Log(
                $"{nameof(CannonBarrelPreview)} laid out {spawned} cannon(s) under \"{PreviewRootName}\" at "
                + $"scale {scale.x:0.###}, barrel-only {barrelOnly}. One row per vehicle, one column per "
                + "level. Delete the object or run Clear Cannon Barrels when you are done - none of it "
                + "belongs in the scene.");
        }

        [MenuItem("Tools/Smashdown/Debug/Clear Cannon Barrels")]
        public static void ClearPreview()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return;
            }

            GameObject existing = GameObject.Find(PreviewRootName);
            if (existing == null)
            {
                return;
            }

            Undo.DestroyObjectImmediate(existing);
            EditorSceneManager.MarkSceneDirty(scene);
        }

        /// <summary>
        /// Reads how the game draws a mounted cannon off the mount in the Slingshot prefab. Falls
        /// back to the component's own defaults when the prefab cannot be opened, so the preview
        /// still runs in a project where the cannon has not been wired yet.
        /// </summary>
        private static bool TryReadMountSettings(out bool barrelOnly, out string[] hiddenPrefixes, out Vector3 scale)
        {
            barrelOnly = true;
            hiddenPrefixes = null;
            scale = new Vector3(0.2f, 0.2f, 0.2f);

            GameObject contents = PrefabUtility.LoadPrefabContents(SlingshotPrefabPath);
            if (contents == null)
            {
                Debug.LogWarning(
                    $"{nameof(CannonBarrelPreview)} could not open {SlingshotPrefabPath}, so the cannons are "
                    + "previewed at this tool's own defaults rather than the mount's.");
                return true;
            }

            try
            {
                VehicleMount mount = contents.GetComponentInChildren<VehicleMount>(true);
                if (mount == null)
                {
                    Debug.LogWarning(
                        $"{nameof(CannonBarrelPreview)} found no {nameof(VehicleMount)} in "
                        + $"{SlingshotPrefabPath}, so this tool's own defaults are used.");
                    return true;
                }

                barrelOnly = mount.BarrelOnly;
                hiddenPrefixes = mount.HiddenPartPrefixes;
                scale = mount.PreviewModelScale;
                return true;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        /// <summary>
        /// The same walk the mount does on spawn, over an instance the mount is not driving.
        ///
        /// It deliberately does not carry the mount's barrel-bone guard: that guard exists to stop a
        /// mis-edited prefix list hiding the thing the aim drives, and here nothing is aimed. If a
        /// barrel does vanish in the preview, that is the bug worth seeing rather than one to be
        /// protected from.
        /// </summary>
        private static void HideNonBarrelParts(GameObject model, string[] hiddenPrefixes)
        {
            if (hiddenPrefixes == null || hiddenPrefixes.Length == 0)
            {
                return;
            }

            List<Transform> frontier = new List<Transform>();
            Transform root = model.transform;
            for (int child = 0; child < root.childCount; child++)
            {
                frontier.Add(root.GetChild(child));
            }

            for (int i = 0; i < frontier.Count; i++)
            {
                Transform node = frontier[i];
                if (VehicleMount.IsHiddenPart(node.name, hiddenPrefixes))
                {
                    node.gameObject.SetActive(false);
                    continue;
                }

                for (int child = 0; child < node.childCount; child++)
                {
                    frontier.Add(node.GetChild(child));
                }
            }
        }
    }
}
#endif
