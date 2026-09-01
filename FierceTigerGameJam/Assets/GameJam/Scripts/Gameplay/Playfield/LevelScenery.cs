using GameJam.Config;
using GameJam.Gameplay.Wall;
using UnityEngine;

namespace GameJam.Gameplay.Playfield
{
    /// <summary>
    /// Dresses the playfield for the mission being played: the backdrop pictures and the ground
    /// texture come from <see cref="MissionConfig"/>'s per-mission scenery, so every level of a
    /// mission shares one look and authoring happens in exactly one place.
    ///
    /// This is also the fix chosen for brief 24's ground note. The ground stays put while the
    /// camera orbits - by design - which only reads well when the floor picture has no strong
    /// direction to it. Per-mission floors (sand, deck plating) are that picture.
    ///
    /// The ground's material is never swapped: the picture rides in a MaterialPropertyBlock, so
    /// nothing instantiates a material at runtime or edits the shared asset in the editor.
    /// </summary>
    public sealed class LevelScenery : MonoBehaviour
    {
        [Tooltip("Where the selected map is read from.")]
        [SerializeField] private MapSelection mapSelection;

        [Tooltip("Carries each mission's background, floor picture and tiling.")]
        [SerializeField] private MissionConfig missionConfig;

        [Tooltip("The backdrop strip's renderers. All of them get the mission's picture; the "
                 + "side tiles keep their authored mirror scales.")]
        [SerializeField] private SpriteRenderer[] backdrops;

        [Tooltip("The ground plane's renderer.")]
        [SerializeField] private MeshRenderer ground;

        private static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
        private static readonly int MainTex = Shader.PropertyToID("_MainTex");
        private static readonly int BaseMapST = Shader.PropertyToID("_BaseMap_ST");

        private Sprite sceneBackground;
        private Vector2 sceneBackgroundSize;
        private Vector3[] authoredScales;
        private Texture sceneFloor;
        private Vector4 sceneFloorST = new Vector4(1f, 1f, 0f, 0f);
        private MaterialPropertyBlock block;
        private string shownMapId;
        private bool captured;

        private void OnEnable()
        {
            CaptureSceneDefaults();
            shownMapId = null;                       // re-dress after a disable, state may be stale
        }

        /// <summary>
        /// Cheap per-frame check rather than an event: the map can change from the mission board,
        /// a retry or a next-level flow, and none of those know this component exists. One string
        /// compare a frame buys never having to be told.
        /// </summary>
        private void LateUpdate()
        {
            MapInfo map = mapSelection != null ? mapSelection.Selected : null;
            string id = map != null ? map.Id : null;
            if (string.Equals(id, shownMapId))
            {
                return;
            }

            shownMapId = id;
            Apply(id);
        }

        /// <summary>What the scene was authored with, kept so a mission without scenery falls back.</summary>
        private void CaptureSceneDefaults()
        {
            if (captured)
            {
                return;
            }

            captured = true;

            if (backdrops != null && backdrops.Length > 0 && backdrops[0] != null)
            {
                sceneBackground = backdrops[0].sprite;
                sceneBackgroundSize = sceneBackground != null
                    ? (Vector2)sceneBackground.bounds.size
                    : Vector2.zero;
                authoredScales = new Vector3[backdrops.Length];
                for (int i = 0; i < backdrops.Length; i++)
                {
                    authoredScales[i] = backdrops[i] != null
                        ? backdrops[i].transform.localScale
                        : Vector3.one;
                }
            }

            if (ground != null && ground.sharedMaterial != null)
            {
                Material material = ground.sharedMaterial;
                sceneFloor = material.HasProperty(BaseMap) ? material.GetTexture(BaseMap)
                    : material.HasProperty(MainTex) ? material.GetTexture(MainTex) : null;
                if (material.HasProperty(BaseMapST))
                {
                    sceneFloorST = material.GetVector(BaseMapST);
                }
            }
        }

        private void Apply(string mapId)
        {
            Sprite background = null;
            Texture2D floor = null;
            float tiling = 1f;
            bool hasScenery = missionConfig != null
                && missionConfig.TryGetScenery(mapId, out background, out floor, out tiling);

            ApplyBackdrop(hasScenery && background != null ? background : sceneBackground);
            ApplyFloor(hasScenery && floor != null ? floor : sceneFloor,
                hasScenery && floor != null ? new Vector4(tiling, tiling, 0f, 0f) : sceneFloorST);
        }

        /// <summary>
        /// Puts one picture on every tile, scaled so a picture drawn at any pixel size covers the
        /// same world area the authored one did. Width drives both axes: stretching each axis
        /// separately is what pulled differently-shaped pictures tall.
        /// </summary>
        private void ApplyBackdrop(Sprite background)
        {
            if (backdrops == null)
            {
                return;
            }

            float fit = 1f;
            if (background != null && sceneBackgroundSize.x > 0.0001f)
            {
                float width = background.bounds.size.x;
                fit = width > 0.0001f ? sceneBackgroundSize.x / width : 1f;
            }

            for (int i = 0; i < backdrops.Length; i++)
            {
                if (backdrops[i] == null)
                {
                    continue;
                }

                backdrops[i].sprite = background;
                if (authoredScales != null && i < authoredScales.Length)
                {
                    Vector3 authored = authoredScales[i];
                    backdrops[i].transform.localScale =
                        new Vector3(authored.x * fit, authored.y * fit, authored.z);
                }
            }
        }

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
    }
}
