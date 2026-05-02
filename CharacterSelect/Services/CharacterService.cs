using System;
using System.Reflection;
using UnityEngine;
using CharacterSelect.Data;
using CharacterSelect.Infrastructure;
using Il2CppInterop.Runtime.InteropTypes;

namespace CharacterSelect.Services
{
    public class CharacterService : ICharacterService
    {
        private readonly IModLogger _logger;
        private readonly ITypeResolver _typeResolver;

        public CharacterService(IModLogger logger, ITypeResolver typeResolver)
        {
            _logger = logger;
            _typeResolver = typeResolver;
        }

        public void SwitchCharacter(int characterId)
        {
            try
            {
                var playerObj = GameObject.Find("Player Networked(Clone)");
                if (playerObj == null)
                {
                    _logger.Warning("No local player found");
                    return;
                }

                if (!_typeResolver.IsInitialized)
                    _typeResolver.Initialize();

                var pcType = _typeResolver.GetPlayerControlType();
                if (pcType == null)
                {
                    _logger.Warning("PlayerControl type not found");
                    return;
                }

                var switchMethod = _typeResolver.GetCmdSwitchCharacterMethod();
                if (switchMethod == null)
                {
                    _logger.Warning("CmdSwitchCharacter method not found");
                    return;
                }

                // Find PlayerControl component
                Component playerControl = FindPlayerControl(playerObj);
                if (playerControl == null)
                {
                    _logger.Warning("PlayerControl not found");
                    return;
                }

                var parameters = switchMethod.GetParameters();
                if (parameters.Length == 1)
                {
                    var paramType = parameters[0].ParameterType;
                    var enumValue = Enum.ToObject(paramType, characterId);

                    var castMethod = typeof(Il2CppObjectBase).GetMethod("Cast").MakeGenericMethod(pcType);
                    var typedPC = castMethod.Invoke(playerControl, null);

                    switchMethod.Invoke(typedPC, new object[] { enumValue });
                    _logger.Info($"Switched to {CharacterData.GetCharacterName(characterId)}!");
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to switch character: {ex.Message}");
            }
        }

        public int GetCurrentCharacterId()
        {
            try
            {
                var playerObj = GameObject.Find("Player Networked(Clone)");
                if (playerObj == null) return -1;

                if (!_typeResolver.IsInitialized)
                    _typeResolver.Initialize();

                var pcType = _typeResolver.GetPlayerControlType();
                if (pcType == null) return -1;

                Component playerControl = FindPlayerControl(playerObj);
                if (playerControl == null) return -1;

                var castMethod = typeof(Il2CppObjectBase).GetMethod("Cast").MakeGenericMethod(pcType);
                var typedPC = castMethod.Invoke(playerControl, null);

                // Try sync_EquippedCharacterName first
                var syncVarProp = _typeResolver.GetSyncEquippedCharacterNameProperty();
                if (syncVarProp != null)
                {
                    var syncVarValue = syncVarProp.GetValue(typedPC);
                    if (syncVarValue != null)
                    {
                        var syncVarType = syncVarValue.GetType();
                        var valueProp = syncVarType.GetProperty("Value");
                        if (valueProp != null)
                        {
                            var charValue = valueProp.GetValue(syncVarValue);
                            if (charValue != null)
                                return Convert.ToInt32(charValue);
                        }
                    }
                }

                // Fallback: try common property/field names
                string[] propNames = { "CharacterModel", "characterModel", "CurrentCharacter",
                                       "currentCharacter", "Character", "character",
                                       "CharacterModelName", "characterModelName",
                                       "EquippedCharacterName", "equippedCharacterName" };

                foreach (var name in propNames)
                {
                    var prop = pcType.GetProperty(name);
                    if (prop != null)
                    {
                        var value = prop.GetValue(typedPC);
                        if (value != null && value.GetType().IsEnum)
                            return Convert.ToInt32(value);
                    }

                    var field = pcType.GetField(name);
                    if (field != null)
                    {
                        var value = field.GetValue(typedPC);
                        if (value != null && value.GetType().IsEnum)
                            return Convert.ToInt32(value);
                    }
                }

                // Search for any property returning CharacterModelName enum
                foreach (var prop in pcType.GetProperties())
                {
                    if (prop.PropertyType.Name == "CharacterModelName")
                    {
                        var value = prop.GetValue(typedPC);
                        if (value != null)
                            return Convert.ToInt32(value);
                    }
                }

                foreach (var field in pcType.GetFields())
                {
                    if (field.FieldType.Name == "CharacterModelName")
                    {
                        var value = field.GetValue(typedPC);
                        if (value != null)
                            return Convert.ToInt32(value);
                    }
                }

                return -1;
            }
            catch (Exception ex)
            {
                _logger.Warning($"GetCurrentCharacterId error: {ex.Message}");
                return -1;
            }
        }

        private Component FindPlayerControl(GameObject playerObj)
        {
            var allComponents = playerObj.GetComponents<Component>();
            foreach (var comp in allComponents)
            {
                if (comp == null) continue;
                try
                {
                    var il2cppType = comp.GetIl2CppType();
                    if ((il2cppType?.Name ?? comp.GetType().Name) == "PlayerControl")
                        return comp;
                }
                catch
                {
                    if (comp.GetType().Name == "PlayerControl")
                        return comp;
                }
            }
            return null;
        }
    }
}
