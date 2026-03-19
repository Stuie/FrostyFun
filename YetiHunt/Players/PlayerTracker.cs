using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using YetiHunt.Infrastructure;
using Object = UnityEngine.Object;

namespace YetiHunt.Players
{
    /// <summary>
    /// Tracks players and caches their usernames by OwnerId.
    /// </summary>
    public class PlayerTracker : IPlayerTracker
    {
        private const float USERNAME_SCAN_INTERVAL = 2.0f;

        private readonly IModLogger _logger;
        private readonly ITypeResolver _typeResolver;

        private readonly Dictionary<int, string> _usernameCache = new Dictionary<int, string>();
        private Transform _playerTransform;
        private float _lastScanTime;

        public Transform LocalPlayerTransform => _playerTransform;

        public PlayerTracker(IModLogger logger, ITypeResolver typeResolver)
        {
            _logger = logger;
            _typeResolver = typeResolver;
        }

        public void Update()
        {
            // Track local player
            if (_playerTransform == null)
            {
                var playerObj = GameObject.Find("Player Networked(Clone)");
                if (playerObj != null)
                {
                    _playerTransform = playerObj.transform;
                }
            }
        }

        public void ScanAndCacheUsernames()
        {
            float currentTime = Time.time;
            if (currentTime - _lastScanTime < USERNAME_SCAN_INTERVAL)
                return;

            _lastScanTime = currentTime;

            foreach (var obj in Object.FindObjectsOfType<GameObject>())
            {
                if (obj == null || !obj.name.Contains("Player Networked")) continue;

                try
                {
                    int ownerId = GetPlayerOwnerId(obj);
                    if (ownerId < 0) continue;

                    if (_usernameCache.ContainsKey(ownerId)) continue;

                    string username = GetPlayerUsernameFromNameLabel(obj);
                    if (!string.IsNullOrEmpty(username))
                    {
                        _usernameCache[ownerId] = username;
                        _logger.Info($"Cached username: OwnerId {ownerId} = '{username}'");
                    }
                }
                catch { }
            }
        }

        public string GetUsername(int ownerId)
        {
            if (_usernameCache.TryGetValue(ownerId, out string username))
            {
                return username;
            }
            return null;
        }

        public string GetLocalPlayerName()
        {
            if (_playerTransform != null)
            {
                string name = GetPlayerUsernameFromNameLabel(_playerTransform.gameObject);
                if (!string.IsNullOrEmpty(name))
                    return name;
            }
            return "You";
        }

        public void ClearCache()
        {
            _usernameCache.Clear();
            _playerTransform = null;
        }

        private int GetPlayerOwnerId(GameObject playerObj)
        {
            var playerControlType = _typeResolver.GetPlayerControlType();
            if (playerControlType == null) return -1;

            var components = playerObj.GetComponents<Component>();
            foreach (var comp in components)
            {
                if (comp == null) continue;
                try
                {
                    var il2cppType = comp.GetIl2CppType();
                    if (il2cppType?.Name == "PlayerControl")
                    {
                        var ownerIdProp = comp.GetType().GetProperty("OwnerId");
                        if (ownerIdProp != null)
                        {
                            return (int)ownerIdProp.GetValue(comp);
                        }
                    }
                }
                catch { }
            }
            return -1;
        }

        private string GetPlayerUsernameFromNameLabel(GameObject playerObj)
        {
            try
            {
                var nameLabelTransform = playerObj.transform.Find("Graphics (all characters start enabled)/Scarf Handler/scarves/name_label");
                if (nameLabelTransform == null) return null;

                var nameLabelObj = nameLabelTransform.gameObject;

                var components = nameLabelObj.GetComponentsInChildren<Component>(true);
                foreach (var comp in components)
                {
                    if (comp == null) continue;
                    try
                    {
                        var compType = comp.GetIl2CppType();
                        if (compType == null) continue;

                        var textProps = new[] { "text", "Text", "m_text" };
                        foreach (var propName in textProps)
                        {
                            var prop = comp.GetType().GetProperty(propName);
                            if (prop != null)
                            {
                                var val = prop.GetValue(comp);
                                if (val != null)
                                {
                                    string text = val.ToString();
                                    if (!string.IsNullOrEmpty(text) && text.Length >= 2 && text.Length < 50)
                                    {
                                        return text;
                                    }
                                }
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }
            return null;
        }
    }
}
