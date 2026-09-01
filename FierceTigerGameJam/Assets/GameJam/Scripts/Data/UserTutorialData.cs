using System;

namespace GameJam.Data
{
    /// <summary>
    /// Whether the player has been through the tutorial.
    ///
    /// Its own record with its own key, the way the vehicle record is: a save written before the
    /// tutorial existed simply has nothing here and reads as not yet done, and one record failing
    /// to parse cannot cost the player any of the others.
    /// </summary>
    [Serializable]
    public sealed class UserTutorialData
    {
        /// <summary>Bumped when the shape of this record changes, so old saves can be migrated.</summary>
        public int version = 1;

        /// <summary>
        /// Set when the tutorial block is destroyed, never on the first shot: quitting
        /// mid-tutorial should repeat it, and firing once proves nothing was learned.
        /// </summary>
        public bool completed;

        /// <summary>The level-1 hold-and-drag hint has been answered with a real drag.</summary>
        public bool dragTaught;

        /// <summary>The one-time "1 FREE Double Shoot" popup has been used (first level after a
        /// cannon reaches level 2).</summary>
        public bool doubleShotIntroDone;

        /// <summary>Same for "1 FREE Triple Shoot" (first level after a cannon reaches level 3).</summary>
        public bool tripleShotIntroDone;
    }
}
