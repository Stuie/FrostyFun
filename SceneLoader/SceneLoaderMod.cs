using System;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;
using SceneLoader.Services;

namespace SceneLoader
{
    public enum SceneLoaderState
    {
        Idle,
        Picking,       // Scene picker UI is open
        InCustomArea   // Playing in a custom scene/area
    }

    public class SceneLoaderMod : MelonMod
    {
        private SceneDiagnostics _diagnostics;
        private PlayerMigration _playerMigration;
        private AssetBundleLoader _bundleLoader;
        private TrainAnimator _trainAnimator;

        private SceneLoaderState _state = SceneLoaderState.Idle;

        // Scene picker state
        private List<BundleInfo> _availableBundles = new();
        private Vector2 _scrollPos;
        private bool _showPicker;
        private string _loadedBundleName = "";

        // Cursor
        private bool _prevCursorVisible;
        private CursorLockMode _prevCursorLock;
        private Texture2D _cursorTexture;

        // Input component state backup (for proper save/restore)
        private Component _playerLocalInput;
        private Component _playerCameraControl;
        private bool _playerLocalInputWasEnabled;
        private bool _playerCameraControlWasEnabled;

        // Flag set when we trigger our own additive scene load
        // so OnSceneWasLoaded doesn't think the game changed scenes on us
        private bool _expectingOurSceneLoad;
        // Position to align the bundle's SpawnPoint to (= player's position when bundle loaded)
        private Vector3 _bundleAlignTarget;

        public override void OnInitializeMelon()
        {
            var logger = Melon<SceneLoaderMod>.Logger;
            _diagnostics = new SceneDiagnostics(logger);
            _playerMigration = new PlayerMigration(logger);
            _bundleLoader = new AssetBundleLoader(logger);
            _trainAnimator = new TrainAnimator(logger);

            logger.Msg("Scene Loader v0.3.0 loaded!");
            logger.Msg("  F4        = Open scene picker / return to game");
            logger.Msg("  Ctrl+F4   = Dump scene diagnostics");
            logger.Msg("  Shift+F4  = Dump world dimensions (boundary, lodge, terrain)");
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            Melon<SceneLoaderMod>.Logger.Msg($"Scene loaded: {sceneName} (buildIndex={buildIndex})");

            // If this is our own additive load, the scene is now actually populated
            // (LoadScene additive completes async, so root objects appear by the time
            // OnSceneWasLoaded fires). Now we can align it to the player.
            if (_expectingOurSceneLoad)
            {
                _expectingOurSceneLoad = false;
                Melon<SceneLoaderMod>.Logger.Msg("  (our additive scene load - aligning to player)");
                AlignBundleSceneToPlayer();
                return;
            }

            if (_state == SceneLoaderState.InCustomArea)
            {
                Melon<SceneLoaderMod>.Logger.Warning("Scene changed while in custom area - resetting");
                _trainAnimator.Reset();
                _bundleLoader.Reset();
                _state = SceneLoaderState.Idle;
            }
            if (_showPicker) ClosePicker();
            _playerMigration.Reset();
        }

        public override void OnUpdate()
        {
            // Animate trains while in custom area
            if (_state == SceneLoaderState.InCustomArea && _trainAnimator.HasTrains)
                _trainAnimator.Update(Time.deltaTime);

            if (Input.GetKeyDown(KeyCode.F4))
            {
                if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
                {
                    _diagnostics.DumpAllScenes();
                    _diagnostics.DumpPlayerHierarchy();
                    _diagnostics.DumpNetworkState();
                    _diagnostics.DumpBoundaryInfo();
                }
                else if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                {
                    // Context-aware: when in custom area, dump info about it instead
                    if (_state == SceneLoaderState.InCustomArea)
                        _diagnostics.DumpCustomSceneState(_playerMigration);
                    else
                        _diagnostics.DumpWorldDimensions();
                }
                else
                {
                    HandleF4();
                }
            }

            // Escape closes picker
            if (_showPicker && Input.GetKeyDown(KeyCode.Escape))
            {
                ClosePicker();
            }
        }

        private void HandleF4()
        {
            switch (_state)
            {
                case SceneLoaderState.Idle:
                    OpenPicker();
                    break;
                case SceneLoaderState.Picking:
                    ClosePicker();
                    break;
                case SceneLoaderState.InCustomArea:
                    ReturnToGame();
                    break;
            }
        }

        private void OpenPicker()
        {
            _availableBundles = _bundleLoader.ScanForBundles();
            _showPicker = true;
            _state = SceneLoaderState.Picking;

            _prevCursorVisible = Cursor.visible;
            _prevCursorLock = Cursor.lockState;
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;

            // Disable player input while picker is open
            DisablePlayerInput();
        }

