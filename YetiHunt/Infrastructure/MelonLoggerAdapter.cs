using MelonLoader;

namespace YetiHunt.Infrastructure
{
    /// <summary>
    /// Adapter that wraps MelonLoader's logger.
    /// </summary>
    public class MelonLoggerAdapter : IModLogger
    {
        private readonly MelonLogger.Instance _logger;

        public MelonLoggerAdapter(MelonLogger.Instance logger)
        {
            _logger = logger;
        }

        public void Info(string message) => _logger.Msg(message);
        public void Warning(string message) => _logger.Warning(message);
        public void Error(string message) => _logger.Error(message);
    }
}
