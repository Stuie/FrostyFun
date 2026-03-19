namespace YetiHunt.Boundary
{
    /// <summary>
    /// Interface for controlling boundary effects (fog, yetis, etc.)
    /// </summary>
    public interface IBoundaryController
    {
        bool IsProtectionEnabled { get; }

        void EnableProtection();
        void DisableProtection();
        void Update(float currentTime);
    }
}
