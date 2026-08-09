namespace GameJam.Gameplay.Cannon
{
    public enum AimRejectReason
    {
        None,
        NoCamera,
        MissingAimPivot,
        BehindCamera,
        TooLow,
        TooHigh,
        TooLeft,
        TooRight
    }
}