        private void ClosePicker()
        {
            _showPicker = false;
            _state = SceneLoaderState.Idle;

            Cursor.visible = _prevCursorVisible;
            Cursor.lockState = _prevCursorLock;

            EnablePlayerInput();
        }

        private void LoadBundleScene(BundleInfo bundle)
        {
            var logger = Melon<SceneLoaderMod>.Logger;
            logger.Msg($"=== LOADING BUNDLE: {bundle.FileName} ===");
            _showPicker = false;

            EnablePlayerInput();
            Cursor.visible = _prevCursorVisible;
            Cursor.lockState = _prevCursorLock;

            // Step 1: Load the bundle
            if (!_bundleLoader.LoadBundle(bundle))
            {
                logger.Warning($"Failed to load bundle: {bundle.FileName}");
                _state = SceneLoaderState.Idle;
                return;
            }

            // Step 2: Load the first scene additively
            if (bundle.ScenePaths == null || bundle.ScenePaths.Length == 0)
            {
                logger.Warning("Bundle contains no scenes");
                _bundleLoader.Reset();
                _state = SceneLoaderState.Idle;
                return;
            }

            // Step 2.5: Hijack 20 real Bench instances BEFORE the world is hidden.
            // Bench is preferred over Chair because it has FishNet NetworkTransform
            // (chairs don't), so when the host moves a bench all clients receive
            // synced positions automatically — real server-authoritative networking.
            // HijackBenches reparents each bench to scene root so it survives
            // World.SetActive(false). FindObjectsOfType skips inactive objects so
            // this MUST run pre-hide.
            _trainAnimator.HijackBenches();
            _playerMigration.AddHideExclusions(_trainAnimator.HijackedBenchObjects);

            // Step 3: Hide the game world and disable boundary (no procedural ground)
            if (!_playerMigration.EnterCustomArea(spawnProceduralContent: false))
            {
                logger.Warning("Failed to enter custom area");
                _bundleLoader.Reset();
                _state = SceneLoaderState.Idle;
                return;
            }

            // Step 4: Load the bundle scene additively.
            // Capture player position BEFORE the load - bundle scene contents
            // will be aligned so SpawnPoint sits at the player's current position.
            // Alignment happens in OnSceneWasLoaded once root objects are populated.
            _bundleAlignTarget = _playerMigration.OriginalPosition;
            _expectingOurSceneLoad = true;
            if (!_bundleLoader.LoadSceneAdditive(bundle.ScenePaths[0]))
            {
                _expectingOurSceneLoad = false;
                logger.Warning("Failed to load scene from bundle");
                _playerMigration.ReturnToGame();
                _bundleLoader.Reset();
                _state = SceneLoaderState.Idle;
                return;
            }

            _loadedBundleName = bundle.FileName;
            _state = SceneLoaderState.InCustomArea;
            logger.Msg("=== IN CUSTOM SCENE ===");
        }

