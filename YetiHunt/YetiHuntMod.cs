using MelonLoader;
using UnityEngine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Object = UnityEngine.Object;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;

[assembly: MelonInfo(typeof(YetiHunt.YetiHuntMod), "YetiHunt", "1.0.0", "FrostyFun")]
[assembly: MelonGame("The Sledding Corporation", "Sledding Game Demo")]

namespace YetiHunt
{
    /// <summary>
    /// YetiHunt - A Battle Royale-style game mode where players hunt a yeti.
    ///
    /// Debug Keys:
    /// - F10: Start/stop YetiHunt round
    /// - F11: Spawn a yeti near player (test)
    /// - F5: Send test chat message
    /// </summary>
    public class YetiHuntMod : MelonMod
    {
        // Game state machine
        public enum GameState
        {
            Idle,
            Countdown,
            Hunting,
            RoundEnd
        }

        private GameState _currentState = GameState.Idle;
        private string _currentScene = "";

        // Timing
        private float _stateStartTime = 0f;
        private const float COUNTDOWN_DURATION = 3f;
        private const float ROUND_END_DURATION = 5f;
        private const float HUNT_TIMEOUT = 120f;

        // Cached types and objects
        private Type _yetiManagerType = null;
        private Type _yetiType = null;
        private Type _chatManagerType = null;
        private Type _playerControlType = null;
        private Type _playerTeleportControllerType = null;
        private Type _snowballType = null;
        private object _yetiManagerInstance = null;
        private MethodInfo _spawnYetiMethod = null;
        private PropertyInfo _teleportControllerProperty = null;
        private MethodInfo _teleportPlayerMethod = null;
        private bool _typesInitialized = false;

        // UI
        private Texture2D _bgTexture;
        private Texture2D _winnerBgTexture;
        private bool _texturesInitialized = false;
        private string _lastWinnerName = null;

        // Player tracking
        private Transform _playerTransform = null;

        // Snowball hit detection
        private HashSet<int> _hitSnowballs = new HashSet<int>(); // Avoid double-counting hits
        private float _lastDebugLogTime = 0f;

        // Collider settings (tuned to match yeti body)
        private float _colliderRadius = 2f;
        private float _colliderHeight = 6.5f;
        private float _colliderYOffset = 4.5f;
        private GameObject _debugCapsule = null;
        private CapsuleCollider _yetiCollider = null;

        public override void OnInitializeMelon()
        {
            Melon<YetiHuntMod>.Logger.Msg("YetiHunt loaded!");
            Melon<YetiHuntMod>.Logger.Msg("  F10 - Start/stop round");
            Melon<YetiHuntMod>.Logger.Msg("  F11 - Spawn yeti near player");
            Melon<YetiHuntMod>.Logger.Msg("  F9  - Dump teleport/spawn related types and UI");
            Melon<YetiHuntMod>.Logger.Msg("  F8  - Test teleport to random sky position");
            Melon<YetiHuntMod>.Logger.Msg("  F7  - Dump Snowball class info");
            Melon<YetiHuntMod>.Logger.Msg("  F4  - Dump snowball/player hit info");
            Melon<YetiHuntMod>.Logger.Msg("  F3  - Dump Yeti class info");
            Melon<YetiHuntMod>.Logger.Msg("  F2  - Dump all yeti animator state");
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            _currentScene = sceneName;
            Melon<YetiHuntMod>.Logger.Msg($"Scene: {sceneName}");

            if (_currentState != GameState.Idle)
            {
                Melon<YetiHuntMod>.Logger.Msg("Scene changed - resetting");
                _currentState = GameState.Idle;
            }

            // Reset cached objects (they may be invalid after scene change)
            _playerTransform = null;
            _yetiManagerInstance = null;
        }

        public override void OnUpdate()
        {
            // Track player
            if (_playerTransform == null)
            {
                var playerObj = GameObject.Find("Player Networked(Clone)");
                if (playerObj != null)
                {
                    _playerTransform = playerObj.transform;
                }
            }

            // Initialize types once
            if (!_typesInitialized)
            {
                InitializeTypes();
            }

            HandleInput();
            UpdateGameState();

            // Continuously control hunt yetis (keep them slow/wandering)
            ControlHuntYetis();

            // Track snowballs for hit detection
            TrackSnowballsAndDetectHits();
        }

        private void InitializeTypes()
        {
            _typesInitialized = true;

            try
            {
                var assembly = Assembly.Load("Assembly-CSharp");

                foreach (var type in assembly.GetTypes())
                {
                    if (type.Name == "YetiManager" && type.Namespace == "Il2Cpp")
                    {
                        _yetiManagerType = type;
                        _spawnYetiMethod = type.GetMethod("Server_SpawnYeti");
                        Melon<YetiHuntMod>.Logger.Msg($"Found YetiManager, SpawnYeti method: {_spawnYetiMethod != null}");
                    }
                    else if (type.Name == "Yeti" && type.Namespace == "Il2Cpp")
                    {
                        _yetiType = type;
                        Melon<YetiHuntMod>.Logger.Msg("Found Yeti type");
                    }
                    else if (type.Name == "ChatManager")
                    {
                        _chatManagerType = type;
                        Melon<YetiHuntMod>.Logger.Msg("Found ChatManager type");
                    }
                    else if (type.Name == "PlayerControl" && type.Namespace == "Il2Cpp")
                    {
                        _playerControlType = type;
                        _teleportControllerProperty = type.GetProperty("teleportationController");
                        Melon<YetiHuntMod>.Logger.Msg($"Found PlayerControl, teleportationController: {_teleportControllerProperty != null}");
                    }
                    else if (type.Name == "PlayerTeleportationController")
                    {
                        _playerTeleportControllerType = type;
                        _teleportPlayerMethod = type.GetMethod("TeleportPlayer");
                        Melon<YetiHuntMod>.Logger.Msg($"Found PlayerTeleportationController, TeleportPlayer: {_teleportPlayerMethod != null}");
                    }
                    else if (type.Name == "Snowball" && type.Namespace == "Il2Cpp")
                    {
                        _snowballType = type;
                        Melon<YetiHuntMod>.Logger.Msg("Found Snowball type");
                    }
                }
            }
            catch (Exception ex)
            {
                Melon<YetiHuntMod>.Logger.Error($"InitializeTypes failed: {ex.Message}");
            }
        }

        private void HandleInput()
        {
            if (Input.GetKeyDown(KeyCode.F10))
            {
                if (_currentState == GameState.Idle)
                    StartRound();
                else
                    StopRound();
            }

            if (Input.GetKeyDown(KeyCode.F11))
            {
                TestSpawnYeti();
            }

            if (Input.GetKeyDown(KeyCode.F4))
            {
                DumpSnowballAndPlayerInfo();
            }

            if (Input.GetKeyDown(KeyCode.F3))
            {
                DumpYetiClassInfo();
            }

            if (Input.GetKeyDown(KeyCode.F2))
            {
                DumpAllYetiAnimatorState();
            }

            if (Input.GetKeyDown(KeyCode.F9))
            {
                DumpTeleportationInfo();
            }

            if (Input.GetKeyDown(KeyCode.F8))
            {
                Melon<YetiHuntMod>.Logger.Msg("=== Testing Teleport ===");
                TeleportPlayerToSky();
            }

            if (Input.GetKeyDown(KeyCode.F7))
            {
                DumpSnowballClassInfo();
            }

            // Collider debug controls (keeping for fine-tuning)
            if (Input.GetKeyDown(KeyCode.UpArrow))
            {
                _colliderHeight += 0.5f;
                UpdateYetiCollider();
                Melon<YetiHuntMod>.Logger.Msg($"Height: {_colliderHeight}");
            }
            if (Input.GetKeyDown(KeyCode.DownArrow))
            {
                _colliderHeight = Mathf.Max(0.5f, _colliderHeight - 0.5f);
                UpdateYetiCollider();
                Melon<YetiHuntMod>.Logger.Msg($"Height: {_colliderHeight}");
            }
            if (Input.GetKeyDown(KeyCode.RightArrow))
            {
                _colliderRadius += 0.25f;
                UpdateYetiCollider();
                Melon<YetiHuntMod>.Logger.Msg($"Radius: {_colliderRadius}");
            }
            if (Input.GetKeyDown(KeyCode.LeftArrow))
            {
                _colliderRadius = Mathf.Max(0.25f, _colliderRadius - 0.25f);
                UpdateYetiCollider();
                Melon<YetiHuntMod>.Logger.Msg($"Radius: {_colliderRadius}");
            }
            if (Input.GetKeyDown(KeyCode.PageUp))
            {
                _colliderYOffset += 0.25f;
                UpdateYetiCollider();
                Melon<YetiHuntMod>.Logger.Msg($"Y Offset: {_colliderYOffset}");
            }
            if (Input.GetKeyDown(KeyCode.PageDown))
            {
                _colliderYOffset -= 0.25f;
                UpdateYetiCollider();
                Melon<YetiHuntMod>.Logger.Msg($"Y Offset: {_colliderYOffset}");
            }
            if (Input.GetKeyDown(KeyCode.P))
            {
                Melon<YetiHuntMod>.Logger.Msg($"=== COLLIDER VALUES ===");
                Melon<YetiHuntMod>.Logger.Msg($"  Radius: {_colliderRadius}");
                Melon<YetiHuntMod>.Logger.Msg($"  Height: {_colliderHeight}");
                Melon<YetiHuntMod>.Logger.Msg($"  Y Offset: {_colliderYOffset}");
                Melon<YetiHuntMod>.Logger.Msg($"=======================");
            }
        }

