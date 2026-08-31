#if UNITY_EDITOR
using GameJam.Gameplay.Cameras;
using GameJam.Gameplay.Playfield;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GameJam.EditorTools
{
    /// <summary>
    /// Builds the parts of the playfield that are scenery and containment rather than gameplay:
    /// the backdrop behind the structure, the ground it stands on, and the volume that clears up
    /// whatever leaves the playfield.
    ///
    /// The backdrop numbers come from the reference project, and port unchanged because this
    /// project's camera rig is the same one - 22.5 degree field of view at (0, 0.75, -6.88). If
    /// the camera is ever moved, the tiles need re-deriving rather than nudging.
    /// </summary>
    public static class PlayfieldBuilder
    {
        private const string BackdropRootName = "Backdrop";
        private const string GroundName = "Ground";
        private const string FallZoneName = "FallZone";
        private const string OrbitPivotName = "OrbitPivot";
        private const string CameraRigName = "CameraController";
        private const string CannonName = "Slingshot";
        private const string AimPlaneName = "AimPlaneAnchor";
        private const string BackdropSpritePath = "Assets/GameJam/Textures/BG_New_02.png";
        private const string BackdropMaterialPath = "Assets/GameJam/Materials/M_Backdrop.mat";
        private const string GroundMaterialPath = "Assets/GameJam/Materials/M_Ground.mat";
        private const string GroundPhysicsMaterialPath = "Assets/GameJam/Materials/PM_Ground.physicMaterial";
        private const string SpriteShaderName = "Sprites/Default";
        private const string UrpLitShaderName = "Universal Render Pipeline/Lit";

        /// <summary>
        /// Drawn in the transparent queue, after the skybox. The reference project puts its
        /// backdrop at 1000, in the background range, which works there only because that scene
        /// has no skybox material at all. Here the skybox pass runs after the opaque range that
        /// 1000 belongs to, and the sprite writes no depth, so a background-queue backdrop is
        /// painted straight over and vanishes.
        ///
        /// Clearing the skybox would fix it too, but ambient light is derived from the skybox in
        /// this scene, so that would flatten the lighting on everything to fix the sky.
        /// </summary>
        private const int BackdropRenderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        private const int BackdropSortingOrder = -200;

        /// <summary>
        /// One tile is 20 x 16.7 world units. Three side by side, mirrored through negative scale,
        /// plus a vertically mirrored and stretched copy stacked on top to fill the sky.
        /// </summary>
        private static readonly (Vector3 position, Vector3 scale)[] BackdropTiles =
        {
            (new Vector3(0f, 2.3f, 19.43f), new Vector3(1f, 1f, 1f)),
            (new Vector3(20f, 2.3f, 19.43f), new Vector3(-1f, 1f, 1f)),
            (new Vector3(-20f, 2.3f, 19.43f), new Vector3(-1f, 1f, 1f)),
            (new Vector3(0f, 32.0274f, 19.43f), new Vector3(1f, -2.5602295f, 1f)),
        };

        [MenuItem("Tools/Smashdown/Set Up Playfield")]
        public static void SetUpPlayfield()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded)
            {
                Debug.LogWarning($"{nameof(PlayfieldBuilder)} needs a loaded scene.");
                return;
            }

            float groundY = ResolveGroundHeight();

            BuildBackdrop();
            BuildGround(groundY);
            BuildFallZone(groundY);
            BuildOrbitRig(scene);

            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log(
                $"{nameof(PlayfieldBuilder)} set up the backdrop, a ground at y {groundY:0.###}, a fall zone, and "
                + "the camera orbit rig. Run Organize Scene Hierarchy to file them into their sections.");
        }

        /// <summary>
        /// The structure is built upward from its root, so the root's height is where the ground
        /// belongs. Read from the scene rather than assumed, since moving the structure otherwise
        /// leaves it hovering or buried.
        /// </summary>
        private static float ResolveGroundHeight()
        {
            GameJam.Gameplay.Wall.KnockdownLayoutMapAuthoring builder =
                Object.FindFirstObjectByType<GameJam.Gameplay.Wall.KnockdownLayoutMapAuthoring>(FindObjectsInactive.Include);
            Transform structureRoot = builder != null ? builder.StructureRoot : null;
            return structureRoot != null ? structureRoot.position.y : 0f;
        }

        private static void BuildBackdrop()
        {
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(BackdropSpritePath);
            if (sprite == null)
            {
                Debug.LogError(
                    $"{nameof(PlayfieldBuilder)} could not load a sprite at {BackdropSpritePath}. "
                    + "Check the texture is imported as Sprite (2D and UI).");
                return;
            }

            Material material = ResolveBackdropMaterial();
            GameObject root = EnsureObject(BackdropRootName, null);
            root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            root.transform.localScale = Vector3.one;

            for (int i = 0; i < BackdropTiles.Length; i++)
            {
                GameObject tile = EnsureObject($"{BackdropRootName}_{i}", root.transform);
                tile.transform.SetPositionAndRotation(BackdropTiles[i].position, Quaternion.identity);
                tile.transform.localScale = BackdropTiles[i].scale;

                if (!tile.TryGetComponent(out SpriteRenderer renderer))
                {
                    renderer = tile.AddComponent<SpriteRenderer>();
                }

                renderer.sprite = sprite;
                renderer.sharedMaterial = material;
                renderer.color = Color.white;
                renderer.sortingOrder = BackdropSortingOrder;
                renderer.rendererPriority = BackdropSortingOrder;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
        }

        private static Material ResolveBackdropMaterial()
        {
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(BackdropMaterialPath);
            if (existing != null)
            {
                // Applied on every run: a material left over from an earlier build carries the
                // old queue, and the backdrop would stay invisible.
                if (existing.renderQueue != BackdropRenderQueue)
                {
                    existing.renderQueue = BackdropRenderQueue;
                    EditorUtility.SetDirty(existing);
                    Debug.Log($"{nameof(PlayfieldBuilder)} moved the backdrop material to render queue {BackdropRenderQueue}.");
                }

                return existing;
            }

            Shader shader = Shader.Find(SpriteShaderName);
            if (shader == null)
            {
                Debug.LogError($"{nameof(PlayfieldBuilder)} could not find the {SpriteShaderName} shader.");
                return null;
            }

            Material material = new Material(shader) { name = "M_Backdrop" };
            material.renderQueue = BackdropRenderQueue;
            material.SetOverrideTag("RenderType", "Background");
            AssetDatabase.CreateAsset(material, BackdropMaterialPath);
            return material;
        }

        /// <summary>
        /// A plain plane with a collider, and a break zone so a block that lands on it hard comes
        /// apart instead of lying there intact.
        /// </summary>
        private static void BuildGround(float groundY)
        {
            GameObject ground = GameObject.Find(GroundName);
            if (ground == null)
            {
                ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
                ground.name = GroundName;
                Undo.RegisterCreatedObjectUndo(ground, "Create Ground");
            }

            // A Unity plane is 10 x 10 at scale 1, so this is 100 x 100 centred on z 20: x +/-50,
            // z -30 to 70. Sized to the arc rather than to the structure - the structure stands at
            // z 10, and the old 40 x 40 plane centred on the origin ended ten units behind it, so
            // an overshot ball or a block knocked backwards left the floor and fell forever.
            ground.transform.SetPositionAndRotation(new Vector3(0f, groundY, 20f), Quaternion.identity);
            ground.transform.localScale = new Vector3(10f, 1f, 10f);

            if (ground.TryGetComponent(out MeshRenderer renderer))
            {
                renderer.sharedMaterial = ResolveGroundMaterial();
            }

            // Friction, so a ball that lands rolls a short way and stops rather than skating on.
            // Unity's implicit default is 0.6 either way, which is not nothing, but it is well
            // short of the reference project's floor and a ball landing on it keeps going.
            //
            // sharedMaterial rather than material: reading `material` from an editor script clones
            // the asset into a scene-only "(Instance)" that is never saved, which would leave the
            // built scene back on the default and leak a material on every run of the menu item.
            //
            // Added rather than merely found, in the same spirit as the break zone below: a Ground
            // that has lost its collider is a scene with no floor at all, and the menu item is
            // meant to be able to put that right.
            MeshCollider groundCollider = ground.GetComponent<MeshCollider>();
            if (groundCollider == null)
            {
                groundCollider = ground.AddComponent<MeshCollider>();
            }

            groundCollider.sharedMaterial = ResolveGroundPhysicsMaterial();

            FallBreakZone zone = ground.GetComponent<FallBreakZone>();
            if (zone == null)
            {
                zone = ground.AddComponent<FallBreakZone>();
            }

            SerializedObject serialized = new SerializedObject(zone);
            serialized.FindProperty("action").enumValueIndex = (int)FallBreakZone.Action.Break;
            serialized.FindProperty("minimumImpactSpeed").floatValue = 1.5f;
            serialized.FindProperty("affectDebris").boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Material ResolveGroundMaterial()
        {
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(GroundMaterialPath);
            if (existing != null)
            {
                return existing;
            }

            Shader shader = Shader.Find(UrpLitShaderName);
            if (shader == null)
            {
                return null;
            }

            Material material = new Material(shader) { name = "M_Ground" };
            material.SetColor("_BaseColor", new Color(0.48f, 0.52f, 0.44f));
            material.SetFloat("_Smoothness", 0.1f);
            AssetDatabase.CreateAsset(material, GroundMaterialPath);
            return material;
        }

        /// <summary>
        /// The floor's friction, ported from the reference project's CannonKnockdownFloor: static
        /// 0.9, dynamic 0.68, no bounce. High on purpose - it is what makes a landed ball roll a
        /// short way and settle rather than slide off the edge of the world, which is most of what
        /// "the floor feels real" means here.
        ///
        /// Created if missing so a fresh clone of the repo builds a working playfield, but not
        /// rewritten if present: the numbers are a feel setting and someone tuning them in the
        /// inspector should not have them stamped back on the next run of the menu item.
        /// </summary>
        private static PhysicsMaterial ResolveGroundPhysicsMaterial()
        {
            PhysicsMaterial existing = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(GroundPhysicsMaterialPath);
            if (existing != null)
            {
                return existing;
            }

            PhysicsMaterial material = new PhysicsMaterial("PM_Ground")
            {
                staticFriction = 0.9f,
                dynamicFriction = 0.68f,
                bounciness = 0f,
                frictionCombine = PhysicsMaterialCombine.Average,
                bounceCombine = PhysicsMaterialCombine.Minimum,
            };

            AssetDatabase.CreateAsset(material, GroundPhysicsMaterialPath);
            return material;
        }

        /// <summary>
        /// A trigger slab under the ground catching whatever falls off the edge or through it.
        /// Deliberately much wider than the playfield: it is a safety net, and a piece escaping it
        /// lives forever.
        ///
        /// Brief 20 asked for a second volume named OutOfBounds with this geometry, on the reading
        /// that no out-of-bounds catch existed. One does - this - and building the other alongside
        /// it would have left the scene with two overlapping despawn volumes racing for the same
        /// colliders. So this one is grown to the geometry the brief wanted instead, keeping its
        /// name so nothing already in the scene is orphaned.
        /// </summary>
        private static void BuildFallZone(float groundY)
        {
            GameObject fallZone = EnsureObject(FallZoneName, null);

            // Centred under the enlarged floor rather than under the origin, and dropped further
            // below it: a catch that sits just under the ground can be clipped by a block resting
            // on the surface, and one that only covers the old 40 x 40 plane misses everything
            // that leaves the new 100 x 100 one. 220 square is margin, not a measurement.
            fallZone.transform.SetPositionAndRotation(new Vector3(0f, groundY - 12f, 20f), Quaternion.identity);
            fallZone.transform.localScale = Vector3.one;

            if (!fallZone.TryGetComponent(out BoxCollider box))
            {
                box = fallZone.AddComponent<BoxCollider>();
            }

            box.isTrigger = true;
            box.center = Vector3.zero;
            box.size = new Vector3(220f, 10f, 220f);

            FallBreakZone zone = fallZone.GetComponent<FallBreakZone>();
            if (zone == null)
            {
                zone = fallZone.AddComponent<FallBreakZone>();
            }

            SerializedObject serialized = new SerializedObject(zone);
            serialized.FindProperty("action").enumValueIndex = (int)FallBreakZone.Action.Despawn;
            serialized.FindProperty("affectDebris").boolValue = true;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// Builds the rig the drag turns: a pivot standing at the structure's centre, carrying
        /// everything that should swing around the structure while the structure itself stays
        /// exactly where physics left it.
        ///
        /// The camera rig, the cannon and the aim plane ride it because they are the player's
        /// point of view and have to stay in the same relationship to each other - the ballistic
        /// solver works in world space, so a cannon that stayed put while the camera moved would
        /// fire off to the side of wherever the player tapped.
        ///
        /// The backdrop rides it too, which is the non-obvious one. It is not scenery that
        /// happens to be behind the structure: it is four flat sprite tiles all facing -z, so a
        /// camera that swung around a fixed backdrop would see it edge-on at 90 degrees and from
        /// behind at 180. Carrying it costs nothing - it has no colliders and no bodies - and it
        /// then sits behind the structure at every angle.
        ///
        /// The ground deliberately stays behind. It is the surface the blocks are resting on, so
        /// moving it is exactly the kind of physics disturbance this whole change exists to
        /// avoid, and it does not need to move: the 100 x 100 plane reaches at least 40 units past
        /// the pivot in every direction, which is far enough that the camera stays well inside it
        /// and the backdrop - which does ride - covers whatever lies beyond the structure.
        /// </summary>
        private static void BuildOrbitRig(Scene scene)
        {
            if (!TryResolveOrbitPivotPosition(out Vector3 pivotPosition))
            {
                Debug.LogWarning(
                    $"{nameof(PlayfieldBuilder)} found no structure to orbit, so no {OrbitPivotName} was built. "
                    + "Build the map first.");
                return;
            }

            CameraOrbit orbit = EnsureOrbitRig(
                pivotPosition,
                FindSection(scene, SceneHierarchyOrganizer.GameplayHeader),
                FindRider(CameraRigName),
                FindRider(CannonName),
                FindRider(AimPlaneName),
                FindRider(BackdropRootName));

            WireOrbit(orbit);
        }

        /// <summary>
        /// The pivot goes where the structure's own centre object is, read from the scene rather
        /// than written down here: it is the point SpinOnAxis used to turn the map about, so
        /// orbiting it is what makes the new gesture look like the old one. The structure root is
        /// the fallback, since a scene whose map has not been built yet has no centre object.
        /// </summary>
        private static bool TryResolveOrbitPivotPosition(out Vector3 position)
        {
            GameJam.Gameplay.Wall.KnockdownLayoutMapAuthoring builder =
                Object.FindFirstObjectByType<GameJam.Gameplay.Wall.KnockdownLayoutMapAuthoring>(FindObjectsInactive.Include);
            Transform structureRoot = builder != null ? builder.StructureRoot : null;
            if (structureRoot == null)
            {
                position = Vector3.zero;
                return false;
            }

            Transform center = structureRoot.Find(GameJam.Gameplay.Wall.StructureLayout.CenterObjectName);
            position = center != null ? center.position : structureRoot.position;
            return true;
        }

        /// <summary>
        /// Creates the pivot if it is missing, puts it where it belongs, and adopts the riders,
        /// preserving every world pose. Shared with the demo level builder so both scenes get the
        /// same rig.
        ///
        /// Written so that running it twice - or running it over a rig an earlier version of the
        /// builder made - changes nothing. Two hazards are guarded rather than assumed away: a
        /// rider that is already an ancestor of the pivot would make the pivot its own descendant,
        /// and moving a pivot that already has riders would drag the camera off its authored
        /// framing, so the riders' world poses are restored across the move.
        /// </summary>
        internal static CameraOrbit EnsureOrbitRig(
            Vector3 pivotWorldPosition,
            Transform pivotParent,
            params Transform[] riders)
        {
            GameObject pivotObject = GameObject.Find(OrbitPivotName);
            if (pivotObject == null)
            {
                pivotObject = new GameObject(OrbitPivotName);
                Undo.RegisterCreatedObjectUndo(pivotObject, $"Create {OrbitPivotName}");
            }

            Transform pivot = pivotObject.transform;

            if (pivotParent != null && pivot.parent != pivotParent && !pivotParent.IsChildOf(pivot))
            {
                Undo.SetTransformParent(pivot, pivotParent, $"Parent {OrbitPivotName}");
            }

            // Captured before the pivot is squared up and released afterwards. The riders belong
            // where the scene put them; only the point they turn about is being set here.
            int riderCount = pivot.childCount;
            Vector3[] riderPositions = new Vector3[riderCount];
            Quaternion[] riderRotations = new Quaternion[riderCount];
            for (int i = 0; i < riderCount; i++)
            {
                pivot.GetChild(i).GetPositionAndRotation(out riderPositions[i], out riderRotations[i]);
            }

            pivot.localScale = Vector3.one;

            // Identity rotation every run: the scene file is what "the authored viewpoint" means,
            // and CameraOrbit.ResetRotation returns to whatever it finds here.
            pivot.SetPositionAndRotation(pivotWorldPosition, Quaternion.identity);

            for (int i = 0; i < riderCount; i++)
            {
                pivot.GetChild(i).SetPositionAndRotation(riderPositions[i], riderRotations[i]);
            }

            for (int i = 0; i < riders.Length; i++)
            {
                Transform rider = riders[i];
                if (rider == null || rider.parent == pivot)
                {
                    continue;
                }

                // True when the rider is the pivot itself or one of its ancestors. Parenting
                // either under the pivot would detach the whole branch from the scene.
                if (pivot.IsChildOf(rider))
                {
                    Debug.LogWarning(
                        $"{nameof(PlayfieldBuilder)} left \"{rider.name}\" where it is: the {OrbitPivotName} "
                        + "sits inside it, so it cannot ride it.",
                        rider);
                    continue;
                }

                // Undo.SetTransformParent keeps the world pose, and the pose is written back
                // afterwards anyway so that a prefab instance - the Slingshot is one - lands
                // exactly where it stood rather than a rounding step away from it.
                rider.GetPositionAndRotation(out Vector3 position, out Quaternion rotation);
                Undo.SetTransformParent(rider, pivot, $"Parent {rider.name} to {OrbitPivotName}");
                rider.SetPositionAndRotation(position, rotation);
            }

            CameraOrbit orbit = pivotObject.GetComponent<CameraOrbit>();
            if (orbit == null)
            {
                orbit = Undo.AddComponent<CameraOrbit>(pivotObject);
            }

            return orbit;
        }

        /// <summary>
        /// Fills the two references that drive and reset the orbit, without overwriting anything
        /// already pointed somewhere on purpose.
        /// </summary>
        private static void WireOrbit(CameraOrbit orbit)
        {
            GameJam.Gameplay.Wall.StructureRotateController rotateController =
                Object.FindFirstObjectByType<GameJam.Gameplay.Wall.StructureRotateController>(FindObjectsInactive.Include);
            if (rotateController != null)
            {
                SerializedObject serialized = new SerializedObject(rotateController);
                UiBuilder.SetIfEmpty(serialized, "cameraOrbit", orbit);
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            GameJam.Gameplay.Flow.GameFlowController flow =
                Object.FindFirstObjectByType<GameJam.Gameplay.Flow.GameFlowController>(FindObjectsInactive.Include);
            if (flow != null)
            {
                SerializedObject serialized = new SerializedObject(flow);
                UiBuilder.SetIfEmpty(serialized, "cameraOrbit", orbit);
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        /// <summary>
        /// Find-by-name, and a warning rather than silence when a rider is missing: a rig that
        /// quietly left the cannon behind would only show up as shots flying sideways.
        /// </summary>
        private static Transform FindRider(string name)
        {
            GameObject found = GameObject.Find(name);
            if (found == null)
            {
                Debug.LogWarning($"{nameof(PlayfieldBuilder)} found no \"{name}\" to put under the {OrbitPivotName}.");
                return null;
            }

            return found.transform;
        }

        private static Transform FindSection(Scene scene, string sectionName)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == sectionName)
                {
                    return root.transform;
                }
            }

            return null;
        }

        private static GameObject EnsureObject(string name, Transform parent)
        {
            GameObject existing = GameObject.Find(name);
            if (existing == null)
            {
                existing = new GameObject(name);
                Undo.RegisterCreatedObjectUndo(existing, $"Create {name}");
            }

            if (parent != null && existing.transform.parent != parent)
            {
                Undo.SetTransformParent(existing.transform, parent, $"Parent {name}");
            }

            return existing;
        }
    }
}
#endif
