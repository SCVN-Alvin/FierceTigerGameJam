using System.Collections.Generic;
using GameJam.Gameplay.Cannon;
using UnityEngine;

namespace GameJam.Gameplay.Tool
{
    /// <summary>
    /// The one camera that draws a 3D model into the garage's preview window.
    ///
    /// A rig standing far under the playfield on a layer of its own, rendering whatever it is
    /// asked to show into a RenderTexture that the UI draws as a flat image. That is the boring
    /// choice, and deliberately: a second camera stacked over the canvas costs a URP camera stack
    /// and an ordering argument on device, and parenting the model into the canvas leaks the
    /// canvas's scale and the scene's lighting into it. A texture is a texture on every device.
    ///
    /// One rig serves both garage tabs, because only one of them is ever on screen. Whoever is
    /// showing something owns the rig, and a <see cref="Hide"/> from anyone else is ignored -
    /// which is what makes switching tabs safe: the tab strip switches the incoming panel on
    /// before it switches the outgoing one off, so the panel being closed would otherwise clear
    /// the model the panel being opened has just put up.
    ///
    /// Nothing here costs anything while the garage is shut: the camera is switched on by
    /// <see cref="Show"/> and off again by <see cref="Hide"/>, and the spin only runs while
    /// there is something to spin.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ModelPreviewRig : MonoBehaviour
    {
        [Tooltip("Models are spawned under here, and this is what spins. Left empty, this "
                 + "transform, which spins the camera with it and is never what you want.")]
        [SerializeField] private Transform pivot;

        [Tooltip("Renders the pivot into the preview texture. Enabled only while a model is up.")]
        [SerializeField] private Camera previewCamera;

        [Tooltip("How fast the model turns about its own vertical axis, in degrees per second.")]
        [SerializeField] private float degreesPerSecond = 40f;

        [Tooltip("How much of the frame the model's largest dimension fills after the auto-fit. "
                 + "0.7 leaves a margin so a wide cannon does not touch the window's edges as it "
                 + "turns side-on.")]
        [Range(0.1f, 1f)]
        [SerializeField] private float frameFill = 0.7f;

        /// <summary>
        /// A fitted scale outside this is a measurement gone wrong rather than an unusual model -
        /// the same reasoning as the vehicle fitting tool's clamp, at wider bounds because a
        /// preview has to swallow both a cannonball and a whole cannon.
        /// </summary>
        private const float MinFittedScale = 0.0001f;

        private const float MaxFittedScale = 10000f;

        /// <summary>
        /// How many further frames a fit that measured nothing is retried for.
        ///
        /// Renderer bounds are read straight after the model is instantiated, which is right for
        /// a mesh renderer and not guaranteed for everything: a skinned mesh that has never been
        /// drawn, or a renderer some import step leaves switched off for a frame, reports a box
        /// of zero size. Rather than write a scale from that - which is how the vehicle fitting
        /// tool once "fitted" nine models to nothing at all - the fit says it failed and is tried
        /// again on the next few frames, by which point the model has certainly been drawn. Three
        /// is generous; in practice the first attempt succeeds.
        /// </summary>
        private const int RefitAttempts = 3;

        /// <summary>
        /// The rig in the scene, for views that hold no reference to it. Set from OnEnable rather
        /// than found on demand, so a scene with no rig costs one null check instead of a search
        /// per redraw.
        /// </summary>
        public static ModelPreviewRig Active { get; private set; }

        /// <summary>What the UI should draw. Null until the camera has been given a target.</summary>
        public RenderTexture TargetTexture => previewCamera != null ? previewCamera.targetTexture : null;

        /// <summary>
        /// Reused by the measuring pass. A field rather than a local because the fit runs twice
        /// per model and up to three more times when the first measurement comes back empty.
        /// </summary>
        private readonly List<Renderer> rendererQuery = new List<Renderer>();

        /// <summary>Whoever asked for what is standing. Only they may take it down.</summary>
        private Object owner;

        private GameObject current;

        /// <summary>What <see cref="current"/> was made from, so an identical ask is free.</summary>
        private GameObject currentSource;

        private int currentLevel;

        private int refitFramesRemaining;

        private void OnEnable()
        {
            // Last one in wins rather than first: a second rig is a mistake either way, and
            // pointing at the one that was just switched on is the less surprising of the two.
            Active = this;
        }

        private void OnDisable()
        {
            if (Active == this)
            {
                Active = null;
            }

            // The model goes with the rig. It is a copy made for a screen that is no longer up,
            // and leaving it standing would keep the pack's meshes in memory for the whole run.
            DestroyCurrent();
            SetCameraEnabled(false);
        }

        /// <summary>
        /// Puts a model in the window and returns whether anything is now showing.
        ///
        /// False for a null prefab, which is the missing-art case the shops fall back to their
        /// flat icon for - the window never shows an empty spin. An identical ask is free: the
        /// shops redraw on every gold change and every selection, and re-instantiating the same
        /// model would restart the spin on every tap.
        /// </summary>
        /// <param name="asker">The view asking, so only it can take this down again.</param>
        /// <param name="modelPrefab">What to show. Never modified - a copy is spawned.</param>
        /// <param name="level">Which "LV{n}" child to enable, for models authored with them.</param>
        public bool Show(Object asker, GameObject modelPrefab, int level)
        {
            if (modelPrefab == null)
            {
                Hide(asker);
                return false;
            }

            if (pivot == null)
            {
                Debug.LogWarning(
                    $"{nameof(ModelPreviewRig)} on \"{name}\" has no pivot, so it has nowhere to put a "
                    + "model. Run Tools > Smashdown > Build Garage Screen.",
                    this);
                return false;
            }

            if (current != null && owner == asker && currentSource == modelPrefab && currentLevel == level)
            {
                return true;
            }

            DestroyCurrent();

            owner = asker;
            currentSource = modelPrefab;
            currentLevel = level;

            // Straight in at the pivot: the fit below decides where it actually sits, and doing
            // that from a known pose means a model whose prefab carries an offset does not
            // quietly inherit it.
            current = Instantiate(modelPrefab, pivot);
            current.transform.localPosition = Vector3.zero;
            current.transform.localRotation = Quaternion.identity;
            current.transform.localScale = Vector3.one;

            // Before anything is measured or drawn: a mannequin on the wrong layer is a cannon
            // hanging in the middle of the playfield.
            SetLayerRecursively(current.transform, gameObject.layer);
            StripToMannequin(current);

            // The artist's level meshes, picked by the projectile's own rule rather than a second
            // copy of it. A vehicle has no such children and comes back untouched.
            GridKnockdownCannonProjectile.ApplyLevelLook(current.transform, level);

            // Every model starts facing the same way, so switching rows does not look like the
            // spin jumping.
            pivot.localRotation = Quaternion.identity;

            refitFramesRemaining = TryFit(current) ? 0 : RefitAttempts;

            SetCameraEnabled(true);
            return true;
        }

        /// <summary>
        /// Takes the model down, if the asker is the one who put it up. A request from anybody
        /// else is ignored - see the note on tab switching in the class summary.
        /// </summary>
        public void Hide(Object asker)
        {
            if (current == null || owner != asker)
            {
                return;
            }

            DestroyCurrent();
            SetCameraEnabled(false);
        }

        private void Update()
        {
            if (current == null || pivot == null || degreesPerSecond == 0f)
            {
                return;
            }

            // Unscaled, because the garage is a menu: whatever the run did to the time scale
            // before the wrench was tapped is not a reason for the preview to stand still.
            pivot.Rotate(0f, degreesPerSecond * Time.unscaledDeltaTime, 0f, Space.Self);
        }

        /// <summary>
        /// The retry for a fit that measured nothing. In LateUpdate rather than Update because
        /// that is after animation and skinning have run, which is the frame's last chance for a
        /// renderer to have honest bounds.
        /// </summary>
        private void LateUpdate()
        {
            if (refitFramesRemaining <= 0 || current == null)
            {
                return;
            }

            refitFramesRemaining--;
            if (TryFit(current))
            {
                refitFramesRemaining = 0;
                return;
            }

            if (refitFramesRemaining == 0)
            {
                // Said out loud once per model rather than silently drawn at whatever size it
                // happens to be: a preview at the wrong scale reads as a broken window, and the
                // cause is always the art rather than this.
                Debug.LogWarning(
                    $"{nameof(ModelPreviewRig)} on \"{name}\" measured no renderer bounds on "
                    + $"\"{currentSource?.name}\", so it is shown at its authored size. The model has no "
                    + "enabled mesh renderer, or every renderer on it is a particle or trail.",
                    this);
            }
        }

        /// <summary>
        /// Scales the copy so its largest dimension fills <see cref="frameFill"/> of the window,
        /// then slides it so the middle of what it draws sits exactly on the pivot.
        ///
        /// Centring on the bounds and not on the model's origin is what makes one rig work for
        /// both tabs: a cannonball is modelled about its centre and a cannon stands on the ground
        /// under its wheels, and only one of those two spins about anything worth looking at.
        /// Because the centre lands on the pivot's origin, the spin stays a spin however far the
        /// model had to move to get there.
        /// </summary>
        /// <returns>False when nothing could be measured; the caller retries.</returns>
        private bool TryFit(GameObject model)
        {
            if (!TryMeasure(model, out Bounds bounds))
            {
                return false;
            }

            float largest = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
            if (largest <= Mathf.Epsilon)
            {
                return false;
            }

            float scale = Mathf.Clamp(FrameSize() * frameFill / largest, MinFittedScale, MaxFittedScale);
            model.transform.localScale = new Vector3(scale, scale, scale);

            // Measured again rather than scaled arithmetically: the first box was taken at scale
            // one about the pivot, and where the middle of the model ends up after scaling
            // depends on where its origin is inside it.
            if (TryMeasure(model, out bounds))
            {
                model.transform.position += pivot.position - bounds.center;
            }

            return true;
        }

        /// <summary>
        /// How wide the camera's view is where the pivot stands, in world units - the shorter of
        /// its two sides, so a model fitted to it fits whichever way the window is shaped.
        ///
        /// Read off the camera rather than written down, so moving it or changing its field of
        /// view re-fits every model instead of quietly cropping them.
        /// </summary>
        private float FrameSize()
        {
            if (previewCamera == null || pivot == null)
            {
                return 1f;
            }

            float aspect = previewCamera.aspect > Mathf.Epsilon ? previewCamera.aspect : 1f;

            if (previewCamera.orthographic)
            {
                float orthographicHeight = previewCamera.orthographicSize * 2f;
                return Mathf.Min(orthographicHeight, orthographicHeight * aspect);
            }

            float distance = Vector3.Distance(previewCamera.transform.position, pivot.position);
            float height = 2f * distance * Mathf.Tan(previewCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            return Mathf.Min(height, height * aspect);
        }

        /// <summary>
        /// The world box of everything the copy actually draws.
        ///
        /// Only renderers on live objects and only ones that are switched on, because an inactive
        /// renderer's bounds are whatever they last were - which for a mesh that has never been
        /// drawn is a box of nothing, and a box of nothing is what would scale a cannon to the
        /// size of the clamp. Particle, trail and line renderers are left out for the same reason
        /// the vehicle fitting tool leaves them out: their bounds describe a simulation rather
        /// than the model's size.
        /// </summary>
        private bool TryMeasure(GameObject model, out Bounds bounds)
        {
            bounds = default;

            model.GetComponentsInChildren(false, rendererQuery);

            bool any = false;
            for (int i = 0; i < rendererQuery.Count; i++)
            {
                Renderer renderer = rendererQuery[i];
                if (renderer == null || !renderer.enabled)
                {
                    continue;
                }

                if (renderer is ParticleSystemRenderer || renderer is TrailRenderer || renderer is LineRenderer)
                {
                    continue;
                }

                if (!any)
                {
                    bounds = renderer.bounds;
                    any = true;
                    continue;
                }

                bounds.Encapsulate(renderer.bounds);
            }

            rendererQuery.Clear();
            return any && bounds.size.sqrMagnitude > Mathf.Epsilon;
        }

        /// <summary>
        /// Takes everything off the copy that is not a picture of the thing.
        ///
        /// A spawned model is not a set of meshes. The vehicles carry a BoxCollider apiece from
        /// the pack and an Animator running its own looping fire clip; the projectiles carry a
        /// Rigidbody, a collider and the shot script itself, which is what would make the garage's
        /// mannequin damage the playfield if it ever met it. Fifty units under the world is not a
        /// defence - it is a place, and things fall.
        ///
        /// The order is load-bearing and the immediacy with it. Scripts go before the physics
        /// they name, because <see cref="GridKnockdownCannonProjectile"/> requires both a Collider
        /// and a Rigidbody and Unity refuses to remove a component something still depends on; and
        /// a deferred Destroy would leave every one of them attached until the end of the frame,
        /// so the refusal would happen anyway and the mannequin would keep its collider while
        /// looking stripped. DestroyImmediate unwinds the chain in the order written.
        /// </summary>
        private static void StripToMannequin(GameObject model)
        {
            // Effects are switched off rather than removed: a trail is authored as a child object,
            // and taking the object out is simpler than unpicking a renderer from the system that
            // feeds it. They are not part of the silhouette either way.
            ParticleSystem[] particles = model.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < particles.Length; i++)
            {
                particles[i].gameObject.SetActive(false);
            }

            TrailRenderer[] trails = model.GetComponentsInChildren<TrailRenderer>(true);
            for (int i = 0; i < trails.Length; i++)
            {
                trails[i].gameObject.SetActive(false);
            }

            MonoBehaviour[] behaviours = model.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                DestroyNow(behaviours[i]);
            }

            // The pack ships its own controller, which holds one looping state: an Animator left
            // on the copy would play the fire animation for as long as the garage is open.
            Animator[] animators = model.GetComponentsInChildren<Animator>(true);
            for (int i = 0; i < animators.Length; i++)
            {
                DestroyNow(animators[i]);
            }

            Animation[] legacyAnimations = model.GetComponentsInChildren<Animation>(true);
            for (int i = 0; i < legacyAnimations.Length; i++)
            {
                DestroyNow(legacyAnimations[i]);
            }

            AudioSource[] sources = model.GetComponentsInChildren<AudioSource>(true);
            for (int i = 0; i < sources.Length; i++)
            {
                DestroyNow(sources[i]);
            }

            Rigidbody[] bodies = model.GetComponentsInChildren<Rigidbody>(true);
            for (int i = 0; i < bodies.Length; i++)
            {
                DestroyNow(bodies[i]);
            }

            // true, and load-bearing: a collider on an object the level look has just switched
            // off is still a collider the moment anything switches that object back on.
            Collider[] colliders = model.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                DestroyNow(colliders[i]);
            }
        }

        /// <summary>
        /// Immediate in play mode as well as out of it - see the ordering note in
        /// <see cref="StripToMannequin"/>. Safe here because this runs from a UI redraw rather
        /// than from inside a physics or rendering callback.
        /// </summary>
        private static void DestroyNow(Component component)
        {
            if (component != null)
            {
                DestroyImmediate(component);
            }
        }

        private static void SetLayerRecursively(Transform node, int layer)
        {
            node.gameObject.layer = layer;
            for (int i = 0; i < node.childCount; i++)
            {
                SetLayerRecursively(node.GetChild(i), layer);
            }
        }

        private void SetCameraEnabled(bool enabledState)
        {
            if (previewCamera != null && previewCamera.enabled != enabledState)
            {
                previewCamera.enabled = enabledState;
            }
        }

        private void DestroyCurrent()
        {
            owner = null;
            currentSource = null;
            currentLevel = 0;
            refitFramesRemaining = 0;

            if (current == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(current);
            }
            else
            {
                DestroyImmediate(current);
            }

            current = null;
        }
    }
}