        private void UpdateGameState()
        {
            float elapsed = Time.time - _stateStartTime;

            switch (_currentState)
            {
                case GameState.Countdown:
                    if (elapsed >= COUNTDOWN_DURATION)
                        TransitionToHunting();
                    break;

                case GameState.Hunting:
                    if (elapsed >= HUNT_TIMEOUT)
                    {
                        Melon<YetiHuntMod>.Logger.Msg("Hunt timed out!");
                        TransitionToRoundEnd(null);
                    }
                    break;

                case GameState.RoundEnd:
                    if (elapsed >= ROUND_END_DURATION)
                        TransitionToIdle();
                    break;
            }
        }

        #region State Transitions

        private void StartRound()
        {
            Melon<YetiHuntMod>.Logger.Msg("=== STARTING YETI HUNT ===");
            _currentState = GameState.Countdown;
            _stateStartTime = Time.time;
        }

        private void StopRound()
        {
            Melon<YetiHuntMod>.Logger.Msg("=== ROUND STOPPED ===");
            _currentState = GameState.Idle;
            _lastWinnerName = null;
            DespawnHuntYetis();
            _hitSnowballs.Clear();
        }

        private void TransitionToHunting()
        {
            Melon<YetiHuntMod>.Logger.Msg("=== HUNT BEGINS! ===");
            _currentState = GameState.Hunting;
            _stateStartTime = Time.time;

            // Teleport player to random sky position
            TeleportPlayerToSky();

            // Spawn yeti when hunt begins
            SpawnYetiForHunt();
        }

        private void TeleportPlayerToSky()
        {
            if (_playerTransform == null)
            {
                Melon<YetiHuntMod>.Logger.Warning("No player to teleport");
                return;
            }

            if (_playerControlType == null || _teleportControllerProperty == null || _teleportPlayerMethod == null)
            {
                Melon<YetiHuntMod>.Logger.Warning("Teleportation types not initialized");
                return;
            }

            try
            {
                // Get PlayerControl component
                var playerObj = _playerTransform.gameObject;
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
                            var castMethod = typeof(Il2CppObjectBase).GetMethod("Cast").MakeGenericMethod(_playerControlType);
                            playerControl = castMethod.Invoke(comp, null);
                            break;
                        }
                    }
                    catch { }
                }

                if (playerControl == null)
                {
                    Melon<YetiHuntMod>.Logger.Warning("Could not find PlayerControl component");
                    return;
                }

                // Get the teleportation controller
                var teleportController = _teleportControllerProperty.GetValue(playerControl);
                if (teleportController == null)
                {
                    Melon<YetiHuntMod>.Logger.Warning("teleportationController is null");
                    return;
                }

                // Generate random position - spread across the map
                // Map is a mountain so height varies significantly
                // Spawn very high to avoid terrain, player will fall down
                float angle = UnityEngine.Random.Range(0f, 360f) * Mathf.Deg2Rad;
                float distance = UnityEngine.Random.Range(100f, 400f);
                Vector3 center = new Vector3(300f, 0f, 400f); // Approximate map center
                Vector3 targetPos = center + new Vector3(
                    Mathf.Cos(angle) * distance,
                    500f, // Very high to clear mountain peaks
                    Mathf.Sin(angle) * distance
                );

                // Face toward center
                Quaternion rotation = Quaternion.LookRotation((center - targetPos).normalized);

