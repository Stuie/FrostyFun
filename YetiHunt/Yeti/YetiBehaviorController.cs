using UnityEngine;

namespace YetiHunt.Yeti
{
    /// <summary>
    /// Controls yeti movement AI: wandering, pausing, and turning states.
    /// </summary>
    public class YetiBehaviorController : IYetiBehaviorController
    {
        public void ControlYeti(HuntYeti yeti, float deltaTime)
        {
            if (yeti.GameObject == null || yeti.YetiComponent == null || yeti.MoveMethod == null)
                return;

            Vector3 currentPos = yeti.GameObject.transform.position;
            yeti.StateTimer -= deltaTime;

            switch (yeti.State)
            {
                case YetiMovementState.Moving:
                    HandleMovingState(yeti, currentPos, deltaTime);
                    break;

                case YetiMovementState.Pausing:
                    HandlePausingState(yeti, deltaTime);
                    break;

                case YetiMovementState.Turning:
                    HandleTurningState(yeti, deltaTime);
                    break;
            }
        }

        public void SetWanderCenter(HuntYeti yeti, Vector3 center)
        {
            yeti.WanderCenter = center;
        }

        private void HandleMovingState(HuntYeti yeti, Vector3 currentPos, float deltaTime)
        {
            Vector3 toTarget = yeti.TargetPosition - currentPos;
            toTarget.y = 0;
            float distToTarget = toTarget.magnitude;

            // Reached target or timer expired - pause and pick new direction
            if (distToTarget < 3f || yeti.StateTimer <= 0f)
            {
                yeti.State = YetiMovementState.Pausing;
                yeti.StateTimer = UnityEngine.Random.Range(1f, 3f);

                if (yeti.Animator != null)
                    yeti.Animator.SetFloat(yeti.SpeedHash, 0f);
                return;
            }

            // Smoothly interpolate current direction toward target direction
            Vector3 desiredDirection = toTarget.normalized;
            yeti.CurrentDirection = Vector3.Lerp(yeti.CurrentDirection, desiredDirection, deltaTime * 2f);

            // Move using the yeti's built-in method
            try
            {
                yeti.MoveMethod.Invoke(yeti.YetiComponent, new object[] { yeti.CurrentDirection, yeti.CurrentDirection });
            }
            catch { }

            // Update walk animation
            if (yeti.Animator != null)
                yeti.Animator.SetFloat(yeti.SpeedHash, 1f);
        }

        private void HandlePausingState(HuntYeti yeti, float deltaTime)
        {
            // Actively stop movement by passing zero direction
            try
            {
                yeti.MoveMethod.Invoke(yeti.YetiComponent, new object[] { Vector3.zero, yeti.CurrentDirection });
            }
            catch { }

            // Stay idle during pause
            if (yeti.Animator != null)
                yeti.Animator.SetFloat(yeti.SpeedHash, 0f);

            if (yeti.StateTimer <= 0f)
            {
                // Pick new target
                float angle = UnityEngine.Random.Range(0f, 360f) * Mathf.Deg2Rad;
                float dist = UnityEngine.Random.Range(10f, 25f);
                yeti.TargetPosition = yeti.WanderCenter + new Vector3(
                    Mathf.Cos(angle) * dist,
                    0,
                    Mathf.Sin(angle) * dist
                );

                // Calculate new direction
                Vector3 toTarget = yeti.TargetPosition - yeti.GameObject.transform.position;
                toTarget.y = 0;
                yeti.TargetDirection = toTarget.normalized;

                // Transition to turning
                yeti.State = YetiMovementState.Turning;
                yeti.StateTimer = 0.5f;
            }
        }

        private void HandleTurningState(HuntYeti yeti, float deltaTime)
        {
            // Smoothly turn toward new direction while stationary
            yeti.CurrentDirection = Vector3.Lerp(yeti.CurrentDirection, yeti.TargetDirection, deltaTime * 4f);

            // Face the direction (move with zero magnitude just to rotate)
            try
            {
                yeti.MoveMethod.Invoke(yeti.YetiComponent, new object[] { Vector3.zero, yeti.CurrentDirection });
            }
            catch { }

            // Keep idle animation during turn
            if (yeti.Animator != null)
                yeti.Animator.SetFloat(yeti.SpeedHash, 0f);

            if (yeti.StateTimer <= 0f)
            {
                // Start moving
                yeti.State = YetiMovementState.Moving;
                yeti.StateTimer = UnityEngine.Random.Range(4f, 8f);
            }
        }
    }
}
