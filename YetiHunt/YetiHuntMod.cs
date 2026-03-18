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
        private object _yetiManagerInstance = null;
        private MethodInfo _spawnYetiMethod = null;
        private bool _typesInitialized = false;

        // UI
        private Texture2D _bgTexture;
        private bool _texturesInitialized = false;

        // Player tracking
        private Transform _playerTransform = null;

        // Snowball tracking for hit detection
        private Dictionary<int, Vector3> _trackedSnowballs = new Dictionary<int, Vector3>();
        private const float SNOWBALL_HIT_RADIUS = 5f; // Direct hit radius
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
            _huntYetis.Clear();
            _hitSnowballs.Clear();
            _trackedSnowballs.Clear();
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
            // TODO: Player teleportation disabled for now - causes lag due to reflection
            // Need to cache types at initialization or find a simpler approach
            Melon<YetiHuntMod>.Logger.Msg("Player teleportation not yet implemented - hunt from current position");
        }

        private void TransitionToRoundEnd(string winnerName)
        {
            if (winnerName != null)
                Melon<YetiHuntMod>.Logger.Msg($"=== {winnerName} WINS! ===");
            else
                Melon<YetiHuntMod>.Logger.Msg("=== NO WINNER ===");

            _currentState = GameState.RoundEnd;
            _stateStartTime = Time.time;
        }

        private void TransitionToIdle()
        {
            Melon<YetiHuntMod>.Logger.Msg("=== ROUND COMPLETE ===");
            _currentState = GameState.Idle;
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

        #endregion

        #region Snowball Hit Detection

        private void TrackSnowballsAndDetectHits()
        {
            if (_huntYetis.Count == 0) return;

            // Periodic debug log
            if (_currentState == GameState.Hunting && Time.time - _lastDebugLogTime > 3f)
            {
                _lastDebugLogTime = Time.time;
                var yetiPos = _huntYetis.Count > 0 && _huntYetis[0].GameObject != null
                    ? _huntYetis[0].GameObject.transform.position.ToString() : "N/A";
                Melon<YetiHuntMod>.Logger.Msg($"[Hit Debug] Tracking {_trackedSnowballs.Count} snowballs, yeti at {yetiPos}");
            }

            // Find all current snowballs
            var currentSnowballs = new HashSet<int>();
            foreach (var obj in Object.FindObjectsOfType<GameObject>())
            {
                if (obj != null && obj.name == "Snowball(Clone)")
                {
                    int id = obj.GetInstanceID();
                    currentSnowballs.Add(id);

                    // Track position for when it disappears
                    _trackedSnowballs[id] = obj.transform.position;
                }
            }

            // Check for snowballs that disappeared (hit something)
            var disappeared = _trackedSnowballs.Keys.Where(id => !currentSnowballs.Contains(id)).ToList();

            foreach (int snowballId in disappeared)
            {
                if (_hitSnowballs.Contains(snowballId)) continue;

                Vector3 lastPos = _trackedSnowballs[snowballId];
                _trackedSnowballs.Remove(snowballId);

                // Check if it disappeared near a yeti (hit the collider)
                foreach (var yeti in _huntYetis)
                {
                    if (yeti.GameObject == null) continue;

                    Vector3 yetiCenter = yeti.GameObject.transform.position + new Vector3(0, _colliderHeight / 2f, 0);
                    float dist = Vector3.Distance(lastPos, yetiCenter);

                    if (dist < SNOWBALL_HIT_RADIUS)
                    {
                        _hitSnowballs.Add(snowballId);
                        Melon<YetiHuntMod>.Logger.Msg($"*** YETI HIT! *** Snowball destroyed {dist:F1}m from yeti");
                        OnYetiHit(yeti, lastPos);
                        break;
                    }
                }
            }

            // Clean up old hit tracking
            if (_hitSnowballs.Count > 100)
                _hitSnowballs.Clear();
        }

        private void OnYetiHit(HuntYeti yeti, Vector3 hitPosition)
        {
            Melon<YetiHuntMod>.Logger.Msg($"*** YETI HIT! *** at {hitPosition}");

            // For now, just log it. Later: determine which player threw it, award points, etc.
            if (_currentState == GameState.Hunting)
            {
                TransitionToRoundEnd("Player"); // TODO: track actual player name
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
        }

        #endregion
    }
}
