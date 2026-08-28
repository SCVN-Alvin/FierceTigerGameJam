using UnityEngine;

namespace GameJam.Config
{
    /// <summary>
    /// How long the splash screen is held before the game moves on.
    ///
    /// A config rather than a field on the view, because the number is a pacing decision and not
    /// a property of the screen: the time is fake today, and the moment there is something real
    /// behind the bar this becomes the minimum the splash is shown for rather than the whole of
    /// it. Nobody editing the screen's layout should have to know that.
    /// </summary>
    [CreateAssetMenu(menuName = "GameJam/Loading Config", fileName = "LoadingConfig")]
    public sealed class LoadingConfig : ScriptableObject
    {
        [Tooltip("How long the fake load takes. There is nothing real behind the bar yet; when there "
                 + "is, this becomes the minimum time the splash is shown.")]
        [Min(0f)] public float fakeLoadingSeconds = 2.5f;
    }
}
