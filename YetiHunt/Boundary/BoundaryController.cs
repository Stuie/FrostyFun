using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using YetiHunt.Infrastructure;
using YetiHunt.Yeti;
using Object = UnityEngine.Object;

namespace YetiHunt.Boundary
{
    /// <summary>
    /// Controls boundary protection: disables fog, yetis, and out-of-bounds effects.
    /// </summary>
    public class BoundaryController : IBoundaryController
    {
        private const float YETI_SCAN_INTERVAL = 1.0f;

        private readonly IModLogger _logger;
        private readonly IYetiManager _yetiManager;

        private readonly List<GameObject> _disabledObjects = new List<GameObject>();
        private bool _protectionEnabled;
        private bool _initialScanDone;
        private float _lastYetiScan;

        public bool IsProtectionEnabled => _protectionEnabled;

        public BoundaryController(IModLogger logger, IYetiManager yetiManager)
        {
            _logger = logger;
            _yetiManager = yetiManager;
        }

        public void EnableProtection()
        {
            _protectionEnabled = true;
            _initialScanDone = false;
            _logger.Info("=== BOUNDARY PROTECTION: ON ===");
            _logger.Info("Disabling boundary effects (one-time scan)...");
            _logger.Info("Yetis will be checked every 1s");
            PerformFullScan();
        }

        public void DisableProtection()
        {
            _protectionEnabled = false;

            foreach (var obj in _disabledObjects)
            {
                if (obj != null)
                {
                    obj.SetActive(true);
                }
            }
            _disabledObjects.Clear();
            _initialScanDone = false;

            // Re-enable RenderSettings fog
            try { RenderSettings.fog = true; } catch { }

            _logger.Info("=== BOUNDARY PROTECTION: OFF ===");
            _logger.Info("Boundary protection disabled - effects re-enabled");
        }

        public void Update(float currentTime)
        {
            if (!_protectionEnabled) return;

            if (currentTime - _lastYetiScan > YETI_SCAN_INTERVAL)
            {
                _lastYetiScan = currentTime;

                if (_initialScanDone)
                {
                    DisableYetisOnly();
                }
                else
                {
                    PerformFullScan();
                }
            }
        }

        private void PerformFullScan()
        {
            _initialScanDone = true;

            try
            {
                var allObjects = Object.FindObjectsOfType<GameObject>();
                var huntYetiObjects = _yetiManager.ActiveYetis.Select(y => y.GameObject).ToHashSet();

                foreach (var obj in allObjects)
                {
                    if (obj == null) continue;

                    string nameLower = obj.name.ToLower();

                    // Disable yetis (except hunt yetis)
                    if (nameLower.Contains("yeti"))
                    {
                        if (!huntYetiObjects.Contains(obj) && obj.activeInHierarchy)
                        {
                            obj.SetActive(false);
                            if (!_disabledObjects.Contains(obj))
                            {
                                _disabledObjects.Add(obj);
                                _logger.Info($"Disabled boundary yeti: {obj.name}");
                            }
                        }
                    }

                    // Disable fog/boundary effects
                    if (nameLower.Contains("fog") || nameLower.Contains("mist") ||
                        nameLower.Contains("outofbound") || nameLower.Contains("out of bound") ||
                        nameLower.Contains("boundary warning") || nameLower.Contains("vignette") ||
                        nameLower.Contains("snowstorm") || nameLower.Contains("snow storm") ||
                        nameLower.Contains("blizzard"))
                    {
                        if (obj.activeInHierarchy)
                        {
                            obj.SetActive(false);
                            if (!_disabledObjects.Contains(obj))
                            {
                                _disabledObjects.Add(obj);
                                _logger.Info($"Disabled boundary effect: {obj.name}");
                            }
                        }

                        // Stop audio and particles
                        var audioSource = obj.GetComponent<AudioSource>();
                        if (audioSource != null)
                        {
                            audioSource.Stop();
                            audioSource.mute = true;
                        }

                        var particleSystem = obj.GetComponent<ParticleSystem>();
                        if (particleSystem != null)
                        {
                            particleSystem.Stop();
                            particleSystem.Clear();
                        }
                    }
                }

                // Disable MapBoundaryController component
                DisableMapBoundaryController();

                // Disable Unity's global fog
                try
                {
                    if (RenderSettings.fog)
                    {
                        RenderSettings.fog = false;
                        _logger.Info("Disabled RenderSettings.fog");
                    }
                }
                catch { }

                // Disable Fly Cam Overlay
                DisableFlyCamOverlay();

                // Disable Volume components
                DisableVolumeComponents();
            }
            catch (Exception ex)
            {
                _logger.Warning($"PerformFullScan error: {ex.Message}");
            }
        }

