using System;
using UnityEngine;
using YetiHunt.Infrastructure;
using Il2CppInterop.Runtime.InteropTypes;

namespace YetiHunt.Players
{
    /// <summary>
    /// Handles player teleportation via Il2Cpp reflection.
    /// </summary>
    public class TeleportationService : ITeleportationService
    {
        private readonly IModLogger _logger;
        private readonly ITypeResolver _typeResolver;
        private readonly IPlayerTracker _playerTracker;

        public TeleportationService(IModLogger logger, ITypeResolver typeResolver, IPlayerTracker playerTracker)
        {
            _logger = logger;
            _typeResolver = typeResolver;
            _playerTracker = playerTracker;
        }

        public bool TeleportPlayer(Vector3 destination, Quaternion rotation)
        {
            var playerTransform = _playerTracker.LocalPlayerTransform;
            if (playerTransform == null)
            {
                _logger.Warning("No player to teleport");
                return false;
            }

            var playerControlType = _typeResolver.GetPlayerControlType();
            var teleportControllerProperty = _typeResolver.GetTeleportControllerProperty();
            var teleportPlayerMethod = _typeResolver.GetTeleportPlayerMethod();

            if (playerControlType == null || teleportControllerProperty == null || teleportPlayerMethod == null)
            {
                _logger.Warning("Teleportation types not initialized");
                return false;
            }

            try
            {
                var playerObj = playerTransform.gameObject;
                var components = playerObj.GetComponents<Component>();
                object playerControl = null;

                foreach (var comp in components)
                {
                    if (comp == null) continue;
                    try
                    {
                        var il2cppType = comp.GetIl2CppType();
                        if (il2cppType?.Name == "PlayerControl")
                        {
                            var castMethod = typeof(Il2CppObjectBase).GetMethod("Cast").MakeGenericMethod(playerControlType);
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

                var teleportController = teleportControllerProperty.GetValue(playerControl);
                if (teleportController == null)
                {
                    _logger.Warning("teleportationController is null");
                    return false;
                }

                _logger.Info($"Teleporting player to {destination}");
                teleportPlayerMethod.Invoke(teleportController, new object[] { destination, rotation });
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error($"Teleport failed: {ex.Message}");
                return false;
            }
        }

        public Vector3 GetRandomSkyPosition(Vector3 center, float minDistance, float maxDistance, float height)
        {
            float angle = UnityEngine.Random.Range(0f, 360f) * Mathf.Deg2Rad;
            float distance = UnityEngine.Random.Range(minDistance, maxDistance);

            return center + new Vector3(
                Mathf.Cos(angle) * distance,
                height,
                Mathf.Sin(angle) * distance
            );
        }
    }
}