        /// <summary>
        /// After an additive bundle scene loads, move all its root objects so the
        /// SpawnPoint aligns with the player's original position. This way the
        /// custom level appears around the player instead of at world origin (0,0,0)
        /// far from the lodge area where boundaries kick in.
        /// </summary>
        private void AlignBundleSceneToPlayer()
        {
            var logger = Melon<SceneLoaderMod>.Logger;
            var loaded = _bundleLoader.LoadedScene;
            if (!loaded.IsValid() || !loaded.isLoaded)
            {
                logger.Warning("AlignBundleSceneToPlayer: scene not valid/loaded");
                return;
            }

            var roots = loaded.GetRootGameObjects();
            logger.Msg($"Aligning bundle scene ({roots.Length} root objects):");

            // First pass: log every root and find the SpawnPoint
            Vector3 spawnPos = Vector3.zero;
            bool foundSpawn = false;
            foreach (var root in roots)
            {
                if (root == null) continue;
                int childCount = root.transform.childCount;
                int rendererCount = root.GetComponentsInChildren<Renderer>(true).Length;
                logger.Msg($"  Root: \"{root.name}\" at {root.transform.position} children={childCount} renderers={rendererCount}");

                if (!foundSpawn)
                {
                    if (IsSpawnPoint(root.name))
                    {
                        spawnPos = root.transform.position;
                        foundSpawn = true;
                    }
                    else
                    {
                        var child = FindSpawnPointInChildren(root.transform);
                        if (child != null)
                        {
                            spawnPos = child.position;
                            foundSpawn = true;
                        }
                    }
                }
            }

            if (!foundSpawn)
            {
                logger.Warning("No SpawnPoint found - using world origin as alignment reference");
            }

            // Calculate offset: bundle's spawn point should land at player's position
            Vector3 offset = _bundleAlignTarget - spawnPos;
            logger.Msg($"Spawn ref: {spawnPos}, target: {_bundleAlignTarget}, offset: {offset}");

            // Apply offset to every root object
            int moved = 0;
            foreach (var root in roots)
            {
                if (root == null) continue;
                root.transform.position += offset;
                moved++;
            }
            logger.Msg($"Moved {moved} root objects by offset");

            // Fix magenta materials - bundle's shader references don't survive cross-project,
            // so reassign URP/Lit from the game's runtime shader registry while preserving colors.
            FixBundleShaders(roots);

            // Final position check after alignment
            logger.Msg("Post-alignment positions:");
            foreach (var root in roots)
            {
                if (root == null) continue;
                logger.Msg($"  \"{root.name}\" at {root.transform.position}");
            }

            // Move lodge furniture (chests, sled customization, shops) onto the gazebo.
            // The bundle has a "LodgeGazebo" GameObject - we use its world position as the layout center.
            Vector3 gazeboCenter = _playerMigration.OriginalPosition;
            foreach (var root in roots)
            {
                if (root != null && root.name == "LodgeGazebo")
                {
                    gazeboCenter = root.transform.position;
                    _playerMigration.MoveLodgeFurnitureToGazebo(gazeboCenter);
                    break;
                }
            }

            // Register any train routes for runtime animation. Train geometry
            // (body/cabin/stack) is built as children of each hijacked bench so
            // it follows FishNet's synced bench transform on every client.
            _trainAnimator.RegisterTrainsFromRoots(roots);
        }

        private void FixBundleShaders(GameObject[] roots)
        {
            var logger = Melon<SceneLoaderMod>.Logger;
            var litShader = Shader.Find("Universal Render Pipeline/Lit");
            if (litShader == null)
            {
                logger.Warning("URP/Lit shader not found - materials may stay magenta");
                return;
            }

            int materialsFixed = 0;
            int rendererCount = 0;
            foreach (var root in roots)
            {
                if (root == null) continue;
                var renderers = root.GetComponentsInChildren<Renderer>(true);
                foreach (var r in renderers)
                {
                    if (r == null) continue;
                    rendererCount++;
                    var mats = r.materials; // creates instances we can mutate safely
                    for (int i = 0; i < mats.Length; i++)
                    {
                        var mat = mats[i];
                        if (mat == null) continue;

                        // Replace shader, preserving the original color, texture, tiling,
                        // smoothness, and metallic. Smoothness is critical to keep the sand
                        // matte rather than glossy after the shader swap.
                        Color originalColor = mat.color;
                        Texture originalTex = mat.mainTexture;
                        Vector2 originalScale = mat.mainTextureScale;
                        Vector2 originalOffset = mat.mainTextureOffset;
                        bool hadSmoothness = mat.HasProperty("_Smoothness");
                        float originalSmoothness = hadSmoothness ? mat.GetFloat("_Smoothness") : 0.5f;
                        bool hadMetallic = mat.HasProperty("_Metallic");
                        float originalMetallic = hadMetallic ? mat.GetFloat("_Metallic") : 0f;

                        mat.shader = litShader;
                        mat.color = originalColor;
                        if (originalTex != null) mat.mainTexture = originalTex;
                        mat.mainTextureScale = originalScale;
                        mat.mainTextureOffset = originalOffset;
                        if (mat.HasProperty("_Smoothness"))
                            mat.SetFloat("_Smoothness", hadSmoothness ? originalSmoothness : 0.05f);
                        if (mat.HasProperty("_Metallic"))
                            mat.SetFloat("_Metallic", originalMetallic);
                        materialsFixed++;
                    }
                    r.materials = mats;
                }
            }
            logger.Msg($"  Fixed shaders on {materialsFixed} materials across {rendererCount} renderers");
        }

        private static bool IsSpawnPoint(string name)
        {
            return name == "SpawnPoint" || name == "Spawn Point" || name == "PlayerSpawn";
        }

