using MelonLoader;
using UnityEngine;
using YetiHunt.Boundary;
using YetiHunt.Combat;
using YetiHunt.Core;
using YetiHunt.Debug;
using YetiHunt.Infrastructure;
using YetiHunt.Players;
using YetiHunt.UI;
using YetiHunt.Yeti;

[assembly: MelonInfo(typeof(YetiHunt.YetiHuntMod), "YetiHunt", "1.0.0", "Stuart Gilbert")]
[assembly: MelonGame("The Sledding Corporation", "Sledding Game Demo")]

namespace YetiHunt
{
    /// <summary>
    /// YetiHunt - A Battle Royale-style game mode where players hunt a yeti.
    ///
    /// Debug Keys:
    /// - Ctrl+1: Start/stop YetiHunt round
    /// - Ctrl+2: Spawn a yeti near player (test)
    /// - Ctrl+3: Dump map/UI info
    /// - Ctrl+4: Dump map coordinate debug
    /// - Ctrl+5: Toggle boundary protection (disable yetis + fog)
    /// - Ctrl+6: Record corner coordinate
    /// - Ctrl+7: Dump player info (for debugging)
    /// - Ctrl+8: Show recorded corners
    /// </summary>
    public class YetiHuntMod : MelonMod
    {
        // Infrastructure
        private IModLogger _logger;
        private ITypeResolver _typeResolver;

        // Core
        private IGameStateMachine _gameStateMachine;

        // Services
        private IPlayerTracker _playerTracker;
        private ITeleportationService _teleportationService;
        private IYetiBehaviorController _yetiBehaviorController;
        private IYetiManager _yetiManager;
        private ISnowballDetector _snowballDetector;
        private IBoundaryController _boundaryController;

        // UI
        private TextureFactory _textureFactory;
        private IMinimapRenderer _minimapRenderer;
        private IHuntUI _huntUI;

        // Debug
        private IDiagnosticsService _diagnosticsService;
        private InputHandler _inputHandler;

        private string _currentScene = "";

        public override void OnInitializeMelon()
        {
            // Create infrastructure
            _logger = new MelonLoggerAdapter(Melon<YetiHuntMod>.Logger);
            _typeResolver = new Il2CppTypeResolver(_logger);

            // Create services
            _playerTracker = new PlayerTracker(_logger, _typeResolver);
            _teleportationService = new TeleportationService(_logger, _typeResolver, _playerTracker);
            _yetiBehaviorController = new YetiBehaviorController();
            _yetiManager = new YetiManager(_logger, _typeResolver, _yetiBehaviorController);
            _snowballDetector = new SnowballDetector(_logger, _typeResolver, _playerTracker);
            _boundaryController = new BoundaryController(_logger, _yetiManager);

            // Create core
            _gameStateMachine = new GameStateMachine(_logger);

            // Create UI
            _textureFactory = new TextureFactory(_logger);
            _minimapRenderer = new MinimapRenderer(_logger, _textureFactory, _playerTracker, _yetiManager);
            _huntUI = new HuntUI(_textureFactory, _minimapRenderer);

            // Create debug
            _diagnosticsService = new DiagnosticsService(_logger, _typeResolver, _playerTracker, _yetiManager);
            _inputHandler = new InputHandler();

            // Wire up events
            WireUpEvents();

            // Log instructions
            _logger.Info("YetiHunt loaded!");
            _logger.Info("Controls (hold Ctrl + number):");
            _logger.Info("  Ctrl+1 - Start/stop round");
            _logger.Info("  Ctrl+2 - Spawn yeti near player");
            _logger.Info("  Ctrl+3 - Dump map/UI info");
            _logger.Info("  Ctrl+4 - Dump map coordinate debug");
            _logger.Info("  Ctrl+5 - Toggle boundary protection (disable yetis + fog)");
            _logger.Info("  Ctrl+6 - Record corner coordinate");
            _logger.Info("  Ctrl+7 - Dump player info (for debugging)");
            _logger.Info("  Ctrl+8 - Show recorded corners");
        }

