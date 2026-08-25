using System.Collections.Generic;
using UnityEngine;

namespace GameJam.Gameplay.Wall
{
    /// <summary>
    /// Keeps a warm set of shattered-block instances and a hard ceiling on how much debris may
    /// be alive at once.
    ///
    /// Breaking a block used to instantiate eight to twelve rigidbodies on the spot, and a wall
    /// coming apart breaks several blocks in the same frame. Renting from a queue moves that
    /// cost to load time. The ceiling matters just as much: without one, a good shot into a
    /// large map leaves hundreds of chunks solving contacts long after the moment has passed, so
    /// a new burst quietly retires the oldest one still on screen.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ShatteredBlockPool : MonoBehaviour
    {
        public const string PoolObjectName = "ShatteredBlockPool";

        [Tooltip("Read at run start to find every block's debris prefab, so the queues are warm "
                 + "before the first shot rather than filling during the first cascade.")]
        [SerializeField] private BlockDatabase blockDatabase;

        [Tooltip("Instances built up front per debris prefab.")]
        [SerializeField] private int warmPerType = 4;

        [Tooltip("Bursts allowed on screen at once. A new one retires the oldest.")]
        [SerializeField] private int maxActiveSessions = 12;

        [Tooltip("Chunks allowed on screen at once, across every burst. The real budget: twelve "
                 + "bursts of twelve pieces is a very different scene from twelve of four.")]
        [SerializeField] private int maxActiveChunks = 96;

        private static ShatteredBlockPool instance;

        private readonly Dictionary<GameObject, Pooled> byPrefab = new Dictionary<GameObject, Pooled>();

        // Insertion order is activation order, so the oldest burst is always at index 0. That is
        // the whole reason this is a list and not a set: eviction has to be by age, and age must
        // not come from Time.time, which a pause or a slowed timescale would distort.
        private readonly List<ShatteredBlock> active = new List<ShatteredBlock>();
        private readonly List<int> activeChunkCounts = new List<int>();

        private int activeChunks;
        private Transform poolRoot;

        /// <summary>Bursts currently on screen. Read by tests and by the profiler overlay.</summary>
        public int ActiveSessionCount => active.Count;

        /// <summary>Chunks currently on screen, across every burst.</summary>
        public int ActiveChunkCount => activeChunks;

        private sealed class Pooled
        {
            public readonly Queue<ShatteredBlock> Idle = new Queue<ShatteredBlock>();
            public GameObject Prefab;
            public int ChunkCount;
            public bool OverflowLogged;
        }

        /// <summary>
        /// Warms the queues for every block the database knows about. Called at run start; safe
        /// to call again, which is what happens when the player retries a map.
        /// </summary>
        public static void Initialize(BlockDatabase database)
        {
            ShatteredBlockPool pool = EnsureInstance();
            if (pool == null)
            {
                return;
            }

            if (database != null)
            {
                pool.blockDatabase = database;
            }

            DebrisPhysicsProfile.Init();
            pool.Prewarm();
        }

        /// <summary>
        /// Hands out a burst, retiring older ones first if this one would not fit under the cap.
        /// Null only when there is no debris prefab to rent, which is a block with no debris.
        /// </summary>
        public static ShatteredBlock Rent(
            GameObject prefab,
            Vector3 position,
            Quaternion rotation,
            Transform parent)
        {
            if (prefab == null)
            {
                return null;
            }

            ShatteredBlockPool pool = EnsureInstance();
            return pool != null ? pool.RentInternal(prefab, position, rotation, parent) : null;
        }

        /// <summary>
        /// Parks every burst that is still on screen. Called before the map is cleared: active
        /// debris hangs under the generated root, which is about to be emptied, and a pooled
        /// instance destroyed with it is an instance the pool thinks it still owns.
        /// </summary>
        public static void ReturnAll()
        {
            // Deliberately does not create a pool: this runs on teardown paths that may never
            // have broken a block.
            if (instance == null)
            {
                return;
            }

            instance.ReturnAllInternal();
        }

        /// <summary>Puts one burst back, from the burst itself once it has shrunk away.</summary>
        public void Return(ShatteredBlock debris)
        {
            if (debris == null)
            {
                return;
            }

            int index = active.IndexOf(debris);
            if (index >= 0)
            {
                ReleaseAt(index);
                return;
            }

            // Never registered - warmed but never rented, or already returned. Park it anyway so
            // it cannot be left switched on somewhere in the hierarchy.
            Park(debris);
        }

        /// <summary>
        /// Drops a burst that was destroyed out from under the pool - a scene unload, or a clear
        /// that beat the teardown to it. Keeps the cap's bookkeeping honest.
        /// </summary>
        internal void Forget(ShatteredBlock debris)
        {
            int index = active.IndexOf(debris);
            if (index < 0)
            {
                return;
            }

            activeChunks -= activeChunkCounts[index];
            active.RemoveAt(index);
            activeChunkCounts.RemoveAt(index);
            if (activeChunks < 0)
            {
                activeChunks = 0;
            }
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            poolRoot = transform;
            DebrisPhysicsProfile.Init();
        }

        private void OnDestroy()
        {
            if (instance == this)
            {
                instance = null;
            }
        }

        private static ShatteredBlockPool EnsureInstance()
        {
            if (instance != null)
            {
                return instance;
            }

            instance = FindFirstObjectByType<ShatteredBlockPool>(FindObjectsInactive.Include);
            if (instance != null)
            {
                return instance;
            }

            // Nothing in the scene wired one up, and a block is breaking regardless. A pool made
            // here uses the serialized defaults, which are the tuned ones.
            GameObject host = new GameObject(PoolObjectName);
            instance = host.AddComponent<ShatteredBlockPool>();
            return instance;
        }

        private ShatteredBlock RentInternal(
            GameObject prefab,
            Vector3 position,
            Quaternion rotation,
            Transform parent)
        {
            Pooled pooled = ResolvePooled(prefab);
            MakeRoomFor(pooled.ChunkCount);

            ShatteredBlock debris = null;
            while (pooled.Idle.Count > 0 && debris == null)
            {
                // A queued instance can have been destroyed by a scene teardown, so the queue is
                // drained rather than trusted.
                debris = pooled.Idle.Dequeue();
            }

            if (debris == null)
            {
                debris = CreateInstance(pooled);
                if (debris == null)
                {
                    return null;
                }

#if DEVELOPMENT_BUILD || UNITY_EDITOR
                if (!pooled.OverflowLogged)
                {
                    pooled.OverflowLogged = true;
                    Debug.Log(
                        $"Debris pool for {prefab.name} ran dry and grew by one. Raise the warm "
                        + $"count above {warmPerType} if this keeps happening.",
                        this);
                }
#endif
            }

            Transform debrisTransform = debris.transform;
            debrisTransform.SetParent(parent, false);
            debrisTransform.SetPositionAndRotation(position, rotation);

            debris.ResetChunks();
            debris.gameObject.SetActive(true);

            active.Add(debris);
            activeChunkCounts.Add(pooled.ChunkCount);
            activeChunks += pooled.ChunkCount;

            GameJam.Diagnostics.RuntimeProfileLogger.Count("debris_rented");
            GameJam.Diagnostics.RuntimeProfileLogger.Peak("debris_sessions", active.Count);
            GameJam.Diagnostics.RuntimeProfileLogger.Peak("debris_chunks", activeChunks);
            return debris;
        }

        /// <summary>
        /// Retires the oldest bursts until this one fits. Both caps are checked: a run of small
        /// bursts hits the session count first, a run of large ones hits the chunk count first.
        /// </summary>
        private void MakeRoomFor(int incomingChunks)
        {
            while (active.Count > 0
                   && (active.Count >= Mathf.Max(1, maxActiveSessions)
                       || activeChunks + incomingChunks > Mathf.Max(1, maxActiveChunks)))
            {
                ReleaseAt(0);
            }
        }

        private void ReleaseAt(int index)
        {
            ShatteredBlock debris = active[index];
            activeChunks -= activeChunkCounts[index];
            active.RemoveAt(index);
            activeChunkCounts.RemoveAt(index);
            if (activeChunks < 0)
            {
                activeChunks = 0;
            }

            Park(debris);
        }

        private void ReturnAllInternal()
        {
            while (active.Count > 0)
            {
                ReleaseAt(0);
            }
        }

        /// <summary>Stops a burst and puts it back under the pool, out of the map's way.</summary>
        private void Park(ShatteredBlock debris)
        {
            if (debris == null)
            {
                return;
            }

            // Already parked. Guarded because enqueuing the same instance twice would hand it out
            // to two breaks at once, which reads as one of them silently losing its debris.
            if (!debris.gameObject.activeSelf)
            {
                return;
            }

            debris.SettleForPool();
            debris.gameObject.SetActive(false);

            if (poolRoot != null)
            {
                debris.transform.SetParent(poolRoot, false);
            }

            Pooled pooled = ResolvePooledFor(debris);
            pooled?.Idle.Enqueue(debris);
        }

        private void Prewarm()
        {
            if (blockDatabase == null)
            {
                return;
            }

            IReadOnlyList<BlockDatabase.Entry> entries = blockDatabase.Entries;
            for (int i = 0; i < entries.Count; i++)
            {
                GameObject blockPrefab = entries[i].prefab;
                if (blockPrefab == null || !blockPrefab.TryGetComponent(out BreakableBlock breakable))
                {
                    continue;
                }

                GameObject debrisPrefab = breakable.ShatteredPrefab;
                if (debrisPrefab == null)
                {
                    continue;
                }

                Pooled pooled = ResolvePooled(debrisPrefab);
                while (pooled.Idle.Count < Mathf.Max(0, warmPerType))
                {
                    ShatteredBlock warmed = CreateInstance(pooled);
                    if (warmed == null)
                    {
                        break;
                    }

                    pooled.Idle.Enqueue(warmed);
                }
            }
        }

        private Pooled ResolvePooled(GameObject prefab)
        {
            if (byPrefab.TryGetValue(prefab, out Pooled pooled))
            {
                return pooled;
            }

            pooled = new Pooled
            {
                Prefab = prefab,

                // Read off the prefab rather than off an instance: the cap has to be checked
                // before anything is rented, which is before there is an instance to count.
                ChunkCount = Mathf.Max(1, prefab.transform.childCount),
            };

            byPrefab[prefab] = pooled;
            return pooled;
        }

        /// <summary>
        /// Which queue an instance belongs to. Instances are only ever made here, and each one
        /// remembers the prefab it came from, so this is a straight lookup.
        /// </summary>
        private Pooled ResolvePooledFor(ShatteredBlock debris)
        {
            GameObject prefab = debris.SourcePrefab;
            return prefab != null && byPrefab.TryGetValue(prefab, out Pooled pooled) ? pooled : null;
        }

        private ShatteredBlock CreateInstance(Pooled pooled)
        {
            if (poolRoot == null)
            {
                poolRoot = transform;
            }

            GameJam.Diagnostics.RuntimeProfileLogger.Count("debris_instantiated");
            GameObject instanceObject = Instantiate(pooled.Prefab, poolRoot);
            if (!instanceObject.TryGetComponent(out ShatteredBlock debris))
            {
                Debug.LogError(
                    $"{pooled.Prefab.name} has no {nameof(ShatteredBlock)} on its root, so it "
                    + "cannot be pooled.",
                    this);
                Destroy(instanceObject);
                return null;
            }

            instanceObject.SetActive(false);
            debris.SetOwner(this, pooled.Prefab);
            return debris;
        }

#if UNITY_EDITOR
        /// <summary>
        /// Statics survive entering play mode when domain reloading is switched off, and a stale
        /// instance from the last session points at a destroyed object.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            instance = null;
        }
#endif
    }
}
