using Xunit;

namespace YetiHunt.Tests
{
    /// <summary>
    /// Tests for yeti behavior state machine logic.
    /// These tests verify the pure state transition logic.
    /// </summary>
    public class YetiBehaviorTests
    {
        public enum TestYetiState
        {
            Moving,
            Pausing,
            Turning
        }

        public class TestableYetiBehavior
        {
            public TestYetiState State { get; set; }
            public float StateTimer { get; set; }
            public float DistanceToTarget { get; set; } = 100f;

            public void Update(float deltaTime)
            {
                StateTimer -= deltaTime;

                switch (State)
                {
                    case TestYetiState.Moving:
                        HandleMovingState();
                        break;
                    case TestYetiState.Pausing:
                        HandlePausingState();
                        break;
                    case TestYetiState.Turning:
                        HandleTurningState();
                        break;
                }
            }

            private void HandleMovingState()
            {
                // Reached target or timer expired
                if (DistanceToTarget < 3f || StateTimer <= 0f)
                {
                    State = TestYetiState.Pausing;
                    StateTimer = 2f; // Fixed for testing
                }
            }

            private void HandlePausingState()
            {
                if (StateTimer <= 0f)
                {
                    State = TestYetiState.Turning;
                    StateTimer = 0.5f;
                }
            }

            private void HandleTurningState()
            {
                if (StateTimer <= 0f)
                {
                    State = TestYetiState.Moving;
                    StateTimer = 6f; // Fixed for testing
                }
            }
        }

        [Fact]
        public void MovingState_WhenCloseToTarget_TransitionsToPausing()
        {
            var behavior = new TestableYetiBehavior
            {
                State = TestYetiState.Moving,
                StateTimer = 5f,
                DistanceToTarget = 2f // Close to target
            };

            behavior.Update(0.1f);

            Assert.Equal(TestYetiState.Pausing, behavior.State);
        }

        [Fact]
        public void MovingState_WhenTimerExpires_TransitionsToPausing()
        {
            var behavior = new TestableYetiBehavior
            {
                State = TestYetiState.Moving,
                StateTimer = 0.1f,
                DistanceToTarget = 100f // Far from target
            };

            behavior.Update(0.2f); // Timer goes to -0.1

            Assert.Equal(TestYetiState.Pausing, behavior.State);
        }

        [Fact]
        public void MovingState_WithTimeRemaining_StaysMoving()
        {
            var behavior = new TestableYetiBehavior
            {
                State = TestYetiState.Moving,
                StateTimer = 5f,
                DistanceToTarget = 100f
            };

            behavior.Update(0.1f);

            Assert.Equal(TestYetiState.Moving, behavior.State);
        }

        [Fact]
        public void PausingState_WhenTimerExpires_TransitionsToTurning()
        {
            var behavior = new TestableYetiBehavior
            {
                State = TestYetiState.Pausing,
                StateTimer = 0.1f
            };

            behavior.Update(0.2f);

            Assert.Equal(TestYetiState.Turning, behavior.State);
        }

        [Fact]
        public void TurningState_WhenTimerExpires_TransitionsToMoving()
        {
            var behavior = new TestableYetiBehavior
            {
                State = TestYetiState.Turning,
                StateTimer = 0.1f
            };

            behavior.Update(0.2f);

            Assert.Equal(TestYetiState.Moving, behavior.State);
        }

        [Fact]
        public void StateTimer_DecrementsCorrectly()
        {
            var behavior = new TestableYetiBehavior
            {
                State = TestYetiState.Moving,
                StateTimer = 5f,
                DistanceToTarget = 100f
            };

            behavior.Update(1.5f);

            Assert.Equal(3.5, (double)behavior.StateTimer, precision: 3);
        }

        [Fact]
        public void FullCycle_MovingToPausingToTurningToMoving()
        {
            var behavior = new TestableYetiBehavior
            {
                State = TestYetiState.Moving,
                StateTimer = 1f,
                DistanceToTarget = 100f
            };

            // Move until timer expires
            behavior.Update(1.1f);
            Assert.Equal(TestYetiState.Pausing, behavior.State);

            // Pause until timer expires
            behavior.Update(2.1f);
            Assert.Equal(TestYetiState.Turning, behavior.State);

            // Turn until timer expires
            behavior.Update(0.6f);
            Assert.Equal(TestYetiState.Moving, behavior.State);
        }
    }
}
