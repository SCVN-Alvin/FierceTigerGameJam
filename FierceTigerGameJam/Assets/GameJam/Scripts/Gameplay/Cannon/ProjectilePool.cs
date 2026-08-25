using System.Collections.Generic;
using UnityEngine;

namespace GameJam.Gameplay.Cannon
{
    /// <summary>
    /// A warm queue of cannon balls, sized to the shots a run allows. A shot used to instantiate
    /// a rigidbody with a collider in the frame the player tapped, which is the one frame in the
    /// game where nothing else should be competing for time.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ProjectilePool : MonoBehaviour
    {
        [Tooltip("What is fired. The same prefab the fire controller would have instantiated.")]
        [SerializeField] private GridKnockdownCannonProjectile projectilePrefab;

        [Tooltip("Instances built up front. Raised to the run's bullet budget when there is one, "
                 + "since that is the most shots that can ever be in the air at once.")]
        [SerializeField] private int poolSize = 10;

        private readonly Queue<GridKnockdownCannonProjectile> idle =
            new Queue<GridKnockdownCannonProjectile>();

        /// <summary>What is in the air, so a run ending mid-flight can call it all back.</summary>
        private readonly List<GridKnockdownCannonProjectile> inFlight =
            new List<GridKnockdownCannonProjectile>();

        private Transform poolRoot;

        public bool HasPrefab => projectilePrefab != null;

        /// <summary>
        /// Warms the queue to <paramref name="size"/>, keeping whatever is already in it. Called
        /// again on every run, which is why it tops up rather than rebuilding.
        /// </summary>
        public void Warm(int size)
        {
            if (projectilePrefab == null)
            {
                return;
            }

            poolSize = Mathf.Max(1, size);
            EnsureRoot();

            while (idle.Count < poolSize)
            {
                GridKnockdownCannonProjectile projectile = Create();
                if (projectile == null)
                {
                    return;
                }

                idle.Enqueue(projectile);
            }
        }

        /// <summary>
        /// A ball ready to be launched, growing the pool if the run somehow outruns its budget.
        /// Null only when no prefab is assigned, which is the fire controller's cue to fall back
        /// to its built-in sphere.
        /// </summary>
        public GridKnockdownCannonProjectile Rent(Vector3 position, Quaternion rotation, Transform parent)
        {
            if (projectilePrefab == null)
            {
                return null;
            }

            EnsureRoot();

            GridKnockdownCannonProjectile projectile = null;
            while (idle.Count > 0 && projectile == null)
            {
                // A queued instance can have been destroyed by a scene teardown.
                projectile = idle.Dequeue();
            }

            if (projectile == null)
            {
                projectile = Create();
                if (projectile == null)
                {
                    return null;
                }
            }

            Transform projectileTransform = projectile.transform;
            projectileTransform.SetParent(parent != null ? parent : poolRoot, false);
            projectileTransform.SetPositionAndRotation(position, rotation);
            projectile.gameObject.SetActive(true);
            inFlight.Add(projectile);
            return projectile;
        }

        /// <summary>Calls every shot still in the air home, for the end of a run.</summary>
        public void ReturnAll()
        {
            for (int i = inFlight.Count - 1; i >= 0; i--)
            {
                Return(inFlight[i]);
            }

            inFlight.Clear();
        }

        /// <summary>Takes a spent ball back, wherever it had got to.</summary>
        public void Return(GridKnockdownCannonProjectile projectile)
        {
            if (projectile == null)
            {
                return;
            }

            inFlight.Remove(projectile);
            projectile.gameObject.SetActive(false);

            if (poolRoot == null)
            {
                return;
            }

            projectile.transform.SetParent(poolRoot, false);
            projectile.transform.localPosition = Vector3.zero;
            idle.Enqueue(projectile);
        }

        private GridKnockdownCannonProjectile Create()
        {
            GridKnockdownCannonProjectile projectile = Instantiate(projectilePrefab, poolRoot);
            projectile.gameObject.SetActive(false);
            projectile.SetPool(this);
            return projectile;
        }

        private void EnsureRoot()
        {
            if (poolRoot == null)
            {
                poolRoot = transform;
            }
        }
    }
}
