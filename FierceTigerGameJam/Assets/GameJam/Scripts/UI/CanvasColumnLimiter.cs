using UnityEngine;

namespace GameJam.UI
{
    /// <summary>
    /// Retired. Squeezing the interface into a 9:16 column on wide screens crammed the corner
    /// chips into the panel, so wide screens are full-stretch again and the chips keep their
    /// authored margins from the outer edges. The class stays only so scenes that still carry
    /// the component load cleanly; "Tools/Smashdown/Setup Adaptive Canvas" removes it and puts
    /// the offsets it wrote back to zero.
    /// </summary>
    public sealed class CanvasColumnLimiter : MonoBehaviour
    {
    }
}