        private static Transform FindSpawnPointInChildren(Transform parent)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (IsSpawnPoint(child.name)) return child;
                var found = FindSpawnPointInChildren(child);
                if (found != null) return found;
            }
            return null;
        }

        private void LoadProcedural()
        {
            var logger = Melon<SceneLoaderMod>.Logger;
            logger.Msg("=== ENTERING PROCEDURAL AREA ===");
            _showPicker = false;

            EnablePlayerInput();
            Cursor.visible = _prevCursorVisible;
            Cursor.lockState = _prevCursorLock;

            if (_playerMigration.EnterCustomArea())
            {
                _loadedBundleName = "(Procedural)";
                _state = SceneLoaderState.InCustomArea;
                logger.Msg("=== IN PROCEDURAL AREA ===");
            }
            else
            {
                logger.Warning("Failed to enter procedural area");
                _state = SceneLoaderState.Idle;
            }
        }

        private void ReturnToGame()
        {
            var logger = Melon<SceneLoaderMod>.Logger;
            logger.Msg("=== RETURNING TO GAME ===");

            // Stop animating trains and unload bundle scene first (before restoring world)
            _trainAnimator.Reset();
            _bundleLoader.Reset();

            if (_playerMigration.ReturnToGame())
            {
                _state = SceneLoaderState.Idle;
                _loadedBundleName = "";
                logger.Msg("=== RETURNED TO GAME ===");
            }
            else
            {
                _state = SceneLoaderState.Idle;
                _loadedBundleName = "";
                logger.Warning("Return had issues (state reset)");
            }
        }

        public override void OnLateUpdate()
        {
            if (_showPicker)
            {
                Cursor.visible = true;
                Cursor.lockState = CursorLockMode.None;
                Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
            }
        }

        public override void OnGUI()
        {
            if (_showPicker)
                DrawPicker();
        }

        private void DrawPicker()
        {
            float width = 400;
            float height = 350;
            float x = (Screen.width - width) / 2;
            float y = (Screen.height - height) / 2;

            GUI.Box(new Rect(x, y, width, height), "");

            var titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white }
            };

            GUI.Label(new Rect(x, y + 10, width, 30), "Scene Loader", titleStyle);

            var labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                normal = { textColor = new Color(0.8f, 0.8f, 0.8f) }
            };

            float listY = y + 50;
            float listHeight = height - 110;

            _scrollPos = GUI.BeginScrollView(
                new Rect(x + 10, listY, width - 20, listHeight),
                _scrollPos,
                new Rect(0, 0, width - 40, (_availableBundles.Count + 1) * 35));

            float itemY = 0;

            // Procedural option
            if (GUI.Button(new Rect(0, itemY, width - 40, 30), "Procedural (flat ground)"))
            {
                LoadProcedural();
            }
            itemY += 35;

            // Bundle files
            foreach (var bundle in _availableBundles)
            {
                if (GUI.Button(new Rect(0, itemY, width - 40, 30), bundle.FileName))
                {
                    LoadBundleScene(bundle);
                }
                itemY += 35;
            }

            GUI.EndScrollView();

            // Close button
            if (GUI.Button(new Rect(x + width - 80, y + height - 40, 70, 30), "Close"))
            {
                ClosePicker();
            }

            // Info
            GUI.Label(new Rect(x + 10, y + height - 40, width - 100, 30),
                $"Place .bundle files in Mods/CustomScenes/", labelStyle);

            // Draw custom cursor (game hides the system cursor)
            if (_cursorTexture == null) _cursorTexture = MakeCursorTexture();
            if (_cursorTexture != null)
            {
                var mousePos = Event.current.mousePosition;
                GUI.DrawTexture(new Rect(mousePos.x, mousePos.y, 16, 16), _cursorTexture);
            }
        }

        private void DisablePlayerInput()
        {
            try
            {
                // Disable PlayerLocalInput
                var playerInputObj = GameObject.Find("Player Input");
                if (playerInputObj != null)
                {
                    foreach (var comp in playerInputObj.GetComponents<Component>())
                    {
                        if (comp != null && GetIl2CppTypeName(comp) == "PlayerLocalInput")
                        {
                            _playerLocalInput = comp;
                            var behaviour = comp.TryCast<Behaviour>();
                            if (behaviour != null)
                            {
                                _playerLocalInputWasEnabled = behaviour.enabled;
                                behaviour.enabled = false;
                            }
                            break;
                        }
                    }
                }

                // Disable PlayerCameraControl
                var playerObj = GameObject.Find("Player Networked(Clone)");
                if (playerObj != null)
                {
                    foreach (var comp in playerObj.GetComponents<Component>())
                    {
                        if (comp != null && GetIl2CppTypeName(comp) == "PlayerCameraControl")
                        {
                            _playerCameraControl = comp;
                            var behaviour = comp.TryCast<Behaviour>();
                            if (behaviour != null)
                            {
                                _playerCameraControlWasEnabled = behaviour.enabled;
                                behaviour.enabled = false;
                            }
                            break;
                        }
                    }
                }

                // Disable Cinemachine components (camera orbit/input)
                var cinemachineObj = GameObject.Find("CinemachineCamera (makes parent null on start)");
                if (cinemachineObj != null)
                {
                    foreach (var comp in cinemachineObj.GetComponents<Component>())
                    {
                        string typeName = GetIl2CppTypeName(comp);
                        if (typeName.Contains("Cinemachine") || typeName.Contains("Input"))
                        {
                            var behaviour = comp.TryCast<Behaviour>();
                            if (behaviour != null && behaviour.enabled)
                                behaviour.enabled = false;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Melon<SceneLoaderMod>.Logger.Warning($"Error disabling input: {ex.Message}");
            }
        }

        private void EnablePlayerInput()
        {
            try
            {
                if (_playerLocalInput != null)
                {
                    var behaviour = _playerLocalInput.TryCast<Behaviour>();
                    if (behaviour != null)
                        behaviour.enabled = _playerLocalInputWasEnabled;
                }

                if (_playerCameraControl != null)
                {
                    var behaviour = _playerCameraControl.TryCast<Behaviour>();
                    if (behaviour != null)
                        behaviour.enabled = _playerCameraControlWasEnabled;
                }

                // Re-enable Cinemachine
                var cinemachineObj = GameObject.Find("CinemachineCamera (makes parent null on start)");
                if (cinemachineObj != null)
                {
                    foreach (var comp in cinemachineObj.GetComponents<Component>())
                    {
                        string typeName = GetIl2CppTypeName(comp);
                        if (typeName.Contains("Cinemachine") || typeName.Contains("Input"))
                        {
                            var behaviour = comp.TryCast<Behaviour>();
                            if (behaviour != null)
                                behaviour.enabled = true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Melon<SceneLoaderMod>.Logger.Warning($"Error enabling input: {ex.Message}");
            }
        }

        private static string GetIl2CppTypeName(Component comp)
        {
            try
            {
                return comp.GetIl2CppType()?.Name ?? comp.GetType().Name;
            }
            catch
            {
                return comp.GetType().Name;
            }
        }

        private static Texture2D MakeCursorTexture()
        {
            int size = 16;
            var tex = new Texture2D(size, size);
            var transparent = new Color(0, 0, 0, 0);
            var white = Color.white;

            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    tex.SetPixel(x, y, transparent);

            // Arrow cursor pointing top-left (Y flipped for Unity texture coords)
            tex.SetPixel(0, 15, white);
            tex.SetPixel(0, 14, white); tex.SetPixel(1, 14, white);
            tex.SetPixel(0, 13, white); tex.SetPixel(1, 13, white); tex.SetPixel(2, 13, white);
            tex.SetPixel(0, 12, white); tex.SetPixel(1, 12, white); tex.SetPixel(2, 12, white); tex.SetPixel(3, 12, white);
            tex.SetPixel(0, 11, white); tex.SetPixel(1, 11, white); tex.SetPixel(2, 11, white); tex.SetPixel(3, 11, white); tex.SetPixel(4, 11, white);
            tex.SetPixel(0, 10, white); tex.SetPixel(1, 10, white); tex.SetPixel(2, 10, white); tex.SetPixel(3, 10, white); tex.SetPixel(4, 10, white); tex.SetPixel(5, 10, white);
            tex.SetPixel(0, 9, white); tex.SetPixel(1, 9, white); tex.SetPixel(2, 9, white); tex.SetPixel(3, 9, white); tex.SetPixel(4, 9, white); tex.SetPixel(5, 9, white); tex.SetPixel(6, 9, white);
            tex.SetPixel(0, 8, white); tex.SetPixel(1, 8, white); tex.SetPixel(2, 8, white); tex.SetPixel(3, 8, white); tex.SetPixel(4, 8, white);
            tex.SetPixel(0, 7, white); tex.SetPixel(1, 7, white); tex.SetPixel(2, 7, white); tex.SetPixel(4, 7, white); tex.SetPixel(5, 7, white);
            tex.SetPixel(0, 6, white); tex.SetPixel(1, 6, white); tex.SetPixel(5, 6, white); tex.SetPixel(6, 6, white);
            tex.SetPixel(0, 5, white); tex.SetPixel(6, 5, white); tex.SetPixel(7, 5, white);
            tex.SetPixel(7, 4, white); tex.SetPixel(8, 4, white);

            tex.Apply();
            return tex;
        }
    }
}
