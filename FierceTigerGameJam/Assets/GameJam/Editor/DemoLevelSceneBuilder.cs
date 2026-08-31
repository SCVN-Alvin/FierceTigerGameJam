#if UNITY_EDITOR
using System;
using GameJam.Gameplay.Cameras;
using GameJam.Gameplay.Cannon;
using GameJam.Gameplay.Wall;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GameJam.EditorTools
{
    public static class DemoLevelSceneBuilder
    {
        private const string ModelPath = "Assets/GameJam/FBX/ModelDemo.fbx";
        private const string ScenePath = "Assets/GameJam/Scene/DemoGameplay.unity";

        [MenuItem("Tools/Smashdown/Build Demo Level")]
        public static void BuildDemoLevel()
        {
            GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (modelAsset == null)
            {
                throw new InvalidOperationException($"Demo model is missing at {ModelPath}.");
            }

            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath)
            {
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }

            GameObject oldWall = GameObject.Find("Wall");
            if (oldWall != null)
            {
                UnityEngine.Object.DestroyImmediate(oldWall);
            }

            GameObject wall = new GameObject("Wall");
            GameObject rotatingRoot = new GameObject("Root");
            rotatingRoot.transform.SetParent(wall.transform, false);

            GameObject demoInstance = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset, scene);
            demoInstance.name = "ModelDemo";
            demoInstance.transform.SetParent(rotatingRoot.transform, false);
            demoInstance.transform.localPosition = Vector3.zero;
            demoInstance.transform.localRotation = Quaternion.identity;
            demoInstance.transform.localScale = Vector3.one;

            Bounds modelBounds = CalculateRendererBounds(demoInstance);
            const float targetMaxDimension = 7.2f;
            float largestDimension = Mathf.Max(modelBounds.size.x, modelBounds.size.y, modelBounds.size.z);
            float levelScale = largestDimension > 0.0001f ? targetMaxDimension / largestDimension : 1f;
            demoInstance.transform.localScale = Vector3.one * levelScale;
            modelBounds = CalculateRendererBounds(demoInstance);
            Vector3 center = modelBounds.center;
            float floorY = modelBounds.min.y;
            demoInstance.transform.position += new Vector3(-center.x, -floorY, 2.8f - center.z);
            modelBounds = CalculateRendererBounds(demoInstance);

            CreateGround(wall.transform, modelBounds);

            GameObject rotationCenter = new GameObject("RotCenter");
            rotationCenter.transform.SetParent(wall.transform, false);
            rotationCenter.transform.position = new Vector3(modelBounds.center.x, modelBounds.min.y, modelBounds.center.z);

            SpinOnAxis spinner = rotatingRoot.AddComponent<SpinOnAxis>();
            SerializedObject spinnerObject = new SerializedObject(spinner);
            spinnerObject.FindProperty("rotationCenter").objectReferenceValue = rotationCenter.transform;
            spinnerObject.FindProperty("speed").floatValue = 0f;
            spinnerObject.ApplyModifiedPropertiesWithoutUndo();

            DemoLevelRuntimeBuilder builder = wall.AddComponent<DemoLevelRuntimeBuilder>();
            SerializedObject builderObject = new SerializedObject(builder);
            builderObject.FindProperty("modelRoot").objectReferenceValue = demoInstance.transform;
            builderObject.FindProperty("buildOnAwake").boolValue = true;
            builderObject.FindProperty("blocksPerFrame").intValue = 0;
            builderObject.ApplyModifiedPropertiesWithoutUndo();

            ConfigureAimAndCamera(modelBounds, rotationCenter.transform);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            Selection.activeGameObject = wall;
            Debug.Log($"Demo level built: {modelBounds.size} at {modelBounds.center}.");
        }

        private static void CreateGround(Transform parent, Bounds modelBounds)
        {
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Ground";
            ground.transform.SetParent(parent, false);
            float size = Mathf.Max(modelBounds.size.x, modelBounds.size.z) * 1.9f;
            ground.transform.position = new Vector3(modelBounds.center.x, modelBounds.min.y - 0.14f, modelBounds.center.z);
            ground.transform.localScale = new Vector3(size, 0.28f, size);

            MeshRenderer renderer = ground.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                Material material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                material.name = "Demo Ground";
                material.color = new Color(0.075f, 0.09f, 0.115f, 1f);
                renderer.sharedMaterial = material;
            }
        }

        /// <summary>
        /// Frames the demo and gives it the same camera orbit rig the gameplay scene has.
        ///
        /// It used to hand the drag controller the structure's <see cref="SpinOnAxis"/>, which is
        /// how the demo turned its model. The drag drives a <see cref="CameraOrbit"/> now, so the
        /// demo builds one too rather than being left with a dead drag - the demo exists to
        /// rehearse the real scene, and a demo that turned its structure while the game turned
        /// its camera would stop being a rehearsal. The spinner is still added and still honours
        /// an authored speed, which is the other way these scenes are used.
        /// </summary>
        private static void ConfigureAimAndCamera(Bounds bounds, Transform rotationCenter)
        {
            CannonAimPlaneAnchor aimPlane = UnityEngine.Object.FindFirstObjectByType<CannonAimPlaneAnchor>();
            if (aimPlane != null)
            {
                aimPlane.transform.position = new Vector3(bounds.center.x, bounds.center.y, bounds.min.z - 0.04f);
                aimPlane.transform.rotation = Quaternion.identity;

                SerializedObject aimObject = new SerializedObject(aimPlane);
                aimObject.FindProperty("planeHalfWidth").floatValue = bounds.extents.x + 0.3f;
                aimObject.FindProperty("planeHalfHeight").floatValue = bounds.extents.y + 0.3f;
                aimObject.FindProperty("enforceBounds").boolValue = false;
                SerializedProperty overlayProperty = aimObject.FindProperty("drawGameViewOverlay");
                if (overlayProperty != null)
                {
                    overlayProperty.boolValue = false;
                }
                aimObject.ApplyModifiedPropertiesWithoutUndo();

                CannonAimController aimController = UnityEngine.Object.FindFirstObjectByType<CannonAimController>();
                if (aimController != null)
                {
                    aimController.SetAimPlane(aimPlane);
                }
            }

            Camera camera = Camera.main;
            if (camera != null)
            {
                GameJam.Gameplay.Cameras.GameJamCameraSizeController controller = camera.GetComponent<GameJam.Gameplay.Cameras.GameJamCameraSizeController>();
                if (controller != null)
                {
                    controller.enabled = false;
                }

                float verticalFov = 28f;
                camera.fieldOfView = verticalFov;
                float distanceForHeight = bounds.extents.y / Mathf.Tan(verticalFov * 0.5f * Mathf.Deg2Rad);
                float horizontalFov = 2f * Mathf.Atan(Mathf.Tan(verticalFov * 0.5f * Mathf.Deg2Rad) * Mathf.Max(0.56f, camera.aspect));
                float distanceForWidth = bounds.extents.x / Mathf.Tan(horizontalFov * 0.5f);
                float distance = Mathf.Max(distanceForHeight, distanceForWidth) * 1.28f;
                camera.transform.position = new Vector3(bounds.center.x, bounds.center.y + bounds.extents.y * 0.03f, bounds.min.z - distance);
                camera.transform.rotation = Quaternion.LookRotation(bounds.center - camera.transform.position, Vector3.up);
            }

            // Built last, once everything it carries is standing where it belongs: the rig keeps
            // world poses, so it adopts the framing above rather than replacing it. The demo has
            // no section headers to file it under and no backdrop to carry.
            CameraOrbit orbit = PlayfieldBuilder.EnsureOrbitRig(
                rotationCenter != null ? rotationCenter.position : bounds.center,
                null,
                // The camera rig if the demo scene has one, and otherwise the camera itself: a
                // rig that left the camera behind would orbit the cannon around a still view.
                FindDemoRider("CameraController") ?? (camera != null ? camera.transform : null),
                FindDemoRider("Slingshot"),
                aimPlane != null ? aimPlane.transform : null);

            StructureRotateController rotateController = UnityEngine.Object.FindFirstObjectByType<StructureRotateController>();
            if (rotateController != null)
            {
                SerializedObject rotateObject = new SerializedObject(rotateController);
                rotateObject.FindProperty("cameraOrbit").objectReferenceValue = orbit;
                rotateObject.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private static Transform FindDemoRider(string name)
        {
            GameObject found = GameObject.Find(name);
            return found != null ? found.transform : null;
        }

        private static Bounds CalculateRendererBounds(GameObject root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                return new Bounds(root.transform.position, Vector3.one);
            }

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            return bounds;
        }
    }
}
#endif
