using System;
using System.Collections.Generic;
using UnityEngine;

namespace YetiHunt.Yeti
{
    /// <summary>
    /// Interface for managing yeti spawning and tracking.
    /// </summary>
    public interface IYetiManager
    {
        IReadOnlyList<HuntYeti> ActiveYetis { get; }

        void SpawnYetiForHunt(Vector3 nearPosition, float minDistance, float maxDistance);
        void SpawnYetiAt(Vector3 position);
        void DespawnAllYetis();
        void Update(float deltaTime);
        void ClearYetiManagerInstance();

        event Action<HuntYeti, Vector3, string> OnYetiHit;
    }
}
