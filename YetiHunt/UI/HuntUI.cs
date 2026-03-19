using UnityEngine;
using YetiHunt.Core;

namespace YetiHunt.UI
{
    /// <summary>
    /// Renders the hunt UI: status bar and winner announcement.
    /// </summary>
    public class HuntUI : IHuntUI
    {
        private readonly TextureFactory _textureFactory;
        private readonly IMinimapRenderer _minimapRenderer;

        private Texture2D _bgTexture;
        private Texture2D _winnerBgTexture;
        private bool _initialized;

        public HuntUI(TextureFactory textureFactory, IMinimapRenderer minimapRenderer)
        {
            _textureFactory = textureFactory;
            _minimapRenderer = minimapRenderer;
        }

        public void Initialize()
        {
            if (_initialized) return;

            _bgTexture = _textureFactory.MakeTexture(2, 2, new Color(0.1f, 0.1f, 0.15f, 0.8f));
            _winnerBgTexture = _textureFactory.MakeTexture(2, 2, new Color(0.1f, 0.3f, 0.1f, 0.9f));

            _minimapRenderer.Initialize();
            _initialized = true;
        }

        public void Draw(GameState state, float stateElapsedTime, string lastWinnerName)
        {
            if (state == GameState.Idle) return;

            Initialize();

            DrawStatusBar(state, stateElapsedTime);

            if (state == GameState.Hunting)
            {
                _minimapRenderer.Draw();
            }

            if (state == GameState.RoundEnd)
            {
                DrawWinnerAnnouncement(lastWinnerName);
            }
        }

        private void DrawStatusBar(GameState state, float elapsed)
        {
            float barWidth = 400;
            float barHeight = 60;
            float barX = (Screen.width - barWidth) / 2;
            float barY = 20;

            GUI.DrawTexture(new Rect(barX, barY, barWidth, barHeight), _bgTexture);

            GUIStyle labelStyle = new GUIStyle(GUI.skin.label);
            labelStyle.alignment = TextAnchor.MiddleCenter;
            labelStyle.fontSize = 24;
            labelStyle.fontStyle = FontStyle.Bold;

            string statusText = state switch
            {
                GameState.Countdown => $"Hunt begins in {Mathf.CeilToInt(GameStateMachine.COUNTDOWN_DURATION - elapsed)}...",
                GameState.Hunting => $"HUNT THE YETI! ({Mathf.CeilToInt(GameStateMachine.HUNT_TIMEOUT - elapsed)}s)",
                GameState.RoundEnd => "Round Over!",
                _ => ""
            };

            GUI.Label(new Rect(barX, barY, barWidth, barHeight), statusText, labelStyle);
        }

        private void DrawWinnerAnnouncement(string winnerName)
        {
            float winnerWidth = 500;
            float winnerHeight = 120;
            float winnerX = (Screen.width - winnerWidth) / 2;
            float winnerY = (Screen.height - winnerHeight) / 2 - 50;

            GUI.DrawTexture(new Rect(winnerX, winnerY, winnerWidth, winnerHeight), _winnerBgTexture);

            GUIStyle winnerTitleStyle = new GUIStyle(GUI.skin.label);
            winnerTitleStyle.alignment = TextAnchor.MiddleCenter;
            winnerTitleStyle.fontSize = 28;
            winnerTitleStyle.fontStyle = FontStyle.Bold;
            winnerTitleStyle.normal.textColor = Color.yellow;

            GUIStyle winnerNameStyle = new GUIStyle(GUI.skin.label);
            winnerNameStyle.alignment = TextAnchor.MiddleCenter;
            winnerNameStyle.fontSize = 36;
            winnerNameStyle.fontStyle = FontStyle.Bold;
            winnerNameStyle.normal.textColor = Color.white;

            if (!string.IsNullOrEmpty(winnerName))
            {
                GUI.Label(new Rect(winnerX, winnerY + 10, winnerWidth, 40), "WINNER!", winnerTitleStyle);
                GUI.Label(new Rect(winnerX, winnerY + 55, winnerWidth, 50), winnerName, winnerNameStyle);
            }
            else
            {
                GUI.Label(new Rect(winnerX, winnerY + 10, winnerWidth, 40), "TIME'S UP!", winnerTitleStyle);
                GUI.Label(new Rect(winnerX, winnerY + 55, winnerWidth, 50), "No one caught the Yeti!", winnerNameStyle);
            }
        }
    }
}