                Melon<YetiHuntMod>.Logger.Msg($"Teleporting player to {targetPos}");
                _teleportPlayerMethod.Invoke(teleportController, new object[] { targetPos, rotation });
            }
            catch (Exception ex)
            {
                Melon<YetiHuntMod>.Logger.Error($"Teleport failed: {ex.Message}");
            }
        }

        private void TransitionToRoundEnd(string winnerName)
        {
            if (winnerName != null)
                Melon<YetiHuntMod>.Logger.Msg($"=== {winnerName} WINS! ===");
            else
                Melon<YetiHuntMod>.Logger.Msg("=== NO WINNER ===");

            _lastWinnerName = winnerName;
            _currentState = GameState.RoundEnd;
            _stateStartTime = Time.time;
        }

        private void TransitionToIdle()
        {
            Melon<YetiHuntMod>.Logger.Msg("=== ROUND COMPLETE ===");
            _currentState = GameState.Idle;
            _lastWinnerName = null;
            DespawnHuntYetis();
            _hitSnowballs.Clear();
        }

        private void DespawnHuntYetis()
        {
            foreach (var yeti in _huntYetis)
            {
                if (yeti.GameObject != null)
                {
                    Melon<YetiHuntMod>.Logger.Msg($"Despawning yeti at {yeti.GameObject.transform.position}");
                    Object.Destroy(yeti.GameObject);
                }
            }
            _huntYetis.Clear();
        }

        #endregion

        #region Yeti Management

        // Track spawned yetis with their target positions
        private enum YetiState { Moving, Pausing, Turning }
        private class HuntYeti
        {
            public GameObject GameObject;
            public object YetiComponent; // The Il2Cpp Yeti component
            public MethodInfo MoveMethod; // MoveFixed_Direction method
            public Animator Animator;
            public int SpeedHash;
            public Vector3 TargetPosition;
            public Vector3 WanderCenter;
            public float StateTimer;
            public YetiState State;
            public Vector3 CurrentDirection;
            public Vector3 TargetDirection;
        }
        private List<HuntYeti> _huntYetis = new List<HuntYeti>();

        private object GetYetiManager()
        {
            if (_yetiManagerInstance != null) return _yetiManagerInstance;
            if (_yetiManagerType == null) return null;

            // Find the Yeti Manager GameObject
            var yetiManagerObj = GameObject.Find("Yeti Manager");
            if (yetiManagerObj == null)
            {
                Melon<YetiHuntMod>.Logger.Warning("Yeti Manager GameObject not found");
                return null;
            }

            // Get the YetiManager component
            var components = yetiManagerObj.GetComponents<Component>();
            foreach (var comp in components)
            {
                if (comp == null) continue;
                try
                {
                    var il2cppType = comp.GetIl2CppType();
                    if (il2cppType?.Name == "YetiManager")
                    {
                        // Cast to the actual type
                        var castMethod = typeof(Il2CppObjectBase).GetMethod("Cast").MakeGenericMethod(_yetiManagerType);
                        _yetiManagerInstance = castMethod.Invoke(comp, null);
                        Melon<YetiHuntMod>.Logger.Msg("Got YetiManager instance");
                        return _yetiManagerInstance;
                    }
                }
                catch { }
            }

            return null;
        }

        private void TestSpawnYeti()
        {
            Melon<YetiHuntMod>.Logger.Msg("=== Testing Yeti Spawn ===");

            if (_playerTransform == null)
            {
                Melon<YetiHuntMod>.Logger.Warning("No player found");
                return;
            }

            // Spawn yeti 20 units in front of player
            Vector3 spawnPos = _playerTransform.position + _playerTransform.forward * 20f;
            SpawnYetiAt(spawnPos);
        }

        private void SpawnYetiForHunt()
        {
            if (_playerTransform == null)
            {
                Melon<YetiHuntMod>.Logger.Warning("No player for yeti spawn");
                return;
            }

            // Spawn yeti at random location near player (since teleport isn't working yet)
            float angle = UnityEngine.Random.Range(0f, 360f) * Mathf.Deg2Rad;
            float distance = UnityEngine.Random.Range(30f, 60f);
            Vector3 spawnPos = _playerTransform.position + new Vector3(
                Mathf.Cos(angle) * distance,
                0,
                Mathf.Sin(angle) * distance
            );

            Melon<YetiHuntMod>.Logger.Msg($"Spawning hunt yeti {distance:F0}m from player");
            SpawnYetiAt(spawnPos);
        }

        private void SpawnYetiAt(Vector3 position)
        {
            Melon<YetiHuntMod>.Logger.Msg($"Spawning yeti at {position}");

            var manager = GetYetiManager();
            if (manager == null)
            {
                Melon<YetiHuntMod>.Logger.Warning("Could not get YetiManager");
                return;
            }

            if (_spawnYetiMethod == null)
            {
                Melon<YetiHuntMod>.Logger.Warning("Server_SpawnYeti method not found");
                return;
            }

            // Get existing yeti IDs before spawning
            var existingYetis = new HashSet<int>();
            foreach (var obj in Object.FindObjectsOfType<GameObject>())
            {
                if (obj != null && obj.name == "Yeti(Clone)")
                    existingYetis.Add(obj.GetInstanceID());
            }

            try
            {
                _spawnYetiMethod.Invoke(manager, new object[] { position });

                // Find the newly spawned yeti
                GameObject newYeti = null;
                foreach (var obj in Object.FindObjectsOfType<GameObject>())
                {
                    if (obj != null && obj.name == "Yeti(Clone)" && !existingYetis.Contains(obj.GetInstanceID()))
                    {
                        newYeti = obj;
                        break;
                    }
                }

                if (newYeti != null)
                {
                    Melon<YetiHuntMod>.Logger.Msg($"Found new yeti at {newYeti.transform.position}");

                    // Create tracking object
                    var huntYeti = new HuntYeti
                    {
                        GameObject = newYeti,
                        TargetPosition = position,
                        WanderCenter = position,
                        State = YetiState.Pausing,
                        StateTimer = 1f, // Start with a brief pause
                        CurrentDirection = newYeti.transform.forward,
                        TargetDirection = newYeti.transform.forward
                    };

                    // Disable the Yeti behaviour component to stop its AI, but keep reference for movement
                    DisableYetiBehaviour(newYeti, huntYeti);

                    // Track it for our custom control
                    _huntYetis.Add(huntYeti);
                }
                else
                {
                    Melon<YetiHuntMod>.Logger.Warning("Could not find spawned yeti");
                }
            }
            catch (Exception ex)
            {
                Melon<YetiHuntMod>.Logger.Error($"SpawnYeti failed: {ex.Message}");
            }
        }

        private void DisableYetiBehaviour(GameObject yetiObj, HuntYeti huntYeti)
        {
            if (_yetiType == null) return;

            var components = yetiObj.GetComponents<Component>();
            foreach (var comp in components)
            {
                if (comp == null) continue;
                try
                {
                    var il2cppType = comp.GetIl2CppType();
                    if (il2cppType?.Name == "Yeti")
                    {
                        // Cast to the Yeti type and store reference
                        var castMethod = typeof(Il2CppObjectBase).GetMethod("Cast").MakeGenericMethod(_yetiType);
                        huntYeti.YetiComponent = castMethod.Invoke(comp, null);
                        huntYeti.MoveMethod = _yetiType.GetMethod("MoveFixed_Direction");

                        // Get animator directly from GameObject and use parameter name
                        huntYeti.Animator = yetiObj.GetComponent<Animator>();
                        if (huntYeti.Animator != null)
                        {
                            huntYeti.SpeedHash = Animator.StringToHash("Speed");
                            Melon<YetiHuntMod>.Logger.Msg($"Got Yeti animator, Speed hash: {huntYeti.SpeedHash}");
                        }

                        // Disable the behaviour so it doesn't chase players
                        var behaviour = comp.TryCast<Behaviour>();
                        if (behaviour != null)
                        {
                            behaviour.enabled = false;
                            Melon<YetiHuntMod>.Logger.Msg("Disabled Yeti behaviour component!");
                        }
                        break;
                    }
                }
                catch (Exception ex)
                {
                    Melon<YetiHuntMod>.Logger.Warning($"Could not disable Yeti: {ex.Message}");
                }
            }

            // Try to disable/shrink the YETI EFFECTS SPHERE that blocks snowballs
            DisableYetiEffectsSphere(yetiObj);

            // Add a collider so snowballs can hit the yeti
            AddYetiCollider(yetiObj);
        }

        private void AddYetiCollider(GameObject yetiObj)
        {
            try
            {
                // Clean up any old debug capsule
                if (_debugCapsule != null)
                {
                    Object.Destroy(_debugCapsule);
                    _debugCapsule = null;
                }

                // Set yeti to Player layer (8) so snowball's CheckHitPlayer() recognizes it
                int playerLayer = 8;
                yetiObj.layer = playerLayer;
                Melon<YetiHuntMod>.Logger.Msg($"Set yeti to layer {playerLayer} (Player)");

                // Add a capsule collider that roughly matches yeti size
                // NOT a trigger - the snowball is already a trigger, so it will call OnTriggerEnter
                _yetiCollider = yetiObj.AddComponent<CapsuleCollider>();
                _yetiCollider.center = new Vector3(0, _colliderYOffset, 0);
                _yetiCollider.radius = _colliderRadius;
                _yetiCollider.height = _colliderHeight;
                _yetiCollider.isTrigger = false; // Solid collider - snowball trigger will detect this
                Melon<YetiHuntMod>.Logger.Msg($"Added CapsuleCollider to yeti (solid, radius={_colliderRadius}, height={_colliderHeight})");
            }
            catch (Exception ex)
            {
                Melon<YetiHuntMod>.Logger.Warning($"Could not add collider: {ex.Message}");
            }
        }

        private void CreateDebugCapsule(GameObject yetiObj)
        {
            try
            {
                // Create a primitive capsule for visualization
                _debugCapsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                _debugCapsule.name = "DebugYetiCollider";

                // Remove its collider (we just want the visual)
                var col = _debugCapsule.GetComponent<Collider>();
                if (col != null) Object.Destroy(col);

                // Make sure it's on a visible layer and active
                _debugCapsule.layer = 0; // Default layer
                _debugCapsule.SetActive(true);

                // Parent to yeti
                _debugCapsule.transform.SetParent(yetiObj.transform);
                _debugCapsule.transform.localPosition = new Vector3(0, _colliderYOffset, 0);
                _debugCapsule.transform.localRotation = Quaternion.identity;

                // Scale to match collider (Unity capsule is height 2, radius 0.5 by default)
                UpdateDebugCapsuleScale();

                // Try to make it visible with a simple bright material
                var renderer = _debugCapsule.GetComponent<MeshRenderer>();
                if (renderer != null)
                {
                    // Try different shaders that might work
                    Shader shader = Shader.Find("Unlit/Color");
                    if (shader == null) shader = Shader.Find("UI/Default");
                    if (shader == null) shader = Shader.Find("Sprites/Default");
                    if (shader == null) shader = Shader.Find("Standard");

                    if (shader != null)
                    {
                        var mat = new Material(shader);
                        mat.color = Color.magenta; // Bright pink, very visible
                        renderer.material = mat;
                        Melon<YetiHuntMod>.Logger.Msg($"Debug capsule using shader: {shader.name}");
                    }
                    else
                    {
                        Melon<YetiHuntMod>.Logger.Warning("No shader found for debug capsule");
                    }
                }

                Melon<YetiHuntMod>.Logger.Msg("Created debug capsule visualization");
            }
            catch (Exception ex)
            {
                Melon<YetiHuntMod>.Logger.Warning($"Could not create debug capsule: {ex.Message}");
            }
        }

        private void UpdateDebugCapsuleScale()
        {
            if (_debugCapsule == null) return;

            // Unity capsule primitive: height=2, radius=0.5
            // Scale to match our collider
            float scaleY = _colliderHeight / 2f;
            float scaleXZ = _colliderRadius / 0.5f;
            _debugCapsule.transform.localScale = new Vector3(scaleXZ, scaleY, scaleXZ);
            _debugCapsule.transform.localPosition = new Vector3(0, _colliderYOffset, 0);

        }

        private void UpdateYetiCollider()
        {
            if (_yetiCollider != null)
            {
                _yetiCollider.center = new Vector3(0, _colliderYOffset, 0);
                _yetiCollider.radius = _colliderRadius;
                _yetiCollider.height = _colliderHeight;
            }
            UpdateDebugCapsuleScale();
        }

        private void DisableYetiEffectsSphere(GameObject yetiObj)
        {
            // Search for the effects sphere in children using index-based iteration
            Transform yetiTransform = yetiObj.transform;
            for (int i = 0; i < yetiTransform.childCount; i++)
            {
                Transform child = yetiTransform.GetChild(i);
                if (child == null) continue;

                if (child.name.Contains("EFFECT") || child.name.Contains("SPHERE"))
                {
                    Melon<YetiHuntMod>.Logger.Msg($"Found child: {child.name}");

                    // Try to disable colliders on it
                    var colliders = child.GetComponents<Collider>();
                    foreach (var col in colliders)
                    {
                        if (col != null)
                        {
                            col.enabled = false;
                            Melon<YetiHuntMod>.Logger.Msg($"Disabled collider on {child.name}");
                        }
                    }
                }
            }

            // Also search the scene for YETI EFFECTS SPHERE objects near this yeti
            foreach (var obj in Object.FindObjectsOfType<GameObject>())
            {
                if (obj == null) continue;
                if (obj.name == "YETI EFFECTS SPHERE")
                {
                    float dist = Vector3.Distance(obj.transform.position, yetiObj.transform.position);
                    if (dist < 30f)
                    {
                        var colliders = obj.GetComponents<Collider>();
                        foreach (var col in colliders)
                        {
                            if (col != null)
                            {
                                col.enabled = false;
                                Melon<YetiHuntMod>.Logger.Msg($"Disabled collider on YETI EFFECTS SPHERE");
                            }
                        }

                        // Also try disabling the whole object or its renderer
                        obj.SetActive(false);
                        Melon<YetiHuntMod>.Logger.Msg("Disabled YETI EFFECTS SPHERE object");
                    }
                }
            }
        }

        private void ControlHuntYetis()
        {
            // Clean up destroyed yetis
            _huntYetis.RemoveAll(y => y.GameObject == null);

            foreach (var yeti in _huntYetis)
            {
                if (yeti.GameObject == null || yeti.YetiComponent == null || yeti.MoveMethod == null)
                    continue;

                Vector3 currentPos = yeti.GameObject.transform.position;
                yeti.StateTimer -= Time.deltaTime;

                switch (yeti.State)
                {
                    case YetiState.Moving:
                        HandleMovingState(yeti, currentPos);
                        break;

                    case YetiState.Pausing:
                        HandlePausingState(yeti);
                        break;

                    case YetiState.Turning:
                        HandleTurningState(yeti);
                        break;
                }
            }
        }

        private void HandleMovingState(HuntYeti yeti, Vector3 currentPos)
        {
            Vector3 toTarget = yeti.TargetPosition - currentPos;
            toTarget.y = 0;
            float distToTarget = toTarget.magnitude;

            // Reached target or timer expired - pause and pick new direction
            if (distToTarget < 3f || yeti.StateTimer <= 0f)
            {
                yeti.State = YetiState.Pausing;
                yeti.StateTimer = UnityEngine.Random.Range(1f, 3f); // Pause for 1-3 seconds

                // Set animation to idle
                if (yeti.Animator != null)
                    yeti.Animator.SetFloat(yeti.SpeedHash, 0f);
                return;
            }

            // Smoothly interpolate current direction toward target direction
            Vector3 desiredDirection = toTarget.normalized;
            yeti.CurrentDirection = Vector3.Lerp(yeti.CurrentDirection, desiredDirection, Time.deltaTime * 2f);

            // Move using the yeti's built-in method
            try
            {
                yeti.MoveMethod.Invoke(yeti.YetiComponent, new object[] { yeti.CurrentDirection, yeti.CurrentDirection });
            }
            catch { }

            // Update walk animation
            if (yeti.Animator != null)
                yeti.Animator.SetFloat(yeti.SpeedHash, 1f);
        }

        private void HandlePausingState(HuntYeti yeti)
        {
            // Actively stop movement by passing zero direction
            try
            {
                yeti.MoveMethod.Invoke(yeti.YetiComponent, new object[] { Vector3.zero, yeti.CurrentDirection });
            }
            catch { }

            // Stay idle during pause
            if (yeti.Animator != null)
                yeti.Animator.SetFloat(yeti.SpeedHash, 0f);

            if (yeti.StateTimer <= 0f)
            {
                // Pick new target
                float angle = UnityEngine.Random.Range(0f, 360f) * Mathf.Deg2Rad;
                float dist = UnityEngine.Random.Range(10f, 25f);
                yeti.TargetPosition = yeti.WanderCenter + new Vector3(
                    Mathf.Cos(angle) * dist,
                    0,
                    Mathf.Sin(angle) * dist
                );

                // Calculate new direction
                Vector3 toTarget = yeti.TargetPosition - yeti.GameObject.transform.position;
                toTarget.y = 0;
                yeti.TargetDirection = toTarget.normalized;

                // Transition to turning
                yeti.State = YetiState.Turning;
                yeti.StateTimer = 0.5f; // Turn for 0.5 seconds
            }
        }

        private void HandleTurningState(HuntYeti yeti)
        {
            // Smoothly turn toward new direction while stationary
            yeti.CurrentDirection = Vector3.Lerp(yeti.CurrentDirection, yeti.TargetDirection, Time.deltaTime * 4f);

            // Face the direction (move with zero magnitude just to rotate)
            try
            {
                yeti.MoveMethod.Invoke(yeti.YetiComponent, new object[] { Vector3.zero, yeti.CurrentDirection });
            }
            catch { }

            // Keep idle animation during turn
            if (yeti.Animator != null)
                yeti.Animator.SetFloat(yeti.SpeedHash, 0f);

            if (yeti.StateTimer <= 0f)
            {
                // Start moving
                yeti.State = YetiState.Moving;
                yeti.StateTimer = UnityEngine.Random.Range(4f, 8f); // Move for 4-8 seconds
            }
        }

        #endregion

        #region Teleportation Investigation

        private void DumpTeleportationInfo()
        {
            Melon<YetiHuntMod>.Logger.Msg("=== TELEPORTATION INFO DUMP ===");

            // Search for teleport/spawn related types in Assembly-CSharp
            Melon<YetiHuntMod>.Logger.Msg("--- Types containing teleport/spawn/lodge/warp ---");
            try
            {
                var assembly = Assembly.Load("Assembly-CSharp");
                var keywords = new[] { "teleport", "spawn", "lodge", "warp", "respawn", "checkpoint", "flag" };

                foreach (var type in assembly.GetTypes())
                {
                    string typeLower = type.Name.ToLower();
                    bool matches = keywords.Any(k => typeLower.Contains(k));

                    if (matches)
                    {
                        Melon<YetiHuntMod>.Logger.Msg($"  {type.Namespace}.{type.Name}");

                        // List methods
                        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                        {
                            if (method.Name.StartsWith("get_") || method.Name.StartsWith("set_")) continue;
                            var parms = string.Join(", ", method.GetParameters().Select(p => p.ParameterType.Name));
                            Melon<YetiHuntMod>.Logger.Msg($"    .{method.Name}({parms})");
                        }

                        // List properties
                        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                        {
                            Melon<YetiHuntMod>.Logger.Msg($"    [{prop.PropertyType.Name}] {prop.Name}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Melon<YetiHuntMod>.Logger.Error($"Assembly search failed: {ex.Message}");
            }

            // Search for GameObjects with teleport/lodge/spawn in name
            Melon<YetiHuntMod>.Logger.Msg("--- GameObjects containing teleport/lodge/spawn ---");
            var goKeywords = new[] { "teleport", "lodge", "spawn", "checkpoint", "flag", "respawn" };
            foreach (var obj in Object.FindObjectsOfType<GameObject>())
            {
                if (obj == null) continue;
                string nameLower = obj.name.ToLower();
                if (goKeywords.Any(k => nameLower.Contains(k)))
                {
                    Melon<YetiHuntMod>.Logger.Msg($"  {obj.name} at {obj.transform.position}");

                    // List components
                    var components = obj.GetComponents<Component>();
                    foreach (var comp in components)
                    {
                        if (comp == null) continue;
                        string typeName = GetComponentTypeName(comp);
                        Melon<YetiHuntMod>.Logger.Msg($"    - {typeName}");
                    }
                }
            }

            // Search for UI buttons with relevant text
            Melon<YetiHuntMod>.Logger.Msg("--- UI Buttons (searching for teleport/lodge) ---");
            try
            {
                // Find all Button components
                var buttonType = typeof(UnityEngine.UI.Button);
                var buttons = Object.FindObjectsOfType(Il2CppType.From(buttonType));

                foreach (var btnObj in buttons)
                {
                    var btn = btnObj.TryCast<UnityEngine.UI.Button>();
                    if (btn == null) continue;

                    string btnName = btn.gameObject.name.ToLower();

                    // Check if button or parent has lodge/teleport in name
                    bool relevant = btnName.Contains("lodge") || btnName.Contains("teleport") ||
                                   btnName.Contains("spawn") || btnName.Contains("menu");

                    // Also check for TMP text children
                    var tmpTexts = btn.GetComponentsInChildren<Component>();
                    string buttonText = "";
                    foreach (var comp in tmpTexts)
                    {
                        if (comp == null) continue;
                        var il2cppType = comp.GetIl2CppType();
                        if (il2cppType?.Name == "TMP_Text" || il2cppType?.Name == "TextMeshProUGUI")
                        {
                            // Try to get text property
                            var textProp = comp.GetType().GetProperty("text");
                            if (textProp != null)
                            {
                                buttonText = textProp.GetValue(comp)?.ToString() ?? "";
                                if (buttonText.ToLower().Contains("lodge") ||
                                    buttonText.ToLower().Contains("teleport"))
                                {
                                    relevant = true;
                                }
                            }
                        }
                    }

                    if (relevant || btn.gameObject.activeInHierarchy)
                    {
                        Melon<YetiHuntMod>.Logger.Msg($"  Button: {btn.gameObject.name}");
                        if (!string.IsNullOrEmpty(buttonText))
                            Melon<YetiHuntMod>.Logger.Msg($"    Text: {buttonText}");
                        Melon<YetiHuntMod>.Logger.Msg($"    Active: {btn.gameObject.activeInHierarchy}");

                        // Show onClick listener count
                        Melon<YetiHuntMod>.Logger.Msg($"    onClick listeners: {btn.onClick.GetPersistentEventCount()}");

                        // Try to get persistent call info
                        for (int i = 0; i < btn.onClick.GetPersistentEventCount(); i++)
                        {
                            var target = btn.onClick.GetPersistentTarget(i);
                            var method = btn.onClick.GetPersistentMethodName(i);
                            Melon<YetiHuntMod>.Logger.Msg($"      -> {target?.GetType().Name ?? "null"}.{method}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Melon<YetiHuntMod>.Logger.Warning($"Button search failed: {ex.Message}");
            }

            // Look at PlayerControl or similar player management classes
            Melon<YetiHuntMod>.Logger.Msg("--- Player-related types with position/teleport methods ---");
            try
            {
                var assembly = Assembly.Load("Assembly-CSharp");
                var playerKeywords = new[] { "player", "character", "avatar", "pawn" };

                foreach (var type in assembly.GetTypes())
                {
                    string typeLower = type.Name.ToLower();
                    if (!playerKeywords.Any(k => typeLower.Contains(k))) continue;

                    // Look for teleport/position/spawn methods
                    var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                    var relevantMethods = methods.Where(m =>
                        m.Name.ToLower().Contains("teleport") ||
                        m.Name.ToLower().Contains("spawn") ||
                        m.Name.ToLower().Contains("setposition") ||
                        m.Name.ToLower().Contains("warp") ||
                        m.Name.ToLower().Contains("respawn") ||
                        m.Name.ToLower().Contains("move") && m.Name.ToLower().Contains("to")
                    ).ToList();

                    if (relevantMethods.Count > 0)
                    {
                        Melon<YetiHuntMod>.Logger.Msg($"  {type.Namespace}.{type.Name}");
                        foreach (var m in relevantMethods)
                        {
                            var parms = string.Join(", ", m.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"));
                            Melon<YetiHuntMod>.Logger.Msg($"    .{m.Name}({parms})");
                        }
                    }
                }
            }
            catch { }

            // Check for SpawnManager or similar
            Melon<YetiHuntMod>.Logger.Msg("--- Manager singletons (spawn/game/level) ---");
            var managerNames = new[] { "SpawnManager", "GameManager", "LevelManager", "PlayerManager", "NetworkManager" };
            foreach (var mgrName in managerNames)
            {
                var obj = GameObject.Find(mgrName);
                if (obj != null)
                {
                    Melon<YetiHuntMod>.Logger.Msg($"  Found: {mgrName}");
                    var components = obj.GetComponents<Component>();
                    foreach (var comp in components)
                    {
                        if (comp == null) continue;
                        string typeName = GetComponentTypeName(comp);
                        Melon<YetiHuntMod>.Logger.Msg($"    - {typeName}");
                    }
                }
            }

            Melon<YetiHuntMod>.Logger.Msg("=== END TELEPORTATION DUMP ===");
        }

        #endregion

        #region Snowball Investigation

        private void DumpSnowballAndPlayerInfo()
        {
            Melon<YetiHuntMod>.Logger.Msg("=== SNOWBALL & PLAYER HIT INFO ===");

            // Find snowballs and dump their components
            Melon<YetiHuntMod>.Logger.Msg("--- Snowballs in scene ---");
            int snowballCount = 0;
            foreach (var obj in Object.FindObjectsOfType<GameObject>())
            {
                if (obj == null) continue;
                if ((obj.name.Contains("Snowball") || obj.name.Contains("snowball") ||
                     obj.name.Contains("Projectile") || obj.name.Contains("projectile")) &&
                    !obj.name.Contains("Snowman"))
                {
                    snowballCount++;
                    Melon<YetiHuntMod>.Logger.Msg($"Found: {obj.name} at {obj.transform.position}");
                    Melon<YetiHuntMod>.Logger.Msg($"  Layer: {obj.layer} ({LayerMask.LayerToName(obj.layer)})");
                    Melon<YetiHuntMod>.Logger.Msg($"  Tag: {obj.tag}");
                    Melon<YetiHuntMod>.Logger.Msg($"  Active: {obj.activeInHierarchy}");

                    var components = obj.GetComponents<Component>();
                    foreach (var comp in components)
                    {
                        if (comp == null) continue;
                        string typeName = GetComponentTypeName(comp);
                        Melon<YetiHuntMod>.Logger.Msg($"  - {typeName}");

                        // If it has a Rigidbody, log velocity
                        var rb = comp.TryCast<Rigidbody>();
                        if (rb != null)
                        {
                            Melon<YetiHuntMod>.Logger.Msg($"    velocity: {rb.linearVelocity}, isKinematic: {rb.isKinematic}");
                        }

                        // If it's a collider, log details
                        var col = comp.TryCast<Collider>();
                        if (col != null)
                        {
                            Melon<YetiHuntMod>.Logger.Msg($"    isTrigger: {col.isTrigger}, enabled: {col.enabled}");
                        }
                    }
                }
            }
            Melon<YetiHuntMod>.Logger.Msg($"Total snowballs found: {snowballCount}");

            // Find player and dump ALL components
            Melon<YetiHuntMod>.Logger.Msg("--- Player ALL components ---");
            var player = GameObject.Find("Player Networked(Clone)");
            if (player != null)
            {
                Melon<YetiHuntMod>.Logger.Msg($"Player layer: {player.layer} ({LayerMask.LayerToName(player.layer)})");

                var components = player.GetComponents<Component>();
                foreach (var comp in components)
                {
                    if (comp == null) continue;
                    string typeName = GetComponentTypeName(comp);
                    Melon<YetiHuntMod>.Logger.Msg($"  - {typeName}");
                }

                // Also check colliders
                var colliders = player.GetComponents<Collider>();
                foreach (var col in colliders)
                {
                    if (col == null) continue;
                    Melon<YetiHuntMod>.Logger.Msg($"  Collider: {col.GetType().Name}, isTrigger: {col.isTrigger}");
                }
            }

            // Check our hunt yetis
            Melon<YetiHuntMod>.Logger.Msg("--- Hunt Yetis ---");
            foreach (var yeti in _huntYetis)
            {
                if (yeti.GameObject == null)
                {
                    Melon<YetiHuntMod>.Logger.Msg("  (destroyed)");
                    continue;
                }
                Melon<YetiHuntMod>.Logger.Msg($"  Yeti at {yeti.GameObject.transform.position}");
                var colliders = yeti.GameObject.GetComponents<Collider>();
                foreach (var col in colliders)
                {
                    if (col == null) continue;
                    Melon<YetiHuntMod>.Logger.Msg($"    Collider: {col.GetType().Name}, isTrigger: {col.isTrigger}, enabled: {col.enabled}");
                }
            }

            // Search for any snowball-related types in Assembly-CSharp
            Melon<YetiHuntMod>.Logger.Msg("--- Snowball/Hit types in Assembly-CSharp ---");
            try
            {
                var assembly = Assembly.Load("Assembly-CSharp");
                foreach (var type in assembly.GetTypes())
                {
                    if (type.Name.ToLower().Contains("snowball") ||
                        type.Name.ToLower().Contains("projectile") ||
                        type.Name.ToLower().Contains("thrown") ||
                        type.Name.ToLower().Contains("hitbox") ||
                        type.Name.ToLower().Contains("hurtbox"))
                    {
                        Melon<YetiHuntMod>.Logger.Msg($"  {type.Namespace}.{type.Name}");

                        // List methods that might be collision-related
                        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic))
                        {
                            if (method.Name.Contains("Collision") || method.Name.Contains("Trigger") ||
                                method.Name.Contains("Hit") || method.Name.Contains("OnContact"))
                            {
                                Melon<YetiHuntMod>.Logger.Msg($"    .{method.Name}()");
                            }
                        }
                    }
                }
            }
            catch { }

            Melon<YetiHuntMod>.Logger.Msg("=== END DUMP ===");
        }

        private string GetComponentTypeName(Component comp)
        {
            try
            {
                var il2cppType = comp.GetIl2CppType();
                return il2cppType?.Name ?? comp.GetType().Name;
            }
            catch
            {
                return comp.GetType().Name;
            }
        }

        private void DumpAllYetiAnimatorState()
        {
            Melon<YetiHuntMod>.Logger.Msg("=== ALL YETI ANIMATOR STATE ===");

            foreach (var obj in Object.FindObjectsOfType<GameObject>())
            {
                if (obj == null || obj.name != "Yeti(Clone)") continue;

                Melon<YetiHuntMod>.Logger.Msg($"--- Yeti at {obj.transform.position} ---");

                // Get animator
                var animator = obj.GetComponent<Animator>();
                if (animator == null)
                {
                    Melon<YetiHuntMod>.Logger.Msg("  No Animator component");
                    continue;
                }

                Melon<YetiHuntMod>.Logger.Msg($"  Animator enabled: {animator.enabled}");
                Melon<YetiHuntMod>.Logger.Msg($"  Animator speed: {animator.speed}");

                // Dump all parameters
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
                    Melon<YetiHuntMod>.Logger.Msg($"  Param '{param.name}' ({param.type}): {value}");
                }

                // Get Yeti component and dump relevant properties
                if (_yetiType != null)
                {
                    var components = obj.GetComponents<Component>();
                    foreach (var comp in components)
                    {
                        if (comp == null) continue;
                        try
                        {
                            var il2cppType = comp.GetIl2CppType();
                            if (il2cppType?.Name == "Yeti")
                            {
                                var castMethod = typeof(Il2CppObjectBase).GetMethod("Cast").MakeGenericMethod(_yetiType);
                                var yetiComp = castMethod.Invoke(comp, null);

                                // Dump movement-related properties
                                var props = new[] { "speed", "movementMultiplier", "moveDirection", "nearestPlayer" };
                                foreach (var propName in props)
                                {
                                    var prop = _yetiType.GetProperty(propName);
                                    if (prop != null)
                                    {
                                        var val = prop.GetValue(yetiComp);
                                        Melon<YetiHuntMod>.Logger.Msg($"  {propName}: {val}");
                                    }
                                }

                                // Check if behaviour is enabled
                                var behaviour = comp.TryCast<Behaviour>();
                                if (behaviour != null)
                                {
                                    Melon<YetiHuntMod>.Logger.Msg($"  Behaviour.enabled: {behaviour.enabled}");
                                }
                                break;
                            }
                        }
                        catch { }
                    }
                }
            }

            Melon<YetiHuntMod>.Logger.Msg("=== END YETI ANIMATOR STATE ===");
        }

        private void DumpYetiClassInfo()
        {
            Melon<YetiHuntMod>.Logger.Msg("=== YETI CLASS INFO ===");

            if (_yetiType == null)
            {
                Melon<YetiHuntMod>.Logger.Warning("Yeti type not found");
                return;
            }

            // Dump fields
            Melon<YetiHuntMod>.Logger.Msg("--- Fields ---");
            foreach (var field in _yetiType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                Melon<YetiHuntMod>.Logger.Msg($"  {field.FieldType.Name} {field.Name}");
            }

            // Dump properties
            Melon<YetiHuntMod>.Logger.Msg("--- Properties ---");
            foreach (var prop in _yetiType.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                string access = (prop.CanRead ? "get" : "") + (prop.CanWrite ? "/set" : "");
                Melon<YetiHuntMod>.Logger.Msg($"  {prop.PropertyType.Name} {prop.Name} [{access}]");
            }

            // Dump methods (excluding property getters/setters and common inherited ones)
            Melon<YetiHuntMod>.Logger.Msg("--- Methods ---");
            foreach (var method in _yetiType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (method.Name.StartsWith("get_") || method.Name.StartsWith("set_")) continue;
                var parameters = string.Join(", ", method.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"));
                Melon<YetiHuntMod>.Logger.Msg($"  {method.ReturnType.Name} {method.Name}({parameters})");
            }

            Melon<YetiHuntMod>.Logger.Msg("=== END YETI CLASS INFO ===");
        }

        private void DumpSnowballClassInfo()
        {
            Melon<YetiHuntMod>.Logger.Msg("=== SNOWBALL CLASS INFO ===");

            if (_snowballType == null)
            {
                Melon<YetiHuntMod>.Logger.Warning("Snowball type not found");
                return;
            }

            // Dump fields
            Melon<YetiHuntMod>.Logger.Msg("--- Fields ---");
            foreach (var field in _snowballType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                Melon<YetiHuntMod>.Logger.Msg($"  {field.FieldType.Name} {field.Name}");
            }

            // Dump properties
            Melon<YetiHuntMod>.Logger.Msg("--- Properties ---");
            foreach (var prop in _snowballType.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                string access = (prop.CanRead ? "get" : "") + (prop.CanWrite ? "/set" : "");
                Melon<YetiHuntMod>.Logger.Msg($"  {prop.PropertyType.Name} {prop.Name} [{access}]");
            }

            // Dump methods (excluding property getters/setters)
            Melon<YetiHuntMod>.Logger.Msg("--- Methods ---");
            foreach (var method in _snowballType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (method.Name.StartsWith("get_") || method.Name.StartsWith("set_")) continue;
                var parameters = string.Join(", ", method.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"));
                Melon<YetiHuntMod>.Logger.Msg($"  {method.ReturnType.Name} {method.Name}({parameters})");
            }

            // Also try to find any snowballs in scene and dump their runtime values
            Melon<YetiHuntMod>.Logger.Msg("--- Live Snowballs ---");
            foreach (var obj in Object.FindObjectsOfType<GameObject>())
            {
                if (obj == null || !obj.name.Contains("Snowball")) continue;

                Melon<YetiHuntMod>.Logger.Msg($"  {obj.name} at {obj.transform.position}");

                var components = obj.GetComponents<Component>();
                foreach (var comp in components)
                {
                    if (comp == null) continue;
                    try
                    {
                        var il2cppType = comp.GetIl2CppType();
                        if (il2cppType?.Name == "Snowball")
                        {
                            var castMethod = typeof(Il2CppObjectBase).GetMethod("Cast").MakeGenericMethod(_snowballType);
                            var snowball = castMethod.Invoke(comp, null);

                            // Try to read owner-related properties
                            var ownerProps = new[] { "owner", "thrower", "player", "playerControl", "networkConnection", "Owner", "Thrower", "Player" };
                            foreach (var propName in ownerProps)
                            {
                                var prop = _snowballType.GetProperty(propName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.IgnoreCase);
                                if (prop != null)
                                {
                                    try
                                    {
                                        var val = prop.GetValue(snowball);
                                        Melon<YetiHuntMod>.Logger.Msg($"    {propName}: {val}");
                                    }
                                    catch { }
                                }
                            }
                            break;
                        }
                    }
                    catch { }
                }
            }

            Melon<YetiHuntMod>.Logger.Msg("=== END SNOWBALL CLASS INFO ===");
        }

        #endregion

        #region Snowball Hit Detection

        private void TrackSnowballsAndDetectHits()
        {
            if (_huntYetis.Count == 0) return;
            if (_currentState != GameState.Hunting) return;

            // Periodic debug log
            if (Time.time - _lastDebugLogTime > 3f)
            {
                _lastDebugLogTime = Time.time;
                var yetiPos = _huntYetis.Count > 0 && _huntYetis[0].GameObject != null
                    ? _huntYetis[0].GameObject.transform.position.ToString() : "N/A";
                Melon<YetiHuntMod>.Logger.Msg($"[Hit Debug] Yeti at {yetiPos}");
            }

            // Use Physics.OverlapCapsule to find snowballs touching the yeti
            foreach (var yeti in _huntYetis)
            {
                if (yeti.GameObject == null) continue;

                Vector3 yetiPos = yeti.GameObject.transform.position;
                Vector3 capsuleCenter = yetiPos + new Vector3(0, _colliderYOffset, 0);

                // Capsule endpoints (bottom and top of the capsule, excluding the hemisphere caps)
                Vector3 point1 = capsuleCenter - new Vector3(0, (_colliderHeight / 2f) - _colliderRadius, 0);
                Vector3 point2 = capsuleCenter + new Vector3(0, (_colliderHeight / 2f) - _colliderRadius, 0);

                // Check for any colliders overlapping our yeti capsule
                // Use a slightly larger radius to catch snowballs that are close
                var hitColliders = Physics.OverlapCapsule(point1, point2, _colliderRadius + 0.5f);

                foreach (var col in hitColliders)
                {
                    if (col == null || col.gameObject == null) continue;

                    // Check if it's a snowball
                    if (col.gameObject.name.Contains("Snowball"))
                    {
                        int snowballId = col.gameObject.GetInstanceID();

                        // Don't count the same snowball twice
                        if (_hitSnowballs.Contains(snowballId)) continue;

                        _hitSnowballs.Add(snowballId);
                        Vector3 hitPos = col.gameObject.transform.position;

                        // Get the thrower info from the snowball
                        string throwerName = GetSnowballThrowerName(col.gameObject);

                        Melon<YetiHuntMod>.Logger.Msg($"*** YETI HIT! *** Snowball at {hitPos} hit yeti at {yetiPos}, thrown by: {throwerName}");
                        OnYetiHit(yeti, hitPos, throwerName);
                        return; // End round on first hit
                    }
                }
            }

            // Clean up old hit tracking periodically
            if (_hitSnowballs.Count > 100)
                _hitSnowballs.Clear();
        }

        private string GetSnowballThrowerName(GameObject snowballObj)
        {
            if (_snowballType == null) return "Unknown";

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
                            var castMethod = typeof(Il2CppObjectBase).GetMethod("Cast").MakeGenericMethod(_snowballType);
                            var snowball = castMethod.Invoke(comp, null);

                            // Try sync_PlayerThatPickedUpObject first - this tracks who threw it
                            var syncPlayerProp = _snowballType.GetProperty("sync_PlayerThatPickedUpObject");
                            if (syncPlayerProp != null)
                            {
                                var syncVar = syncPlayerProp.GetValue(snowball);
                                if (syncVar != null)
                                {
                                    // SyncVar<T> has a Value property
                                    var valueProp = syncVar.GetType().GetProperty("Value");
                                    if (valueProp != null)
                                    {
                                        var playerRef = valueProp.GetValue(syncVar);
                                        Melon<YetiHuntMod>.Logger.Msg($"sync_PlayerThatPickedUpObject.Value: {playerRef}");

                                        if (playerRef != null)
                                        {
                                            // This might be a PlayerControl or GameObject reference
                                            // Try to get the player's username from it
                                            string name = ExtractPlayerName(playerRef);
                                            if (!string.IsNullOrEmpty(name) && name != "Unknown")
                                                return name;
                                        }
                                    }
                                }
                            }

                            // Try OwnerId
                            var ownerIdProp = _snowballType.GetProperty("OwnerId");
                            if (ownerIdProp != null)
                            {
                                int ownerId = (int)ownerIdProp.GetValue(snowball);
                                Melon<YetiHuntMod>.Logger.Msg($"Snowball OwnerId: {ownerId}");

                                if (ownerId >= 0)
                                {
                                    string playerName = FindPlayerNameByOwnerId(ownerId);
                                    if (!string.IsNullOrEmpty(playerName) && !playerName.StartsWith("Player "))
                                        return playerName;
                                }
                            }

                            // Fallback: Check if this is the local player's snowball
                            var isOwnerProp = _snowballType.GetProperty("IsOwner");
                            if (isOwnerProp != null)
                            {
                                bool isOwner = (bool)isOwnerProp.GetValue(snowball);
                                Melon<YetiHuntMod>.Logger.Msg($"Snowball IsOwner: {isOwner}");
                                if (isOwner)
                                {
                                    // This is our snowball - get local player name
                                    return GetLocalPlayerName();
                                }
                            }

                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        Melon<YetiHuntMod>.Logger.Warning($"Error getting snowball owner: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Melon<YetiHuntMod>.Logger.Warning($"GetSnowballThrowerName failed: {ex.Message}");
            }

            // Last resort: if we're the only player, it must be us
            return GetLocalPlayerName();
        }

        private string ExtractPlayerName(object playerRef)
        {
            if (playerRef == null) return "Unknown";

            try
            {
                var type = playerRef.GetType();
                Melon<YetiHuntMod>.Logger.Msg($"ExtractPlayerName from type: {type.Name}");

                // If it's a PlayerControl, try to get the usernameController
                var usernameControllerProp = type.GetProperty("usernameController");
                if (usernameControllerProp != null)
                {
                    var usernameController = usernameControllerProp.GetValue(playerRef);
                    Melon<YetiHuntMod>.Logger.Msg($"usernameController: {usernameController}");
                    if (usernameController != null)
                    {
                        // Try various property names for the username
                        var ucType = usernameController.GetType();
                        var propNames = new[] { "username", "Username", "playerName", "PlayerName", "displayName", "sync_username", "sync_Username" };
                        foreach (var propName in propNames)
                        {
                            var prop = ucType.GetProperty(propName);
                            if (prop != null)
                            {
                                var val = prop.GetValue(usernameController);
                                Melon<YetiHuntMod>.Logger.Msg($"  {propName}: {val}");

                                // Handle SyncVar wrapper
                                if (val != null && val.GetType().Name.Contains("SyncVar"))
                                {
                                    var valueProp = val.GetType().GetProperty("Value");
                                    if (valueProp != null)
                                    {
                                        val = valueProp.GetValue(val);
                                        Melon<YetiHuntMod>.Logger.Msg($"  {propName}.Value: {val}");
                                    }
                                }

                                if (val != null && val is string strVal && !string.IsNullOrEmpty(strVal))
                                {
                                    return strVal;
                                }
                            }
                        }

                        // List all properties of usernameController for debugging
                        Melon<YetiHuntMod>.Logger.Msg("PlayerUsernameController all properties:");
                        foreach (var prop in ucType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                        {
                            Melon<YetiHuntMod>.Logger.Msg($"  {prop.Name}: {prop.PropertyType.Name}");
                        }
                    }
                }

                // Try to get GameObject and find PlayerUsernameController
                GameObject playerObj = null;
                var gameObjectProp = type.GetProperty("gameObject");
                if (gameObjectProp != null)
                {
                    playerObj = gameObjectProp.GetValue(playerRef) as GameObject;
                }

                if (playerObj == null)
                {
                    // Try casting to Component
                    var castMethod = typeof(Il2CppObjectBase).GetMethod("TryCast");
                    if (castMethod != null)
                    {
                        var componentCast = castMethod.MakeGenericMethod(typeof(Component));
                        var asComponent = componentCast.Invoke(playerRef, null) as Component;
                        if (asComponent != null)
                        {
                            playerObj = asComponent.gameObject;
                        }
                    }
                }

                if (playerObj != null)
                {
                    Melon<YetiHuntMod>.Logger.Msg($"Found player GameObject: {playerObj.name}");
                    var username = GetPlayerUsername(playerObj);
                    if (!string.IsNullOrEmpty(username))
                        return username;
                }

                // List available properties for debugging
                Melon<YetiHuntMod>.Logger.Msg("PlayerControl properties:");
                foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (prop.Name.ToLower().Contains("name") || prop.Name.ToLower().Contains("user"))
                    {
                        Melon<YetiHuntMod>.Logger.Msg($"  {prop.Name}: {prop.PropertyType.Name}");
                    }
                }
            }
            catch (Exception ex)
            {
                Melon<YetiHuntMod>.Logger.Warning($"ExtractPlayerName error: {ex.Message}");
            }

            return "Unknown";
        }

        private string GetLocalPlayerName()
        {
            if (_playerTransform != null)
            {
                string name = GetPlayerUsername(_playerTransform.gameObject);
                if (!string.IsNullOrEmpty(name))
                    return name;
            }
            return "You";
        }

        private string FindPlayerNameByOwnerId(int ownerId)
        {
            // Search all players to find one with matching owner ID
            foreach (var obj in Object.FindObjectsOfType<GameObject>())
            {
                if (obj == null || !obj.name.Contains("Player Networked")) continue;

                var components = obj.GetComponents<Component>();
                foreach (var comp in components)
                {
                    if (comp == null) continue;
                    try
                    {
                        var il2cppType = comp.GetIl2CppType();
                        // Look for NetworkObject component which has OwnerId
                        if (il2cppType?.Name == "NetworkObject")
                        {
                            var ownerIdProp = comp.GetType().GetProperty("OwnerId");
                            if (ownerIdProp != null)
                            {
                                int playerOwnerId = (int)ownerIdProp.GetValue(comp);
                                if (playerOwnerId == ownerId)
                                {
                                    // Found the player! Now get their username
                                    return GetPlayerUsername(obj);
                                }
                            }
                        }
                    }
                    catch { }
                }
            }

            return $"Player {ownerId}";
        }

        private string GetPlayerUsername(GameObject playerObj)
        {
            // Try to find PlayerUsernameController or similar
            var components = playerObj.GetComponents<Component>();
            foreach (var comp in components)
            {
                if (comp == null) continue;
                try
                {
                    var il2cppType = comp.GetIl2CppType();
                    if (il2cppType?.Name == "PlayerUsernameController")
                    {
                        // Try common property names for username
                        var type = comp.GetType();
                        var props = new[] { "username", "Username", "playerName", "PlayerName", "displayName", "DisplayName" };
                        foreach (var propName in props)
                        {
                            var prop = type.GetProperty(propName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                            if (prop != null)
                            {
                                var val = prop.GetValue(comp);
                                if (val != null)
                                    return val.ToString();
                            }
                        }

                        // Try fields
                        var fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        foreach (var field in fields)
                        {
                            if (field.Name.ToLower().Contains("name") || field.Name.ToLower().Contains("user"))
                            {
                                var val = field.GetValue(comp);
                                if (val != null && val is string strVal && !string.IsNullOrEmpty(strVal))
                                    return strVal;
                            }
                        }
                    }
                }
                catch { }
            }

            return null;
        }

        private void OnYetiHit(HuntYeti yeti, Vector3 hitPosition, string throwerName)
        {
            Melon<YetiHuntMod>.Logger.Msg($"*** YETI HIT! *** at {hitPosition} by {throwerName}");

            if (_currentState == GameState.Hunting)
            {
                TransitionToRoundEnd(throwerName);
            }
        }

        #endregion

        #region Chat / Networking

        private void TestChatMessage()
        {
            Melon<YetiHuntMod>.Logger.Msg("=== Testing Chat Message ===");

            if (_chatManagerType == null)
            {
                Melon<YetiHuntMod>.Logger.Warning("ChatManager type not found");
                return;
            }

            try
            {
                // Find Chat Manager GameObject
                var chatManagerObj = GameObject.Find("Chat Manager");
                if (chatManagerObj == null)
                {
                    Melon<YetiHuntMod>.Logger.Warning("Chat Manager GameObject not found");
                    return;
                }

                // Get the ChatManager component
                var components = chatManagerObj.GetComponents<Component>();
                object chatManager = null;

                foreach (var comp in components)
                {
                    if (comp == null) continue;
                    try
                    {
                        var il2cppType = comp.GetIl2CppType();
                        if (il2cppType?.Name == "ChatManager")
                        {
                            var castMethod = typeof(Il2CppObjectBase).GetMethod("Cast").MakeGenericMethod(_chatManagerType);
                            chatManager = castMethod.Invoke(comp, null);
                            break;
                        }
                    }
                    catch { }
                }

                if (chatManager == null)
                {
                    Melon<YetiHuntMod>.Logger.Warning("Could not get ChatManager instance");
                    return;
                }

                // Try SendPlayerChatMessage
                var sendMethod = _chatManagerType.GetMethod("SendPlayerChatMessage");
                if (sendMethod != null)
                {
                    sendMethod.Invoke(chatManager, new object[] { "[YETI] Test message from YetiHunt mod!" });
                    Melon<YetiHuntMod>.Logger.Msg("Sent chat message!");
                }
                else
                {
                    Melon<YetiHuntMod>.Logger.Warning("SendPlayerChatMessage method not found");

                    // List available methods
                    Melon<YetiHuntMod>.Logger.Msg("Available methods:");
                    foreach (var m in _chatManagerType.GetMethods())
                    {
                        if (m.Name.Contains("Send") || m.Name.Contains("Message"))
                            Melon<YetiHuntMod>.Logger.Msg($"  {m.Name}");
                    }
                }
            }
            catch (Exception ex)
            {
                Melon<YetiHuntMod>.Logger.Error($"Chat test failed: {ex.Message}");
            }
        }

        #endregion

        #region UI

        public override void OnGUI()
        {
            if (_currentState == GameState.Idle) return;

            InitTextures();
            DrawHuntUI();
        }

        private void InitTextures()
        {
            if (_texturesInitialized) return;
            _bgTexture = MakeTexture(2, 2, new Color(0.1f, 0.1f, 0.15f, 0.8f));
            _winnerBgTexture = MakeTexture(2, 2, new Color(0.1f, 0.3f, 0.1f, 0.9f)); // Green for victory
            _texturesInitialized = true;
        }

        private Texture2D MakeTexture(int width, int height, Color color)
        {
            Color[] pixels = new Color[width * height];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = color;
            Texture2D texture = new Texture2D(width, height);
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        private void DrawHuntUI()
        {
            float elapsed = Time.time - _stateStartTime;
            float barWidth = 400;
            float barHeight = 60;
            float barX = (Screen.width - barWidth) / 2;
            float barY = 20;

            // Draw main status bar
            GUI.DrawTexture(new Rect(barX, barY, barWidth, barHeight), _bgTexture);

            GUIStyle labelStyle = new GUIStyle(GUI.skin.label);
            labelStyle.alignment = TextAnchor.MiddleCenter;
            labelStyle.fontSize = 24;
            labelStyle.fontStyle = FontStyle.Bold;

            string statusText = _currentState switch
            {
                GameState.Countdown => $"Hunt begins in {Mathf.CeilToInt(COUNTDOWN_DURATION - elapsed)}...",
                GameState.Hunting => $"HUNT THE YETI! ({Mathf.CeilToInt(HUNT_TIMEOUT - elapsed)}s)",
                GameState.RoundEnd => "Round Over!",
                _ => ""
            };

            GUI.Label(new Rect(barX, barY, barWidth, barHeight), statusText, labelStyle);

            // Draw winner announcement during RoundEnd
            if (_currentState == GameState.RoundEnd)
            {
                float winnerWidth = 500;
                float winnerHeight = 120;
                float winnerX = (Screen.width - winnerWidth) / 2;
                float winnerY = (Screen.height - winnerHeight) / 2 - 50; // Slightly above center

                GUI.DrawTexture(new Rect(winnerX, winnerY, winnerWidth, winnerHeight), _winnerBgTexture);

                GUIStyle winnerTitleStyle = new GUIStyle(GUI.skin.label);
                winnerTitleStyle.alignment = TextAnchor.MiddleCenter;
                winnerTitleStyle.fontSize = 28;
                winnerTitleStyle.fontStyle = FontStyle.Bold;
                winnerTitleStyle.normal.textColor = Color.yellow;

                GUIStyle winnerNameStyle = new GUIStyle(GUI.skin.label);
                winnerNameStyle.alignment = TextAnchor.MiddleCenter;
                winnerNameStyle.fontSize = 36;
                winnerNameStyle.fontStyle = FontStyle.Bold;
                winnerNameStyle.normal.textColor = Color.white;

                if (!string.IsNullOrEmpty(_lastWinnerName))
                {
                    GUI.Label(new Rect(winnerX, winnerY + 10, winnerWidth, 40), "WINNER!", winnerTitleStyle);
                    GUI.Label(new Rect(winnerX, winnerY + 55, winnerWidth, 50), _lastWinnerName, winnerNameStyle);
                }
                else
                {
                    GUI.Label(new Rect(winnerX, winnerY + 10, winnerWidth, 40), "TIME'S UP!", winnerTitleStyle);
                    GUI.Label(new Rect(winnerX, winnerY + 55, winnerWidth, 50), "No one caught the Yeti!", winnerNameStyle);
                }
            }
        }

        #endregion
    }
}
