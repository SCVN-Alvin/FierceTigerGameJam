using System;
using GameJam.Gameplay;
using UnityEngine;

namespace GameJam.Gameplay.Wall
{
    /// <summary>
    /// Hit points for a block, and the swap to loose debris once they run out. Additive to
    /// <see cref="KnockdownBlock"/>: toppling still works exactly as it did, and breaking is a
    /// second, harder outcome layered on top. A block with no shattered prefab still tracks
    /// damage and still reports Broken, it just vanishes instead of coming apart.
    /// </summary>
    /// <remarks>
    /// Still not [RequireComponent(typeof(KnockdownBlock))], though the reason has changed. The
    /// block prefabs now carry a configured KnockdownBlock from the prefab builder, so requiring
    /// one would be harmless on them - but a BreakableBlock added to something else by hand
    /// would silently gain an unconfigured one, and the damage model is meant to work on
    /// anything, whether or not it can be knocked over.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class BreakableBlock : MonoBehaviour
    {
        [Header("Durability")]
        [Tooltip("What this block is made of: brick, glass, concrete. Ammunition damage is "
                 + "authored per material, so this is what decides whether a shot can hurt it.")]
        [SerializeField] private string materialId;

        [SerializeField] private float maxHitPoints = 3f;

        [Tooltip("Impacts slower than this do no damage at all. Without a floor here a structure "
                 + "grinds itself down as it settles, and blocks break from resting contact.")]
        [SerializeField] private float minimumImpactSpeed = 2.5f;

        [Tooltip("Hit points taken per metre/second of impact speed above the minimum.")]
        [SerializeField] private float damagePerImpactSpeed = 0.5f;

        [Tooltip("Ceiling on any single impact, so one very fast hit only one-shots a block when "
                 + "the material is meant to be one-shot.")]
        [SerializeField] private float maxDamagePerImpact = 3f;

        [Header("Shatter")]
        [Tooltip("Debris swapped in when the block breaks. Built next to the block by "
                 + "Tools > Smashdown > Build Block Prefabs.")]
        [SerializeField] private GameObject shatteredPrefab;

        [Tooltip("Optional. Spawned at the impact point, and left to clean itself up.")]
        [SerializeField] private GameObject breakEffectPrefab;

        [Tooltip("Speed each chunk is thrown at, away from the middle of the block.")]
        [SerializeField] private float shardOutwardSpeed = 1.2f;

        [Tooltip("Extra speed along the direction of the blow, so debris carries on the way the "
                 + "hit was going instead of puffing out symmetrically.")]
        [SerializeField] private float shardDirectionalSpeed = 1.8f;

        [SerializeField] private float shardSpin = 4f;

        /// <summary>Raised just before the block is destroyed, whether or not it left debris.</summary>
        public event Action<BreakableBlock> Broken;

        public string MaterialId => materialId;
        public float MaxHitPoints => maxHitPoints;

        /// <summary>The debris this block leaves, read by the pool so it can be warmed up front.</summary>
        public GameObject ShatteredPrefab => shatteredPrefab;
        public float RemainingHitPoints => remainingHitPoints;
        public bool IsBroken => isBroken;

        /// <summary>0 when the block is untouched, 1 when it is about to come apart.</summary>
        public float DamageFraction => maxHitPoints <= 0f
            ? 1f
            : Mathf.Clamp01(1f - (remainingHitPoints / maxHitPoints));

        private float remainingHitPoints;
        private bool isBroken;

        private void Awake()
        {
            remainingHitPoints = maxHitPoints;
        }

        /// <summary>
        /// Damage worked out from how hard something hit, shared by the projectile and by
        /// block-on-block collisions so both read off the same curve.
        /// </summary>
        public float DamageForImpactSpeed(float impactSpeed)
        {
            if (impactSpeed <= minimumImpactSpeed)
            {
                return 0f;
            }

            return Mathf.Min(maxDamagePerImpact, (impactSpeed - minimumImpactSpeed) * damagePerImpactSpeed);
        }

        public void ApplyImpact(float impactSpeed, Vector3 impactPoint, Vector3 impactDirection)
        {
            ApplyDamage(DamageForImpactSpeed(impactSpeed), impactPoint, impactDirection);
        }

        public void ApplyDamage(float amount, Vector3 impactPoint, Vector3 impactDirection)
        {
            if (isBroken || amount <= 0f)
            {
                return;
            }

            remainingHitPoints -= amount;
            if (remainingHitPoints <= 0f)
            {
                Break(impactPoint, impactDirection);
            }
        }

        [ContextMenu("Break")]
        public void BreakNow()
        {
            Break(transform.position, Vector3.zero);
        }

        public void Break(Vector3 impactPoint, Vector3 impactDirection)
        {
            if (isBroken)
            {
                return;
            }

            isBroken = true;
            remainingHitPoints = 0f;

            // A block can be broken by splash or by a neighbour while it is still frozen in the
            // wall, and it may be holding up a whole column. Activating it first runs the support
            // cascade, so what it was carrying falls instead of hanging in mid-air once it is
            // gone. Harmless on a block that was already knocked: Activate returns early.
            if (TryGetComponent(out KnockdownBlock block))
            {
                block.Activate();
            }

            // Read before anything is destroyed, so the debris carries on with whatever the block
            // was already doing rather than appearing from rest mid-flight. Looked up here rather
            // than cached in Awake: the rigidbody is added after the block is instantiated.
            Vector3 inheritedVelocity = TryGetComponent(out Rigidbody blockRigidbody)
                ? blockRigidbody.linearVelocity
                : Vector3.zero;

            SpawnBreakEffect(impactPoint);
            SpawnDebris(inheritedVelocity, impactDirection);

            Broken?.Invoke(this);
            Destroy(gameObject);
        }

        /// <summary>
        /// Damage from being hit by anything that is not the projectile: neighbours toppling into
        /// this block, and the floor when it lands.
        /// </summary>
        private void OnCollisionEnter(Collision collision)
        {
            if (isBroken)
            {
                return;
            }

            float impactSpeed = collision.relativeVelocity.magnitude;
            float damage = DamageForImpactSpeed(impactSpeed);
            if (damage <= 0f)
            {
                return;
            }

            ContactPoint contact = collision.GetContact(0);
            ApplyDamage(damage, contact.point, -contact.normal);
        }

        private void SpawnDebris(Vector3 inheritedVelocity, Vector3 impactDirection)
        {
            if (shatteredPrefab == null)
            {
                return;
            }

            // Rented rather than instantiated: a wall coming apart breaks several blocks in the
            // same frame, and each one is a dozen rigidbodies. Parented where the block was, so
            // the debris spins with the structure like the rest of the map; the pool takes it
            // back before the map is cleared out from under it.
            ShatteredBlock debris = ShatteredBlockPool.Rent(
                shatteredPrefab,
                transform.position,
                transform.rotation,
                transform.parent);

            if (debris == null)
            {
                return;
            }

            debris.transform.localScale = transform.localScale;

            Vector3 directionalVelocity = impactDirection.sqrMagnitude > 0.000001f
                ? impactDirection.normalized * shardDirectionalSpeed
                : Vector3.zero;

            debris.Launch(
                inheritedVelocity,
                transform.position,
                directionalVelocity,
                shardOutwardSpeed,
                shardSpin);
        }

        private void SpawnBreakEffect(Vector3 impactPoint)
        {
            if (breakEffectPrefab == null)
            {
                return;
            }

            BreakEffectPool.Play(breakEffectPrefab, impactPoint);
        }

        private void OnValidate()
        {
            maxHitPoints = Mathf.Max(0.01f, maxHitPoints);
            minimumImpactSpeed = Mathf.Max(0f, minimumImpactSpeed);
            damagePerImpactSpeed = Mathf.Max(0f, damagePerImpactSpeed);
            maxDamagePerImpact = Mathf.Max(0.01f, maxDamagePerImpact);
        }
    }
}
