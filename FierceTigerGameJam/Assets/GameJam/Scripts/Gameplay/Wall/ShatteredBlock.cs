using UnityEngine;

namespace GameJam.Gameplay.Wall
{
    /// <summary>
    /// The loose debris a block leaves behind. Sits on the root of a shattered prefab, whose
    /// children are the chunks: a rigidbody and a box collider each, no component of their own.
    /// Keeping the timing here rather than on every chunk means one Update for the whole burst
    /// and no empty container left over once the pieces are gone.
    ///
    /// An instance is reused rather than thrown away. Everything it needs per frame - the chunk
    /// transforms, their bodies and colliders, and the pose the prefab authored them at - is
    /// read once in Awake, so a burst costs no lookups and no allocations while it plays.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ShatteredBlock : MonoBehaviour
    {
        [Tooltip("Seconds the debris is left lying around before it starts to disappear.")]
        [SerializeField] private float lifetime = 2f;

        [Tooltip("Seconds spent shrinking away once the lifetime is up.")]
        [SerializeField] private float shrinkDuration = 0.4f;

        [Tooltip("Chunks stop colliding when they start to shrink, so a shrinking collider never "
                 + "drags the rest of the structure around.")]
        [SerializeField] private bool freezeWhileShrinking = true;

        [Tooltip("Seconds after the burst during which chunks that have landed have their slide "
                 + "damped away. Debris that skates across the floor keeps solving contacts long "
                 + "after anyone has stopped looking at it.")]
        [SerializeField] private float settleSeconds = 0.4f;

        private Transform[] chunks;
        private Rigidbody[] chunkBodies;
        private Collider[] chunkColliders;
        private Vector3[] restPositions;
        private Quaternion[] restRotations;
        private Vector3[] restScales;

        private ShatteredBlockPool owner;
        private GameObject sourcePrefab;
        private float age;
        private bool isShrinking;

        /// <summary>How many pieces this burst is worth, which is what the pool's cap counts.</summary>
        public int ChunkCount => chunks != null ? chunks.Length : transform.childCount;

        /// <summary>The prefab this was made from, which is the queue it goes back to.</summary>
        internal GameObject SourcePrefab => sourcePrefab;

        private void Awake()
        {
            CacheChunks();
        }

        /// <summary>
        /// Told which pool to go back to, and which of its queues. Debris dropped into a scene by
        /// hand has no pool and destroys itself when it is done, exactly as it used to.
        /// </summary>
        internal void SetOwner(ShatteredBlockPool pool, GameObject prefab)
        {
            owner = pool;
            sourcePrefab = prefab;
        }

        /// <summary>
        /// Throws the chunks outwards from <paramref name="burstOrigin"/>, on top of whatever the
        /// block was already doing. Velocity is set rather than added as a force: the chunks have
        /// wildly different masses, and the speed each one leaves at is the thing worth
        /// controlling, not the impulse it took to get there.
        /// </summary>
        public void Launch(
            Vector3 inheritedVelocity,
            Vector3 burstOrigin,
            Vector3 directionalVelocity,
            float outwardSpeed,
            float spin)
        {
            if (chunks == null)
            {
                CacheChunks();
            }

            for (int i = 0; i < chunks.Length; i++)
            {
                Rigidbody body = chunkBodies[i];
                if (chunks[i] == null || body == null)
                {
                    continue;
                }

                Vector3 outward = chunks[i].position - burstOrigin;
                if (outward.sqrMagnitude < 0.000001f)
                {
                    // A chunk sitting exactly on the impact point has no outward direction of its
                    // own, so it gets a random one instead of dropping straight down.
                    outward = Random.onUnitSphere;
                }

                body.linearVelocity = inheritedVelocity + (outward.normalized * outwardSpeed) + directionalVelocity;
                body.angularVelocity = Random.onUnitSphere * spin;
            }
        }

        /// <summary>
        /// Puts the pieces back where the prefab authored them and hands them back to physics.
        /// Called before a pooled burst is shown again: the chunks were left wherever they had
        /// scattered to, at whatever scale they had shrunk to.
        /// </summary>
        public void ResetChunks()
        {
            if (chunks == null)
            {
                CacheChunks();
            }

            age = 0f;
            isShrinking = false;

            for (int i = 0; i < chunks.Length; i++)
            {
                if (chunks[i] == null)
                {
                    continue;
                }

                chunks[i].SetLocalPositionAndRotation(restPositions[i], restRotations[i]);
                chunks[i].localScale = restScales[i];

                if (chunkColliders[i] != null)
                {
                    chunkColliders[i].enabled = true;
                }

                Rigidbody body = chunkBodies[i];
                if (body == null)
                {
                    continue;
                }

                body.isKinematic = false;
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
        }

        /// <summary>Stops the burst dead, ready to be parked in the pool.</summary>
        internal void SettleForPool()
        {
            FreezeChunks();
            isShrinking = false;
            age = 0f;
        }

        private void Update()
        {
            age += Time.deltaTime;
            if (age < lifetime)
            {
                return;
            }

            if (!isShrinking)
            {
                isShrinking = true;
                if (freezeWhileShrinking)
                {
                    FreezeChunks();
                }
            }

            float shrunk = (age - lifetime) / Mathf.Max(0.01f, shrinkDuration);
            if (shrunk >= 1f)
            {
                Despawn();
                return;
            }

            // Scaled down rather than faded out: the block materials are opaque, and giving them
            // a transparent variant just to despawn debris would cost a second set of shaders.
            float remaining = 1f - shrunk;
            for (int i = 0; i < chunks.Length; i++)
            {
                if (chunks[i] != null)
                {
                    chunks[i].localScale = restScales[i] * remaining;
                }
            }
        }

        /// <summary>
        /// Only while the burst is young: a chunk that has come to rest keeps being nudged by its
        /// neighbours settling around it, and damping the slide is far cheaper than letting the
        /// solver grind it out.
        /// </summary>
        private void FixedUpdate()
        {
            if (isShrinking || age > settleSeconds)
            {
                return;
            }

            for (int i = 0; i < chunkBodies.Length; i++)
            {
                DebrisPhysicsProfile.DampGroundedMotion(chunkBodies[i]);
            }
        }

        private void Despawn()
        {
            if (owner != null)
            {
                owner.Return(this);
                return;
            }

            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (owner != null)
            {
                owner.Forget(this);
            }
        }

        /// <summary>
        /// Reads the chunks once. The layer and the shared surface are enforced here as well as
        /// in the prefab builder, so a prefab built before either existed still behaves - a chunk
        /// on the wrong layer collides with every other chunk on screen.
        /// </summary>
        private void CacheChunks()
        {
            int count = transform.childCount;
            chunks = new Transform[count];
            chunkBodies = new Rigidbody[count];
            chunkColliders = new Collider[count];
            restPositions = new Vector3[count];
            restRotations = new Quaternion[count];
            restScales = new Vector3[count];

            int debrisLayer = DebrisPhysicsProfile.Layer;

            for (int i = 0; i < count; i++)
            {
                Transform chunk = transform.GetChild(i);
                chunks[i] = chunk;
                restPositions[i] = chunk.localPosition;
                restRotations[i] = chunk.localRotation;
                restScales[i] = chunk.localScale;
                chunk.gameObject.layer = debrisLayer;

                chunk.TryGetComponent(out chunkBodies[i]);
                chunk.TryGetComponent(out chunkColliders[i]);

                DebrisPhysicsProfile.ApplyToRigidbody(chunkBodies[i]);
                DebrisPhysicsProfile.ApplyToCollider(chunkColliders[i]);
            }
        }

        private void FreezeChunks()
        {
            if (chunks == null)
            {
                return;
            }

            for (int i = 0; i < chunks.Length; i++)
            {
                if (chunkBodies[i] != null)
                {
                    chunkBodies[i].isKinematic = true;
                }

                if (chunkColliders[i] != null)
                {
                    chunkColliders[i].enabled = false;
                }
            }
        }

        private void OnValidate()
        {
            lifetime = Mathf.Max(0f, lifetime);
            shrinkDuration = Mathf.Max(0.01f, shrinkDuration);
            settleSeconds = Mathf.Max(0f, settleSeconds);
        }
    }
}
