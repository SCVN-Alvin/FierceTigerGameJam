using UnityEngine;

namespace GameJam.Rendering
{
    /// <summary>
    /// Asks the platform for 60 frames a second.
    ///
    /// Without this the game ran at exactly 30. Unity's default <c>targetFrameRate</c> on Android
    /// is 30, and nothing in the project had ever set it: a device capture showed a dead-flat
    /// 33.1 ms frame time with the phone at 11% of its eight cores, 42 draw calls and no thermal
    /// throttling at all. That is not a game struggling to reach 30, it is a game held at 30 with
    /// most of the machine idle.
    ///
    /// vSyncCount is left alone. It is already 0 on both quality levels, and on Android
    /// targetFrameRate is only honoured while vSync is off - setting one without checking the
    /// other is how this silently becomes a no-op again.
    ///
    /// Run from <see cref="RuntimeInitializeOnLoadMethod"/> rather than a component so it cannot
    /// be lost to an unassigned inspector reference, which is exactly how PrepareForRun stopped
    /// being called.
    /// </summary>
    public static class ApplicationFrameRate
    {
        /// <summary>
        /// The frame rate to ask for. A request, not a promise: the platform caps it at the
        /// display's own rate, so a 90 Hz panel gives 60 and a 60 Hz panel also gives 60.
        /// </summary>
        public const int Target = 60;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Apply()
        {
            Application.targetFrameRate = Target;
        }
    }
}
