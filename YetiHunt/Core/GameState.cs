namespace YetiHunt.Core
{
    /// <summary>
    /// Game state machine states for YetiHunt rounds.
    /// </summary>
    public enum GameState
    {
        Idle,
        Countdown,
        Hunting,
        RoundEnd
    }
}
