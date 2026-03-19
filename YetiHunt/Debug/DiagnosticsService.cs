using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using YetiHunt.Infrastructure;
using YetiHunt.Players;
using YetiHunt.Yeti;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using Object = UnityEngine.Object;

namespace YetiHunt.Debug
{
    /// <summary>
    /// Provides diagnostic dump methods for debugging.
    /// </summary>
    public class DiagnosticsService : IDiagnosticsService
    {
        private readonly IModLogger _logger;
        private readonly ITypeResolver _typeResolver;
        private readonly IPlayerTracker _playerTracker;
        private readonly IYetiManager _yetiManager;

        private readonly List<Vector3> _recordedCorners = new List<Vector3>();

        public DiagnosticsService(IModLogger logger, ITypeResolver typeResolver, IPlayerTracker playerTracker, IYetiManager yetiManager)
        {
            _logger = logger;
            _typeResolver = typeResolver;
            _playerTracker = playerTracker;
            _yetiManager = yetiManager;
        }

        public void DumpMapInfo()
        {
            _logger.Info("=== MAP INFO DUMP ===");

            var filterWords = new[] {
                "fence", "chair", "lift", "tree", "rock", "snow", "terrain", "ground",
                "mountain", "lodge", "building", "pole", "sign", "light", "lamp",
                "bush", "plant", "grass", "path", "road", "rail", "cable", "wire",
                "post", "barrier", "wall", "floor", "roof", "window", "door",
                "bench", "table", "trash", "bin", "flag", "banner", "decoration",
                "particle", "effect", "audio", "sound", "ambient", "wind", "weather",
                "collider", "trigger", "spawn", "point", "volume", "zone", "area",
                "reflection", "probe", "shadow", "directional", "spot", "point"
            };

            _logger.Info("--- Types containing map/minimap/radar/compass ---");
            try
            {
                var assembly = Assembly.Load("Assembly-CSharp");
                var keywords = new[] { "map", "minimap", "radar", "compass", "hud", "overlay", "waypoint", "marker", "icon", "indicator" };

                foreach (var type in assembly.GetTypes())
                {
                    string typeLower = type.Name.ToLower();
                    bool matches = keywords.Any(k => typeLower.Contains(k));

                    if (matches)
                    {
                        _logger.Info($"  {type.Namespace}.{type.Name}");

                        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                        {
                            if (method.Name.StartsWith("get_") || method.Name.StartsWith("set_")) continue;
                            var parms = string.Join(", ", method.GetParameters().Select(p => p.ParameterType.Name));
                            _logger.Info($"    .{method.Name}({parms})");
                        }

                        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                        {
                            _logger.Info($"    [{prop.PropertyType.Name}] {prop.Name}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Assembly search failed: {ex.Message}");
            }

            _logger.Info("=== END MAP INFO DUMP ===");
        }

        public void DumpMapCoordinateDebug()
        {
            _logger.Info("=== MAP COORDINATE DEBUG ===");

            var playerTransform = _playerTracker.LocalPlayerTransform;
            if (playerTransform != null)
            {
                Vector3 playerPos = playerTransform.position;
                _logger.Info($"Player world position: {playerPos}");
            }
            else
            {
                _logger.Info("Player: not found");
            }

            _logger.Info("=== END MAP COORDINATE DEBUG ===");
        }

        public void DumpPlayerInfo()
        {
            _logger.Info("=== PLAYER INFO DUMP ===");

            foreach (var obj in Object.FindObjectsOfType<GameObject>())
            {
                if (obj == null || !obj.name.Contains("Player Networked")) continue;

                _logger.Info($"\n--- {obj.name} ---");

                int ownerId = -1;
                var components = obj.GetComponents<Component>();
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
                                ownerId = (int)ownerIdProp.GetValue(comp);
                                _logger.Info($"OwnerId (from PlayerControl): {ownerId}");
                            }
                        }
                    }
                    catch { }
                }

                _logger.Info("Looking for name_label...");
                var nameLabelTransform = obj.transform.Find("Graphics (all characters start enabled)/Scarf Handler/scarves/name_label");
                if (nameLabelTransform != null)
                {
                    var nameLabelObj = nameLabelTransform.gameObject;
                    _logger.Info($"  Found name_label! Active: {nameLabelObj.activeSelf}");

                    var labelComponents = nameLabelObj.GetComponents<Component>();
                    _logger.Info($"  Components on name_label:");
                    foreach (var comp in labelComponents)
                    {
                        if (comp == null) continue;
                        try
                        {
                            var compType = comp.GetIl2CppType();
                            _logger.Info($"    {compType?.Name ?? comp.GetType().Name}");
                        }
                        catch { }
                    }
                }
                else
                {
                    _logger.Info("  name_label not found at expected path");
                }
            }

