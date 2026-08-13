using UnityEngine;

namespace GameJam.Gameplay.Wall
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public sealed class SmashBlock : MonoBehaviour
    {
        [SerializeField] private SmashMaterialType materialType;
        [SerializeField] private float mass = 1f;
        [SerializeField] private float impulseMultiplier = 1f;
        [SerializeField] private float linearDamping = 0.15f;
        [SerializeField] private float angularDamping = 0.1f;
        [SerializeField] private float collisionActivationVelocity = 2f;
        [SerializeField] private bool fractureOnImpact;

        private Rigidbody body;
        private Collider blockCollider;
        private MeshFilter sourceMeshFilter;
        private MeshRenderer sourceRenderer;
        private bool activated;
        private bool fractured;

        private static Transform debrisRoot;

        public SmashMaterialType MaterialType => materialType;
        public bool IsActivated => activated;

        public void Configure(
            SmashMaterialType type,
            float configuredMass,
            float configuredImpulseMultiplier,
            float configuredLinearDamping,
            float configuredAngularDamping,
            float configuredCollisionActivationVelocity,
            bool shouldFracture)
        {
            materialType = type;
            mass = Mathf.Max(0.01f, configuredMass);
            impulseMultiplier = Mathf.Max(0f, configuredImpulseMultiplier);
            linearDamping = Mathf.Max(0f, configuredLinearDamping);
            angularDamping = Mathf.Max(0f, configuredAngularDamping);
            collisionActivationVelocity = Mathf.Max(0f, configuredCollisionActivationVelocity);
            fractureOnImpact = shouldFracture;
            CacheComponents();
            ApplyBodySettings();
        }

        private void Awake()
        {
            CacheComponents();
            ApplyBodySettings();
            Sleep();
        }

        private void Start()
        {
            CacheComponents();
        }

        public void Knock(Vector3 impactPoint, Vector3 impulse, float falloff, bool allowFracture = true)
        {
            float strength = Mathf.Clamp01(falloff) * impulseMultiplier;
            if (strength <= 0f)
            {
                return;
            }

            if (allowFracture && fractureOnImpact && !fractured && sourceMeshFilter != null && sourceRenderer != null)
            {
                fractured = true;
                if (RuntimeGlassFracture.TryFracture(this, sourceMeshFilter, sourceRenderer, blockCollider, impactPoint, impulse * strength))
                {
                    Destroy(gameObject);
                    return;
                }
            }

            Activate();
            body.AddForceAtPosition(impulse * strength, impactPoint, ForceMode.Impulse);
        }

        public void Activate()
        {
            if (activated)
            {
                return;
            }

            activated = true;
            transform.SetParent(GetDebrisRoot(), true);
            if (body == null)
            {
                body = gameObject.AddComponent<Rigidbody>();
                ApplyBodySettings();
            }

            body.isKinematic = false;
            body.useGravity = true;
            body.WakeUp();
        }

        private static Transform GetDebrisRoot()
        {
            if (debrisRoot != null)
            {
                return debrisRoot;
            }

            GameObject existing = GameObject.Find("ActiveDebris");
            GameObject rootObject = existing != null ? existing : new GameObject("ActiveDebris");
            debrisRoot = rootObject.transform;
            return debrisRoot;
        }

        public float GetMass()
        {
            return body != null ? body.mass : mass;
        }

        private void Sleep()
        {
            activated = false;
            if (body != null)
            {
                body.isKinematic = true;
                body.useGravity = false;
            }
        }

        private void CacheComponents()
        {
            if (body == null)
            {
                body = GetComponent<Rigidbody>();
            }

            if (blockCollider == null)
            {
                blockCollider = GetComponent<Collider>();
            }

            if (sourceMeshFilter == null)
            {
                sourceMeshFilter = GetComponentInChildren<MeshFilter>();
            }

            if (sourceRenderer == null)
            {
                sourceRenderer = GetComponentInChildren<MeshRenderer>();
            }
        }

        private void ApplyBodySettings()
        {
            if (body == null)
            {
                return;
            }

            body.mass = mass;
            body.linearDamping = linearDamping;
            body.angularDamping = angularDamping;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.Continuous;
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (activated || collision.relativeVelocity.sqrMagnitude < collisionActivationVelocity * collisionActivationVelocity)
            {
                return;
            }

            SmashBlock other = collision.rigidbody != null ? collision.rigidbody.GetComponent<SmashBlock>() : null;
            if (other == null || !other.IsActivated)
            {
                return;
            }

            Activate();
        }
    }
}
