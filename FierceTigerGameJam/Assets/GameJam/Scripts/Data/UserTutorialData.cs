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
    }
}
