using System.Collections.Generic;
using UnityEngine;

namespace GameJam.Gameplay.Cannon
{
    /// <summary>
    /// A warm queue of cannon balls, sized to the shots a run allows. A shot used to instantiate
    /// a rigidbody with a collider in the frame the player tapped, which is the one frame in the
    /// game where nothing else should be competing for time.
    ///
    /// Keyed by prefab, because the player brings a mix of ammunition and each kind has its own
    /// projectile: one shared queue would hand a rock back out wearing a cannon ball's mesh.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ProjectilePool : MonoBehaviour
    {
        [Tooltip("What is fired when the caller names no prefab of its own. The same prefab the "
                 + "fire controller would have instantiated.")]
        [SerializeField] private GridKnockdownCannonProjectile projectilePrefab;

        [Tooltip("Instances built up front for one kind of ammunition, used when a warm is asked "
                 + "for without a size. Raised to the run's bullet budget when there is one, "
                 + "since that is the most shots that can ever be in the air at once.")]
        [SerializeField] private int poolSize = 10;

        /// <summary>
        /// Idle instances filed under the prefab they were made from. A shot only ever comes out
        /// of the stack for the kind that was asked for.
        /// </summary>
        private readonly Dictionary<GridKnockdownCannonProjectile, Stack<GridKnockdownCannonProjectile>> idleByPrefab =
            new Dictionary<GridKnockdownCannonProjectile, Stack<GridKnockdownCannonProjectile>>();

        /// <summary>
        /// Which prefab each live instance came from. Read on return rather than trusting the
        /// currently selected ammunition: a rock fired before the player switched types must go
        /// back in the rock stack however long it stays in the air.
        /// </summary>
        private readonly Dictionary<GridKnockdownCannonProjectile, GridKnockdownCannonProjectile> originByInstance =
            new Dictionary<GridKnockdownCannonProjectile, GridKnockdownCannonProjectile>();

        /// <summary>What is in the air, so a run ending mid-flight can call it all back.</summary>
        private readonly List<GridKnockdownCannonProjectile> inFlight =
            new List<GridKnockdownCannonProjectile>();

        private Transform poolRoot;

        public bool HasPrefab => projectilePrefab != null;

        /// <summary>
        /// Warms the serialized fallback prefab. Kept so callers that know of only one kind of
        /// ammunition still work.
        /// </summary>
        public void Warm(int size)
        {
            Warm(projectilePrefab, size);
        }

        /// <summary>
        /// Warms one kind to <paramref name="size"/>, keeping whatever is already queued. Called
        /// again on every run, which is why it tops up rather than rebuilding.
        /// </summary>
        public void Warm(GridKnockdownCannonProjectile prefab, int size)
        {
            prefab = Resolve(prefab);
            if (prefab == null)
            {
                return;
            }

            EnsureRoot();
            Stack<GridKnockdownCannonProjectile> idle = GetIdleStack(prefab);
            int wanted = Mathf.Max(1, size > 0 ? size : poolSize);

            while (idle.Count < wanted)
            {
                GridKnockdownCannonProjectile projectile = Create(prefab);
                if (projectile == null)
                {
                    return;
                }

                idle.Push(projectile);
            }
        }

        /// <summary>Rents from the serialized fallback prefab, for callers with only one kind.</summary>
        public GridKnockdownCannonProjectile Rent(Vector3 position, Quaternion rotation, Transform parent)
        {
            return Rent(projectilePrefab, position, rotation, parent);
        }

        /// <summary>
        /// A ball of one kind ready to be launched, growing that kind's queue if the run somehow
        /// outruns its budget. Null only when neither a prefab nor the serialized fallback is
        /// available, which is the fire controller's cue to fall back to its built-in sphere.
        /// </summary>
        public GridKnockdownCannonProjectile Rent(
            GridKnockdownCannonProjectile prefab,
            Vector3 position,
            Quaternion rotation,
            Transform parent)
        {
            prefab = Resolve(prefab);
            if (prefab == null)
            {
                return null;
            }

            EnsureRoot();
            Stack<GridKnockdownCannonProjectile> idle = GetIdleStack(prefab);

            GridKnockdownCannonProjectile projectile = null;
            while (idle.Count > 0 && projectile == null)
            {
                // A queued instance can have been destroyed by a scene teardown.
                projectile = idle.Pop();
            }

            if (projectile == null)
            {
                projectile = Create(prefab);
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

            // An instance this pool never made has nowhere to be filed; it is left parked and
            // disabled rather than queued under a prefab it is not a copy of.
            if (originByInstance.TryGetValue(projectile, out GridKnockdownCannonProjectile prefab))
            {
                GetIdleStack(prefab).Push(projectile);
            }
        }

        /// <summary>
        /// Standing in the serialized prefab for a caller that named none keeps the one-kind
        /// behaviour this pool had before it was keyed, rather than turning an unwired caller
        /// into a silent refusal to fire.
        /// </summary>
        private GridKnockdownCannonProjectile Resolve(GridKnockdownCannonProjectile prefab)
        {
            return prefab != null ? prefab : projectilePrefab;
        }

        private Stack<GridKnockdownCannonProjectile> GetIdleStack(GridKnockdownCannonProjectile prefab)
        {
            if (!idleByPrefab.TryGetValue(prefab, out Stack<GridKnockdownCannonProjectile> idle))
            {
                idle = new Stack<GridKnockdownCannonProjectile>();
                idleByPrefab.Add(prefab, idle);
            }

            return idle;
        }

        private GridKnockdownCannonProjectile Create(GridKnockdownCannonProjectile prefab)
        {
            GridKnockdownCannonProjectile projectile = Instantiate(prefab, poolRoot);
            projectile.gameObject.SetActive(false);
            projectile.SetPool(this);
            originByInstance[projectile] = prefab;
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
