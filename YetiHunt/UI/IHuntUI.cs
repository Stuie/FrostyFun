using YetiHunt.Core;

namespace YetiHunt.UI
{
    /// <summary>
    /// Interface for the hunt UI display.
    /// </summary>
    public interface IHuntUI
    {
        void Initialize();
        void Draw(GameState state, float stateElapsedTime, string lastWinnerName);
    }
}
