using MelonLoader;
using UnityEngine;
using System;
using System.Linq;
using System.Reflection;
using FrostyFun.Shared.Il2Cpp;
using FrostyFun.Shared.Logging;
using FrostyFun.Shared.Players;
using FrostyFun.Shared.UI;
using RespawnFlags.Services;
using RespawnFlags.UI;
using Object = UnityEngine.Object;

namespace RespawnFlags
{
    public class RespawnFlagsMod : MelonMod
    {
        private PlayerTeleporter _teleporter;
        private SpawnPointService _spawnPointService;
        private RespawnUI _ui;
        private PlayerInputBlocker _inputBlocker;

        private string _currentScene = "";
        private CursorSnapshot _cursorSnapshot;

        // CapsLock double-tap detection
        private float _lastCapsLockTime = 0f;
        private const float DoubleTapThreshold = 0.4f;

        public override void OnInitializeMelon()
        {
            var logger = new MelonLoggerAdapter(Melon<RespawnFlagsMod>.Logger);
            var typeResolver = new Il2CppTypeResolver(logger);
            _teleporter = new PlayerTeleporter(logger, typeResolver);
            _spawnPointService = new SpawnPointService(Melon<RespawnFlagsMod>.Logger);
            _ui = new RespawnUI();
            _inputBlocker = new PlayerInputBlocker(logger);

            _spawnPointService.LoadHistory();

            Melon<RespawnFlagsMod>.Logger.Msg("Respawn Flags loaded!");
            Melon<RespawnFlagsMod>.Logger.Msg("  F8 = Toggle spawn point UI");
            Melon<RespawnFlagsMod>.Logger.Msg("  CapsLock (double-tap) = Quick respawn");
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            _currentScene = sceneName;
            if (_ui.IsVisible) CloseUI();
            _inputBlocker.Reset();
            _spawnPointService.Reset();
            Melon<RespawnFlagsMod>.Logger.Msg($"Scene loaded: {sceneName}");
        }

        public override void OnUpdate()
        {
            // Deferred spawn point scanning
            if (!_spawnPointService.IsScanned)
                _spawnPointService.TryScanFixedPoints();

            // Periodically check for user marker changes
            _spawnPointService.UpdateUserMarker();

            // Show eviction confirmation if needed
            if (_spawnPointService.HasPendingEviction)
            {
                _ui.ShowEvictionConfirmation(
                    _spawnPointService.PendingEvictName,
                    _spawnPointService.ConfirmEviction,
                    _spawnPointService.CancelEviction);
                if (!_ui.IsVisible) OpenUI();
            }

            // F8 = toggle UI, Ctrl+F8 = debug dump
            if (Input.GetKeyDown(KeyCode.F8))
            {
                if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
                {
                    DumpRaceAndUIInfo();
                }
                else
                {
                    if (_ui.IsVisible)
                        CloseUI();
                    else
                        OpenUI();
                }
            }

            // CapsLock double-tap = quick respawn
            if (Input.GetKeyDown(KeyCode.CapsLock))
            {
                float now = Time.unscaledTime;
                if (now - _lastCapsLockTime < DoubleTapThreshold)
                {
                    var lastPoint = _spawnPointService.GetQuickRespawnPoint();
                    if (lastPoint != null)
                    {
                        _teleporter.TeleportTo(lastPoint.Value.Position, Quaternion.identity, leaveRaceFirst: true);
                        Melon<RespawnFlagsMod>.Logger.Msg($"Quick respawn: {lastPoint.Value.Name}");
                    }
                    _lastCapsLockTime = 0f;
                }
                else
                {
                    _lastCapsLockTime = now;
                }
            }
        }

        public override void OnLateUpdate()
        {
            if (_ui.IsVisible)
                CursorState.ShowFree();
        }

        public override void OnGUI()
        {
            if (!_ui.IsVisible) return;

            // Consume Escape
            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
            {
                CloseUI();
                Event.current.Use();
                return;
            }

            var points = _spawnPointService.GetAllSpawnPoints();
            var lastUsed = _spawnPointService.GetLastUsedPoint();
            _ui.Draw(points, lastUsed, OnSpawnPointSelected, CloseUI,
                _spawnPointService.RemoveMarker, _spawnPointService.RenameMarker,
                _spawnPointService.MarkerCount);
        }

