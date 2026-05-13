using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MelonLoader;
using UnityEngine;
using UnityEngine.SceneManagement;
using Il2CppInterop.Runtime.InteropTypes;
using Object = UnityEngine.Object;

namespace SceneLoader.Services
{
    public class PlayerMigration
    {
        private readonly MelonLogger.Instance _logger;

        // Player object references
        private GameObject _playerObj;
        private GameObject _cinemachineObj;

        // Cached reflection for TeleportPlayer (same pattern as TeleportService)
        private Type _playerControlType;
        private PropertyInfo _teleportControllerProperty;
        private MethodInfo _teleportPlayerMethod;
        private bool _resolved;

        // State for return
        private Vector3 _originalPlayerPosition;
        public Vector3 OriginalPosition => _originalPlayerPosition;
        private readonly List<GameObject> _hiddenRootObjects = new();
        private readonly List<GameObject> _spawnedObjects = new();
        private readonly HashSet<int> _excludedFromHide = new();
        private bool _isMigrated;

        // Boundary controller state
        private Component _boundaryComponent;
        private bool _boundaryWasEnabled;

        // Camera occlusion culling state - we disable while in custom scene
        // because the game's pre-baked occlusion data culls our spawned/loaded objects
        private readonly List<(Camera cam, bool wasEnabled)> _camerasOcclusionState = new();

        // Lodge furniture we've borrowed and moved to the gazebo. Tracked so we
        // can return them to their original parent + local position on exit.
        private struct FurnitureRecord
        {
            public GameObject Obj;
            public Transform OriginalParent;
            public Vector3 OriginalLocalPos;
            public Quaternion OriginalLocalRot;
            public bool OriginalActiveSelf;
            public Vector3 TargetOffset;
            public float TargetYaw;
        }
        private readonly List<FurnitureRecord> _movedFurniture = new();

        public int CapturedFurnitureCount => _movedFurniture.Count;

        // Items to grab from the lodge and place around the gazebo.
        // Each entry: (path, offset from gazebo center, Y rotation in degrees)
        // The shops' default front-facing direction is +X (their original camera was at +X
        // relative to their position), so rotating Y by 180 makes them face -X.
        private static readonly (string path, Vector3 offset, float yaw)[] LODGE_FURNITURE_LAYOUT = new[]
        {
            // Single chest (character/clothing/cosmetics) at the west "wall", facing east into the gazebo
            ("World/Lodge/(Interact) Inventory Chest (2)",  new Vector3(-10f, 0.6f,  0f),  90f),
            // Sled Customization in the center as a "table" - other items can have their backs to it
            ("World/Lodge/(Interact) Sled Customization",   new Vector3(  0f, 0.6f,  0f),   0f),
            // Pair 1: Shops sleds + hats back-to-back at the north end
            ("World/Lodge/Shops/Shop (sleds)",              new Vector3( -2f, 0.6f,  9f), 180f), // faces -X
            ("World/Lodge/Shops/Shop (hats)",               new Vector3(  2f, 0.6f,  9f),   0f), // faces +X
            // Shop (props): south end, back facing the central sled customization "table"
            ("World/Lodge/Shops/Shop (props)",              new Vector3(  0f, 0.6f, -8f), 180f), // faces -X
        };

        public bool IsMigrated => _isMigrated;

        // Custom area spawns at the player's position (no need to go far if world is hidden)
        private Vector3 _customAreaOrigin;

        // Root objects that are safe to hide (visual/world geometry only).
        // We keep "Directional Light" so the bundle scene has lighting -
        // bundles often don't preserve shader/lighting references properly.
        private static readonly HashSet<string> HideableNames = new()
        {
            "World",
            "Terrains",
            "General Snow",
            "Snowstorm Snow",
            "SPAWN POINTS",
            "Audio",
            "(VCAM) Starting Cam",
        };

        // Prefixes of objects safe to hide. Broadened from "Ski Lift Chair"
        // to "Ski Lift" to catch any cables/towers/hubs that might exist at
        // scene root (defensive — may not be necessary).
        private static readonly string[] HideablePrefixes = { "Ski Lift" };