            _logger.Info("=== END PLAYER DUMP ===");
        }

        public void DumpSnowballAndPlayerInfo()
        {
            _logger.Info("=== SNOWBALL & PLAYER HIT INFO ===");

            _logger.Info("--- Snowballs in scene ---");
            int snowballCount = 0;
            foreach (var obj in Object.FindObjectsOfType<GameObject>())
            {
                if (obj == null) continue;
                if ((obj.name.Contains("Snowball") || obj.name.Contains("snowball") ||
                     obj.name.Contains("Projectile") || obj.name.Contains("projectile")) &&
                    !obj.name.Contains("Snowman"))
                {
                    snowballCount++;
                    _logger.Info($"Found: {obj.name} at {obj.transform.position}");
                    _logger.Info($"  Layer: {obj.layer} ({LayerMask.LayerToName(obj.layer)})");
                }
            }
            _logger.Info($"Total snowballs found: {snowballCount}");

            _logger.Info("--- Hunt Yetis ---");
            foreach (var yeti in _yetiManager.ActiveYetis)
            {
                if (yeti.GameObject == null)
                {
                    _logger.Info("  (destroyed)");
                    continue;
                }
                _logger.Info($"  Yeti at {yeti.GameObject.transform.position}");
            }

            _logger.Info("=== END DUMP ===");
        }

