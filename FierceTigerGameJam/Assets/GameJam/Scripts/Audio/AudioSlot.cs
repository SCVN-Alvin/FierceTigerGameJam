namespace GameJam.Audio
{
    /// <summary>
    /// Every sound the game asks for, by what it means rather than by which file plays it.
    ///
    /// Call sites name a slot, never a clip: swapping the file behind "a block shattered" is then
    /// an edit to one asset instead of a hunt through gameplay code, and a slot left empty is a
    /// silent event rather than a compile error.
    /// </summary>
    public enum AudioSlot
    {
        /// <summary>A shot leaves the cannon.</summary>
        Fire,

        /// <summary>The ball is accepted as having hit a block.</summary>
        BallImpact,

        /// <summary>The ball lands on the floor, once per flight.</summary>
        BallFall,

        HitBrick,
        HitConcrete,
        HitGlass,

        BreakBrick,
        BreakConcrete,
        BreakGlass,

        /// <summary>A result-screen star slamming onto its slot.</summary>
        StarLand,

        /// <summary>Any UI button.</summary>
        UiClick,

        /// <summary>A refused purchase, upgrade or continue.</summary>
        Denied,

        /// <summary>Gold granted or spent successfully.</summary>
        Coin,

        StageClear,
        StageFailed,

        /// <summary>Menu music. Looping; only <see cref="AudioService.PlayMusic"/> plays these.</summary>
        MusicTitle,

        /// <summary>Run music. Looping.</summary>
        MusicGame,
    }
}
