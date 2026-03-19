using System;
using YetiHunt.Infrastructure;

namespace YetiHunt.Core
{
    /// <summary>
    /// Manages game state transitions and timing for YetiHunt rounds.
    /// </summary>
    public class GameStateMachine : IGameStateMachine
    {
        public const float COUNTDOWN_DURATION = 3f;
        public const float ROUND_END_DURATION = 5f;
        public const float HUNT_TIMEOUT = 120f;

        private readonly IModLogger _logger;

        private GameState _currentState = GameState.Idle;
        private float _stateStartTime;
        private string _lastWinnerName;

        public GameState CurrentState => _currentState;
        public float StateElapsedTime { get; private set; }
        public string LastWinnerName => _lastWinnerName;

        public event Action<GameState, GameState> OnStateChanged;
        public event Action OnHuntingStarted;
        public event Action<string> OnRoundEnded;

        public GameStateMachine(IModLogger logger)
        {
            _logger = logger;
        }

        public void StartRound()
        {
            if (_currentState != GameState.Idle) return;

            _logger.Info("=== STARTING YETI HUNT ===");
            TransitionTo(GameState.Countdown);
        }

        public void StopRound()
        {
            _logger.Info("=== ROUND STOPPED ===");
            _lastWinnerName = null;
            TransitionTo(GameState.Idle);
        }

        public void Update(float deltaTime, float currentTime)
        {
            StateElapsedTime = currentTime - _stateStartTime;

            switch (_currentState)
            {
                case GameState.Countdown:
                    if (StateElapsedTime >= COUNTDOWN_DURATION)
                        TransitionToHunting(currentTime);
                    break;

                case GameState.Hunting:
                    if (StateElapsedTime >= HUNT_TIMEOUT)
                    {
                        _logger.Info("Hunt timed out!");
                        TransitionToRoundEnd(null, currentTime);
                    }
                    break;

                case GameState.RoundEnd:
                    if (StateElapsedTime >= ROUND_END_DURATION)
                        TransitionToIdle();
                    break;
            }
        }

        public void SetWinner(string winnerName)
        {
            if (_currentState == GameState.Hunting)
            {
                TransitionToRoundEnd(winnerName, _stateStartTime + StateElapsedTime);
            }
        }

        public void ResetForSceneChange()
        {
            if (_currentState != GameState.Idle)
            {
                _logger.Info("Scene changed - resetting");
                _currentState = GameState.Idle;
                _lastWinnerName = null;
            }
        }

        private void TransitionTo(GameState newState)
        {
            var oldState = _currentState;
            _currentState = newState;
            _stateStartTime = UnityEngine.Time.time;
            StateElapsedTime = 0f;
            OnStateChanged?.Invoke(oldState, newState);
        }

        private void TransitionToHunting(float currentTime)
        {
            _logger.Info("=== HUNT BEGINS! ===");
            TransitionTo(GameState.Hunting);
            OnHuntingStarted?.Invoke();
        }

        private void TransitionToRoundEnd(string winnerName, float currentTime)
        {
            if (winnerName != null)
                _logger.Info($"=== {winnerName} WINS! ===");
            else
                _logger.Info("=== NO WINNER ===");

            _lastWinnerName = winnerName;
            TransitionTo(GameState.RoundEnd);
            OnRoundEnded?.Invoke(winnerName);
        }

        private void TransitionToIdle()
        {
            _logger.Info("=== ROUND COMPLETE ===");
            _lastWinnerName = null;
            TransitionTo(GameState.Idle);
        }
    }
}