        public void DumpYetiClassInfo()
        {
            _logger.Info("=== YETI CLASS INFO ===");

            var yetiType = _typeResolver.GetYetiType();
            if (yetiType == null)
            {
                _logger.Warning("Yeti type not found");
                return;
            }

            _logger.Info("--- Fields ---");
            foreach (var field in yetiType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                _logger.Info($"  {field.FieldType.Name} {field.Name}");
            }

            _logger.Info("--- Properties ---");
            foreach (var prop in yetiType.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                string access = (prop.CanRead ? "get" : "") + (prop.CanWrite ? "/set" : "");
                _logger.Info($"  {prop.PropertyType.Name} {prop.Name} [{access}]");
            }

            _logger.Info("--- Methods ---");
            foreach (var method in yetiType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (method.Name.StartsWith("get_") || method.Name.StartsWith("set_")) continue;
                var parameters = string.Join(", ", method.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"));
                _logger.Info($"  {method.ReturnType.Name} {method.Name}({parameters})");
            }

            _logger.Info("=== END YETI CLASS INFO ===");
        }

        public void DumpSnowballClassInfo()
        {
            _logger.Info("=== SNOWBALL CLASS INFO ===");

            var snowballType = _typeResolver.GetSnowballType();
            if (snowballType == null)
            {
                _logger.Warning("Snowball type not found");
                return;
            }

            _logger.Info("--- Fields ---");
            foreach (var field in snowballType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                _logger.Info($"  {field.FieldType.Name} {field.Name}");
            }

            _logger.Info("--- Properties ---");
            foreach (var prop in snowballType.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                string access = (prop.CanRead ? "get" : "") + (prop.CanWrite ? "/set" : "");
                _logger.Info($"  {prop.PropertyType.Name} {prop.Name} [{access}]");
            }

            _logger.Info("--- Methods ---");
            foreach (var method in snowballType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (method.Name.StartsWith("get_") || method.Name.StartsWith("set_")) continue;
                var parameters = string.Join(", ", method.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"));
                _logger.Info($"  {method.ReturnType.Name} {method.Name}({parameters})");
            }

            _logger.Info("=== END SNOWBALL CLASS INFO ===");
        }

        public void DumpAllYetiAnimatorState()
        {
            _logger.Info("=== ALL YETI ANIMATOR STATE ===");

            var yetiType = _typeResolver.GetYetiType();

            foreach (var obj in Object.FindObjectsOfType<GameObject>())
            {
                if (obj == null || obj.name != "Yeti(Clone)") continue;

                _logger.Info($"--- Yeti at {obj.transform.position} ---");

                var animator = obj.GetComponent<Animator>();
                if (animator == null)
                {
                    _logger.Info("  No Animator component");
                    continue;
                }

                _logger.Info($"  Animator enabled: {animator.enabled}");
                _logger.Info($"  Animator speed: {animator.speed}");

                for (int i = 0; i < animator.parameterCount; i++)
                {
                    var param = animator.GetParameter(i);
                    string value = param.type switch
                    {
                        AnimatorControllerParameterType.Float => animator.GetFloat(param.nameHash).ToString("F2"),
                        AnimatorControllerParameterType.Int => animator.GetInteger(param.nameHash).ToString(),
                        AnimatorControllerParameterType.Bool => animator.GetBool(param.nameHash).ToString(),
                        AnimatorControllerParameterType.Trigger => "(trigger)",
                        _ => "?"
                    };
                    _logger.Info($"  Param '{param.name}' ({param.type}): {value}");
                }
            }

            _logger.Info("=== END YETI ANIMATOR STATE ===");
        }

        public void RecordCornerCoordinate()
        {
            var playerTransform = _playerTracker.LocalPlayerTransform;
            if (playerTransform == null)
            {
                _logger.Warning("Cannot record corner - player not found");
                return;
            }

            Vector3 pos = playerTransform.position;
            _recordedCorners.Add(pos);
            _logger.Info($"=== CORNER {_recordedCorners.Count} RECORDED ===");
            _logger.Info($"  World Position: ({pos.x:F2}, {pos.y:F2}, {pos.z:F2})");
            _logger.Info("  Now press Ctrl+8 to see all recorded corners");
        }

        public void ShowRecordedCorners()
        {
            _logger.Info($"=== RECORDED CORNERS ({_recordedCorners.Count}) ===");

            if (_recordedCorners.Count == 0)
            {
                _logger.Info("No corners recorded yet. Use Ctrl+6 to record.");
                return;
            }

            for (int i = 0; i < _recordedCorners.Count; i++)
            {
                var pos = _recordedCorners[i];
                _logger.Info($"  Corner {i + 1}: ({pos.x:F2}, {pos.y:F2}, {pos.z:F2})");
            }

            if (_recordedCorners.Count >= 2)
            {
                float minX = _recordedCorners.Min(p => p.x);
                float maxX = _recordedCorners.Max(p => p.x);
                float minZ = _recordedCorners.Min(p => p.z);
                float maxZ = _recordedCorners.Max(p => p.z);

                float boundsX = maxX - minX;
                float boundsZ = maxZ - minZ;
                float centerX = (minX + maxX) / 2f;
                float centerZ = (minZ + maxZ) / 2f;

                _logger.Info($"--- Calculated from corners ---");
                _logger.Info($"  Min: ({minX:F2}, {minZ:F2})");
                _logger.Info($"  Max: ({maxX:F2}, {maxZ:F2})");
                _logger.Info($"  Bounds: ({boundsX:F2}, {boundsZ:F2})");
                _logger.Info($"  Center: ({centerX:F2}, {centerZ:F2})");
            }

            _logger.Info("Use Ctrl+6 at each corner to record more.");
        }
    }
}
