namespace YetiHunt.Infrastructure
{
    /// <summary>
    /// Abstraction for logging to decouple from MelonLoader.
    /// </summary>
    public interface IModLogger
    {
        void Info(string message);
        void Warning(string message);
        void Error(string message);
    }
}
