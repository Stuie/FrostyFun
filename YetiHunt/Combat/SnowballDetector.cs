using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using YetiHunt.Infrastructure;
using YetiHunt.Players;
using YetiHunt.Yeti;
using Il2CppInterop.Runtime.InteropTypes;

namespace YetiHunt.Combat
{
    /// <summary>
    /// Detects snowball hits on yetis using physics overlap.
    /// </summary>
    public class SnowballDetector : ISnowballDetector
    {
        private const float COLLIDER_RADIUS = 2f;
        private const float COLLIDER_HEIGHT = 6.5f;
        private const float COLLIDER_Y_OFFSET = 4.5f;

        private readonly IModLogger _logger;
        private readonly ITypeResolver _typeResolver;
        private readonly IPlayerTracker _playerTracker;

        private readonly HashSet<int> _hitSnowballs = new HashSet<int>();

        public event Action<HitEventArgs> OnSnowballHit;

        public SnowballDetector(IModLogger logger, ITypeResolver typeResolver, IPlayerTracker playerTracker)
        {
            _logger = logger;
            _typeResolver = typeResolver;
            _playerTracker = playerTracker;
        }

        public void CheckForHits(IReadOnlyList<HuntYeti> yetis)
        {
            if (yetis.Count == 0) return;

            foreach (var yeti in yetis)
            {
                if (yeti.GameObject == null) continue;

                Vector3 yetiPos = yeti.GameObject.transform.position;
                Vector3 capsuleCenter = yetiPos + new Vector3(0, COLLIDER_Y_OFFSET, 0);

                // Capsule endpoints
                Vector3 point1 = capsuleCenter - new Vector3(0, (COLLIDER_HEIGHT / 2f) - COLLIDER_RADIUS, 0);
                Vector3 point2 = capsuleCenter + new Vector3(0, (COLLIDER_HEIGHT / 2f) - COLLIDER_RADIUS, 0);

                // Check for overlapping colliders
                var hitColliders = Physics.OverlapCapsule(point1, point2, COLLIDER_RADIUS + 0.5f);

                foreach (var col in hitColliders)
                {
                    if (col == null || col.gameObject == null) continue;

                    if (col.gameObject.name.Contains("Snowball"))
                    {
                        int snowballId = col.gameObject.GetInstanceID();

                        if (_hitSnowballs.Contains(snowballId)) continue;

                        _hitSnowballs.Add(snowballId);
                        Vector3 hitPos = col.gameObject.transform.position;

                        string throwerName = GetSnowballThrowerName(col.gameObject);

                        _logger.Info($"*** YETI HIT! *** Snowball at {hitPos} hit yeti at {yetiPos}, thrown by: {throwerName}");
                        OnSnowballHit?.Invoke(new HitEventArgs(yeti, hitPos, throwerName));
                        return;
                    }
                }
            }

            // Clean up old tracking
            if (_hitSnowballs.Count > 100)
                _hitSnowballs.Clear();
        }

        public void ClearTracking()
        {
            _hitSnowballs.Clear();
        }

        private string GetSnowballThrowerName(GameObject snowballObj)
        {
            var snowballType = _typeResolver.GetSnowballType();
            if (snowballType == null) return "Unknown";

            try
            {
                var components = snowballObj.GetComponents<Component>();
                foreach (var comp in components)
                {
                    if (comp == null) continue;
                    try
                    {
                        var il2cppType = comp.GetIl2CppType();
                        if (il2cppType?.Name == "Snowball")
                        {
                            var castMethod = typeof(Il2CppObjectBase).GetMethod("Cast").MakeGenericMethod(snowballType);
                            var snowball = castMethod.Invoke(comp, null);

                            // Try sync_PlayerThatPickedUpObject first
                            var syncPlayerProp = snowballType.GetProperty("sync_PlayerThatPickedUpObject");
                            if (syncPlayerProp != null)
                            {
                                var syncVar = syncPlayerProp.GetValue(snowball);
                                if (syncVar != null)
                                {
                                    var valueProp = syncVar.GetType().GetProperty("Value");
                                    if (valueProp != null)
                                    {
                                        var playerRef = valueProp.GetValue(syncVar);
                                        if (playerRef != null)
                                        {
                                            string name = ExtractPlayerName(playerRef);
                                            if (!string.IsNullOrEmpty(name) && name != "Unknown")
                                                return name;
                                        }
                                    }
                                }
                            }

                            // Try OwnerId
                            var ownerIdProp = snowballType.GetProperty("OwnerId");
                            if (ownerIdProp != null)
                            {
                                try
                                {
                                    int ownerId = (int)ownerIdProp.GetValue(snowball);
                                    if (ownerId >= 0)
                                    {
                                        string playerName = _playerTracker.GetUsername(ownerId);
                                        if (!string.IsNullOrEmpty(playerName))
                                            return playerName;
                                    }
                                }
                                catch { }
                            }

                            // Check if local player's snowball
                            var isOwnerProp = snowballType.GetProperty("IsOwner");
                            if (isOwnerProp != null)
                            {
                                try
                                {
                                    bool isOwner = (bool)isOwnerProp.GetValue(snowball);
                                    if (isOwner)
                                    {
                                        return _playerTracker.GetLocalPlayerName();
                                    }
                                    else
                                    {
                                        return "Other Player";
                                    }
                                }
                                catch { }
                            }

                            break;
                        }
                    }
                    catch { }
                }
            }
            catch { }

            return "Unknown";
        }

        private string ExtractPlayerName(object playerRef)
        {
            if (playerRef == null) return "Unknown";

            try
            {
                var type = playerRef.GetType();

                // Try OwnerId lookup from cache
                var ownerIdProp = type.GetProperty("OwnerId");
                if (ownerIdProp != null)
                {
                    int ownerId = (int)ownerIdProp.GetValue(playerRef);
                    var cachedName = _playerTracker.GetUsername(ownerId);
                    if (!string.IsNullOrEmpty(cachedName))
                        return cachedName;
                }

                // Try usernameController
                var usernameControllerProp = type.GetProperty("usernameController");
                if (usernameControllerProp != null)
                {
                    var usernameController = usernameControllerProp.GetValue(playerRef);
                    if (usernameController != null)
                    {
                        var ucType = usernameController.GetType();

                        // Check fields for TMP_Text
                        foreach (var field in ucType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                        {
                            try
                            {
                                var val = field.GetValue(usernameController);
                                if (val != null && (field.FieldType.Name.Contains("TMP") || field.FieldType.Name.Contains("Text")))
                                {
                                    var textProp = val.GetType().GetProperty("text");
                                    if (textProp != null)
                                    {
                                        var textVal = textProp.GetValue(val);
                                        if (textVal != null)
                                        {
                                            string txt = textVal.ToString();
                                            if (!string.IsNullOrEmpty(txt) && txt.Length > 1)
                                                return txt;
                                        }
                                    }
                                }
                            }
                            catch { }
                        }

                        // Try sync_Username
                        var syncUsernameProp = ucType.GetProperty("sync_Username");
                        if (syncUsernameProp != null)
                        {
                            var syncVar = syncUsernameProp.GetValue(usernameController);
                            if (syncVar != null)
                            {
                                var valueProp = syncVar.GetType().GetProperty("Value");
                                if (valueProp != null)
                                {
                                    var username = valueProp.GetValue(syncVar);
                                    if (username != null)
                                    {
                                        string strVal = username.ToString();
                                        if (!string.IsNullOrEmpty(strVal))
                                            return strVal;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            return "Unknown";
        }
    }
}
