using System.Collections.Generic;
using System.Diagnostics;
using Unity.Profiling;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace GameJam.Diagnostics
{
    /// <summary>
    /// Prints the numbers Task Brief 03 is judged on to the device log, so a run on a real phone
    /// can be measured without anyone reading them off a profiler window.
    ///
    /// Every line starts with <see cref="Prefix"/> and is a flat list of key=value pairs, which
    /// makes the whole session greppable out of logcat:
    ///
    ///   adb logcat -s Unity:* | grep FTPROF
    ///
    /// Profiler counters only report in a development build, which is what the brief asks for
    /// anyway. In a release build every entry point compiles away at the call site, so the cost
    /// of leaving the hooks in the game code is nothing.
    /// </summary>
    public sealed class RuntimeProfileLogger : MonoBehaviour
    {
        public const string Prefix = "FTPROF";

        private const float DefaultReportInterval = 1f;
        private const double NanosecondsPerMillisecond = 1e6;

        /// <summary>
        /// One stretch of time worth reporting on its own: a map build, a cascade, an idle spell.
        /// Windows nest so a phase inside a phase still reports separately.
        /// </summary>
        private sealed class Window
        {
            public string Name;

            /// <summary>
            /// Wall clock, not frames. A map build runs to completion inside a single call, so
            /// Update never ticks while it is open and a frame count would read zero - which is
            /// exactly how the first attempt at this managed to swallow the build spike it
            /// existed to measure.
            /// </summary>
            public readonly Stopwatch Clock = Stopwatch.StartNew();

            public int Frames;
            public float WorstFrameMs;
            public float TotalSeconds;
            public long GcAllocated;
            public long WorstPhysicsNs;
            public long TotalPhysicsNs;
            public long WorstDrawCalls;
            public long WorstSetPassCalls;
            public long WorstBatches;
            public long WorstActiveBodies;
            public readonly Dictionary<string, int> Counts = new Dictionary<string, int>();

            /// <summary>Highest value seen, for things judged on their worst moment.</summary>
            public readonly Dictionary<string, int> Peaks = new Dictionary<string, int>();
        }

        private static RuntimeProfileLogger instance;

        private readonly List<Window> phases = new List<Window>();

        /// <summary>
        /// Built at field level rather than in Awake. A duplicate instance is told to destroy
        /// itself, but Destroy is deferred to the end of the frame and Update runs first, so an
        /// Awake that bailed early used to leave this null and throw once a frame until the
        /// object actually went away.
        /// </summary>
        private Window rolling = new Window { Name = "rolling" };

        private ProfilerRecorder gcAllocated;
        private ProfilerRecorder drawCalls;
        private ProfilerRecorder setPassCalls;
        private ProfilerRecorder batches;
        private ProfilerRecorder physicsProcessing;
        private ProfilerRecorder activeBodies;

        private float sinceReport;
        private float reportInterval = DefaultReportInterval;

        /// <summary>
        /// Creates itself, so measuring a build is a matter of installing it rather than
        /// remembering to drag a component into the scene first.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void AutoCreate()
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (instance != null)
            {
                return;
            }

            GameObject host = new GameObject(nameof(RuntimeProfileLogger));
            DontDestroyOnLoad(host);
            instance = host.AddComponent<RuntimeProfileLogger>();
#endif
        }

        /// <summary>
        /// Opens a named stretch of time. Anything counted or measured until the matching
        /// <see cref="EndPhase"/> is reported against it as well as against the rolling window.
        /// </summary>
        [Conditional("DEVELOPMENT_BUILD")]
        [Conditional("UNITY_EDITOR")]
        public static void BeginPhase(string phaseName)
        {
            if (instance == null)
            {
                return;
            }

            instance.phases.Add(new Window { Name = phaseName });
            instance.phases[instance.phases.Count - 1].Clock.Restart();
            Debug.Log($"{Prefix}|phase_begin|name={phaseName}|mesh_count={CountLoadedMeshes()}");
        }

        /// <summary>Closes the innermost open phase and reports it.</summary>
        [Conditional("DEVELOPMENT_BUILD")]
        [Conditional("UNITY_EDITOR")]
        public static void EndPhase()
        {
            if (instance == null || instance.phases.Count == 0)
            {
                return;
            }

            int last = instance.phases.Count - 1;
            Window window = instance.phases[last];
            instance.phases.RemoveAt(last);
            instance.Report("phase_end", window, includeMeshCount: true);
        }

        /// <summary>
        /// Tallies something worth counting rather than timing: components added at runtime,
        /// objects instantiated, builds started. Reported with whatever window is open.
        /// </summary>
        [Conditional("DEVELOPMENT_BUILD")]
        [Conditional("UNITY_EDITOR")]
        public static void Count(string key, int amount = 1)
        {
            if (instance == null)
            {
                return;
            }

            instance.Add(instance.rolling, key, amount);
            for (int i = 0; i < instance.phases.Count; i++)
            {
                instance.Add(instance.phases[i], key, amount);
            }
        }

        /// <summary>
        /// Records a high-water mark rather than a running total: a cap is judged on the most
        /// that was ever alive at once, which a sum of arrivals cannot tell you.
        /// </summary>
        [Conditional("DEVELOPMENT_BUILD")]
        [Conditional("UNITY_EDITOR")]
        public static void Peak(string key, int value)
        {
            if (instance == null)
            {
                return;
            }

            instance.RaisePeak(instance.rolling, key, value);
            for (int i = 0; i < instance.phases.Count; i++)
            {
                instance.RaisePeak(instance.phases[i], key, value);
            }
        }

        /// <summary>A one-off note in the same greppable format.</summary>
        [Conditional("DEVELOPMENT_BUILD")]
        [Conditional("UNITY_EDITOR")]
        public static void Mark(string message)
        {
            Debug.Log($"{Prefix}|mark|{message}");
        }

        /// <summary>
        /// Live mesh count, which is how the brief's "no leak across retries" check is read.
        /// Walks every loaded mesh, so it is only ever called at a phase boundary.
        /// </summary>
        public static int CountLoadedMeshes()
        {
            return Resources.FindObjectsOfTypeAll<Mesh>().Length;
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            StartRecorders();
            LogDeviceHeader();
        }

        private void OnDestroy()
        {
            gcAllocated.Dispose();
            drawCalls.Dispose();
            setPassCalls.Dispose();
            batches.Dispose();
            physicsProcessing.Dispose();
            activeBodies.Dispose();

            if (instance == this)
            {
                instance = null;
            }
        }

        /// <summary>
        /// Started defensively: which counters exist varies by platform and graphics backend, and
        /// a missing one should cost that column rather than the whole log.
        /// </summary>
        private void StartRecorders()
        {
            gcAllocated = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            drawCalls = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            setPassCalls = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
            physicsProcessing = StartFirstAvailable(
                ProfilerCategory.Physics,
                "Physics.Processing",
                "PhysicsFixedUpdate",
                "FixedUpdate.PhysicsFixedUpdate",
                "Physics.Simulate");

            activeBodies = StartFirstAvailable(
                ProfilerCategory.Physics,
                "Active Dynamic Bodies",
                "Active Rigidbodies",
                "Dynamic Bodies");
        }

        /// <summary>
        /// Counter and marker names differ by platform and physics backend - Physics.Processing
        /// resolves in the editor but not in an Android player - so each candidate is tried in
        /// turn and the first one that attaches wins. A column that cannot be filled is worth
        /// losing on its own; it is not worth losing the log for.
        /// </summary>
        private static ProfilerRecorder StartFirstAvailable(ProfilerCategory category, params string[] names)
        {
            for (int i = 0; i < names.Length; i++)
            {
                ProfilerRecorder candidate = ProfilerRecorder.StartNew(category, names[i]);
                if (candidate.Valid)
                {
                    return candidate;
                }

                candidate.Dispose();
            }

            return default;
        }

        private void LogDeviceHeader()
        {
            Debug.Log(
                $"{Prefix}|device"
                + $"|model={SystemInfo.deviceModel}"
                + $"|gpu={SystemInfo.graphicsDeviceName}"
                + $"|api={SystemInfo.graphicsDeviceType}"
                + $"|cpu={SystemInfo.processorType}"
                + $"|cores={SystemInfo.processorCount}"
                + $"|ram_mb={SystemInfo.systemMemorySize}"
                + $"|screen={Screen.width}x{Screen.height}"
                + $"|unity={Application.unityVersion}"
                + $"|target_fps={Application.targetFrameRate}"
                + $"|vsync={QualitySettings.vSyncCount}"
                + $"|quality={QualitySettings.names[QualitySettings.GetQualityLevel()]}");

            Debug.Log(
                $"{Prefix}|recorders"
                + $"|gc_alloc={gcAllocated.Valid}"
                + $"|draw_calls={drawCalls.Valid}"
                + $"|setpass={setPassCalls.Valid}"
                + $"|batches={batches.Valid}"
                + $"|physics={physicsProcessing.Valid}"
                + $"|active_bodies={activeBodies.Valid}");
        }

        private void Update()
        {
            float frameMs = Time.unscaledDeltaTime * 1000f;
            long gc = gcAllocated.Valid ? gcAllocated.LastValue : 0L;
            long physicsNs = physicsProcessing.Valid ? physicsProcessing.LastValue : 0L;

            Sample(rolling, frameMs, gc, physicsNs);
            for (int i = 0; i < phases.Count; i++)
            {
                Sample(phases[i], frameMs, gc, physicsNs);
            }

            sinceReport += Time.unscaledDeltaTime;
            if (sinceReport < reportInterval)
            {
                return;
            }

            sinceReport = 0f;
            Report("window", rolling, includeMeshCount: false);
            rolling = new Window { Name = "rolling" };
        }

        private void Sample(Window window, float frameMs, long gc, long physicsNs)
        {
            if (window == null)
            {
                return;
            }

            window.Frames++;
            window.TotalSeconds += Time.unscaledDeltaTime;
            window.GcAllocated += gc;
            window.TotalPhysicsNs += physicsNs;

            if (frameMs > window.WorstFrameMs)
            {
                window.WorstFrameMs = frameMs;
            }

            if (physicsNs > window.WorstPhysicsNs)
            {
                window.WorstPhysicsNs = physicsNs;
            }

            if (drawCalls.Valid && drawCalls.LastValue > window.WorstDrawCalls)
            {
                window.WorstDrawCalls = drawCalls.LastValue;
            }

            if (setPassCalls.Valid && setPassCalls.LastValue > window.WorstSetPassCalls)
            {
                window.WorstSetPassCalls = setPassCalls.LastValue;
            }

            if (batches.Valid && batches.LastValue > window.WorstBatches)
            {
                window.WorstBatches = batches.LastValue;
            }

            if (activeBodies.Valid && activeBodies.LastValue > window.WorstActiveBodies)
            {
                window.WorstActiveBodies = activeBodies.LastValue;
            }
        }

        private void RaisePeak(Window window, string key, int value)
        {
            if (window == null)
            {
                return;
            }

            if (!window.Peaks.TryGetValue(key, out int current) || value > current)
            {
                window.Peaks[key] = value;
            }
        }

        private void Add(Window window, string key, int amount)
        {
            if (window == null)
            {
                return;
            }

            window.Counts.TryGetValue(key, out int current);
            window.Counts[key] = current + amount;
        }

        /// <summary>
        /// A quiet rolling window is not worth a line. Phases always report, because the absence
        /// of a build is itself the thing being checked.
        /// </summary>
        private void Report(string kind, Window window, bool includeMeshCount)
        {
            if (window.Frames == 0 && kind == "window")
            {
                return;
            }

            bool idle = window.WorstFrameMs < 25f
                        && window.GcAllocated == 0
                        && window.Counts.Count == 0
                        && window.Peaks.Count == 0;
            if (kind == "window" && idle)
            {
                return;
            }

            double elapsedMs = window.Clock.Elapsed.TotalMilliseconds;
            float averageMs = window.Frames > 0 && window.TotalSeconds > 0f
                ? (window.TotalSeconds * 1000f) / window.Frames
                : 0f;

            string line =
                $"{Prefix}|{kind}"
                + $"|name={window.Name}"
                + $"|frames={window.Frames}"
                + $"|elapsed_ms={elapsedMs:0.##}"
                + $"|seconds={window.TotalSeconds:0.###}"
                + $"|worst_ms={window.WorstFrameMs:0.##}"
                + $"|avg_ms={averageMs:0.##}"
                + $"|fps={(averageMs > 0f ? 1000f / averageMs : 0f):0.#}"
                + $"|gc_bytes={window.GcAllocated}"
                + $"|physics_worst_ms={window.WorstPhysicsNs / NanosecondsPerMillisecond:0.###}"
                + $"|physics_total_ms={window.TotalPhysicsNs / NanosecondsPerMillisecond:0.###}"
                + $"|draw_calls={window.WorstDrawCalls}"
                + $"|setpass={window.WorstSetPassCalls}"
                + $"|batches={window.WorstBatches}"
                + $"|active_bodies={window.WorstActiveBodies}";

            foreach (KeyValuePair<string, int> count in window.Counts)
            {
                line += $"|{count.Key}={count.Value}";
            }

            foreach (KeyValuePair<string, int> peak in window.Peaks)
            {
                line += $"|peak_{peak.Key}={peak.Value}";
            }

            if (includeMeshCount)
            {
                line += $"|mesh_count={CountLoadedMeshes()}";
            }

            Debug.Log(line);
        }
    }
}
