using System;
using System.Reflection;
using UnityEngine;
using Il2CppInterop.Runtime.InteropTypes;
using FrostyFun.Shared.Il2Cpp;
using FrostyFun.Shared.Logging;

namespace FrostyFun.Shared.Players
{
    /// <summary>
    /// Teleports the local player via reflected PlayerControl.teleportationController.TeleportPlayer.
    /// Optionally leaves the active race first (via PlayerRacingController.Button_LeaveRace).
    /// </summary>
    public class PlayerTeleporter
    {
        private readonly IModLogger _logger;
        private readonly Il2CppTypeResolver _typeResolver;

        private Type _playerControlType;
        private PropertyInfo _teleportControllerProperty;
        private MethodInfo _teleportPlayerMethod;
        private PropertyInfo _racingControllerProperty;
        private MethodInfo _buttonLeaveRaceMethod;
        private MethodInfo _isInRaceMethod;
        private bool _resolved;

        public PlayerTeleporter(IModLogger logger, Il2CppTypeResolver typeResolver)
        {
            _logger = logger;
            _typeResolver = typeResolver;
        }

        public bool TeleportTo(Vector3 position, Quaternion rotation, bool leaveRaceFirst = false)
        {
            try
            {
                EnsureResolved();

                if (_playerControlType == null || _teleportControllerProperty == null || _teleportPlayerMethod == null)
                {
                    _logger.Warning("PlayerTeleporter: teleport reflection unresolved");
                    return false;
                }

                var playerObj = PlayerLocator.FindLocal();
                if (playerObj == null)
                {
                    _logger.Warning("PlayerTeleporter: local player not found");
                    return false;
                }

                object playerControl = FindPlayerControl(playerObj);
                if (playerControl == null)
                {
                    _logger.Warning("PlayerTeleporter: PlayerControl component not found");
                    return false;
                }

                var teleportController = _teleportControllerProperty.GetValue(playerControl);
                if (teleportController == null)
                {
                    _logger.Warning("PlayerTeleporter: teleportationController is null");
                    return false;
                }

                if (leaveRaceFirst)
                    LeaveRaceIfActive(playerControl);

                _logger.Info($"PlayerTeleporter: teleporting to {position}");
                _teleportPlayerMethod.Invoke(teleportController, new object[] { position, rotation });
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error($"PlayerTeleporter.TeleportTo failed: {ex.Message}");
                return false;
            }
        }

        private object FindPlayerControl(GameObject playerObj)
        {
            foreach (var comp in playerObj.GetComponents<Component>())
            {
                if (comp == null) continue;
                try
                {
                    if (comp.GetIl2CppTypeName() == "PlayerControl")
                    {
                        var castMethod = typeof(Il2CppObjectBase).GetMethod("Cast").MakeGenericMethod(_playerControlType);
                        return castMethod.Invoke(comp, null);
                    }
                }
                catch { }
            }
            return null;
        }

        private void LeaveRaceIfActive(object playerControl)
        {
            if (_racingControllerProperty == null || _buttonLeaveRaceMethod == null) return;
            try
            {
                var racingController = _racingControllerProperty.GetValue(playerControl);
                if (racingController == null) return;

                if (_isInRaceMethod != null)
                {
                    var inRace = _isInRaceMethod.Invoke(racingController, null);
                    if (inRace is bool b && !b) return;
                }

                _buttonLeaveRaceMethod.Invoke(racingController, null);
                _logger.Info("PlayerTeleporter: left active race before teleporting");
            }
            catch (Exception ex)
            {
                _logger.Warning($"PlayerTeleporter.LeaveRaceIfActive failed: {ex.Message}");
            }
        }

        private void EnsureResolved()
        {
            if (_resolved) return;
            _resolved = true;

            _playerControlType = _typeResolver.GetType("PlayerControl");
            if (_playerControlType == null)
            {
                _logger.Warning("PlayerTeleporter: PlayerControl type not found in Assembly-CSharp");
                return;
            }

            _teleportControllerProperty = _playerControlType.GetProperty("teleportationController")
                ?? _playerControlType.GetProperty("TeleportationController");

            if (_teleportControllerProperty != null)
            {
                _teleportPlayerMethod = _teleportControllerProperty.PropertyType.GetMethod("TeleportPlayer");
            }

            _racingControllerProperty = _playerControlType.GetProperty("racingController");
            if (_racingControllerProperty != null)
            {
                var racingType = _racingControllerProperty.PropertyType;
                _buttonLeaveRaceMethod = racingType.GetMethod("Button_LeaveRace");
                _isInRaceMethod = racingType.GetMethod("IsInRace");
            }

            if (_teleportPlayerMethod != null)
                _logger.Info($"PlayerTeleporter: resolved (race exit: {(_buttonLeaveRaceMethod != null ? "yes" : "no")})");
            else
                _logger.Warning("PlayerTeleporter: could not resolve TeleportPlayer method");
        }
    }
}
