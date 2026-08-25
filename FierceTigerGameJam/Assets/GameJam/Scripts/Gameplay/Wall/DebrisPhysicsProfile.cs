using UnityEngine;

namespace GameJam.Gameplay.Wall
{
    /// <summary>
    /// Everything the debris chunks share: the layer they live on, the surface they bounce with,
    /// and the damping that brings them to rest. Held in one place because a chunk is configured
    /// twice - once by the prefab builder at author time and once by
    /// <see cref="ShatteredBlock"/> at runtime - and the two must agree.
    ///
    /// The numbers are the ones luna_smashdown settled on. The point of them is that debris is
    /// scenery: it should land, stop, and stop costing anything, rather than skitter around
    /// solving contacts with every other piece of debris on screen.
    /// </summary>
    public static class DebrisPhysicsProfile
    {
        /// <summary>
        /// Chunks are put on their own layer purely so the layer can be made to ignore itself.
        /// A broken wall is a hundred chunks in one place, and chunk-on-chunk contacts are the
        /// single biggest cost in that moment - they are also the contacts nobody looks at.
        /// </summary>
        public const string LayerName = "Debris";

        public const float LinearDamping = 0.32f;
        public const float AngularDamping = 0.32f;
        public const float StaticFriction = 0.52f;
        public const float DynamicFriction = 0.4f;
        public const float Bounciness = 0.12f;

        /// <summary>Name of the shared surface, and of the asset the prefab builder writes.</summary>
        public const string MaterialName = "BlockDebris";

        private const float GroundedVerticalSpeed = 0.35f;
        private const float GroundedHorizontalDamp = 0.88f;
        private const float GroundedAngularDamp = 0.86f;
        private const float MinimumDampedSpeedSqr = 0.0001f;

        private const int UnresolvedLayer = -1;

        private static PhysicsMaterial sharedMaterial;
        private static int resolvedLayer = UnresolvedLayer;
        private static bool layerWarningLogged;
        private static bool layerCollisionsDisabled;

        /// <summary>
        /// The debris layer, or the default layer when the project has no "Debris" layer yet. A
        /// missing layer costs performance, not correctness, so it warns once rather than
        /// throwing.
        /// </summary>
        public static int Layer
        {
            get
            {
                if (resolvedLayer != UnresolvedLayer)
                {
                    return resolvedLayer;
                }

                int layer = LayerMask.NameToLayer(LayerName);
                if (layer < 0)
                {
                    if (!layerWarningLogged)
                    {
                        layerWarningLogged = true;
                        Debug.LogWarning(
                            $"No \"{LayerName}\" layer in the project, so debris chunks will collide "
                            + "with each other. Add the layer in Project Settings > Tags and Layers.");
                    }

                    layer = 0;
                }

                resolvedLayer = layer;
                return resolvedLayer;
            }
        }

        /// <summary>
        /// Called once when a run starts. Only has to happen once per session, but doing it again
        /// costs nothing, and a run that starts without it would be a run of debris colliding
        /// with debris.
        /// </summary>
        public static void Init()
        {
            if (layerCollisionsDisabled)
            {
                return;
            }

            int layer = Layer;
            if (layer == 0)
            {
                // Never make the default layer ignore itself: that is where the blocks live.
                return;
            }

            Physics.IgnoreLayerCollision(layer, layer, true);
            layerCollisionsDisabled = true;
        }

        public static void ApplyToRigidbody(Rigidbody body)
        {
            if (body == null)
            {
                return;
            }

            body.linearDamping = LinearDamping;
            body.angularDamping = AngularDamping;

            // Also fixed here rather than only in the prefab builder, so debris built before any
            // of this existed still costs what it should without being rebuilt. Chunks are small,
            // slow and short-lived: there is nothing for them to tunnel through and nobody is
            // reading their sub-frame position.
            body.interpolation = RigidbodyInterpolation.None;
            body.collisionDetectionMode = CollisionDetectionMode.Discrete;
        }

        /// <summary>
        /// Assigned through sharedMaterial rather than material: reading a collider's material
        /// instantiates a copy of it, and one copy per chunk is exactly the per-break allocation
        /// this whole task is trying to remove.
        /// </summary>
        public static void ApplyToCollider(Collider chunkCollider)
        {
            if (chunkCollider == null)
            {
                return;
            }

            chunkCollider.sharedMaterial = GetSharedMaterial();
        }

        /// <summary>
        /// Bleeds off the sliding of a chunk that has already landed. Only applied once the piece
        /// is no longer moving vertically, so a chunk still in the air keeps its arc and only the
        /// long slide along the floor afterwards is cut short.
        /// </summary>
        public static void DampGroundedMotion(Rigidbody body)
        {
            if (body == null || body.isKinematic)
            {
                return;
            }

            Vector3 velocity = body.linearVelocity;
            if (Mathf.Abs(velocity.y) > GroundedVerticalSpeed)
            {
                return;
            }

            float horizontalSqr = (velocity.x * velocity.x) + (velocity.z * velocity.z);
            if (horizontalSqr <= MinimumDampedSpeedSqr)
            {
                return;
            }

            velocity.x *= GroundedHorizontalDamp;
            velocity.z *= GroundedHorizontalDamp;
            body.linearVelocity = velocity;

            body.angularVelocity = body.angularVelocity * GroundedAngularDamp;
        }

        /// <summary>
        /// Created rather than loaded so nothing has to be wired up in a scene for debris to
        /// behave. The prefab builder writes an asset with the same numbers, which is what makes
        /// the chunks look right before anything enters play mode.
        /// </summary>
        private static PhysicsMaterial GetSharedMaterial()
        {
            if (sharedMaterial != null)
            {
                return sharedMaterial;
            }

            sharedMaterial = new PhysicsMaterial(MaterialName)
            {
                staticFriction = StaticFriction,
                dynamicFriction = DynamicFriction,
                bounciness = Bounciness,
                frictionCombine = PhysicsMaterialCombine.Minimum,
                bounceCombine = PhysicsMaterialCombine.Average,

                // Runtime-only, so it must not be written into whatever scene happens to be open
                // when a block breaks in edit mode.
                hideFlags = HideFlags.HideAndDontSave,
            };

            return sharedMaterial;
        }
    }
}