        private void WireUpEvents()
        {
            // Input handler events
            _inputHandler.OnStartStopRound += HandleStartStopRound;
            _inputHandler.OnTestSpawnYeti += HandleTestSpawnYeti;
            _inputHandler.OnDumpMapInfo += () => _diagnosticsService.DumpMapInfo();
            _inputHandler.OnDumpMapCoordinateDebug += () => _diagnosticsService.DumpMapCoordinateDebug();
            _inputHandler.OnToggleBoundaryProtection += HandleToggleBoundaryProtection;
            _inputHandler.OnRecordCorner += () => _diagnosticsService.RecordCornerCoordinate();
            _inputHandler.OnDumpPlayerInfo += () => _diagnosticsService.DumpPlayerInfo();
            _inputHandler.OnShowRecordedCorners += () => _diagnosticsService.ShowRecordedCorners();

            // Game state events
            _gameStateMachine.OnHuntingStarted += HandleHuntingStarted;
            _gameStateMachine.OnRoundEnded += HandleRoundEnded;

            // Snowball hit event
            _snowballDetector.OnSnowballHit += HandleSnowballHit;
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            _currentScene = sceneName;
            _logger.Info($"Scene: {sceneName}");

            // Reset state on scene change
            ((GameStateMachine)_gameStateMachine).ResetForSceneChange();
            _playerTracker.ClearCache();
            _yetiManager.ClearYetiManagerInstance();
        }

        public override void OnUpdate()
        {
            // Initialize types once
            if (!_typeResolver.IsInitialized)
            {
                _typeResolver.Initialize();
            }

            // Update player tracker
            _playerTracker.Update();

            // Handle input
            _inputHandler.HandleInput();

            // Update game state
            _gameStateMachine.Update(Time.deltaTime, Time.time);

            // Update yeti AI
            _yetiManager.Update(Time.deltaTime);

            // Detect snowball hits during hunting
            if (_gameStateMachine.CurrentState == GameState.Hunting)
            {
                _snowballDetector.CheckForHits(_yetiManager.ActiveYetis);
                _playerTracker.ScanAndCacheUsernames();
            }

            // Update boundary protection
            _boundaryController.Update(Time.time);
        }

        public override void OnGUI()
        {
            _huntUI.Draw(_gameStateMachine.CurrentState, _gameStateMachine.StateElapsedTime, _gameStateMachine.LastWinnerName);
        }

        private void HandleStartStopRound()
        {
            if (_gameStateMachine.CurrentState == GameState.Idle)
            {
                _gameStateMachine.StartRound();
            }
            else
            {
                _gameStateMachine.StopRound();
                _yetiManager.DespawnAllYetis();
                _snowballDetector.ClearTracking();
            }
        }

        private void HandleTestSpawnYeti()
        {
            _logger.Info("=== Testing Yeti Spawn ===");

            var playerTransform = _playerTracker.LocalPlayerTransform;
            if (playerTransform == null)
            {
                _logger.Warning("No player found");
                return;
            }

            Vector3 spawnPos = playerTransform.position + playerTransform.forward * 20f;
            _yetiManager.SpawnYetiAt(spawnPos);
        }

        private void HandleToggleBoundaryProtection()
        {
            if (_boundaryController.IsProtectionEnabled)
            {
                _boundaryController.DisableProtection();
            }
            else
            {
                _boundaryController.EnableProtection();
            }
        }

        private void HandleHuntingStarted()
        {
            // Pre-scan players for username cache
            _playerTracker.ScanAndCacheUsernames();

            // Teleport player to random sky position
            var playerTransform = _playerTracker.LocalPlayerTransform;
            if (playerTransform != null)
            {
                Vector3 center = new Vector3(300f, 0f, 400f);
                Vector3 targetPos = _teleportationService.GetRandomSkyPosition(center, 100f, 400f, 500f);
                Quaternion rotation = Quaternion.LookRotation((center - targetPos).normalized);
                _teleportationService.TeleportPlayer(targetPos, rotation);
            }

            // Spawn yeti for hunt
            if (playerTransform != null)
            {
                _yetiManager.SpawnYetiForHunt(playerTransform.position, 30f, 60f);
            }
        }

        private void HandleRoundEnded(string winnerName)
        {
            _yetiManager.DespawnAllYetis();
            _snowballDetector.ClearTracking();
        }

        private void HandleSnowballHit(HitEventArgs args)
        {
            _logger.Info($"*** YETI HIT! *** at {args.HitPosition} by {args.ThrowerName}");
            _gameStateMachine.SetWinner(args.ThrowerName);
        }
    }
}
