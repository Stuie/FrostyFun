using UnityEngine;

namespace YetiHunt.Players
{
    /// <summary>
    /// Interface for tracking players and caching usernames.
    /// </summary>
    public interface IPlayerTracker
    {
        Transform LocalPlayerTransform { get; }

        void ScanAndCacheUsernames();
        string GetUsername(int ownerId);
        string GetLocalPlayerName();
        void Update();
        void ClearCache();
    }
}