        private void DisableYetisOnly()
        {
            try
            {
                var huntYetiObjects = _yetiManager.ActiveYetis.Select(y => y.GameObject).ToHashSet();
                var yetiNames = new[] { "Yeti(Clone)", "yeti", "Yeti Manager", "YETI EFFECTS SPHERE", "yeti position" };

                foreach (var yetiName in yetiNames)
                {
                    var obj = GameObject.Find(yetiName);
                    if (obj != null && obj.activeInHierarchy)
                    {
                        if (!huntYetiObjects.Contains(obj))
                        {
                            obj.SetActive(false);
                            if (!_disabledObjects.Contains(obj))
                            {
                                _disabledObjects.Add(obj);
                                _logger.Info($"Disabled boundary yeti: {obj.name}");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"DisableYetisOnly error: {ex.Message}");
            }
        }

        private void DisableMapBoundaryController()
        {
            try
            {
                var mapBoundaryController = GameObject.Find("Map Boundary Controller [ DEMO ]");
                if (mapBoundaryController != null)
                {
                    var components = mapBoundaryController.GetComponents<Component>();
                    foreach (var comp in components)
                    {
                        if (comp == null) continue;
                        var il2cppType = comp.GetIl2CppType();
                        if (il2cppType?.Name == "MapBoundaryController")
                        {
                            var enabledProp = comp.GetType().GetProperty("enabled");
                            if (enabledProp != null && enabledProp.CanWrite)
                            {
                                var currentVal = (bool)enabledProp.GetValue(comp);
                                if (currentVal)
                                {
                                    enabledProp.SetValue(comp, false);
                                    _logger.Info("Disabled MapBoundaryController component");
                                }
                            }
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"MapBoundaryController disable error: {ex.Message}");
            }
        }

        private void DisableFlyCamOverlay()
        {
            try
            {
                var flyCamOverlay = GameObject.Find("Fly Cam Overlay (on)");
                if (flyCamOverlay != null && flyCamOverlay.activeInHierarchy)
                {
                    flyCamOverlay.SetActive(false);
                    if (!_disabledObjects.Contains(flyCamOverlay))
                    {
                        _disabledObjects.Add(flyCamOverlay);
                        _logger.Info("Disabled Fly Cam Overlay");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Fly Cam Overlay disable error: {ex.Message}");
            }
        }

        private void DisableVolumeComponents()
        {
            try
            {
                var volumeNames = new[] { "(Volume) Global Volume", "(Volume) Billboard Camera Volume" };

                foreach (var volName in volumeNames)
                {
                    var volObj = GameObject.Find(volName);
                    if (volObj != null && volObj.activeInHierarchy)
                    {
                        volObj.SetActive(false);
                        if (!_disabledObjects.Contains(volObj))
                        {
                            _disabledObjects.Add(volObj);
                            _logger.Info($"Disabled Volume: {volName}");
                        }
                    }
                }

                var allObjects = Object.FindObjectsOfType<GameObject>();
                foreach (var obj in allObjects)
                {
                    if (obj == null) continue;
                    string nameLower = obj.name.ToLower();

                    if (!nameLower.Contains("volume")) continue;

                    var components = obj.GetComponents<Component>();
                    foreach (var comp in components)
                    {
                        if (comp == null) continue;
                        var il2cppType = comp.GetIl2CppType();
                        if (il2cppType == null) continue;

                        if (il2cppType.Name == "Volume")
                        {
                            var enabledProp = comp.GetType().GetProperty("enabled");
                            if (enabledProp != null && enabledProp.CanWrite)
                            {
                                try
                                {
                                    var currentVal = (bool)enabledProp.GetValue(comp);
                                    if (currentVal)
                                    {
                                        enabledProp.SetValue(comp, false);
                                        _logger.Info($"Disabled Volume component on: {obj.name}");
                                    }
                                }
                                catch { }
                            }

                            var weightProp = comp.GetType().GetProperty("weight");
                            if (weightProp != null && weightProp.CanWrite)
                            {
                                try
                                {
                                    weightProp.SetValue(comp, 0f);
                                }
                                catch { }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Volume disable error: {ex.Message}");
            }
        }
    }
}
