#if UNITY_EDITOR
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

            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log(
                $"{nameof(PlayfieldBuilder)} set up the backdrop, a ground at y {groundY:0.###}, and a fall zone. "
                + "Run Organize Scene Hierarchy to file them into their sections.");
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
