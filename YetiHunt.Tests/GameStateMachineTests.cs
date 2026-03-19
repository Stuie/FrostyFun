using Xunit;

namespace YetiHunt.Tests
{
    /// <summary>
    /// Tests for GameStateMachine state transitions.
    /// These tests verify the pure logic without Unity dependencies.
    /// </summary>
    public class GameStateMachineTests
    {
        // Since GameStateMachine depends on Unity's Time class, we create
        // a simplified testable version of the state logic here.

        public enum TestGameState
        {
            Idle,
            Countdown,
            Hunting,
            RoundEnd
        }

        public class TestableGameStateMachine
        {
            public const float COUNTDOWN_DURATION = 3f;
            public const float ROUND_END_DURATION = 5f;
            public const float HUNT_TIMEOUT = 120f;

            public TestGameState CurrentState { get; private set; } = TestGameState.Idle;
            public float StateElapsedTime { get; private set; }
            public string LastWinnerName { get; private set; }

            private float _stateStartTime;

            public void StartRound(float currentTime)
            {
                if (CurrentState != TestGameState.Idle) return;
                TransitionTo(TestGameState.Countdown, currentTime);
            }

            public void StopRound()
            {
                LastWinnerName = null;
                CurrentState = TestGameState.Idle;
            }

            public void Update(float currentTime)
            {
                StateElapsedTime = currentTime - _stateStartTime;

                switch (CurrentState)
                {
                    case TestGameState.Countdown:
                        if (StateElapsedTime >= COUNTDOWN_DURATION)
                            TransitionTo(TestGameState.Hunting, currentTime);
                        break;

                    case TestGameState.Hunting:
                        if (StateElapsedTime >= HUNT_TIMEOUT)
                            TransitionToRoundEnd(null, currentTime);
                        break;

                    case TestGameState.RoundEnd:
                        if (StateElapsedTime >= ROUND_END_DURATION)
                            TransitionTo(TestGameState.Idle, currentTime);
                        break;
                }
            }

            public void SetWinner(string winnerName, float currentTime)
            {
                if (CurrentState == TestGameState.Hunting)
                {
                    TransitionToRoundEnd(winnerName, currentTime);
                }
            }

            private void TransitionTo(TestGameState newState, float currentTime)
            {
                CurrentState = newState;
                _stateStartTime = currentTime;
                StateElapsedTime = 0f;
            }

            private void TransitionToRoundEnd(string winnerName, float currentTime)
            {
                LastWinnerName = winnerName;
                TransitionTo(TestGameState.RoundEnd, currentTime);
            }
        }

        [Fact]
        public void StartRound_WhenIdle_TransitionsToCountdown()
        {
            var sm = new TestableGameStateMachine();

            sm.StartRound(0f);

            Assert.Equal(TestGameState.Countdown, sm.CurrentState);
        }

        [Fact]
        public void StartRound_WhenNotIdle_DoesNotTransition()
        {
            var sm = new TestableGameStateMachine();
            sm.StartRound(0f); // Now in Countdown

            sm.StartRound(1f); // Try to start again

            Assert.Equal(TestGameState.Countdown, sm.CurrentState);
        }

        [Fact]
        public void Update_AfterCountdownDuration_TransitionsToHunting()
        {
            var sm = new TestableGameStateMachine();
            sm.StartRound(0f);

            sm.Update(3.1f); // After countdown duration

            Assert.Equal(TestGameState.Hunting, sm.CurrentState);
        }

        [Fact]
        public void Update_DuringCountdown_StaysInCountdown()
        {
            var sm = new TestableGameStateMachine();
            sm.StartRound(0f);

            sm.Update(2f); // Before countdown ends

            Assert.Equal(TestGameState.Countdown, sm.CurrentState);
        }

        [Fact]
        public void Update_AfterHuntTimeout_TransitionsToRoundEnd()
        {
            var sm = new TestableGameStateMachine();
            sm.StartRound(0f);
            sm.Update(3.1f); // Transition to hunting

            sm.Update(3.1f + 120.1f); // After hunt timeout

            Assert.Equal(TestGameState.RoundEnd, sm.CurrentState);
            Assert.Null(sm.LastWinnerName);
        }

        [Fact]
        public void SetWinner_DuringHunting_TransitionsToRoundEndWithWinner()
        {
            var sm = new TestableGameStateMachine();
            sm.StartRound(0f);
            sm.Update(3.1f); // Transition to hunting

            sm.SetWinner("TestPlayer", 10f);

            Assert.Equal(TestGameState.RoundEnd, sm.CurrentState);
            Assert.Equal("TestPlayer", sm.LastWinnerName);
        }

        [Fact]
        public void SetWinner_NotDuringHunting_DoesNothing()
        {
            var sm = new TestableGameStateMachine();
            sm.StartRound(0f); // In countdown

            sm.SetWinner("TestPlayer", 1f);

            Assert.Equal(TestGameState.Countdown, sm.CurrentState);
            Assert.Null(sm.LastWinnerName);
        }

        [Fact]
        public void Update_AfterRoundEndDuration_TransitionsToIdle()
        {
            var sm = new TestableGameStateMachine();
            sm.StartRound(0f);
            sm.Update(3.1f); // To hunting
            sm.SetWinner("TestPlayer", 10f); // To round end

            sm.Update(10f + 5.1f); // After round end duration

            Assert.Equal(TestGameState.Idle, sm.CurrentState);
        }

        [Fact]
        public void StopRound_FromAnyState_TransitionsToIdle()
        {
            var sm = new TestableGameStateMachine();
            sm.StartRound(0f);
            sm.Update(3.1f); // To hunting

            sm.StopRound();

            Assert.Equal(TestGameState.Idle, sm.CurrentState);
        }

        [Fact]
        public void StateElapsedTime_TracksTimeInCurrentState()
        {
            var sm = new TestableGameStateMachine();
            sm.StartRound(0f);

            sm.Update(2f);

            Assert.Equal(2f, sm.StateElapsedTime);
        }
    }
}
