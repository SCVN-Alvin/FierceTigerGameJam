using UnityEngine;

namespace GameJam.Config
{
    /// <summary>
    /// What a failed run costs to pick back up.
    ///
    /// A flat price rather than one that climbs with each continue: the player is deciding
    /// whether five more rounds are worth four thousand gold, and that is a clearer question when
    /// the answer does not change halfway through a run. Escalation can be added here later
    /// without any caller learning about it.
    /// </summary>
    [CreateAssetMenu(menuName = "GameJam/Lose Config", fileName = "LoseConfig")]
    public sealed class LoseConfig : ScriptableObject
    {
        [Tooltip("Gold taken for one continue. Flat: the same every time in a run.")]
        [Min(0)] public int continuePrice = 4000;

        [Tooltip("Rounds added by one continue, of the loaded ammunition. The fail screen's banner art "
                 + "says +5; change the art with this number.")]
        [Min(1)] public int continueAmmo = 5;
    }
}
