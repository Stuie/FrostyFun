using System;
using System.Reflection;

namespace CharacterSelect.Infrastructure
{
    public class Il2CppTypeResolver : ITypeResolver
    {
        private readonly IModLogger _logger;

        private Type _playerControlType;
        private MethodInfo _cmdSwitchCharacterMethod;
        private PropertyInfo _syncEquippedCharacterNameProperty;
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
                    if (type.Name == "PlayerControl" && type.Namespace == "Il2Cpp")
                    {
                        _playerControlType = type;
                        _cmdSwitchCharacterMethod = type.GetMethod("CmdSwitchCharacter");
                        _syncEquippedCharacterNameProperty = type.GetProperty("sync_EquippedCharacterName");

                        _logger.Info($"Found PlayerControl, CmdSwitchCharacter: {_cmdSwitchCharacterMethod != null}, " +
                                     $"sync_EquippedCharacterName: {_syncEquippedCharacterNameProperty != null}");
                        return;
                    }
                }

                // Fallback: try without namespace
                foreach (var type in assembly.GetTypes())
                {
                    if (type.Name == "PlayerControl")
                    {
                        _playerControlType = type;
                        _cmdSwitchCharacterMethod = type.GetMethod("CmdSwitchCharacter");
                        _syncEquippedCharacterNameProperty = type.GetProperty("sync_EquippedCharacterName");

                        _logger.Info($"Found PlayerControl (no namespace), CmdSwitchCharacter: {_cmdSwitchCharacterMethod != null}");
                        return;
                    }
                }

                _logger.Warning("PlayerControl type not found in Assembly-CSharp");
            }
            catch (Exception ex)
            {
                _logger.Error($"Il2CppTypeResolver.Initialize failed: {ex.Message}");
            }
        }

        public Type GetPlayerControlType() => _playerControlType;
        public MethodInfo GetCmdSwitchCharacterMethod() => _cmdSwitchCharacterMethod;
        public PropertyInfo GetSyncEquippedCharacterNameProperty() => _syncEquippedCharacterNameProperty;
    }
}