        public PlayerMigration(MelonLogger.Instance logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Exclude a set of GameObjects from <see cref="HideWorldObjects"/> so
        /// they remain active during the custom area session. Used to "hijack"
        /// real ski lift chairs (which are real NetworkObjects with working
        /// Seat components) and reposition them onto our trains. Cleared on
        /// <see cref="ReturnToGame"/>.
        /// </summary>
        public void AddHideExclusions(IEnumerable<GameObject> objs)
        {
            if (objs == null) return;
            foreach (var o in objs)
                if (o != null) _excludedFromHide.Add(o.GetInstanceID());
        }

        public bool FindPlayer()
        {
            _playerObj = GameObject.Find("Player Networked(Clone)");
            _cinemachineObj = GameObject.Find("CinemachineCamera (makes parent null on start)");

            if (_playerObj == null)
            {
                _logger.Warning("Player object not found");
                return false;
            }

            _logger.Msg($"Player found at {_playerObj.transform.position}");
            return true;
        }

        /// <summary>
        /// Hides the game world, spawns a custom ground plane far away, and teleports the player there.
        /// Stays in the same scene to avoid crashing FishNet NetworkObjects.
        /// </summary>
        public bool EnterCustomArea(bool spawnProceduralContent = true)
        {
            if (_isMigrated)
            {
                _logger.Warning("Already in custom area");
                return true;
            }

            if (!FindPlayer()) return false;

            _originalPlayerPosition = _playerObj.transform.position;
            // Place custom area at the player's current position (stays within map bounds)
            _customAreaOrigin = new Vector3(
                _originalPlayerPosition.x,
                _originalPlayerPosition.y - 1, // ground surface slightly below player
                _originalPlayerPosition.z);
            _logger.Msg($"Saved original position: {_originalPlayerPosition}");
            _logger.Msg($"Custom area origin: {_customAreaOrigin}");

            try
            {
                // Step 1: Disable boundary
                DisableMapBoundary();

                // Step 1.5: Capture lodge furniture references BEFORE hiding world.
                // GameObject.Find skips inactive objects so we must do this while the
                // world is still active. (Skipped for procedural mode - no gazebo to use.)
                if (!spawnProceduralContent)
                    CaptureLodgeFurniture();

                // Step 2: Hide visual world objects, disable occlusion culling
                HideWorldObjects();
                DisableOcclusionCulling();

                // Step 3: Spawn custom ground at the player's location
                // (skip when loading from bundle - bundle provides its own scene contents)
                if (spawnProceduralContent)
                    SpawnCustomArea();

                // For procedural mode, teleport to the spawned ground.
                // For bundle mode, the caller will teleport to the bundle's SpawnPoint.
                if (spawnProceduralContent)
                {
                    var targetPos = _customAreaOrigin + new Vector3(0, 2, 0);
                    if (!TeleportPlayer(targetPos))
                    {
                        _logger.Warning("TeleportPlayer failed, falling back to direct transform set");
                        ResetPlayerPhysics();
                        _playerObj.transform.position = targetPos;
                    }
                    _logger.Msg($"Teleported player to {_playerObj.transform.position}");
                    DumpSpawnedObjects();
                }

                _isMigrated = true;
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error($"EnterCustomArea failed: {ex.GetType().Name}: {ex.Message}");
                // Try to restore on failure
                RestoreWorldObjects();
                CleanupSpawnedObjects();
                return false;
            }
        }

        /// <summary>
        /// Returns the player to the game world.
        /// </summary>
        public bool ReturnToGame()
        {
            if (!_isMigrated)
            {
                _logger.Warning("Not in custom area");
                return true;
            }

            try
            {
                _logger.Msg("Returning to game...");

                // Step 1: Cleanup our spawned objects
                CleanupSpawnedObjects();

                // Step 2: Teleport player back
                if (_playerObj != null)
                {
                    if (!TeleportPlayer(_originalPlayerPosition))
                    {
                        ResetPlayerPhysics();
                        _playerObj.transform.position = _originalPlayerPosition;
                    }
                    _logger.Msg($"Restored player to {_originalPlayerPosition}");
                }

                // Step 3: Restore lodge furniture FIRST (back to lodge parent),
                // then unhide world (so they end up active again under World/Lodge),
                // then restore boundary + occlusion.
                RestoreLodgeFurniture();
                RestoreWorldObjects();
                EnableMapBoundary();
                RestoreOcclusionCulling();

                _isMigrated = false;
                _logger.Msg("Returned to game successfully");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error($"ReturnToGame failed: {ex.Message}");
                // Force reset state even on error
                _isMigrated = false;
                return false;
            }
        }

        private void SpawnCustomArea()
        {
            // Find a URP-compatible material from an existing game object
            Material urpMaterial = FindURPMaterial();

            _logger.Msg("Spawning custom ground (Cube with BoxCollider)...");
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "SceneLoader_Ground";
            float groundThickness = 2f;
            ground.transform.position = _customAreaOrigin - new Vector3(0, groundThickness / 2f, 0);
            ground.transform.localScale = new Vector3(200, groundThickness, 200);
            ground.layer = 10; // Terrain layer
            _spawnedObjects.Add(ground);

            ApplyMaterial(ground, urpMaterial, new Color(0.35f, 0.55f, 0.35f));

            _logger.Msg($"  Ground position: {ground.transform.position}");
            _logger.Msg($"  Ground scale: {ground.transform.localScale}");
            _logger.Msg($"  Ground layer: {ground.layer} ({LayerMask.LayerToName(ground.layer)})");

            // Directional light (since we hid the game's)
            var lightObj = new GameObject("SceneLoader_Light");
            var light = lightObj.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.95f, 0.9f);
            light.intensity = 1.5f;
            lightObj.transform.rotation = Quaternion.Euler(50, -30, 0);
            _spawnedObjects.Add(lightObj);

            // Reference markers (sit on top of ground)
            float markerY = _customAreaOrigin.y + 0.5f;
            SpawnMarker(new Vector3(_customAreaOrigin.x + 10, markerY, _customAreaOrigin.z), Color.red, "SceneLoader_MarkerX", urpMaterial);
            SpawnMarker(new Vector3(_customAreaOrigin.x, markerY, _customAreaOrigin.z + 10), Color.blue, "SceneLoader_MarkerZ", urpMaterial);
            SpawnMarker(new Vector3(_customAreaOrigin.x, markerY, _customAreaOrigin.z), Color.yellow, "SceneLoader_MarkerOrigin", urpMaterial);

            _logger.Msg($"Spawned custom area at {_customAreaOrigin} ({_spawnedObjects.Count} objects)");
        }

