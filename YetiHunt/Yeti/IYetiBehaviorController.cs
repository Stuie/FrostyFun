using UnityEngine;

namespace YetiHunt.Yeti
{
    /// <summary>
    /// Interface for controlling yeti movement and AI behavior.
    /// </summary>
    public interface IYetiBehaviorController
    {
        void ControlYeti(HuntYeti yeti, float deltaTime);
        void SetWanderCenter(HuntYeti yeti, Vector3 center);
    }
}
