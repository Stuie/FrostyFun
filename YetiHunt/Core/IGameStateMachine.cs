using System;

namespace YetiHunt.Core
{
    /// <summary>
    /// Interface for the game state machine that controls round flow.
    /// </summary>
    public interface IGameStateMachine
    {
        GameState CurrentState { get; }
        float StateElapsedTime { get; }
        string LastWinnerName { get; }

        void StartRound();
        void StopRound();
        void Update(float deltaTime, float currentTime);
        void SetWinner(string winnerName);

        event Action<GameState, GameState> OnStateChanged;
        event Action OnHuntingStarted;
        event Action<string> OnRoundEnded;
    }
}
