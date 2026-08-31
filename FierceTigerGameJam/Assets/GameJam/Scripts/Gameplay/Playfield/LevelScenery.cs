using System;
using System.Collections.Generic;
using GameJam.Gameplay.Wall;
using GameJam.UI;
using UnityEngine;

namespace GameJam.Gameplay.Playfield
{
    /// <summary>
    /// Dresses the playfield for whichever map is loaded: the backdrop sprites behind the
    /// structure and the picture on the ground plane.
    ///
    /// Both were fixed in the scene, which meant every level looked like the same place. The
    /// pieces stay in the scene - this only swaps what they show, and only when the map has
    /// something to say. A map with no scenery of its own leaves the scene exactly as authored,
    /// so nothing goes blank while the art is still being made.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class LevelScenery : MonoBehaviour
    {
        [Tooltip("Where the current map comes from. The same asset the rest of the game reads.")]
        [SerializeField] private MapSelection mapSelection;

        [Tooltip("The mission list - drag the MissionScreen prefab. Scenery is authored per "
                 + "mission, and this is what says which mission the map being played belongs to.")]
        [SerializeField] private MissionPanelView missionSource;

        [Tooltip("The strip of backdrop sprites behind the structure. All of them get the same "
                 + "sprite; they are tiles of one picture, not separate pictures.")]
        [SerializeField] private SpriteRenderer[] backdrops;

        /// <summary>How a picture of a different size is made to fit the strip.</summary>
        public enum BackdropFit
        {
            /// <summary>Stretch both axes to cover exactly what the scene was built for. A
            /// picture of a different aspect is squashed or pulled to suit.</summary>
            Stretch,

            /// <summary>Scale both axes by the same amount, chosen so the width matches. The
            /// picture keeps its own proportions and its height falls where it falls.</summary>
            KeepAspect,
        }

        [Tooltip("Stretch pulls a picture of a different shape to fill the authored area exactly. "
                 + "Keep Aspect scales it evenly instead, so nothing is distorted.")]
        [SerializeField] private BackdropFit fit = BackdropFit.KeepAspect;

        [Tooltip("Extra height on top of the fit, for pictures that still sit too tall or too "
                 + "short. 1 is the fit as calculated.")]
        [SerializeField, Range(0.3f, 2.5f)] private float backdropHeight = 1f;

        [Tooltip("Turn off to leave the backdrops exactly as they sit in the scene and only swap "
                 + "the picture. Use it when a strip has been placed by hand.")]
        [SerializeField] private bool rescaleBackdrops = true;

        /// <summary>How one mission wants its backdrop strip placed.</summary>
        [Serializable]
        public sealed class BackdropPlacement
        {
            [Tooltip("Mission number as the player sees it: 1, 2, 3.")]
            public int mission = 1;

            [Tooltip("Level inside that mission, as the player sees it: 1..9. Zero means the "
                     + "whole mission - every level that has no row of its own uses it.")]
            public int level;

            [Tooltip("Where the group sits, and where each of the four tiles sits inside it. "
                     + "Whole transforms, not offsets - whatever was dragged is what comes back.")]
            public Vector3 rootPosition;

            public Vector3 rootScale = Vector3.one;
            public Vector3[] positions;
            public Vector3[] scales;

            /// <summary>A row that has never been placed carries no transforms at all.</summary>
            public bool IsSet => rootScale.sqrMagnitude > 0.0001f
                                 && positions != null && positions.Length > 0
                                 && scales != null && scales.Length == positions.Length;
        }

        [Header("Mission BG Setting")]
        [Tooltip("Type a mission number to load that mission's background and floor into the "
                 + "scene straight away. Move or scale the backdrops and the placement is "
                 + "remembered for that mission - there is no button to press. Set it to 0 when "
                 + "you are done.")]
        [SerializeField] private int previewMission;

        [Tooltip("Level inside that mission: 1..9. Leave it at 0 to place the backdrop for the "
                 + "whole mission. Set a number and the placement is saved for that one level "
                 + "instead, overriding the mission's.")]
        [SerializeField] private int previewLevel;

        /// <summary>
        /// Where the placements are kept, one row per mission. Hidden on purpose: it is
        /// bookkeeping, not a control. Shown, its + button makes empty rows, and an empty row is
        /// a scale of zero - which multiplied the backdrop away to nothing.
        ///
        /// Set a mission number, drag the backdrops, and this fills itself in.
        /// </summary>
        [SerializeField, HideInInspector]
        private List<BackdropPlacement> missionBackdrops = new List<BackdropPlacement>();

        [Header("Camera Frame")]
        [Tooltip("Draws what the game camera actually sees, in the scene view, so a backdrop can "
                 + "be placed against the real frame instead of by guessing.")]
        [SerializeField] private bool showCameraFrame = true;

        [Tooltip("Left empty the main camera is used.")]
        [SerializeField] private Camera frameCamera;

        [Tooltip("Screen shape to draw the frame for. The game is portrait; the scene view and "
                 + "the game view are usually not, which is why this cannot be read from either.")]
        [SerializeField] private Vector2 frameAspect = new Vector2(9f, 16f);

        [Header("Backdrop Baseline")]
        [Tooltip("World size the backdrop strip was designed around, in units. A picture is fitted "
                 + "against THIS, not against whatever sprite happens to be on the renderer - "
                 + "reading it from the renderer meant a previewed picture became the new baseline "
                 + "and every preview after it compounded.")]
        [SerializeField] private Vector2 authoredBackdropSize = new Vector2(20f, 16.7f);

        [Tooltip("The parent the four tiles hang under. This is the thing you move and scale - "
                 + "it carries all four at once. Left empty it is taken from the tiles.")]
        [SerializeField] private Transform backdropRoot;

        [Tooltip("The ground plane's renderer. Its material is never swapped - only the picture "
                 + "on it - so the ground keeps one material for the whole game.")]
        [SerializeField] private MeshRenderer ground;

        private static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
        private static readonly int MainTex = Shader.PropertyToID("_MainTex");
        private static readonly int BaseMapST = Shader.PropertyToID("_BaseMap_ST");

        private Sprite sceneBackground;
        private Texture sceneFloor;
        private Vector4 sceneFloorST = new Vector4(1f, 1f, 0f, 0f);
        /// <summary>
        /// Where the strip sits when nothing has been placed. SAVED, not read from the scene at
        /// load: read from the scene it becomes whatever the last preview left behind, which made
        /// every later placement relative to a moving mark.
        /// </summary>
        [SerializeField, HideInInspector] private Vector3[] backdropScales;
        [SerializeField, HideInInspector] private Vector3[] backdropPositions;
        [SerializeField, HideInInspector] private Vector3 rootPosition;
        [SerializeField, HideInInspector] private Vector3 rootScale = Vector3.one;

        /// <summary>
        /// Whether the baseline above holds anything real.
        ///
        /// The group's baseline was added after the tile baselines, and the guard only watched
        /// the tile arrays - so the group's stayed at the field default of position zero while
        /// the tiles' were correct. Every mission with no placement of its own then dragged the
        /// whole strip up to y = 0, which is what put mission 1's framing on mission 3.
        /// </summary>
        [SerializeField, HideInInspector] private bool baselineCaptured;
        private Vector2 sceneBackgroundSize;
        private MaterialPropertyBlock block;
        private bool captured;

        private void Awake()
        {
            CaptureSceneDefaults();
        }

        private void OnEnable()
        {
            if (mapSelection != null)
            {
                mapSelection.SelectionChanged += Apply;
            }

            Apply(mapSelection != null ? mapSelection.Selected : null);
        }

        private void OnDisable()
        {
            if (mapSelection != null)
            {
                mapSelection.SelectionChanged -= Apply;
            }
        }

        /// <summary>
        /// Remembers what the scene was authored with, so a map that specifies no scenery can be
        /// restored to it rather than inheriting whatever the previous map happened to set.
        /// </summary>
        private void CaptureSceneDefaults()
        {
            if (captured)
            {
                return;
            }

            captured = true;
            if (backdrops != null)
            {
                // Only the first time, or after the list changed length. After that the saved
                // baseline stands, whatever the renderers currently look like.
                if (!baselineCaptured
                    || backdropScales == null || backdropScales.Length != backdrops.Length
                    || backdropPositions == null || backdropPositions.Length != backdrops.Length)
                {
                    CaptureBaseline();
                }

                if (backdrops.Length > 0 && backdrops[0] != null)
                {
                    sceneBackground = backdrops[0].sprite;
                    sceneBackgroundSize = sceneBackground != null
                        ? (Vector2)sceneBackground.bounds.size
                        : Vector2.one;
                }
            }

            if (ground != null && ground.sharedMaterial != null)
            {
                Material material = ground.sharedMaterial;
                sceneFloor = material.HasProperty(BaseMap)
                    ? material.GetTexture(BaseMap)
                    : material.HasProperty(MainTex) ? material.GetTexture(MainTex) : null;
                if (material.HasProperty(BaseMapST))
                {
                    sceneFloorST = material.GetVector(BaseMapST);
                }
            }
        }

        /// <summary>Takes the strip exactly as it stands to be the baseline.</summary>
        private void CaptureBaseline()
        {
            backdropScales = new Vector3[backdrops.Length];
            backdropPositions = new Vector3[backdrops.Length];
            for (int i = 0; i < backdrops.Length; i++)
            {
                backdropScales[i] = backdrops[i] != null
                    ? backdrops[i].transform.localScale
                    : Vector3.one;
                backdropPositions[i] = backdrops[i] != null
                    ? backdrops[i].transform.localPosition
                    : Vector3.zero;
            }

            if (backdropRoot == null && backdrops[0] != null)
            {
                backdropRoot = backdrops[0].transform.parent;
            }

            if (backdropRoot != null)
            {
                rootPosition = backdropRoot.localPosition;
                rootScale = backdropRoot.localScale;
            }

            baselineCaptured = true;
        }

        /// <summary>
        /// Puts the group where a placement says, or back on its baseline when there is none.
        ///
        /// The placement rides on the PARENT, not on the four tiles. The tiles are a mirrored
        /// strip whose spacing has to stay as authored; moving one of them on its own tears a gap
        /// in the strip, and moving all four by hand is four chances to get it wrong. Moving the
        /// parent moves the strip.
        /// </summary>
        private void PlaceRoot(BackdropPlacement placement)
        {
            if (placement == null || !placement.IsSet)
            {
                return;
            }

            if (backdropRoot != null)
            {
                backdropRoot.localPosition = placement.rootPosition;
                backdropRoot.localScale = placement.rootScale;
            }

            for (int i = 0; i < backdrops.Length && i < placement.positions.Length; i++)
            {
                if (backdrops[i] == null)
                {
                    continue;
                }

                backdrops[i].transform.localPosition = placement.positions[i];
                backdrops[i].transform.localScale = placement.scales[i];
            }
        }

        /// <summary>Puts the strip back exactly as the scene was built, group included.</summary>
        private void PlaceBaseline()
        {
            if (backdropRoot != null)
            {
                backdropRoot.localPosition = rootPosition;
                backdropRoot.localScale = rootScale;
            }

            for (int i = 0; i < backdrops.Length; i++)
            {
                if (backdrops[i] == null || backdropPositions == null
                    || i >= backdropPositions.Length)
                {
                    continue;
                }

                backdrops[i].transform.localPosition = backdropPositions[i];
                backdrops[i].transform.localScale = backdropScales[i];
            }
        }

        /// <summary>Reads the strip as it stands now into a row.</summary>
        private void CapturePlacement(BackdropPlacement placement)
        {
            placement.rootPosition = backdropRoot != null
                ? backdropRoot.localPosition
                : rootPosition;
            placement.rootScale = backdropRoot != null ? backdropRoot.localScale : rootScale;

            placement.positions = new Vector3[backdrops.Length];
            placement.scales = new Vector3[backdrops.Length];
            for (int i = 0; i < backdrops.Length; i++)
            {
                placement.positions[i] = backdrops[i] != null
                    ? backdrops[i].transform.localPosition
                    : Vector3.zero;
                placement.scales[i] = backdrops[i] != null
                    ? backdrops[i].transform.localScale
                    : Vector3.one;
            }
        }

        /// <summary>Whether a row already says what the strip is doing right now.</summary>
        private bool Matches(BackdropPlacement placement)
        {
            if (placement == null || !placement.IsSet
                || placement.positions.Length != backdrops.Length)
            {
                return false;
            }

            Vector3 nowPosition = backdropRoot != null
                ? backdropRoot.localPosition
                : rootPosition;
            Vector3 nowScale = backdropRoot != null ? backdropRoot.localScale : rootScale;
            if ((placement.rootPosition - nowPosition).sqrMagnitude > 0.00000001f
                || (placement.rootScale - nowScale).sqrMagnitude > 0.00000001f)
            {
                return false;
            }

            for (int i = 0; i < backdrops.Length; i++)
            {
                if (backdrops[i] == null)
                {
                    continue;
                }

                if ((placement.positions[i] - backdrops[i].transform.localPosition).sqrMagnitude
                        > 0.00000001f
                    || (placement.scales[i] - backdrops[i].transform.localScale).sqrMagnitude
                        > 0.00000001f)
                {
                    return false;
                }
            }

            return true;
        }

        private void Apply(MapInfo map)
        {
            CaptureSceneDefaults();

            // Scenery belongs to the MISSION, not to the level. One picture per mission is what
            // the game actually wants, and letting each level carry its own only ever produced
            // rows that drifted apart.
            int mission = MissionOf(map);
            int level = SlotOf(map) + 1;             // 0 when the map is not in any mission
            Sprite missionBackground = null;
            Texture2D missionFloor = null;
            float missionTiling = 1f;
            if (missionSource != null)
            {
                missionSource.TryGetMissionScenery(mission, out missionBackground, out missionFloor, out missionTiling);
            }

            Sprite background = missionBackground != null ? missionBackground : sceneBackground;
            Texture floor = missionFloor != null ? missionFloor : sceneFloor;

            // Tiling travels with the picture. The ground plane is forty metres across, so one
            // repeat stretches a deck plate to the width of a building; grass hides that and
            // plating does not, which is why this cannot be one shared number on the material.
            Vector4 st = missionFloor != null
                ? new Vector4(missionTiling, missionTiling, 0f, 0f)
                : sceneFloorST;

            if (backdrops != null)
            {
                // The backdrop tiles are placed and mirrored for the world size of the sprite the
                // scene was built with, so a picture drawn at a different pixel size would leave
                // gaps down the sides and bare sky at the top. Scaling each tile by how much the
                // new sprite falls short keeps the strip covering exactly what it used to, and
                // means the art does not have to be redrawn at one fixed size forever.
                Vector2 baseline = authoredBackdropSize.sqrMagnitude > 0.0001f
                    ? authoredBackdropSize
                    : sceneBackgroundSize;
                Vector2 size = background != null ? (Vector2)background.bounds.size : baseline;
                float sx = size.x > 0.0001f ? baseline.x / size.x : 1f;
                float sy = size.y > 0.0001f ? baseline.y / size.y : 1f;

                // Keeping the aspect means one scale for both axes. Stretching each axis to the
                // authored area is what made a picture drawn at a different shape come out
                // pulled tall, with nothing exposed to correct it.
                if (fit == BackdropFit.KeepAspect)
                {
                    sy = sx;
                }

                sy *= backdropHeight;

                // Placed by hand beats the automatic fit outright. The only reason anyone places
                // a strip by hand is that the automatic answer was wrong, so nothing here is
                // allowed to argue with it afterwards.
                BackdropPlacement placement = FindPlacement(mission, level);
                bool handSet = placement != null;

                for (int i = 0; i < backdrops.Length; i++)
                {
                    if (backdrops[i] != null)
                    {
                        backdrops[i].sprite = background;
                    }
                }

                if (handSet)
                {
                    PlaceRoot(placement);
                }
                else if (rescaleBackdrops)
                {
                    PlaceBaseline();
                    for (int i = 0; i < backdrops.Length; i++)
                    {
                        if (backdrops[i] == null || backdropScales == null
                            || i >= backdropScales.Length)
                        {
                            continue;
                        }

                        // Signs are kept: the tiles either side are mirrored copies of the middle.
                        Vector3 authored = backdropScales[i];
                        backdrops[i].transform.localScale =
                            new Vector3(authored.x * sx, authored.y * sy, authored.z);
                    }
                }
            }

            ApplyFloor(floor, st);
        }

        /// <summary>
        /// Puts a picture and its tiling on the ground plane.
        ///
        /// A property block rather than a new material: swapping materials at runtime either
        /// leaks an instance per level or edits the asset on disk in the editor. This changes what
        /// the shared material shows for this one renderer.
        /// </summary>
        private void ApplyFloor(Texture floor, Vector4 st)
        {
            if (ground == null || floor == null)
            {
                return;
            }

            block ??= new MaterialPropertyBlock();
            ground.GetPropertyBlock(block);
            Material material = ground.sharedMaterial;
            if (material != null && material.HasProperty(BaseMap))
            {
                block.SetTexture(BaseMap, floor);
            }

            if (material != null && material.HasProperty(MainTex))
            {
                block.SetTexture(MainTex, floor);
            }

            if (material != null && material.HasProperty(BaseMapST))
            {
                block.SetVector(BaseMapST, st);
            }

            ground.SetPropertyBlock(block);
        }

        /// <summary>Which mission the map being played belongs to, zero-based, or -1.</summary>
        private int MissionOf(MapInfo map)
        {
            MapConfig config = mapSelection != null ? mapSelection.Config : null;
            return map != null && missionSource != null && config != null
                ? missionSource.MissionOf(map.Id, config)
                : -1;
        }

        /// <summary>Which level slot inside its mission the map sits in, zero-based, or -1.</summary>
        private int SlotOf(MapInfo map)
        {
            MapConfig config = mapSelection != null ? mapSelection.Config : null;
            return map != null && missionSource != null && config != null
                ? missionSource.SlotOf(map.Id, config)
                : -1;
        }

        /// <summary>
        /// The placement saved for one mission and level, or null if it has never been placed.
        ///
        /// A row with a zero scale counts as NOT placed. Rows appear empty when someone presses +
        /// on the list, and treating one of those as a real placement multiplies the backdrop by
        /// zero - the picture disappears and nothing says why.
        /// </summary>
        private BackdropPlacement FindPlacement(int mission, int level)
        {
            // The level's own row wins; without one the mission's row is what every level in it
            // uses. That way placing a mission once still covers all nine levels, and a level
            // that needs something different can say so without disturbing the rest.
            BackdropPlacement row = FindRow(mission, level);
            if (row != null && row.IsSet)
            {
                return row;
            }

            if (level > 0)
            {
                row = FindRow(mission, 0);
                if (row != null && row.IsSet)
                {
                    return row;
                }
            }

            return null;
        }

        /// <summary>The row for one mission and level whether it holds anything useful or not.</summary>
        private BackdropPlacement FindRow(int mission, int level)
        {
            if (mission < 0 || missionBackdrops == null)
            {
                return null;
            }

            for (int i = 0; i < missionBackdrops.Count; i++)
            {
                if (missionBackdrops[i] != null
                    && missionBackdrops[i].mission == mission + 1
                    && missionBackdrops[i].level == level)
                {
                    return missionBackdrops[i];
                }
            }

            return null;
        }

#if UNITY_EDITOR
        private int shownMission = -1;
        private int shownLevel = -1;

        /// <summary>
        /// Draws the camera's real frame across the backdrop plane and across the plane the
        /// blocks stand on.
        ///
        /// The scene view is free to look from anywhere, and the game view is usually a different
        /// shape from the phone, so neither shows where the edges of the picture will fall. That
        /// gap is why three backgrounds in a row were composed with their subject outside the
        /// frame. This puts the answer on screen while the backdrop is being dragged.
        /// </summary>
        private void OnDrawGizmos()
        {
            Camera camera = frameCamera != null ? frameCamera : Camera.main;
            if (!showCameraFrame || camera == null || camera.orthographic)
            {
                return;
            }

            float backdropZ = backdrops != null && backdrops.Length > 0 && backdrops[0] != null
                ? backdrops[0].transform.position.z
                : 0f;

            DrawFrameAt(camera, backdropZ, new Color(1f, 0.85f, 0.2f, 0.9f));   // the picture
            DrawFrameAt(camera, 0f, new Color(0.3f, 0.9f, 1f, 0.7f));           // where blocks stand

            // A note where the person is actually looking. The preview being off is the normal
            // state, and silence at that moment reads as the feature being broken.
            if (Application.isPlaying)
            {
                return;
            }

            string note = previewMission <= 0
                ? "Preview Mission = 0  -  set it to 1, 2 or 3 to load that mission's scenery here"
                : $"Previewing mission {previewMission}  -  drag the backdrops, the placement saves itself";
            UnityEditor.Handles.color = Color.white;
            UnityEditor.Handles.Label(transform.position + Vector3.up * 0.5f, note);
        }

        private void DrawFrameAt(Camera camera, float planeZ, Color colour)
        {
            Transform view = camera.transform;
            float forwardZ = view.forward.z;
            if (Mathf.Abs(forwardZ) < 0.0001f)
            {
                return;
            }

            // Distance ALONG the camera's forward to where it crosses the plane, not the
            // difference in z: the camera is pitched, and using the z gap would draw the frame
            // slightly too small and slightly too high.
            float distance = (planeZ - view.position.z) / forwardZ;
            if (distance <= 0f)
            {
                return;
            }

            Vector3 centre = view.position + view.forward * distance;
            float halfHeight = distance * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float aspect = frameAspect.y > 0.0001f ? frameAspect.x / frameAspect.y : 0.5625f;
            float halfWidth = halfHeight * aspect;

            Vector3 right = view.right * halfWidth;
            Vector3 up = view.up * halfHeight;
            Vector3 a = centre - right - up;
            Vector3 b = centre + right - up;
            Vector3 c = centre + right + up;
            Vector3 d = centre - right + up;

            Gizmos.color = colour;
            Gizmos.DrawLine(a, b);
            Gizmos.DrawLine(b, c);
            Gizmos.DrawLine(c, d);
            Gizmos.DrawLine(d, a);

            UnityEditor.Handles.color = new Color(colour.r, colour.g, colour.b, 0.08f);
            UnityEditor.Handles.DrawAAConvexPolygon(a, b, c, d);
        }

        /// <summary>
        /// The whole authoring loop, and it has no buttons.
        ///
        /// Type a mission number in Mission BG Setting and that mission's background and floor
        /// appear in the scene at once. Drag or scale the backdrops and the numbers are written
        /// into that mission's row as you go. Nothing to remember to press, because the thing
        /// people forget to press is the save.
        ///
        /// Editor only, only while a mission is being previewed, and it writes only when the
        /// transform has actually moved - otherwise the scene would never stop being dirty.
        /// </summary>
        private void Update()
        {
            if (Application.isPlaying || previewMission <= 0
                || backdrops == null || backdrops.Length == 0 || backdrops[0] == null)
            {
                shownMission = -1;
                shownLevel = -1;
                return;
            }

            // Found rather than demanded. The mission list lives on a prefab and this component
            // lives in a scene, so the two cannot be wired to each other in the project, and an
            // empty field with no explanation is how the preview looked broken.
            if (missionSource == null)
            {
                missionSource = FindMissionSource();
            }

            CaptureSceneDefaults();

            if (shownMission != previewMission || shownLevel != previewLevel)
            {
                shownMission = previewMission;
                shownLevel = previewLevel;
                ShowPreview();
                return;
            }

            RememberPlacement();
        }

        /// <summary>The one prefab in the project that carries a mission list.</summary>
        private static MissionPanelView FindMissionSource()
        {
            foreach (string guid in UnityEditor.AssetDatabase.FindAssets("t:Prefab"))
            {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
                MissionPanelView view = prefab != null
                    ? prefab.GetComponentInChildren<MissionPanelView>(true)
                    : null;
                if (view != null)
                {
                    return view;
                }
            }

            return null;
        }

        /// <summary>Puts the previewed mission's scenery on the scene, placed as it stands now.</summary>
        private void ShowPreview()
        {
            if (missionSource == null)
            {
                Debug.LogWarning("Level Scenery: no prefab with a mission list found. Drag the "
                                 + "MissionScreen prefab into Mission Source.", this);
                return;
            }

            missionSource.TryGetMissionScenery(previewMission - 1, out Sprite background,
                out Texture2D floor, out float tiling);
            for (int i = 0; i < backdrops.Length; i++)
            {
                if (backdrops[i] != null && background != null)
                {
                    backdrops[i].sprite = background;
                }
            }

            // Always laid out, even with nothing saved for this mission - then the strip goes
            // back to the baseline. Leaving it alone meant switching missions kept the previous
            // one's placement and looked as though nothing had been saved.
            BackdropPlacement placement = FindPlacement(previewMission - 1, previewLevel);
            if (placement != null)
            {
                PlaceRoot(placement);
            }
            else
            {
                PlaceBaseline();
            }

            ApplyFloor(floor != null ? floor : sceneFloor,
                floor != null ? new Vector4(tiling, tiling, 0f, 0f) : sceneFloorST);
            UnityEditor.SceneView.RepaintAll();
        }

        /// <summary>Writes where the strip is now into the previewed mission's row.</summary>
        private void RememberPlacement()
        {
            // Reuse the mission's row even when it is empty, and clear out any duplicates. One
            // mission cannot have two placements, and a second row for it would win or lose
            // depending on list order.
            BackdropPlacement placement = FindRow(previewMission - 1, previewLevel);
            if (placement == null)
            {
                // A level inherits its mission's placement until it is actually moved off it.
                // Writing a row the moment a level is previewed would pin every level to
                // whatever the mission looked like that day, and a later change to the mission
                // would then stop reaching any of them.
                if (previewLevel > 0 && Matches(FindPlacement(previewMission - 1, 0)))
                {
                    return;                          // still sitting where it inherited from
                }

                placement = new BackdropPlacement
                {
                    mission = previewMission,
                    level = previewLevel,
                };
                missionBackdrops.Add(placement);
            }
            else
            {
                for (int i = missionBackdrops.Count - 1; i >= 0; i--)
                {
                    if (missionBackdrops[i] != placement
                        && (missionBackdrops[i] == null
                            || (missionBackdrops[i].mission == previewMission
                                && missionBackdrops[i].level == previewLevel)))
                    {
                        missionBackdrops.RemoveAt(i);
                    }
                }

                if (Matches(placement))
                {
                    return;                              // nothing moved, nothing to write
                }
            }

            CapturePlacement(placement);
            UnityEditor.EditorUtility.SetDirty(this);
        }

        /// <summary>
        /// Takes the strip as it stands now to be the design baseline.
        ///
        /// Needed after the baseline has been polluted - a preview left a scale on the renderers
        /// and the component captured that as "authored". Straighten the backdrops by hand, then
        /// run this once.
        /// </summary>
        [ContextMenu("Use current backdrops as the baseline")]
        public void RebaselineBackdrops()
        {
            captured = false;
            baselineCaptured = false;
            CaptureSceneDefaults();
            if (backdrops != null && backdrops.Length > 0)
            {
                CaptureBaseline();
            }

            if (backdrops != null && backdrops.Length > 0 && backdrops[0] != null
                && backdrops[0].sprite != null)
            {
                Vector2 sprite = backdrops[0].sprite.bounds.size;
                Vector3 scale = backdrops[0].transform.localScale;
                authoredBackdropSize = new Vector2(sprite.x * Mathf.Abs(scale.x),
                                                   sprite.y * Mathf.Abs(scale.y));
            }

            UnityEditor.EditorUtility.SetDirty(this);
            Debug.Log($"Level Scenery: baseline is now {authoredBackdropSize}.", this);
        }

        /// <summary>
        /// Drops the row for the mission and level being previewed.
        ///
        /// For a level that was moved by mistake: without this the only way back to the mission's
        /// placement would be to nudge the level until it matched by eye.
        /// </summary>
        public void ClearPlacement()
        {
            if (previewMission <= 0 || missionBackdrops == null)
            {
                return;
            }

            for (int i = missionBackdrops.Count - 1; i >= 0; i--)
            {
                if (missionBackdrops[i] != null
                    && missionBackdrops[i].mission == previewMission
                    && missionBackdrops[i].level == previewLevel)
                {
                    missionBackdrops.RemoveAt(i);
                }
            }

            shownMission = -1;                        // forces the preview to be laid out again
            UnityEditor.EditorUtility.SetDirty(this);
        }

        /// <summary>Fills the two lists from the scene so the component does not have to be wired by hand.</summary>
        [ContextMenu("Find backdrops and ground")]
        public void FindPieces()
        {
            backdrops = FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None);
            System.Array.Sort(backdrops, (a, b) => string.CompareOrdinal(a.name, b.name));

            foreach (MeshRenderer renderer in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (renderer.name == "Ground")
                {
                    ground = renderer;
                    break;
                }
            }

            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif
    }
}
