using System;
using System.Reflection;

namespace YetiHunt.Infrastructure
{
    /// <summary>
    /// Resolves and caches Il2Cpp types from Assembly-CSharp.
    /// </summary>
    public class Il2CppTypeResolver : ITypeResolver
    {
        private readonly IModLogger _logger;

        private Type _yetiManagerType;
        private Type _yetiType;
        private Type _chatManagerType;
        private Type _playerControlType;
        private Type _playerTeleportControllerType;
        private Type _snowballType;

        private MethodInfo _spawnYetiMethod;
        private PropertyInfo _teleportControllerProperty;
        private MethodInfo _teleportPlayerMethod;

        private bool _initialized;

        public bool IsInitialized => _initialized;

        public Il2CppTypeResolver(IModLogger logger)
        {
            _logger = logger;
        }

        public void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            try
            {
                var assembly = Assembly.Load("Assembly-CSharp");

                foreach (var type in assembly.GetTypes())
                {
                    if (type.Name == "YetiManager" && type.Namespace == "Il2Cpp")
                    {
                        _yetiManagerType = type;
                        _spawnYetiMethod = type.GetMethod("Server_SpawnYeti");
                        _logger.Info($"Found YetiManager, SpawnYeti method: {_spawnYetiMethod != null}");
                    }
                    else if (type.Name == "Yeti" && type.Namespace == "Il2Cpp")
                    {
                        _yetiType = type;
                        _logger.Info("Found Yeti type");
                    }
                    else if (type.Name == "ChatManager")
                    {
                        _chatManagerType = type;
                        _logger.Info("Found ChatManager type");
                    }
                    else if (type.Name == "PlayerControl" && type.Namespace == "Il2Cpp")
                    {
                        _playerControlType = type;
                        _teleportControllerProperty = type.GetProperty("teleportationController");
                        _logger.Info($"Found PlayerControl, teleportationController: {_teleportControllerProperty != null}");
                    }
                    else if (type.Name == "PlayerTeleportationController")
                    {
                        _playerTeleportControllerType = type;
                        _teleportPlayerMethod = type.GetMethod("TeleportPlayer");
                        _logger.Info($"Found PlayerTeleportationController, TeleportPlayer: {_teleportPlayerMethod != null}");
                    }
                    else if (type.Name == "Snowball" && type.Namespace == "Il2Cpp")
                    {
                        _snowballType = type;
                        _logger.Info("Found Snowball type");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Il2CppTypeResolver.Initialize failed: {ex.Message}");
            }
        }

        public Type GetYetiManagerType() => _yetiManagerType;
        public Type GetYetiType() => _yetiType;
        public Type GetChatManagerType() => _chatManagerType;
        public Type GetPlayerControlType() => _playerControlType;
        public Type GetPlayerTeleportControllerType() => _playerTeleportControllerType;
        public Type GetSnowballType() => _snowballType;

        public MethodInfo GetSpawnYetiMethod() => _spawnYetiMethod;
        public PropertyInfo GetTeleportControllerProperty() => _teleportControllerProperty;
        public MethodInfo GetTeleportPlayerMethod() => _teleportPlayerMethod;
    }
}