        private void OnSpawnPointSelected(SpawnPoint point)
        {
            _teleporter.TeleportTo(point.Position, Quaternion.identity, leaveRaceFirst: true);
            _spawnPointService.SetLastUsedPoint(point);
            CloseUI();
        }

        private void DumpRaceAndUIInfo()
        {
            var logger = Melon<RespawnFlagsMod>.Logger;
            logger.Msg("=== RespawnFlags Debug Dump ===");

            var assembly = Assembly.Load("Assembly-CSharp");
            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;

            // Dump PlayerRacingController (found on PlayerControl.racingController)
            logger.Msg("--- PlayerRacingController ---");
            try
            {
                var prcType = assembly.GetTypes().FirstOrDefault(t => t.Name == "PlayerRacingController");
                if (prcType != null)
                {
                    logger.Msg($"  Type: {prcType.Namespace}.{prcType.Name}");
                    logger.Msg($"  Base: {prcType.BaseType?.Name}");
                    foreach (var method in prcType.GetMethods(flags))
                    {
                        if (method.Name.StartsWith("NetworkInitialize") || method.Name.StartsWith("RpcWriter") || method.Name.StartsWith("RpcReader")) continue;
                        var parms = string.Join(", ", method.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"));
                        logger.Msg($"  .{method.Name}({parms})");
                    }
                    foreach (var prop in prcType.GetProperties(flags))
                        logger.Msg($"  [{prop.PropertyType.Name}] {prop.Name}");
                    foreach (var field in prcType.GetFields(flags))
                        logger.Msg($"  <{field.FieldType.Name}> {field.Name}");
                }
                else
                {
                    logger.Msg("  Type not found");
                }
            }
            catch (Exception ex)
            {
                logger.Warning($"PlayerRacingController scan failed: {ex.Message}");
            }

            // Dump Leave Race button onClick listeners
            logger.Msg("--- Leave Race Button onClick ---");
            try
            {
                var leaveBtn = GameObject.Find(
                    "(Canvas) Out of Game (on)/(Canvas) Pause Menu (off)/Content/Secondary Background/(Button) Leave Race (on / off)");
                if (leaveBtn != null)
                {
                    var button = leaveBtn.GetComponent<UnityEngine.UI.Button>();
                    if (button != null)
                    {
                        var onClick = button.onClick;
                        int count = onClick.GetPersistentEventCount();
                        logger.Msg($"  Persistent listener count: {count}");
                        for (int i = 0; i < count; i++)
                        {
                            var target = onClick.GetPersistentTarget(i);
                            var methodName = onClick.GetPersistentMethodName(i);
                            string targetType = target != null ? target.GetIl2CppType()?.Name ?? target.GetType().Name : "null";
                            string targetName = (target as Component)?.gameObject.name ?? target?.ToString() ?? "null";
                            logger.Msg($"  [{i}] target={targetType} ({targetName})  method={methodName}");
                        }
                    }
                }
                else
                {
                    logger.Msg("  Button not found");
                }
            }
            catch (Exception ex)
            {
                logger.Warning($"Leave Race button scan failed: {ex.Message}");
            }

            // Also scan for types with "Racing" in the name
            logger.Msg("--- Types containing 'Racing' ---");
            try
            {
                foreach (var type in assembly.GetTypes())
                {
                    if (type.Name.Contains("Racing") && !type.Name.StartsWith("_") && !type.Name.Contains("d__"))
                    {
                        logger.Msg($"  {type.Namespace}.{type.Name}");
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Warning($"Racing type scan failed: {ex.Message}");
            }

            logger.Msg("=== END Debug Dump ===");
        }

        private static string GetHierarchyPath(Transform t)
        {
            string path = t.name;
            while (t.parent != null)
            {
                t = t.parent;
                path = t.name + "/" + path;
            }
            return path;
        }

        private void OpenUI()
        {
            _cursorSnapshot = CursorState.Snapshot();
            CursorState.ShowFree();
            _inputBlocker.Disable();
            _ui.Open();
        }

        private void CloseUI()
        {
            _inputBlocker.Restore();
            CursorState.Restore(_cursorSnapshot);
            _ui.Close();
        }
    }
}
