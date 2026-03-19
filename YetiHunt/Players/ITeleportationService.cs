using UnityEngine;

namespace YetiHunt.Players
{
    /// <summary>
    /// Interface for player teleportation.
    /// </summary>
    public interface ITeleportationService
    {
        bool TeleportPlayer(Vector3 destination, Quaternion rotation);
        Vector3 GetRandomSkyPosition(Vector3 center, float minDistance, float maxDistance, float height);
    }
}