        private Material FindURPMaterial()
        {
            // Try to find URP Lit shader
            try
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader != null)
                {
                    _logger.Msg("Found URP/Lit shader");
                    return new Material(shader);
                }

                shader = Shader.Find("Universal Render Pipeline/Simple Lit");
                if (shader != null)
                {
                    _logger.Msg("Found URP/Simple Lit shader");
                    return new Material(shader);
                }

                shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader != null)
                {
                    _logger.Msg("Found URP/Unlit shader");
                    return new Material(shader);
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Shader.Find failed: {ex.Message}");
            }

            // Fallback: grab a material from a game object that's still visible
            try
            {
                var renderers = Object.FindObjectsOfType<MeshRenderer>();
                foreach (var r in renderers)
                {
                    if (r != null && r.material != null && r.gameObject.activeInHierarchy)
                    {
                        _logger.Msg($"Borrowing material from: {r.gameObject.name} (shader: {r.material.shader?.name})");
                        return new Material(r.material);
                    }
                }
            }
            catch { }

            _logger.Warning("No URP material found, objects may be invisible");
            return null;
        }

        private void ApplyMaterial(GameObject obj, Material baseMaterial, Color color)
        {
            try
            {
                var renderer = obj.GetComponent<MeshRenderer>();
                if (renderer == null) return;

                if (baseMaterial != null)
                {
                    renderer.material = new Material(baseMaterial);
                    renderer.material.color = color;
                }
                else
                {
                    renderer.material.color = color;
                }
            }
            catch { }
        }

        private void SpawnMarker(Vector3 position, Color color, string name, Material baseMaterial)
        {
            var marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            marker.name = name;
            marker.transform.position = position;
            _spawnedObjects.Add(marker);
            ApplyMaterial(marker, baseMaterial, color);
        }

        /// <summary>
        /// Spawns a ring of tall bright-orange poles around the player's start
        /// position. Each pole is grounded by raycasting downward against the
        /// (still-active) game terrain at the time of spawning. The fence is in
        /// world coords so it stays put regardless of bundle alignment.
        /// Multiple radii are tried to find the actual lodge boundary.
        /// </summary>
        private void SpawnLodgeBoundaryFence()
        {
            try
            {
                Material urpMat = FindURPMaterial();

                // Spawn rings at several radii - the user can see which ring matches the actual boundary
                SpawnFenceRing(_originalPlayerPosition, 30f, 24, 6f, new Color(1f, 0.20f, 0f), urpMat, "30");
                SpawnFenceRing(_originalPlayerPosition, 60f, 32, 6f, new Color(1f, 0.45f, 0f), urpMat, "60");
                SpawnFenceRing(_originalPlayerPosition, 100f, 40, 6f, new Color(1f, 0.75f, 0f), urpMat, "100");
                _logger.Msg("Spawned lodge boundary fence rings at radii 30/60/100");
            }
            catch (Exception ex)
            {
                _logger.Warning($"SpawnLodgeBoundaryFence failed: {ex.Message}");
            }
        }

        private void SpawnFenceRing(Vector3 center, float radius, int poleCount, float poleHeight, Color color, Material mat, string label)
        {
            for (int i = 0; i < poleCount; i++)
            {
                float angle = (i / (float)poleCount) * Mathf.PI * 2f;
                float x = center.x + Mathf.Cos(angle) * radius;
                float z = center.z + Mathf.Sin(angle) * radius;

                // Raycast from high above down to find ground level (real terrain still active)
                float groundY = center.y;
                var ray = new Ray(new Vector3(x, center.y + 200f, z), Vector3.down);
                if (Physics.Raycast(ray, out var hit, 500f, ~0, QueryTriggerInteraction.Ignore))
                {
                    groundY = hit.point.y;
                }

                var pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                pole.name = $"LodgeFence_R{label}_{i}";
                pole.transform.position = new Vector3(x, groundY + poleHeight / 2f, z);
                // Cylinder default: radius 0.5, height 2 => scale (0.6, h/2, 0.6) for 0.3 radius
                pole.transform.localScale = new Vector3(0.6f, poleHeight / 2f, 0.6f);
                pole.layer = 0;
                ApplyMaterial(pole, mat, color);
                _spawnedObjects.Add(pole);
            }
        }

        private void ResetPlayerPhysics()
        {
            try
            {
                var rb = _playerObj.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                    _logger.Msg($"  Reset Rigidbody velocity (isKinematic={rb.isKinematic}, useGravity={rb.useGravity})");
                }
                else
                {
                    _logger.Msg("  No Rigidbody found on player");
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"  Could not reset physics: {ex.Message}");
            }
        }

        private void DumpSpawnedObjects()
        {
            _logger.Msg("--- Spawned Object Diagnostics ---");
            foreach (var obj in _spawnedObjects)
            {
                if (obj == null) continue;
                _logger.Msg($"  {obj.name}: pos={obj.transform.position} scale={obj.transform.localScale} layer={obj.layer}");

                var colliders = obj.GetComponents<Collider>();
                foreach (var col in colliders)
                {
                    if (col == null) continue;
                    _logger.Msg($"    Collider: {col.GetType().Name} enabled={col.enabled} isTrigger={col.isTrigger} bounds={col.bounds}");
                }
            }

            // Also dump player state
            if (_playerObj != null)
            {
                _logger.Msg($"  Player pos: {_playerObj.transform.position}");
                _logger.Msg($"  Player layer: {_playerObj.layer} ({LayerMask.LayerToName(_playerObj.layer)})");

                var playerColliders = _playerObj.GetComponents<Collider>();
                foreach (var col in playerColliders)
                {
                    if (col == null) continue;
                    _logger.Msg($"    Player collider: {col.GetType().Name} enabled={col.enabled} bounds={col.bounds}");
                }

                // Check what layers the game terrain used
                try
                {
                    for (int i = 0; i < 32; i++)
                    {
                        string layerName = LayerMask.LayerToName(i);
                        if (!string.IsNullOrEmpty(layerName))
                            _logger.Msg($"    Layer {i}: \"{layerName}\"");
                    }
                }
                catch { }
            }
            _logger.Msg("--- End Diagnostics ---");
        }

        private void HideWorldObjects()
        {
            _hiddenRootObjects.Clear();

            var activeScene = SceneManager.GetActiveScene();
            if (!activeScene.IsValid() || !activeScene.isLoaded)
            {
                _logger.Warning("Active scene not valid for hiding");
                return;
            }

            // Group similar names (e.g. "Ski Lift Chair(Clone)") into counts
            var nameCounts = new Dictionary<string, int>();
            var rootObjects = activeScene.GetRootGameObjects();
            foreach (var obj in rootObjects)
            {
                if (obj == null || !obj.activeSelf) continue;
                if (_excludedFromHide.Contains(obj.GetInstanceID())) continue;

                if (ShouldHide(obj.name))
                {
                    obj.SetActive(false);
                    _hiddenRootObjects.Add(obj);
                    nameCounts.TryGetValue(obj.name, out int c);
                    nameCounts[obj.name] = c + 1;
                }
            }

            // Log unique items individually, group repeats with a count
            foreach (var kvp in nameCounts)
            {
                if (kvp.Value == 1)
                    _logger.Msg($"  Hidden: \"{kvp.Key}\"");
                else
                    _logger.Msg($"  Hidden: \"{kvp.Key}\" x{kvp.Value}");
            }
            _logger.Msg($"Hidden {_hiddenRootObjects.Count} world objects total");
            LogRemainingActiveSkiLiftObjects();
        }

        /// <summary>
        /// After the regular hide pass, scan all GameObjects in the scene and
        /// log any whose name suggests they're ski-lift-related but are still
        /// active. If anything leaks through (e.g., a cable LineRenderer
        /// parented somewhere unexpected), this lets us see exactly what to
        /// add to the hide rules. Capped at 25 lines to avoid log spam.
        /// </summary>
        private void LogRemainingActiveSkiLiftObjects()
        {
            try
            {
                var allObjs = UnityEngine.Object.FindObjectsOfType<GameObject>();
                int leaks = 0;
                foreach (var go in allObjs)
                {
                    if (go == null || !go.activeInHierarchy) continue;
                    if (_excludedFromHide.Contains(go.GetInstanceID())) continue;
                    string n = go.name;
                    if (n.IndexOf("Ski Lift", StringComparison.OrdinalIgnoreCase) < 0 &&
                        n.IndexOf("ski-lift", StringComparison.OrdinalIgnoreCase) < 0 &&
                        n.IndexOf("ski_lift", StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    // Walk the hierarchy path
                    string path = n;
                    var t = go.transform.parent;
                    while (t != null) { path = t.name + "/" + path; t = t.parent; }

                    _logger.Msg($"  (still active) ski-lift-related: \"{path}\"");
                    leaks++;
                    if (leaks >= 25) { _logger.Msg("  ... (cap, more remain)"); break; }
                }
                if (leaks == 0)
                    _logger.Msg("  No ski-lift-related objects remain active.");
            }
            catch (Exception ex)
            {
                _logger.Warning($"  LogRemainingActiveSkiLiftObjects failed: {ex.Message}");
            }
        }

        private static bool ShouldHide(string name)
        {
            if (HideableNames.Contains(name))
                return true;

            foreach (var prefix in HideablePrefixes)
            {
                if (name.StartsWith(prefix))
                    return true;
            }

            return false;
        }

        private void RestoreWorldObjects()
        {
            int restored = 0;
            foreach (var obj in _hiddenRootObjects)
            {
                if (obj != null)
                {
                    obj.SetActive(true);
                    restored++;
                }
            }
            _hiddenRootObjects.Clear();
            _excludedFromHide.Clear();
            _logger.Msg($"Restored {restored} world objects");
        }

        private void DisableMapBoundary()
        {
            try
            {
                var boundaryObj = GameObject.Find("Map Boundary Controller [ DEMO ]");
                if (boundaryObj == null)
                {
                    _logger.Msg("MapBoundaryController not found");
                    return;
                }

                var components = boundaryObj.GetComponents<Component>();
                foreach (var comp in components)
                {
                    if (comp == null) continue;
                    var il2cppType = comp.GetIl2CppType();
                    if (il2cppType?.Name == "MapBoundaryController")
                    {
                        var behaviour = comp.TryCast<Behaviour>();
                        if (behaviour != null && behaviour.enabled)
                        {
                            _boundaryComponent = comp;
                            _boundaryWasEnabled = true;
                            behaviour.enabled = false;
                            _logger.Msg("Disabled MapBoundaryController");
                        }
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Failed to disable MapBoundaryController: {ex.Message}");
            }
        }

        private void EnableMapBoundary()
        {
            if (_boundaryComponent != null && _boundaryWasEnabled)
            {
                try
                {
                    var behaviour = _boundaryComponent.TryCast<Behaviour>();
                    if (behaviour != null)
                    {
                        behaviour.enabled = true;
                        _logger.Msg("Re-enabled MapBoundaryController");
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Failed to re-enable MapBoundaryController: {ex.Message}");
                }
            }
            _boundaryComponent = null;
            _boundaryWasEnabled = false;
        }

        /// <summary>
        /// Capture references to lodge furniture BEFORE the world is hidden.
        /// GameObject.Find skips inactive objects, so we have to find them while
        /// World is still active. The actual move happens later once we know the
        /// gazebo's world position (after the bundle scene aligns).
        /// </summary>
        public void CaptureLodgeFurniture()
        {
            _movedFurniture.Clear();
            foreach (var (path, offset, yaw) in LODGE_FURNITURE_LAYOUT)
            {
                var obj = GameObject.Find(path);
                if (obj == null)
                {
                    _logger.Warning($"Lodge furniture not found at capture time: {path}");
                    continue;
                }

                _movedFurniture.Add(new FurnitureRecord
                {
                    Obj = obj,
                    OriginalParent = obj.transform.parent,
                    OriginalLocalPos = obj.transform.localPosition,
                    OriginalLocalRot = obj.transform.localRotation,
                    OriginalActiveSelf = obj.activeSelf,
                    TargetOffset = offset,
                    TargetYaw = yaw,
                });
            }
            _logger.Msg($"Captured {_movedFurniture.Count} lodge furniture references");
        }

        /// <summary>
        /// Detach captured furniture from World/Lodge and place around the gazebo.
        /// Call only after CaptureLodgeFurniture() has been invoked while the world
        /// was still active.
        /// </summary>
        public void MoveLodgeFurnitureToGazebo(Vector3 gazeboCenter)
        {
            int moved = 0;
            foreach (var rec in _movedFurniture)
            {
                if (rec.Obj == null) continue;

                // Detach from World/Lodge (which is hidden) - now becomes a scene root
                rec.Obj.transform.SetParent(null, worldPositionStays: false);
                rec.Obj.transform.position = gazeboCenter + rec.TargetOffset;
                rec.Obj.transform.rotation = Quaternion.Euler(0, rec.TargetYaw, 0);
                if (!rec.Obj.activeSelf) rec.Obj.SetActive(true);
                moved++;
            }
            _logger.Msg($"Moved {moved} lodge furniture items to gazebo at {gazeboCenter}");
        }

        public void RestoreLodgeFurniture()
        {
            int restored = 0;
            foreach (var rec in _movedFurniture)
            {
                if (rec.Obj == null) continue;
                try
                {
                    rec.Obj.transform.SetParent(rec.OriginalParent, worldPositionStays: false);
                    rec.Obj.transform.localPosition = rec.OriginalLocalPos;
                    rec.Obj.transform.localRotation = rec.OriginalLocalRot;
                    if (rec.Obj.activeSelf != rec.OriginalActiveSelf)
                        rec.Obj.SetActive(rec.OriginalActiveSelf);
                    restored++;
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Failed to restore lodge furniture: {ex.Message}");
                }
            }
            _movedFurniture.Clear();
            _logger.Msg($"Restored {restored} lodge furniture items");
        }

        private void DisableOcclusionCulling()
        {
            _camerasOcclusionState.Clear();
            try
            {
                var cameras = Object.FindObjectsOfType<Camera>();
                int disabled = 0;
                foreach (var cam in cameras)
                {
                    if (cam == null) continue;
                    bool wasEnabled = cam.useOcclusionCulling;
                    _camerasOcclusionState.Add((cam, wasEnabled));
                    if (wasEnabled)
                    {
                        cam.useOcclusionCulling = false;
                        disabled++;
                    }
                }
                _logger.Msg($"Disabled occlusion culling on {disabled} cameras");
            }
            catch (Exception ex)
            {
                _logger.Warning($"DisableOcclusionCulling failed: {ex.Message}");
            }
        }

        private void RestoreOcclusionCulling()
        {
            int restored = 0;
            foreach (var (cam, wasEnabled) in _camerasOcclusionState)
            {
                try
                {
                    if (cam != null && wasEnabled)
                    {
                        cam.useOcclusionCulling = true;
                        restored++;
                    }
                }
                catch { }
            }
            _camerasOcclusionState.Clear();
            _logger.Msg($"Restored occlusion culling on {restored} cameras");
        }

        private void CleanupSpawnedObjects()
        {
            foreach (var obj in _spawnedObjects)
            {
                if (obj != null)
                    Object.Destroy(obj);
            }
            _spawnedObjects.Clear();
            _logger.Msg("Cleaned up spawned objects");
        }

        /// <summary>
        /// Public teleport for use after loading an additive scene with a spawn point.
        /// </summary>
        public bool TeleportTo(Vector3 position)
        {
            if (_playerObj == null) FindPlayer();
            if (_playerObj == null) return false;
            return TeleportPlayer(position);
        }

        private bool TeleportPlayer(Vector3 position)
        {
            try
            {
                if (!_resolved)
                    ResolveTypes();

                if (_teleportPlayerMethod == null || _playerControlType == null)
                {
                    _logger.Warning("Teleport types not resolved");
                    return false;
                }

                // Find PlayerControl component and cast it
                object playerControl = null;
                var components = _playerObj.GetComponents<Component>();
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
                    _logger.Warning("Could not find/cast PlayerControl");
                    return false;
                }

                var teleportController = _teleportControllerProperty?.GetValue(playerControl);
                if (teleportController == null)
                {
                    _logger.Warning("teleportationController is null");
                    return false;
                }

                _logger.Msg($"Calling TeleportPlayer({position})");
                _teleportPlayerMethod.Invoke(teleportController, new object[] { position, Quaternion.identity });
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error($"TeleportPlayer failed: {ex.Message}");
                return false;
            }
        }

        private void ResolveTypes()
        {
            _resolved = true;
            try
            {
                var assembly = Assembly.Load("Assembly-CSharp");
                _playerControlType = assembly.GetTypes().FirstOrDefault(t => t.Name == "PlayerControl");

                if (_playerControlType == null)
                {
                    _logger.Warning("PlayerControl type not found");
                    return;
                }

                _teleportControllerProperty = _playerControlType.GetProperty("teleportationController")
                    ?? _playerControlType.GetProperty("TeleportationController");

                if (_teleportControllerProperty != null)
                {
                    var controllerType = _teleportControllerProperty.PropertyType;
                    _teleportPlayerMethod = controllerType.GetMethod("TeleportPlayer");
                }

                _logger.Msg($"Teleport types resolved: controller={_teleportControllerProperty != null} " +
                           $"method={_teleportPlayerMethod != null}");
            }
            catch (Exception ex)
            {
                _logger.Error($"ResolveTypes failed: {ex.Message}");
            }
        }

        public void Reset()
        {
            _isMigrated = false;
            _playerObj = null;
            _cinemachineObj = null;
            _hiddenRootObjects.Clear();
            _spawnedObjects.Clear();
            _boundaryComponent = null;
            _boundaryWasEnabled = false;
            _camerasOcclusionState.Clear();
        }
    }
}
