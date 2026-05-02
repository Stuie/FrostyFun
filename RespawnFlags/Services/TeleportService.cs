using System;
using System.Reflection;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;
using Il2CppInterop.Runtime.InteropTypes;

namespace RespawnFlags.Services
{
    public class TeleportService
    {
        private readonly MelonLogger.Instance _logger;

        // Cached reflection (resolved once, reused)
        private Type _playerControlType;
        private PropertyInfo _teleportControllerProperty;
        private MethodInfo _teleportPlayerMethod;
        private PropertyInfo _racingControllerProperty;
        private MethodInfo _buttonLeaveRaceMethod;
        private MethodInfo _isInRaceMethod;
        private bool _resolved;

        public TeleportService(MelonLogger.Instance logger)
        {
            _logger = logger;
        }

        public bool TeleportTo(Vector3 position)
        {
            try
            {
                if (!_resolved)
                    ResolveTypes();

                var playerObj = GameObject.Find("Player Networked(Clone)");
                if (playerObj == null)
                {
                    _logger.Warning("No player found for teleport");
                    return false;
                }

                object playerControl = null;
                var components = playerObj.GetComponents<Component>();
                foreach (var comp in components)
                {
                    if (comp == null) continue;
                    try
                    {
                        if (comp.GetIl2CppType()?.Name == "PlayerControl")
                        {
                            var castMethod = typeof(Il2CppObjectBase).GetMethod("Cast")
                                .MakeGenericMethod(_playerControlType);
                            playerControl = castMethod.Invoke(comp, null);
                            break;
                        }
                    }
                    catch { }
                }

                if (playerControl == null)
                {
                    _logger.Warning("Could not find PlayerControl component");
                    return false;
                }

                var teleportController = _teleportControllerProperty?.GetValue(playerControl);
                if (teleportController == null)
                {
                    _logger.Warning("teleportationController is null");
                    return false;
                }

                LeaveRaceIfActive(playerControl);

                _logger.Msg($"Teleporting to {position}");
                _teleportPlayerMethod.Invoke(teleportController, new object[] { position, Quaternion.identity });
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error($"Teleport failed: {ex.Message}");
                return false;
            }
        }

        private void LeaveRaceIfActive(object playerControl)
        {
            if (_racingControllerProperty == null || _buttonLeaveRaceMethod == null) return;

            try
            {
                var racingController = _racingControllerProperty.GetValue(playerControl);
                if (racingController == null) return;

                // Check if actually in a race
                if (_isInRaceMethod != null)
                {
                    var inRace = _isInRaceMethod.Invoke(racingController, null);
                    if (inRace is bool b && !b) return;
                }

                _buttonLeaveRaceMethod.Invoke(racingController, null);
                _logger.Msg("Left active race before teleporting");
            }
            catch (Exception ex)
            {
                _logger.Warning($"LeaveRace failed: {ex.Message}");
            }
        }

        private void ResolveTypes()
        {
            _resolved = true;
            try
            {
                var assembly = Assembly.Load("Assembly-CSharp");
                foreach (var type in assembly.GetTypes())
                {
                    if (type.Name == "PlayerControl")
                    {
                        _playerControlType = type;
                        break;
                    }
                }

                if (_playerControlType == null) return;

                // Find teleportationController property
                _teleportControllerProperty = _playerControlType.GetProperty("teleportationController")
                    ?? _playerControlType.GetProperty("TeleportationController");

                if (_teleportControllerProperty != null)
                {
                    var controllerType = _teleportControllerProperty.PropertyType;
                    _teleportPlayerMethod = controllerType.GetMethod("TeleportPlayer");
                }

                // Find racingController property and Button_LeaveRace method
                _racingControllerProperty = _playerControlType.GetProperty("racingController");
                if (_racingControllerProperty != null)
                {
                    var racingType = _racingControllerProperty.PropertyType;
                    _buttonLeaveRaceMethod = racingType.GetMethod("Button_LeaveRace");
                    _isInRaceMethod = racingType.GetMethod("IsInRace");
                }

                if (_teleportPlayerMethod != null)
                    _logger.Msg("TeleportService: types resolved successfully" +
                        (_buttonLeaveRaceMethod != null ? " (with race exit)" : " (no race exit)"));
                else
                    _logger.Warning("TeleportService: could not resolve teleport method");
            }
            catch (Exception ex)
            {
                _logger.Error($"TeleportService resolve failed: {ex.Message}");
            }
        }
    }
}
