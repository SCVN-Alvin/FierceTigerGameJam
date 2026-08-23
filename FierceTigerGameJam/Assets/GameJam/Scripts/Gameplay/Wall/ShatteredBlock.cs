using UnityEngine;

namespace GameJam.Gameplay.Wall
{
    /// <summary>
    /// The loose debris a block leaves behind. Sits on the root of a shattered prefab, whose
    /// children are the chunks: a rigidbody and a box collider each, no component of their own.
    /// Keeping the timing here rather than on every chunk means one Update for the whole burst
    /// and no empty container left over once the pieces are gone.
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

        private Transform[] chunks;
        private Vector3[] chunkScales;
        private float age;
        private bool isShrinking;

        private void Awake()
        {
            CacheChunks();
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
                if (chunks[i] == null || !chunks[i].TryGetComponent(out Rigidbody body))
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
                Destroy(gameObject);
                return;
            }

            // Scaled down rather than faded out: the block materials are opaque, and giving them
            // a transparent variant just to despawn debris would cost a second set of shaders.
            float remaining = 1f - shrunk;
            for (int i = 0; i < chunks.Length; i++)
            {
                if (chunks[i] != null)
                {
                    chunks[i].localScale = chunkScales[i] * remaining;
                }
            }
        }

        private void CacheChunks()
        {
            chunks = new Transform[transform.childCount];
            chunkScales = new Vector3[transform.childCount];
            for (int i = 0; i < transform.childCount; i++)
            {
                chunks[i] = transform.GetChild(i);
                chunkScales[i] = chunks[i].localScale;
            }
        }

        private void FreezeChunks()
        {
            for (int i = 0; i < chunks.Length; i++)
            {
                if (chunks[i] == null)
                {
                    continue;
                }

                if (chunks[i].TryGetComponent(out Rigidbody body))
                {
                    body.isKinematic = true;
                }

                if (chunks[i].TryGetComponent(out Collider chunkCollider))
                {
                    chunkCollider.enabled = false;
                }
            }
        }

        private void OnValidate()
        {
            lifetime = Mathf.Max(0f, lifetime);
            shrinkDuration = Mathf.Max(0.01f, shrinkDuration);
        }
    }
}
