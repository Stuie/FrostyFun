using MelonLoader;
using UnityEngine;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Object = UnityEngine.Object;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;

namespace CharacterSelect
{
    public class CharacterSelectMod : MelonMod
    {
        private string _currentScene = "";
        private bool _showUI = false;
        private bool _stylesInitialized = false;

        // Cursor state to restore when closing
        private bool _previousCursorVisible;
        private CursorLockMode _previousLockState;

        // Components to disable while UI is open
        private Component _playerLocalInput;
        private Component _playerCameraControl;
        private bool _playerLocalInputWasEnabled;
        private bool _playerCameraControlWasEnabled;

        // UI Styling - use basic textures
        private Texture2D _bgTexture;
        private Texture2D _buttonTexture;
        private Texture2D _buttonHoverTexture;
        private Texture2D _buttonSelectedTexture;
        private Texture2D _cursorTexture;
        private Texture2D _wrenchTexture;

        // Character data - maps display info to actual game character IDs
        // Turtle (ID 4) is excluded as it doesn't work properly
        private static readonly (string Name, int GameId, string IconName)[] Characters = {
            ("Frog", 1, "icon_character_frogdefault"),
            ("Penguin", 2, "icon_character_penguin"),
            ("Harbor Seal", 3, null),
            ("Brown Bear", 5, "icon_character_bearbrown"),
            ("Polar Bear", 6, null),
            ("Black Bear", 7, null),
            ("Ringed Seal", 8, null),
            ("Baikal Seal", 9, "icon_character_sealbaikal"),
            ("Strawberry Frog", 10, null),
            ("Tree Frog", 11, null),
            ("Orange Toad", 12, "icon_character_toadorange"),
            ("Brown Toad", 13, null),
            ("Orange Fox", 14, "icon_character_foxorange"),
            ("Arctic Fox", 15, null),
            ("Panda", 16, null),
        };

        // Model groups — top-level UI shows these, click to expand variants + skins
        private static readonly (string GroupName, string ModelKey, int[] GameIds)[] ModelGroups = {
            ("Frog",    "frog",    new[] { 1, 10, 11 }),
            ("Penguin", "penguin", new[] { 2 }),
            ("Seal",    "seal",    new[] { 3, 8, 9 }),
            ("Bear",    "bear",    new[] { 5, 6, 7 }),
            ("Toad",    "toad",    new[] { 12, 13 }),
            ("Fox",     "fox",     new[] { 14, 15 }),
            ("Panda",   "panda",   new[] { 16 }),
        };

        // Custom embedded icons for characters without game icons (keyed by GameId)
        private static readonly Dictionary<int, string> CustomIcons = new()
        {
            { 3, "harbor_seal.png" },      // Harbor Seal
            { 6, "polar_bear.png" },       // Polar Bear
            { 7, "black_bear.png" },       // Black Bear
            { 8, "ringed_seal.png" },      // Ringed Seal
            { 10, "strawberry_frog.png" }, // Strawberry Frog
            { 11, "tree_frog.png" },       // Tree Frog
            { 13, "brown_toad.png" },      // Brown Toad
            { 15, "arctic_fox.png" },      // Arctic Fox
            { 16, "panda.png" },           // Panda
        };

        // Loaded character textures (indexed by position in Characters array)
        private Texture2D[] _characterTextures;
        private bool _texturesLoaded = false;
        private Texture2D _placeholderTexture; // Fallback for characters without game icons

        private int _currentCharacterId = 1;
        private int _hoverCharacterId = -1;

        // Reskin system
        private bool _dumpedRendererInfo = false;
        private string _reskinsDir;

        // Maps character key (e.g. "panda") → list of available skins
        private Dictionary<string, List<SkinEntry>> _availableSkins = new();

        private struct SkinEntry
        {
            public string DisplayName; // "Sunburned Panda"
            public string FilePath;    // full path to skin texture PNG/BMP (or generated cache path for JSON skins)
            public string IconPath;    // full path to icon PNG (nullable)
            public bool NeedsGeneration; // true if this is a JSON procedural skin that needs generating
            public string JsonPath;      // full path to .json definition (null for file-based skins)
        }

        // JSON procedural skin data model
        private class SkinDefinition
        {
            [JsonPropertyName("transforms")]
            public List<SkinTransform> Transforms { get; set; } = new();
        }

        private class SkinTransform
        {
            [JsonPropertyName("name")]
            public string Name { get; set; }
            [JsonPropertyName("action")]
            public string Action { get; set; }
            [JsonPropertyName("where")]
            public SkinWhere Where { get; set; }
            [JsonPropertyName("color")]
            public float[] Color { get; set; }
            [JsonPropertyName("blend")]
            public float Blend { get; set; }
            [JsonPropertyName("degrees")]
            public float Degrees { get; set; }
        }

        private class SkinWhere
        {
            [JsonPropertyName("brightness_min")]
            public float BrightnessMin { get; set; }
            [JsonPropertyName("brightness_max")]
            public float BrightnessMax { get; set; } = 1.0f;
        }

        private Dictionary<string, Texture2D> _skinTextureCache = new(); // keyed by file path
        private Dictionary<string, Texture2D> _skinIconCache = new();  // keyed by icon file path
        private Dictionary<int, Texture> _originalSkinTextures = new(); // keyed by character GameId
        private float _reskinApplyTime = 0f;
        private int _pendingReskinCharacterId = -1;
        private string _pendingReskinPath = null;

        // UI state for two-step skin selection
        private int _expandedGroupIndex = -1; // Which model group is expanded (-1 = none)
        private bool _showToolsPanel = false; // Gear/tools panel visible

        // Currently active skin (null = original)
        private string _activeSkinPath = null;

        // Persistence - remember character across sessions
        private const string PREF_KEY = "CharacterSelect_SavedCharacterId";
        private const string PREF_SKIN_KEY = "CharacterSelect_SavedSkinName";
        private int _savedCharacterId = 0;           // 0 = no saved preference
        private int _lastModSetCharacterId = 0;      // What we last set via the mod
        private int _lastDetectedCharacterId = 0;    // What we last detected on the player
        private float _lastCharacterCheckTime = 0f;
        private const float CHARACTER_CHECK_INTERVAL = 1.0f; // Check every second
        private bool _appliedSavedCharacter = false; // Have we applied the saved character this session?
        private float _sceneLoadTime = 0f;           // When scene was loaded (for delayed apply)
        private bool _playerWasPresent = false;      // Track player presence for spawn detection
        private float _playerSpawnTime = 0f;         // When player was detected

        public override void OnInitializeMelon()
        {
            Melon<CharacterSelectMod>.Logger.Msg("CharacterSelect loaded! Press F6 to open.");

            // Deploy embedded reskins to Mods/reskins/ (first run)
            DeployEmbeddedReskins();

            // Load saved character preference
            LoadSavedCharacter();

            // Scan for reskin files in Mods/reskins/
            ScanForReskins();
        }

        private void LoadSavedCharacter()
        {
            _savedCharacterId = PlayerPrefs.GetInt(PREF_KEY, 0);
            _activeSkinPath = PlayerPrefs.GetString(PREF_SKIN_KEY, "");
            if (string.IsNullOrEmpty(_activeSkinPath)) _activeSkinPath = null;
            if (_savedCharacterId > 0)
                Melon<CharacterSelectMod>.Logger.Msg($"Saved character: {GetCharacterName(_savedCharacterId)}");
        }

        private void SaveCharacterPreference(int characterId, string skinPath = null)
        {
            _savedCharacterId = characterId;
            _lastModSetCharacterId = characterId;
            _activeSkinPath = skinPath;
            PlayerPrefs.SetInt(PREF_KEY, characterId);
            PlayerPrefs.SetString(PREF_SKIN_KEY, skinPath ?? "");
            PlayerPrefs.Save();
            Melon<CharacterSelectMod>.Logger.Msg($"Saved preference: {GetCharacterName(characterId)}" +
                (skinPath != null ? $" (skin: {Path.GetFileNameWithoutExtension(skinPath)})" : ""));
        }

        private void ClearCharacterPreference()
        {
            if (_savedCharacterId != 0)
            {
                _savedCharacterId = 0;
                _lastModSetCharacterId = 0;
                _activeSkinPath = null;
                PlayerPrefs.SetInt(PREF_KEY, 0);
                PlayerPrefs.SetString(PREF_SKIN_KEY, "");
                PlayerPrefs.Save();
            }
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            _currentScene = sceneName;
            _sceneLoadTime = Time.time;
            _appliedSavedCharacter = false;
            _lastDetectedCharacterId = 0;
        }

        public override void OnUpdate()
        {
            // F6 = toggle character selection UI
            if (Input.GetKeyDown(KeyCode.F6))
            {
                if (_showUI)
                {
                    CloseUI();
                }
                else
                {
                    OpenUI();
                }
            }

            // Escape closes the UI
            if (_showUI && Input.GetKeyDown(KeyCode.Escape))
            {
                CloseUI();
            }

            // Detect player spawn/despawn
            var playerObj = GameObject.Find("Player Networked(Clone)");
            bool playerPresent = playerObj != null;

            if (playerPresent && !_playerWasPresent)
            {
                // Player just spawned
                _playerSpawnTime = Time.time;
                _appliedSavedCharacter = false;
                _lastDetectedCharacterId = 0;
            }
            else if (!playerPresent && _playerWasPresent)
            {
            }
            _playerWasPresent = playerPresent;

            // Auto-apply saved character after player spawn (with delay)
            if (!_appliedSavedCharacter && _savedCharacterId > 0 && playerPresent)
            {
                float timeSinceSpawn = Time.time - _playerSpawnTime;
                if (timeSinceSpawn > 1.5f && timeSinceSpawn < 15.0f) // Apply 1.5-15s after spawn
                {
                    Melon<CharacterSelectMod>.Logger.Msg($"Auto-applying saved character: {GetCharacterName(_savedCharacterId)}");
                    _lastModSetCharacterId = _savedCharacterId;
                    SwitchCharacter(_savedCharacterId);
                    ScheduleReskin(_savedCharacterId, _activeSkinPath);
                    _currentCharacterId = _savedCharacterId;
                    _lastDetectedCharacterId = _savedCharacterId;
                    _appliedSavedCharacter = true;
                }
            }

            // Apply pending reskin after delay (waits for model swap to complete)
            if (_pendingReskinCharacterId >= 0 && Time.time >= _reskinApplyTime)
            {
                ApplyReskin(_pendingReskinCharacterId, _pendingReskinPath);
                _pendingReskinCharacterId = -1;
                _pendingReskinPath = null;
            }

            // Periodically check current character to detect in-game changes
            if (Time.time - _lastCharacterCheckTime > CHARACTER_CHECK_INTERVAL)
            {
                _lastCharacterCheckTime = Time.time;
                CheckForExternalCharacterChange();
            }
        }

        private void CheckForExternalCharacterChange()
        {
            // Only check if we have a saved preference
            if (_savedCharacterId == 0) return;

            int currentChar = GetCurrentCharacterId();
            if (currentChar <= 0) return; // Player not found or error

            // First detection after spawn - just record it
            if (_lastDetectedCharacterId == 0)
            {
                _lastDetectedCharacterId = currentChar;
                return;
            }

            // If character changed and it wasn't us who changed it
            if (currentChar != _lastDetectedCharacterId)
            {
                // Did we recently set this character?
                if (currentChar == _lastModSetCharacterId)
                {
                    // This is our change taking effect - just update tracking
                    _lastDetectedCharacterId = currentChar;
                }
                else
                {
                    // User changed character via in-game menu - clear our preference
                    ClearCharacterPreference();
                    _lastDetectedCharacterId = currentChar;
                    _currentCharacterId = currentChar;
                }
            }
        }

        private int GetCurrentCharacterId()
        {
            try
            {
                var playerObj = GameObject.Find("Player Networked(Clone)");
                if (playerObj == null) return -1;

                // Find PlayerControl component
                Component playerControl = null;
                var allComponents = playerObj.GetComponents<Component>();

                foreach (var comp in allComponents)
                {
                    if (comp == null) continue;
                    string typeName = GetIl2CppTypeName(comp);
                    if (typeName == "PlayerControl")
                    {
                        playerControl = comp;
                        break;
                    }
                }

                if (playerControl == null) return -1;

                // Get the actual Il2Cpp type from the assembly
                var assembly = Assembly.Load("Assembly-CSharp");
                Type pcType = null;
                foreach (var type in assembly.GetTypes())
                {
                    if (type.Name == "PlayerControl" && type.Namespace == "Il2Cpp")
                    {
                        pcType = type;
                        break;
                    }
                }

                if (pcType == null)
                {
                    foreach (var type in assembly.GetTypes())
                    {
                        if (type.Name == "PlayerControl")
                        {
                            pcType = type;
                            break;
                        }
                    }
                }

                if (pcType == null) return -1;

                // Cast to actual type
                var castMethod = typeof(Il2CppObjectBase).GetMethod("Cast").MakeGenericMethod(pcType);
                var typedPC = castMethod.Invoke(playerControl, null);

                // The character is stored in sync_EquippedCharacterName which is a SyncVar<CharacterModelName>
                var syncVarProp = pcType.GetProperty("sync_EquippedCharacterName");
                if (syncVarProp != null)
                {
                    var syncVarValue = syncVarProp.GetValue(typedPC);
                    if (syncVarValue != null)
                    {
                        // SyncVar<T> has a Value property that returns T
                        var syncVarType = syncVarValue.GetType();
                        var valueProp = syncVarType.GetProperty("Value");
                        if (valueProp != null)
                        {
                            var charValue = valueProp.GetValue(syncVarValue);
                            if (charValue != null)
                            {
                                return Convert.ToInt32(charValue);
                            }
                        }
                    }
                }

                // Fallback: try common property/field names for character model
                string[] propNames = { "CharacterModel", "characterModel", "CurrentCharacter",
                                       "currentCharacter", "Character", "character",
                                       "CharacterModelName", "characterModelName",
                                       "EquippedCharacterName", "equippedCharacterName" };

                foreach (var name in propNames)
                {
                    var prop = pcType.GetProperty(name);
                    if (prop != null)
                    {
                        var value = prop.GetValue(typedPC);
                        if (value != null && value.GetType().IsEnum)
                        {
                            return Convert.ToInt32(value);
                        }
                    }

                    var field = pcType.GetField(name);
                    if (field != null)
                    {
                        var value = field.GetValue(typedPC);
                        if (value != null && value.GetType().IsEnum)
                        {
                            return Convert.ToInt32(value);
                        }
                    }
                }

                // If not found by name, search for any property that returns CharacterModelName enum
                foreach (var prop in pcType.GetProperties())
                {
                    if (prop.PropertyType.Name == "CharacterModelName")
                    {
                        var value = prop.GetValue(typedPC);
                        if (value != null)
                        {
                            return Convert.ToInt32(value);
                        }
                    }
                }

                foreach (var field in pcType.GetFields())
                {
                    if (field.FieldType.Name == "CharacterModelName")
                    {
                        var value = field.GetValue(typedPC);
                        if (value != null)
                        {
                            return Convert.ToInt32(value);
                        }
                    }
                }

                return -1;
            }
            catch (Exception ex)
            {
                Melon<CharacterSelectMod>.Logger.Warning($"GetCurrentCharacterId error: {ex.Message}");
                return -1;
            }
        }

        private void OpenUI()
        {
            // Save current cursor state
            _previousCursorVisible = Cursor.visible;
            _previousLockState = Cursor.lockState;

            // Show and unlock cursor
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;

            // Find and disable input/camera components
            DisablePlayerInput();

            _showUI = true;
            _expandedGroupIndex = -1;
            _showToolsPanel = false;
        }

        private void CloseUI()
        {
            // Re-enable input/camera components
            EnablePlayerInput();

            // Restore previous cursor state
            Cursor.visible = _previousCursorVisible;
            Cursor.lockState = _previousLockState;

            _showUI = false;
        }

        private void DisablePlayerInput()
        {
            try
            {
                // Find PlayerLocalInput and cast to Behaviour to disable
                var playerInputObj = GameObject.Find("Player Input");
                if (playerInputObj != null)
                {
                    var components = playerInputObj.GetComponents<Component>();
                    foreach (var comp in components)
                    {
                        if (comp != null && GetIl2CppTypeName(comp) == "PlayerLocalInput")
                        {
                            _playerLocalInput = comp;
                            // Cast to Behaviour and disable
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

                // Find PlayerCameraControl on local player
                var playerObj = GameObject.Find("Player Networked(Clone)");
                if (playerObj != null)
                {
                    var components = playerObj.GetComponents<Component>();
                    foreach (var comp in components)
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

                // Also try disabling CinemachineCamera input
                var cinemachineObj = GameObject.Find("CinemachineCamera (makes parent null on start)");
                if (cinemachineObj != null)
                {
                    var components = cinemachineObj.GetComponents<Component>();
                    foreach (var comp in components)
                    {
                        string typeName = GetIl2CppTypeName(comp);
                        if (typeName.Contains("Cinemachine") || typeName.Contains("Input"))
                        {
                            var behaviour = comp.TryCast<Behaviour>();
                            if (behaviour != null && behaviour.enabled)
                            {
                                behaviour.enabled = false;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Melon<CharacterSelectMod>.Logger.Warning($"Error disabling input: {ex.Message}");
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
                    {
                        behaviour.enabled = _playerLocalInputWasEnabled;
                    }
                }

                if (_playerCameraControl != null)
                {
                    var behaviour = _playerCameraControl.TryCast<Behaviour>();
                    if (behaviour != null)
                    {
                        behaviour.enabled = _playerCameraControlWasEnabled;
                    }
                }

                // Re-enable cinemachine
                var cinemachineObj = GameObject.Find("CinemachineCamera (makes parent null on start)");
                if (cinemachineObj != null)
                {
                    var components = cinemachineObj.GetComponents<Component>();
                    foreach (var comp in components)
                    {
                        string typeName = GetIl2CppTypeName(comp);
                        if (typeName.Contains("Cinemachine") || typeName.Contains("Input"))
                        {
                            var behaviour = comp.TryCast<Behaviour>();
                            if (behaviour != null)
                            {
                                behaviour.enabled = true;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Melon<CharacterSelectMod>.Logger.Warning($"Error enabling input: {ex.Message}");
            }
        }

        public override void OnLateUpdate()
        {
            // Force cursor in LateUpdate - something is overriding it
            if (_showUI)
            {
                Cursor.visible = true;
                Cursor.lockState = CursorLockMode.None;
                // Force default system cursor (in case game uses custom/hidden cursor)
                Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
            }
        }

        public override void OnGUI()
        {
            if (!_showUI) return;

            InitTextures();
            LoadCharacterTextures();
            DrawCharacterSelectUI();
        }

        private void InitTextures()
        {
            if (_stylesInitialized) return;

            _bgTexture = MakeTexture(2, 2, new Color(0.1f, 0.1f, 0.15f, 0.95f));
            _buttonTexture = MakeTexture(2, 2, new Color(0.25f, 0.25f, 0.35f, 1f));
            _buttonHoverTexture = MakeTexture(2, 2, new Color(0.35f, 0.35f, 0.5f, 1f));
            _buttonSelectedTexture = MakeTexture(2, 2, new Color(0.2f, 0.5f, 0.3f, 1f));
            _cursorTexture = MakeCursorTexture();

            _stylesInitialized = true;
        }

        private void LoadCharacterTextures()
        {
            if (_texturesLoaded) return;

            _placeholderTexture = LoadEmbeddedTexture("character_placeholder.png");

            _characterTextures = new Texture2D[Characters.Length];
            var allTextures = Resources.FindObjectsOfTypeAll<Texture2D>();

            for (int i = 0; i < Characters.Length; i++)
            {
                string iconName = Characters[i].IconName;

                // First try to load from game assets
                if (iconName != null)
                {
                    foreach (var tex in allTextures)
                    {
                        if (tex != null && tex.name == iconName)
                        {
                            _characterTextures[i] = tex;
                            break;
                        }
                    }
                }

                // If no game icon found, try custom embedded icon
                if (_characterTextures[i] == null && CustomIcons.TryGetValue(Characters[i].GameId, out var customFile))
                    _characterTextures[i] = LoadEmbeddedTexture(customFile);

                if (_characterTextures[i] == null && _placeholderTexture != null)
                    _characterTextures[i] = _placeholderTexture;
            }

            // Find wrench/settings icon from game textures
            foreach (var tex in allTextures)
            {
                if (tex == null) continue;
                var name = tex.name?.ToLower() ?? "";
                if (name.Contains("wrench") || name.Contains("spanner"))
                {
                    _wrenchTexture = tex;
                    break;
                }
            }
            if (_wrenchTexture == null)
                _wrenchTexture = MakeWrenchTexture();

            _texturesLoaded = true;
        }

        private Texture2D LoadEmbeddedTexture(string fileName)
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                var resourceName = $"CharacterSelect.Assets.{fileName}";

                using (var stream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream == null)
                        return null;

                    byte[] data = new byte[stream.Length];
                    stream.Read(data, 0, data.Length);

                    // Create texture and load image data
                    var texture = new Texture2D(2, 2);
                    if (ImageConversion.LoadImage(texture, data))
                    {
                        return texture;
                    }
                    else
                        return null;
                }
            }
            catch (Exception ex)
            {
                Melon<CharacterSelectMod>.Logger.Error($"Error loading embedded texture {fileName}: {ex.Message}");
                return null;
            }
        }

        private Texture2D MakeTexture(int width, int height, Color color)
        {
            Color[] pixels = new Color[width * height];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = color;
            }
            Texture2D texture = new Texture2D(width, height);
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        private Texture2D MakeCursorTexture()
        {
            // Simple arrow cursor 16x16
            int size = 16;
            Texture2D tex = new Texture2D(size, size);
            Color transparent = new Color(0, 0, 0, 0);
            Color white = Color.white;
            Color black = Color.black;

            // Fill with transparent
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    tex.SetPixel(x, y, transparent);
                }
            }

            // Draw arrow cursor (pointing top-left, but we flip Y)
            // Row 0 (top)
            tex.SetPixel(0, 15, white);
            // Row 1
            tex.SetPixel(0, 14, white); tex.SetPixel(1, 14, white);
            // Row 2
            tex.SetPixel(0, 13, white); tex.SetPixel(1, 13, white); tex.SetPixel(2, 13, white);
            // Row 3
            tex.SetPixel(0, 12, white); tex.SetPixel(1, 12, white); tex.SetPixel(2, 12, white); tex.SetPixel(3, 12, white);
            // Row 4
            tex.SetPixel(0, 11, white); tex.SetPixel(1, 11, white); tex.SetPixel(2, 11, white); tex.SetPixel(3, 11, white); tex.SetPixel(4, 11, white);
            // Row 5
            tex.SetPixel(0, 10, white); tex.SetPixel(1, 10, white); tex.SetPixel(2, 10, white); tex.SetPixel(3, 10, white); tex.SetPixel(4, 10, white); tex.SetPixel(5, 10, white);
            // Row 6
            tex.SetPixel(0, 9, white); tex.SetPixel(1, 9, white); tex.SetPixel(2, 9, white); tex.SetPixel(3, 9, white); tex.SetPixel(4, 9, white); tex.SetPixel(5, 9, white); tex.SetPixel(6, 9, white);
            // Row 7
            tex.SetPixel(0, 8, white); tex.SetPixel(1, 8, white); tex.SetPixel(2, 8, white); tex.SetPixel(3, 8, white); tex.SetPixel(4, 8, white);
            // Row 8
            tex.SetPixel(0, 7, white); tex.SetPixel(1, 7, white); tex.SetPixel(2, 7, white); tex.SetPixel(4, 7, white); tex.SetPixel(5, 7, white);
            // Row 9
            tex.SetPixel(0, 6, white); tex.SetPixel(1, 6, white); tex.SetPixel(5, 6, white); tex.SetPixel(6, 6, white);
            // Row 10
            tex.SetPixel(0, 5, white); tex.SetPixel(6, 5, white); tex.SetPixel(7, 5, white);
            // Row 11
            tex.SetPixel(7, 4, white); tex.SetPixel(8, 4, white);

            tex.Apply();
            return tex;
        }

        private Texture2D MakeWrenchTexture()
        {
            // 16x16 simple wrench icon
            int size = 16;
            var tex = new Texture2D(size, size);
            var clear = new Color(0, 0, 0, 0);
            var white = Color.white;

            for (int py = 0; py < size; py++)
                for (int px = 0; px < size; px++)
                    tex.SetPixel(px, py, clear);

            // Draw a simple wrench shape (bottom-left to top-right diagonal with head)
            // Handle (diagonal bar)
            int[][] handle = { new[]{2,1}, new[]{3,2}, new[]{4,3}, new[]{5,4}, new[]{6,5}, new[]{7,6}, new[]{8,7},
                               new[]{3,1}, new[]{4,2}, new[]{5,3}, new[]{6,4}, new[]{7,5}, new[]{8,6}, new[]{9,7} };
            foreach (var p in handle) tex.SetPixel(p[0], p[1], white);

            // Wrench head (top-right open jaw)
            int[][] head = { new[]{9,8}, new[]{10,9}, new[]{11,10}, new[]{12,11}, new[]{13,12},
                             new[]{10,8}, new[]{11,9}, new[]{12,10}, new[]{13,11}, new[]{14,12},
                             new[]{13,13}, new[]{14,13},
                             new[]{11,12}, new[]{10,11}, new[]{9,10}, new[]{9,9},
                             new[]{12,13}, new[]{11,13}, new[]{10,12}, new[]{10,10} };
            foreach (var p in head) tex.SetPixel(p[0], p[1], white);

            // Small nub at handle base
            tex.SetPixel(1, 1, white);
            tex.SetPixel(2, 0, white);
            tex.SetPixel(1, 0, white);

            tex.Apply();
            return tex;
        }

        private void DrawCharacterSelectUI()
        {
            int columns = 4;
            int groupCount = ModelGroups.Length;
            int rows = (groupCount + columns - 1) / columns;

            float buttonWidth = 130;
            float buttonHeight = 115;
            float spacing = 8;
            float windowPadding = 20;

            float windowWidth = columns * buttonWidth + (columns - 1) * spacing + windowPadding * 2;

            // Calculate expanded panel height if a group is open
            float panelHeight = 0;
            int panelItemCount = 0;
            List<SkinEntry> activeSkins = null;
            int[] expandedVariants = null;
            string expandedModelKey = null;

            if (_showToolsPanel)
            {
                // Tools panel takes the expanded area
                panelHeight = 150;
            }
            else if (_expandedGroupIndex >= 0 && _expandedGroupIndex < ModelGroups.Length)
            {
                expandedVariants = ModelGroups[_expandedGroupIndex].GameIds;
                expandedModelKey = ModelGroups[_expandedGroupIndex].ModelKey;
                _availableSkins.TryGetValue(expandedModelKey, out activeSkins);

                // Panel items: variants + custom skins
                panelItemCount = expandedVariants.Length + (activeSkins?.Count ?? 0);
                int panelRows = (panelItemCount + columns - 1) / columns;
                panelHeight = 30 + panelRows * (buttonHeight + spacing);
            }

            float windowHeight = rows * buttonHeight + (rows - 1) * spacing + 120 + panelHeight;

            float x = (Screen.width - windowWidth) / 2;
            float y = (Screen.height - windowHeight) / 2;

            // Draw background
            GUI.DrawTexture(new Rect(x, y, windowWidth, windowHeight), _bgTexture);

            // Title
            GUIStyle titleStyle = new GUIStyle(GUI.skin.label);
            titleStyle.alignment = TextAnchor.MiddleCenter;
            titleStyle.fontSize = 18;
            titleStyle.fontStyle = FontStyle.Bold;
            GUI.Label(new Rect(x, y + 10, windowWidth, 30), "Select Character", titleStyle);

            // Wrench/settings button (top-right corner of window)
            float gearSize = 30;
            Rect gearRect = new Rect(x + windowWidth - gearSize - 8, y + 8, gearSize, gearSize);
            bool gearHover = gearRect.Contains(Event.current.mousePosition);
            GUI.DrawTexture(gearRect, _showToolsPanel ? _buttonSelectedTexture : gearHover ? _buttonHoverTexture : _buttonTexture);
            if (_wrenchTexture != null)
            {
                float iconPad = 5;
                Rect iconRect = new Rect(gearRect.x + iconPad, gearRect.y + iconPad, gearSize - iconPad * 2, gearSize - iconPad * 2);
                GUI.DrawTexture(iconRect, _wrenchTexture, ScaleMode.ScaleToFit);
            }
            if (gearHover && Event.current.type == EventType.MouseDown && Event.current.button == 0)
            {
                _showToolsPanel = !_showToolsPanel;
                if (_showToolsPanel) _expandedGroupIndex = -1;
                Event.current.Use();
            }

            float startX = x + windowPadding;
            float startY = y + 50;

            float imgSize = 70;
            float imgPadding = (buttonWidth - imgSize) / 2;
            float labelHeight = 25;

            _hoverCharacterId = -1;

            GUIStyle labelStyle = new GUIStyle(GUI.skin.label);
            labelStyle.alignment = TextAnchor.MiddleCenter;
            labelStyle.fontSize = 11;

            GUIStyle badgeStyle = new GUIStyle(GUI.skin.label);
            badgeStyle.alignment = TextAnchor.UpperRight;
            badgeStyle.fontSize = 14;
            badgeStyle.fontStyle = FontStyle.Bold;
            badgeStyle.normal.textColor = new Color(1f, 0.85f, 0.2f);

            // --- Top level: model group buttons ---
            for (int gi = 0; gi < ModelGroups.Length; gi++)
            {
                int row = gi / columns;
                int col = gi % columns;

                float btnX = startX + col * (buttonWidth + spacing);
                float btnY = startY + row * (buttonHeight + spacing);
                Rect buttonRect = new Rect(btnX, btnY, buttonWidth, buttonHeight);

                var group = ModelGroups[gi];
                bool isExpanded = (gi == _expandedGroupIndex);
                bool isCurrentGroup = group.GameIds.Contains(_currentCharacterId);
                bool hasCustomSkins = _availableSkins.ContainsKey(group.ModelKey) && _availableSkins[group.ModelKey].Count > 0;
                bool isExpandable = group.GameIds.Length > 1 || hasCustomSkins;
                bool isHover = buttonRect.Contains(Event.current.mousePosition);

                // Background — only show current group as selected when no group is expanded
                Texture2D btnTex = isExpanded ? _buttonSelectedTexture
                    : (isCurrentGroup && _expandedGroupIndex < 0) ? _buttonSelectedTexture
                    : isHover ? _buttonHoverTexture
                    : _buttonTexture;
                GUI.DrawTexture(buttonRect, btnTex);

                // Icon — use the first variant's icon
                int firstId = group.GameIds[0];
                Texture2D icon = GetCharacterIcon(firstId);
                if (icon != null)
                {
                    Rect imgRect = new Rect(btnX + imgPadding, btnY + 8, imgSize, imgSize);
                    GUI.DrawTexture(imgRect, icon, ScaleMode.ScaleToFit);
                }

                // Badge indicating expandable (variants or skins)
                if (isExpandable)
                    GUI.Label(new Rect(btnX + buttonWidth - 22, btnY + 2, 20, 20), "\u2605", badgeStyle);

                // Label
                Rect labelRect = new Rect(btnX, btnY + buttonHeight - labelHeight - 5, buttonWidth, labelHeight);
                GUI.Label(labelRect, group.GroupName, labelStyle);

                // Click
                if (isHover && Event.current.type == EventType.MouseDown && Event.current.button == 0)
                {
                    _showToolsPanel = false;
                    if (!isExpandable)
                    {
                        // Single variant, no skins — immediate select
                        SelectCharacter(group.GameIds[0], null);
                    }
                    else
                    {
                        // Toggle expanded panel
                        _expandedGroupIndex = isExpanded ? -1 : gi;
                    }
                    Event.current.Use();
                }

                if (isHover) _hoverCharacterId = firstId;
            }

            // --- Tools panel ---
            if (_showToolsPanel)
            {
                float panelY = startY + rows * (buttonHeight + spacing) + 5;

                GUIStyle sectionStyle = new GUIStyle(GUI.skin.label);
                sectionStyle.alignment = TextAnchor.MiddleCenter;
                sectionStyle.fontSize = 13;
                sectionStyle.fontStyle = FontStyle.Bold;
                GUI.Label(new Rect(x, panelY, windowWidth, 22),
                    "\u2500\u2500\u2500  Skin Tools  \u2500\u2500\u2500", sectionStyle);
                panelY += 28;

                GUIStyle descStyle = new GUIStyle(GUI.skin.label);
                descStyle.alignment = TextAnchor.MiddleCenter;
                descStyle.fontSize = 11;
                descStyle.wordWrap = true;
                GUI.Label(new Rect(x + windowPadding, panelY, windowWidth - windowPadding * 2, 30),
                    "Export the current character's skin texture as a PNG file for editing.", descStyle);
                panelY += 35;

                float toolBtnWidth = 200;
                float toolBtnHeight = 32;
                float toolBtnX = x + (windowWidth - toolBtnWidth * 2 - spacing) / 2;

                // Export Skin Texture button
                Rect exportRect = new Rect(toolBtnX, panelY, toolBtnWidth, toolBtnHeight);
                bool exportHover = exportRect.Contains(Event.current.mousePosition);
                GUI.DrawTexture(exportRect, exportHover ? _buttonHoverTexture : _buttonTexture);
                GUIStyle toolLabelStyle = new GUIStyle(GUI.skin.label);
                toolLabelStyle.alignment = TextAnchor.MiddleCenter;
                GUI.Label(exportRect, "Export Skin Texture", toolLabelStyle);
                if (exportHover && Event.current.type == EventType.MouseDown && Event.current.button == 0)
                {
                    DumpCurrentSkinTexture();
                    Event.current.Use();
                }

                // Open Folder button
                Rect folderRect = new Rect(toolBtnX + toolBtnWidth + spacing, panelY, toolBtnWidth, toolBtnHeight);
                bool folderHover = folderRect.Contains(Event.current.mousePosition);
                GUI.DrawTexture(folderRect, folderHover ? _buttonHoverTexture : _buttonTexture);
                GUI.Label(folderRect, "Open Skin Dumps Folder", toolLabelStyle);
                if (folderHover && Event.current.type == EventType.MouseDown && Event.current.button == 0)
                {
                    var modsDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                    var dumpDir = Path.Combine(modsDir, "skin_dumps");
                    Directory.CreateDirectory(dumpDir);
                    System.Diagnostics.Process.Start("explorer.exe", dumpDir);
                    Event.current.Use();
                }
            }

            // --- Expanded panel: variants + skins ---
            if (!_showToolsPanel && _expandedGroupIndex >= 0 && expandedVariants != null)
            {
                float panelY = startY + rows * (buttonHeight + spacing) + 5;
                var group = ModelGroups[_expandedGroupIndex];

                // Separator
                GUIStyle sectionStyle = new GUIStyle(GUI.skin.label);
                sectionStyle.alignment = TextAnchor.MiddleCenter;
                sectionStyle.fontSize = 13;
                sectionStyle.fontStyle = FontStyle.Bold;
                GUI.Label(new Rect(x, panelY, windowWidth, 22),
                    $"\u2500\u2500\u2500  {group.GroupName}  \u2500\u2500\u2500", sectionStyle);
                panelY += 28;

                int itemIndex = 0;

                // Variant buttons
                for (int vi = 0; vi < expandedVariants.Length; vi++)
                {
                    int gameId = expandedVariants[vi];
                    int pRow = itemIndex / columns;
                    int pCol = itemIndex % columns;
                    float btnX = startX + pCol * (buttonWidth + spacing);
                    float btnY = panelY + pRow * (buttonHeight + spacing);
                    Rect btnRect = new Rect(btnX, btnY, buttonWidth, buttonHeight);

                    bool hover = btnRect.Contains(Event.current.mousePosition);
                    bool selected = (gameId == _currentCharacterId && _activeSkinPath == null);

                    GUI.DrawTexture(btnRect, selected ? _buttonSelectedTexture : hover ? _buttonHoverTexture : _buttonTexture);

                    Texture2D varIcon = GetCharacterIcon(gameId);
                    if (varIcon != null)
                    {
                        Rect imgRect = new Rect(btnX + imgPadding, btnY + 8, imgSize, imgSize);
                        GUI.DrawTexture(imgRect, varIcon, ScaleMode.ScaleToFit);
                    }

                    Rect lblRect = new Rect(btnX, btnY + buttonHeight - labelHeight - 5, buttonWidth, labelHeight);
                    GUI.Label(lblRect, GetCharacterName(gameId), labelStyle);

                    if (hover && Event.current.type == EventType.MouseDown && Event.current.button == 0)
                    {
                        SelectCharacter(gameId, null);
                        Event.current.Use();
                    }

                    if (hover) _hoverCharacterId = gameId;
                    itemIndex++;
                }

                // Custom skin buttons
                if (activeSkins != null)
                {
                    for (int si = 0; si < activeSkins.Count; si++)
                    {
                        var entry = activeSkins[si];
                        int pRow = itemIndex / columns;
                        int pCol = itemIndex % columns;
                        float btnX = startX + pCol * (buttonWidth + spacing);
                        float btnY = panelY + pRow * (buttonHeight + spacing);
                        Rect btnRect = new Rect(btnX, btnY, buttonWidth, buttonHeight);

                        bool hover = btnRect.Contains(Event.current.mousePosition);
                        bool selected = (_activeSkinPath == entry.FilePath);

                        GUI.DrawTexture(btnRect, selected ? _buttonSelectedTexture : hover ? _buttonHoverTexture : _buttonTexture);

                        // Load skin icon
                        Texture2D skinIcon = null;
                        if (entry.IconPath != null)
                        {
                            if (!_skinIconCache.TryGetValue(entry.IconPath, out skinIcon))
                            {
                                skinIcon = LoadReskinTexture(entry.IconPath);
                                _skinIconCache[entry.IconPath] = skinIcon;
                            }
                        }
                        if (skinIcon != null)
                        {
                            Rect imgRect = new Rect(btnX + imgPadding, btnY + 8, imgSize, imgSize);
                            GUI.DrawTexture(imgRect, skinIcon, ScaleMode.ScaleToFit);
                        }

                        Rect lblRect = new Rect(btnX, btnY + buttonHeight - labelHeight - 5, buttonWidth, labelHeight);
                        GUI.Label(lblRect, entry.DisplayName, labelStyle);

                        if (hover && Event.current.type == EventType.MouseDown && Event.current.button == 0)
                        {
                            // Apply skin to current character, or first variant if no character from this group selected
                            int targetId = group.GameIds.Contains(_currentCharacterId) ? _currentCharacterId : group.GameIds[0];
                            SelectCharacter(targetId, entry.FilePath);
                            Event.current.Use();
                        }

                        itemIndex++;
                    }
                }
            }

            // Close button
            float closeBtnWidth = 180;
            float closeBtnHeight = 35;
            float closeBtnX = x + (windowWidth - closeBtnWidth) / 2;
            float closeBtnY = y + windowHeight - closeBtnHeight - 15;
            Rect closeRect = new Rect(closeBtnX, closeBtnY, closeBtnWidth, closeBtnHeight);

            bool closeHover = closeRect.Contains(Event.current.mousePosition);
            GUI.DrawTexture(closeRect, closeHover ? _buttonHoverTexture : _buttonTexture);

            GUIStyle closeLabelStyle = new GUIStyle(GUI.skin.label);
            closeLabelStyle.alignment = TextAnchor.MiddleCenter;
            GUI.Label(closeRect, "Close (F6 / Esc)", closeLabelStyle);

            if (closeHover && Event.current.type == EventType.MouseDown && Event.current.button == 0)
            {
                CloseUI();
                Event.current.Use();
            }

            // Draw custom cursor
            if (_cursorTexture != null)
            {
                Vector2 mousePos = Event.current.mousePosition;
                GUI.DrawTexture(new Rect(mousePos.x, mousePos.y, 16, 16), _cursorTexture);
            }
        }

        private Texture2D GetCharacterIcon(int gameId)
        {
            if (_characterTextures == null) return null;
            for (int i = 0; i < Characters.Length; i++)
            {
                if (Characters[i].GameId == gameId && i < _characterTextures.Length)
                    return _characterTextures[i];
            }
            return null;
        }

        private void SelectCharacter(int characterId, string skinPath)
        {
            _currentCharacterId = characterId;
            _lastDetectedCharacterId = characterId;
            _appliedSavedCharacter = true;
            _expandedGroupIndex = -1;
            SaveCharacterPreference(characterId, skinPath);
            SwitchCharacter(characterId);
            ScheduleReskin(characterId, skinPath);
            CloseUI();
        }

        private string GetCharacterName(int gameId)
        {
            foreach (var character in Characters)
            {
                if (character.GameId == gameId)
                    return character.Name;
            }
            return "Unknown";
        }

        private void DumpCharacterAssets()
        {
            Melon<CharacterSelectMod>.Logger.Msg("=== CHARACTER ASSETS DUMP ===");

            // Look for character-related textures
            var textures = Resources.FindObjectsOfTypeAll<Texture2D>();
            Melon<CharacterSelectMod>.Logger.Msg($"Found {textures.Length} textures total");

            foreach (var tex in textures)
            {
                if (tex == null) continue;
                string name = tex.name.ToLower();
                if (name.Contains("frog") || name.Contains("penguin") || name.Contains("bear") ||
                    name.Contains("seal") || name.Contains("turtle") || name.Contains("fox") ||
                    name.Contains("panda") || name.Contains("toad") || name.Contains("character") ||
                    name.Contains("icon") || name.Contains("portrait") || name.Contains("avatar"))
                {
                    Melon<CharacterSelectMod>.Logger.Msg($"  Texture: {tex.name} ({tex.width}x{tex.height})");
                }
            }

            // Look for sprites
            var sprites = Resources.FindObjectsOfTypeAll<Sprite>();
            Melon<CharacterSelectMod>.Logger.Msg($"Found {sprites.Length} sprites total");

            foreach (var sprite in sprites)
            {
                if (sprite == null) continue;
                string name = sprite.name.ToLower();
                if (name.Contains("frog") || name.Contains("penguin") || name.Contains("bear") ||
                    name.Contains("seal") || name.Contains("turtle") || name.Contains("fox") ||
                    name.Contains("panda") || name.Contains("toad") || name.Contains("character") ||
                    name.Contains("icon") || name.Contains("portrait") || name.Contains("avatar"))
                {
                    Melon<CharacterSelectMod>.Logger.Msg($"  Sprite: {sprite.name} ({sprite.rect.width}x{sprite.rect.height})");
                }
            }

            Melon<CharacterSelectMod>.Logger.Msg("=== END CHARACTER ASSETS ===");
        }

        private void DumpInputState()
        {
            Melon<CharacterSelectMod>.Logger.Msg("=== INPUT/CURSOR STATE DUMP ===");
            Melon<CharacterSelectMod>.Logger.Msg($"Cursor.visible: {Cursor.visible}");
            Melon<CharacterSelectMod>.Logger.Msg($"Cursor.lockState: {Cursor.lockState}");
            Melon<CharacterSelectMod>.Logger.Msg($"Time.timeScale: {Time.timeScale}");

            // Look for input-related managers/objects
            var allObjects = Object.FindObjectsOfType<GameObject>();
            foreach (var obj in allObjects)
            {
                var name = obj.name.ToLower();
                if (name.Contains("input") || name.Contains("cursor") || name.Contains("pause") ||
                    name.Contains("menu") || name.Contains("ui") || name.Contains("manager"))
                {
                    if (obj.activeInHierarchy)
                    {
                        // Get components on this object
                        var components = obj.GetComponents<Component>();
                        var compNames = new List<string>();
                        foreach (var comp in components)
                        {
                            if (comp != null)
                            {
                                string typeName = GetIl2CppTypeName(comp);
                                if (typeName != "Transform" && typeName != "RectTransform")
                                {
                                    compNames.Add(typeName);
                                }
                            }
                        }
                        if (compNames.Count > 0)
                        {
                            Melon<CharacterSelectMod>.Logger.Msg($"[ACTIVE] {obj.name}: {string.Join(", ", compNames)}");
                        }
                    }
                }
            }

            // Look for specific types that might control input
            Melon<CharacterSelectMod>.Logger.Msg("--- Searching for input controllers ---");
            var allComponents = Object.FindObjectsOfType<Component>();
            foreach (var comp in allComponents)
            {
                if (comp == null) continue;
                string typeName = GetIl2CppTypeName(comp);
                if (typeName.ToLower().Contains("input") || typeName.ToLower().Contains("cursor") ||
                    typeName.ToLower().Contains("camera") || typeName.ToLower().Contains("player"))
                {
                    // Check for enabled/disabled state
                    var enabledProp = comp.GetType().GetProperty("enabled");
                    string enabledStr = "";
                    if (enabledProp != null)
                    {
                        try
                        {
                            bool enabled = (bool)enabledProp.GetValue(comp);
                            enabledStr = enabled ? " [enabled]" : " [DISABLED]";
                        }
                        catch { }
                    }
                    Melon<CharacterSelectMod>.Logger.Msg($"  {typeName} on {comp.gameObject.name}{enabledStr}");
                }
            }

            Melon<CharacterSelectMod>.Logger.Msg("=== END DUMP ===");
        }

        private string GetIl2CppTypeName(Component comp)
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

        private void DumpPlayerRendererInfo()
        {
            if (_dumpedRendererInfo) return;
            _dumpedRendererInfo = true;

            try
            {
                var playerObj = GameObject.Find("Player Networked(Clone)");
                if (playerObj == null)
                {
                    Melon<CharacterSelectMod>.Logger.Warning("DumpPlayerRendererInfo: No player found");
                    return;
                }

                var renderers = playerObj.GetComponentsInChildren<SkinnedMeshRenderer>();
                Melon<CharacterSelectMod>.Logger.Msg($"=== PLAYER RENDERER DUMP ({renderers.Length} SkinnedMeshRenderers) ===");
                foreach (var r in renderers)
                {
                    if (r == null) continue;
                    var mat = r.material;
                    Melon<CharacterSelectMod>.Logger.Msg($"  Renderer: {r.gameObject.name}, Mesh: {r.sharedMesh?.name}, Bones: {r.bones?.Length}");
                    if (mat != null)
                    {
                        Melon<CharacterSelectMod>.Logger.Msg($"    Material: {mat.name}, Shader: {mat.shader?.name}");
                        foreach (var prop in new[] { "_MainTex", "_BaseMap", "_BaseColorMap", "_AlbedoMap" })
                        {
                            if (mat.HasProperty(prop))
                            {
                                var tex = mat.GetTexture(prop);
                                Melon<CharacterSelectMod>.Logger.Msg($"    Has {prop}: {tex?.name} ({tex?.width}x{tex?.height})");
                            }
                        }
                    }
                }

                // Also check regular MeshRenderers
                var meshRenderers = playerObj.GetComponentsInChildren<MeshRenderer>();
                if (meshRenderers.Length > 0)
                {
                    Melon<CharacterSelectMod>.Logger.Msg($"  ({meshRenderers.Length} regular MeshRenderers)");
                    foreach (var mr in meshRenderers)
                    {
                        if (mr == null) continue;
                        var mat = mr.material;
                        Melon<CharacterSelectMod>.Logger.Msg($"  MeshRenderer: {mr.gameObject.name}");
                        if (mat != null)
                        {
                            Melon<CharacterSelectMod>.Logger.Msg($"    Material: {mat.name}, Shader: {mat.shader?.name}");
                            foreach (var prop in new[] { "_MainTex", "_BaseMap", "_BaseColorMap", "_AlbedoMap" })
                            {
                                if (mat.HasProperty(prop))
                                {
                                    var tex = mat.GetTexture(prop);
                                    Melon<CharacterSelectMod>.Logger.Msg($"    Has {prop}: {tex?.name} ({tex?.width}x{tex?.height})");
                                }
                            }
                        }
                    }
                }

                Melon<CharacterSelectMod>.Logger.Msg("=== END RENDERER DUMP ===");
            }
            catch (Exception ex)
            {
                Melon<CharacterSelectMod>.Logger.Error($"DumpPlayerRendererInfo error: {ex.Message}");
            }
        }

        /// <summary>Copy a GPU-only texture to a readable Texture2D via RenderTexture.</summary>
        /// <summary>Bake a SkinnedMeshRenderer into a readable Mesh snapshot (preserves UVs).</summary>
        private Mesh BakeMeshReadable(SkinnedMeshRenderer smr)
        {
            try
            {
                var baked = new Mesh();
                smr.BakeMesh(baked);
                return baked;
            }
            catch (Exception ex)
            {
                Melon<CharacterSelectMod>.Logger.Warning($"    BakeMesh failed for '{smr.gameObject.name}': {ex.Message}");
                return null;
            }
        }

        /// <summary>Copy a GPU-only texture to a readable Texture2D via RenderTexture.</summary>
        private Texture2D CopyTextureToReadable(Texture2D source)
        {
            int w = source.width, h = source.height;
            var rt = new RenderTexture(w, h, 0);
            rt.Create();
            Graphics.Blit(source, rt);

            var prevRT = RenderTexture.active;
            RenderTexture.active = rt;

            var readable = new Texture2D(w, h, TextureFormat.RGBA32, false);
            readable.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            readable.Apply();

            RenderTexture.active = prevRT;
            rt.Release();
            Object.Destroy(rt);
            return readable;
        }

        /// <summary>Encode a readable Texture2D to BMP (32bpp BGRA) byte array.</summary>
        private byte[] EncodeToBmp(Texture2D tex)
        {
            int w = tex.width, h = tex.height;
            int pixelDataSize = w * h * 4;
            int fileSize = 54 + pixelDataSize;
            byte[] bmp = new byte[fileSize];

            // BMP file header
            bmp[0] = 0x42; bmp[1] = 0x4D;
            BitConverter.GetBytes(fileSize).CopyTo(bmp, 2);
            BitConverter.GetBytes(54).CopyTo(bmp, 10);

            // DIB header
            BitConverter.GetBytes(40).CopyTo(bmp, 14);
            BitConverter.GetBytes(w).CopyTo(bmp, 18);
            BitConverter.GetBytes(h).CopyTo(bmp, 22);
            bmp[26] = 1; bmp[28] = 32;

            int offset = 54;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    var c = tex.GetPixel(x, y);
                    bmp[offset++] = (byte)(c.b * 255f);
                    bmp[offset++] = (byte)(c.g * 255f);
                    bmp[offset++] = (byte)(c.r * 255f);
                    bmp[offset++] = (byte)(c.a * 255f);
                }
            }
            return bmp;
        }

        /// <summary>Encode a readable Texture2D to PNG byte array manually (Il2Cpp-safe, no ImageConversion.EncodeToPNG).</summary>
        private byte[] EncodeToPngManual(Texture2D tex)
        {
            int w = tex.width, h = tex.height;

            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);

            // PNG signature
            bw.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

            // IHDR chunk
            WriteChunk(bw, "IHDR", writer =>
            {
                writer.Write(ToBigEndian(w));
                writer.Write(ToBigEndian(h));
                writer.Write((byte)8);  // bit depth
                writer.Write((byte)6);  // color type: RGBA
                writer.Write((byte)0);  // compression
                writer.Write((byte)0);  // filter
                writer.Write((byte)0);  // interlace
            });

            // IDAT chunk — build raw scanlines then DEFLATE
            byte[] rawData;
            using (var rawMs = new MemoryStream())
            {
                for (int y = h - 1; y >= 0; y--) // PNG is top-down, Unity y=0 is bottom
                {
                    rawMs.WriteByte(0); // filter: None
                    for (int x = 0; x < w; x++)
                    {
                        var c = tex.GetPixel(x, y);
                        rawMs.WriteByte((byte)(c.r * 255f));
                        rawMs.WriteByte((byte)(c.g * 255f));
                        rawMs.WriteByte((byte)(c.b * 255f));
                        rawMs.WriteByte((byte)(c.a * 255f));
                    }
                }
                rawData = rawMs.ToArray();
            }

            byte[] compressedData;
            using (var compMs = new MemoryStream())
            {
                // zlib header: CM=8, CINFO=7 (32K window), FCHECK so header%31==0
                compMs.WriteByte(0x78);
                compMs.WriteByte(0x01);
                using (var deflate = new DeflateStream(compMs, System.IO.Compression.CompressionLevel.Fastest, leaveOpen: true))
                {
                    deflate.Write(rawData, 0, rawData.Length);
                }
                // zlib Adler-32 checksum
                uint adler = Adler32(rawData);
                compMs.WriteByte((byte)((adler >> 24) & 0xFF));
                compMs.WriteByte((byte)((adler >> 16) & 0xFF));
                compMs.WriteByte((byte)((adler >> 8) & 0xFF));
                compMs.WriteByte((byte)(adler & 0xFF));
                compressedData = compMs.ToArray();
            }

            WriteChunk(bw, "IDAT", writer => writer.Write(compressedData));

            // IEND chunk
            WriteChunk(bw, "IEND", _ => { });

            return ms.ToArray();
        }

        private static void WriteChunk(BinaryWriter bw, string type, Action<BinaryWriter> writeData)
        {
            using var dataMs = new MemoryStream();
            using (var dataWriter = new BinaryWriter(dataMs, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                writeData(dataWriter);
            }
            byte[] data = dataMs.ToArray();
            byte[] typeBytes = System.Text.Encoding.ASCII.GetBytes(type);

            bw.Write(ToBigEndian(data.Length));
            bw.Write(typeBytes);
            bw.Write(data);

            // CRC32 over type + data
            uint crc = Crc32(typeBytes, data);
            bw.Write(ToBigEndian((int)crc));
        }

        private static byte[] ToBigEndian(int value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
            return bytes;
        }

        private static uint Adler32(byte[] data)
        {
            uint a = 1, b = 0;
            foreach (byte d in data)
            {
                a = (a + d) % 65521;
                b = (b + a) % 65521;
            }
            return (b << 16) | a;
        }

        private static uint Crc32(byte[] typeBytes, byte[] data)
        {
            // Standard CRC32 with polynomial 0xEDB88320
            uint crc = 0xFFFFFFFF;
            foreach (byte b in typeBytes) crc = Crc32Update(crc, b);
            foreach (byte b in data) crc = Crc32Update(crc, b);
            return crc ^ 0xFFFFFFFF;
        }

        private static uint Crc32Update(uint crc, byte b)
        {
            crc ^= b;
            for (int i = 0; i < 8; i++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
            return crc;
        }

        /// <summary>Get the original skin texture for a character from the player's material.</summary>
        private Texture2D GetOriginalTexture(int characterId)
        {
            // Check cache first
            if (_originalSkinTextures.TryGetValue(characterId, out var cached) && cached != null)
            {
                var tex2d = cached.TryCast<Texture2D>();
                if (tex2d != null) return tex2d;
            }

            var playerObj = GameObject.Find("Player Networked(Clone)");
            if (playerObj == null) return null;

            if (!CharacterSkinMaterials.TryGetValue(characterId, out var skinPrefix)) return null;

            var renderers = playerObj.GetComponentsInChildren<SkinnedMeshRenderer>();
            foreach (var r in renderers)
            {
                if (r == null) continue;
                var mat = r.material;
                if (mat == null) continue;
                var shaderName = mat.shader?.name;
                if (shaderName == null || shaderName == "Standard" || shaderName.Contains("Eyes")) continue;
                var matName = mat.name ?? "";
                if (!matName.StartsWith(skinPrefix, StringComparison.OrdinalIgnoreCase)) continue;

                Texture tex = null;
                if (mat.HasProperty("_BaseMap")) tex = mat.GetTexture("_BaseMap");
                else if (mat.HasProperty("_MainTex")) tex = mat.GetTexture("_MainTex");

                if (tex != null)
                {
                    _originalSkinTextures[characterId] = tex;
                    var tex2d = tex.TryCast<Texture2D>();
                    if (tex2d != null) return tex2d;
                }
            }
            return null;
        }

        private void DumpCurrentSkinTexture()
        {
            try
            {
                var playerObj = GameObject.Find("Player Networked(Clone)");
                if (playerObj == null)
                {
                    Melon<CharacterSelectMod>.Logger.Warning("DumpSkinTexture: No player found");
                    return;
                }

                if (!CharacterSkinMaterials.TryGetValue(_currentCharacterId, out var skinPrefix))
                {
                    Melon<CharacterSelectMod>.Logger.Warning($"DumpSkinTexture: No skin prefix for character {_currentCharacterId}");
                    return;
                }

                // Find skin texture
                Texture skinTexture = null;
                string textureName = null;
                var renderers = playerObj.GetComponentsInChildren<SkinnedMeshRenderer>();
                foreach (var r in renderers)
                {
                    if (r == null) continue;
                    var mat = r.material;
                    if (mat == null) continue;
                    var shaderName = mat.shader?.name;
                    if (shaderName == null || shaderName == "Standard" || shaderName.Contains("Eyes")) continue;
                    var matName = mat.name ?? "";
                    if (!matName.StartsWith(skinPrefix, StringComparison.OrdinalIgnoreCase)) continue;

                    if (mat.HasProperty("_BaseMap"))
                    {
                        skinTexture = mat.GetTexture("_BaseMap");
                        textureName = skinTexture?.name;
                        break;
                    }
                    if (mat.HasProperty("_MainTex"))
                    {
                        skinTexture = mat.GetTexture("_MainTex");
                        textureName = skinTexture?.name;
                        break;
                    }
                }

                if (skinTexture == null)
                {
                    Melon<CharacterSelectMod>.Logger.Warning("DumpSkinTexture: No skin texture found on player");
                    return;
                }

                var skinTex2D = skinTexture.TryCast<Texture2D>();
                if (skinTex2D == null)
                {
                    Melon<CharacterSelectMod>.Logger.Warning("DumpSkinTexture: Could not cast to Texture2D");
                    return;
                }


                // Make readable if needed
                Texture2D readableTex = skinTex2D.isReadable ? skinTex2D : CopyTextureToReadable(skinTex2D);

                // Save as PNG
                byte[] png = EncodeToPngManual(readableTex);

                if (!skinTex2D.isReadable)
                    Object.Destroy(readableTex);

                var modsDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                var dumpDir = Path.Combine(modsDir, "skin_dumps");
                Directory.CreateDirectory(dumpDir);

                var charName = GetCharacterName(_currentCharacterId).ToLower().Replace(" ", "_");
                var outputPath = Path.Combine(dumpDir, $"{charName}_skin.png");
                File.WriteAllBytes(outputPath, png);
                Melon<CharacterSelectMod>.Logger.Msg($"Saved skin texture to: {outputPath} ({png.Length} bytes)");

                // Also export UV wireframe template
                ExportUVTemplate(playerObj, skinPrefix, charName, dumpDir);
            }
            catch (Exception ex)
            {
                Melon<CharacterSelectMod>.Logger.Error($"DumpSkinTexture error: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private void AutoExportUVTemplates()
        {
            try
            {
                var playerObj = GameObject.Find("Player Networked(Clone)");
                if (playerObj == null) return;

                if (!CharacterSkinMaterials.TryGetValue(_currentCharacterId, out var skinPrefix)) return;

                var modsDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                var dumpDir = Path.Combine(modsDir, "skin_dumps");
                var modelKey = GetModelKey(skinPrefix);
                var outputPath = Path.Combine(dumpDir, $"{modelKey}_uv_template.png");

                // Skip if already exported for this model
                if (File.Exists(outputPath)) return;

                Directory.CreateDirectory(dumpDir);
                ExportUVTemplate(playerObj, skinPrefix, modelKey, dumpDir);
            }
            catch (Exception ex)
            {
                Melon<CharacterSelectMod>.Logger.Warning($"AutoExportUVTemplates error: {ex.Message}");
            }
        }

        private void ExportUVTemplate(GameObject playerObj, string skinPrefix, string charName, string dumpDir)
        {
            try
            {
                int templateSize = 1024;
                var template = new Texture2D(templateSize, templateSize, TextureFormat.RGBA32, false);

                // Fill with transparent black
                var clearPixels = new Color[templateSize * templateSize];
                for (int i = 0; i < clearPixels.Length; i++)
                    clearPixels[i] = new Color(0, 0, 0, 0);
                template.SetPixels(clearPixels);

                // Color palette for different body parts
                Color[] partColors = {
                    new Color(1f, 0.2f, 0.2f, 1f),   // Red
                    new Color(0.2f, 1f, 0.2f, 1f),    // Green
                    new Color(0.3f, 0.5f, 1f, 1f),    // Blue
                    new Color(1f, 1f, 0.2f, 1f),       // Yellow
                    new Color(1f, 0.5f, 0f, 1f),       // Orange
                    new Color(0.8f, 0.2f, 1f, 1f),     // Purple
                    new Color(0f, 1f, 1f, 1f),          // Cyan
                    new Color(1f, 0.5f, 0.7f, 1f),     // Pink
                };

                var renderers = playerObj.GetComponentsInChildren<SkinnedMeshRenderer>();
                int partIndex = 0;
                var legend = new List<string>();


                foreach (var r in renderers)
                {
                    if (r == null) continue;
                    var mat = r.material;
                    if (mat == null) continue;
                    var shaderName = mat.shader?.name;
                    if (shaderName == null || shaderName == "Standard" || shaderName.Contains("Eyes")) continue;
                    var matName = mat.name ?? "";
                    if (!matName.StartsWith(skinPrefix, StringComparison.OrdinalIgnoreCase)) continue;

                    var mesh = r.sharedMesh;
                    if (mesh == null) continue;

                    // Meshes may not be readable — bake via SkinnedMeshRenderer to get readable copy
                    if (!mesh.isReadable)
                    {
                        mesh = BakeMeshReadable(r);
                        if (mesh == null) continue;
                    }

                    var uvs = mesh.uv;
                    var triangles = mesh.triangles;
                    if (uvs == null || uvs.Length == 0 || triangles == null || triangles.Length == 0)
                    {
                        continue;
                    }

                    Color color = partColors[partIndex % partColors.Length];
                    string colorName = partIndex < partColors.Length
                        ? new[] { "Red", "Green", "Blue", "Yellow", "Orange", "Purple", "Cyan", "Pink" }[partIndex]
                        : $"Color {partIndex}";

                    legend.Add($"  {colorName} = {r.gameObject.name}");

                    // Draw triangle edges
                    for (int t = 0; t < triangles.Length; t += 3)
                    {
                        int i0 = triangles[t], i1 = triangles[t + 1], i2 = triangles[t + 2];
                        if (i0 >= uvs.Length || i1 >= uvs.Length || i2 >= uvs.Length) continue;

                        DrawLineUV(template, uvs[i0], uvs[i1], templateSize, color);
                        DrawLineUV(template, uvs[i1], uvs[i2], templateSize, color);
                        DrawLineUV(template, uvs[i2], uvs[i0], templateSize, color);
                    }

                    partIndex++;
                }

                if (partIndex == 0)
                {
                    Object.Destroy(template);
                    return;
                }

                template.Apply();
                byte[] pngData = EncodeToPngManual(template);
                Object.Destroy(template);

                var outputPath = Path.Combine(dumpDir, $"{charName}_uv_template.png");
                File.WriteAllBytes(outputPath, pngData);

                var legendPath = Path.Combine(dumpDir, $"{charName}_uv_legend.txt");
                File.WriteAllText(legendPath, $"UV Template Legend - {charName}\n\n" + string.Join("\n", legend) + "\n");

            }
            catch (Exception ex)
            {
                Melon<CharacterSelectMod>.Logger.Error($"ExportUVTemplate error: {ex.Message}\n{ex.StackTrace}");
            }
        }

        /// <summary>Draw a line between two UV coordinates using Bresenham's algorithm.</summary>
        private static void DrawLineUV(Texture2D tex, Vector2 uv0, Vector2 uv1, int size, Color color)
        {
            int x0 = (int)(uv0.x * (size - 1));
            int y0 = (int)(uv0.y * (size - 1));
            int x1 = (int)(uv1.x * (size - 1));
            int y1 = (int)(uv1.y * (size - 1));

            // Clamp to texture bounds
            x0 = Math.Clamp(x0, 0, size - 1); y0 = Math.Clamp(y0, 0, size - 1);
            x1 = Math.Clamp(x1, 0, size - 1); y1 = Math.Clamp(y1, 0, size - 1);

            int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
            int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
            int err = dx + dy;

            while (true)
            {
                tex.SetPixel(x0, y0, color);
                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err;
                if (e2 >= dy) { err += dy; x0 += sx; }
                if (e2 <= dx) { err += dx; y0 += sy; }
            }
        }

        private void ScheduleReskin(int characterId, string skinPath)
        {
            _pendingReskinCharacterId = characterId;
            _pendingReskinPath = skinPath;
            _reskinApplyTime = Time.time + 0.5f;
        }

        // Maps skin prefix → base model key (for UV template dedup and reskin grouping)
        private static readonly Dictionary<string, string> SkinPrefixToModelKey = new()
        {
            { "Skin_Frog", "frog" },
            { "Skin_Penguin", "penguin" },
            { "Skin_Seal", "seal" },
            { "Skin_Bear_Brown", "bear" },
            { "Skin_Bear_Polar", "bear" },
            { "Skin_Bear_Black", "bear" },
            { "Skin_Toad", "toad" },
            { "Skin_Fox", "fox" },
            { "Skin_Panda", "panda" },
        };

        private static string GetModelKey(string skinPrefix)
        {
            return SkinPrefixToModelKey.TryGetValue(skinPrefix, out var key)
                ? key
                : skinPrefix.Replace("Skin_", "").ToLower();
        }

        // Maps GameId to the material name prefix used by that character's skin
        private static readonly Dictionary<int, string> CharacterSkinMaterials = new()
        {
            { 1, "Skin_Frog" },           // Frog
            { 2, "Skin_Penguin" },         // Penguin
            { 3, "Skin_Seal" },            // Harbor Seal
            { 5, "Skin_Bear_Brown" },      // Brown Bear
            { 6, "Skin_Bear_Polar" },      // Polar Bear
            { 7, "Skin_Bear_Black" },      // Black Bear
            { 8, "Skin_Seal" },            // Ringed Seal
            { 9, "Skin_Seal" },            // Baikal Seal
            { 10, "Skin_Frog" },           // Strawberry Frog
            { 11, "Skin_Frog" },           // Tree Frog
            { 12, "Skin_Toad" },           // Orange Toad
            { 13, "Skin_Toad" },           // Brown Toad
            { 14, "Skin_Fox" },            // Orange Fox
            { 15, "Skin_Fox" },            // Arctic Fox
            { 16, "Skin_Panda" },          // Panda
        };

        private void DeployEmbeddedReskins()
        {
            try
            {
                var modsDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                var reskinsDir = Path.Combine(modsDir, "reskins");
                var assembly = Assembly.GetExecutingAssembly();
                var prefix = "CharacterSelect.Assets.reskins.";

                foreach (var resourceName in assembly.GetManifestResourceNames())
                {
                    if (!resourceName.StartsWith(prefix)) continue;

                    // Convert resource name to file path: CharacterSelect.Assets.reskins.panda.sunburned_panda.json → panda/sunburned_panda.json
                    var relativeParts = resourceName.Substring(prefix.Length);
                    // First segment is the character folder, rest is the filename
                    var dotIndex = relativeParts.IndexOf('.');
                    if (dotIndex < 0) continue;

                    // Find the last dot that separates filename from extension
                    var lastDot = relativeParts.LastIndexOf('.');
                    var ext = relativeParts.Substring(lastDot); // e.g. ".json" or ".png"

                    // Everything between prefix removal and extension, with first dot as path separator
                    var pathPart = relativeParts.Substring(0, lastDot);
                    var charFolder = pathPart.Substring(0, dotIndex);
                    var fileName = pathPart.Substring(dotIndex + 1) + ext;

                    var targetDir = Path.Combine(reskinsDir, charFolder);
                    var targetPath = Path.Combine(targetDir, fileName);

                    if (File.Exists(targetPath)) continue; // Don't overwrite user modifications

                    Directory.CreateDirectory(targetDir);
                    using (var stream = assembly.GetManifestResourceStream(resourceName))
                    {
                        if (stream == null) continue;
                        var data = new byte[stream.Length];
                        stream.Read(data, 0, data.Length);
                        File.WriteAllBytes(targetPath, data);
                    }
                }
            }
            catch (Exception ex)
            {
                Melon<CharacterSelectMod>.Logger.Error($"Error deploying embedded reskins: {ex.Message}");
            }
        }

        private void ScanForReskins()
        {
            var modsDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            _reskinsDir = Path.Combine(modsDir, "reskins");

            if (!Directory.Exists(_reskinsDir))
            {
                Directory.CreateDirectory(_reskinsDir);
                return;
            }

            // Scan subdirectories: reskins/{character_name}/{skin_name}.png|.json
            foreach (var charDir in Directory.GetDirectories(_reskinsDir))
            {
                var charKey = Path.GetFileName(charDir).ToLower();
                var skins = new List<SkinEntry>();

                foreach (var file in Directory.GetFiles(charDir))
                {
                    var ext = Path.GetExtension(file).ToLower();
                    var skinName = Path.GetFileNameWithoutExtension(file);

                    // Skip icon files and generated cache files
                    if (skinName.EndsWith("_icon", StringComparison.OrdinalIgnoreCase)) continue;
                    if (skinName.EndsWith("_generated", StringComparison.OrdinalIgnoreCase)) continue;

                    if (ext == ".json")
                    {
                        // JSON procedural skin
                        var cachePath = Path.Combine(charDir, skinName + "_generated.png");
                        bool needsGen = true;
                        if (File.Exists(cachePath))
                        {
                            // Regenerate if JSON is newer than cached PNG
                            var jsonTime = File.GetLastWriteTimeUtc(file);
                            var cacheTime = File.GetLastWriteTimeUtc(cachePath);
                            needsGen = jsonTime > cacheTime;
                        }

                        string iconPath = null;
                        foreach (var iconExt in new[] { ".png", ".jpg", ".bmp" })
                        {
                            var candidate = Path.Combine(charDir, skinName + "_icon" + iconExt);
                            if (File.Exists(candidate)) { iconPath = candidate; break; }
                        }

                        var displayName = string.Join(" ",
                            skinName.Split('_').Select(w => w.Length > 0 ? char.ToUpper(w[0]) + w.Substring(1) : w));

                        skins.Add(new SkinEntry
                        {
                            DisplayName = displayName,
                            FilePath = cachePath,
                            IconPath = iconPath,
                            NeedsGeneration = needsGen,
                            JsonPath = file
                        });
                    }
                    else if (ext == ".png" || ext == ".jpg" || ext == ".bmp")
                    {
                        // Image-based skin
                        string iconPath = null;
                        foreach (var iconExt in new[] { ".png", ".jpg", ".bmp" })
                        {
                            var candidate = Path.Combine(charDir, skinName + "_icon" + iconExt);
                            if (File.Exists(candidate)) { iconPath = candidate; break; }
                        }

                        var displayName = string.Join(" ",
                            skinName.Split('_').Select(w => w.Length > 0 ? char.ToUpper(w[0]) + w.Substring(1) : w));

                        skins.Add(new SkinEntry { DisplayName = displayName, FilePath = file, IconPath = iconPath });
                    }
                }

                if (skins.Count > 0)
                    _availableSkins[charKey] = skins;
            }

            // Also support legacy flat files: reskins/{character}_skin.* (backward compat)
            foreach (var file in Directory.GetFiles(_reskinsDir, "*_skin.*"))
            {
                var ext = Path.GetExtension(file).ToLower();
                if (ext != ".png" && ext != ".jpg" && ext != ".bmp") continue;

                var nameWithoutExt = Path.GetFileNameWithoutExtension(file);
                if (!nameWithoutExt.EndsWith("_skin")) continue;
                var charKey = nameWithoutExt.Substring(0, nameWithoutExt.Length - 5);

                if (!_availableSkins.ContainsKey(charKey))
                    _availableSkins[charKey] = new List<SkinEntry>();

                var displayName = string.Join(" ",
                    charKey.Split('_').Select(w => w.Length > 0 ? char.ToUpper(w[0]) + w.Substring(1) : w)) + " (Custom)";

                _availableSkins[charKey].Add(new SkinEntry { DisplayName = displayName, FilePath = file });
            }

            int totalSkins = _availableSkins.Values.Sum(s => s.Count);
            Melon<CharacterSelectMod>.Logger.Msg($"Found {totalSkins} custom skin(s) for {_availableSkins.Count} character(s)");
        }

        private string GetCharacterReskinKey(int characterId)
        {
            // Use model key so skins are shared across variants (e.g. all bears share "bear")
            if (CharacterSkinMaterials.TryGetValue(characterId, out var prefix))
                return GetModelKey(prefix);
            return GetCharacterName(characterId).ToLower().Replace(" ", "_");
        }

        private Texture2D GenerateProceduralSkin(Texture2D original, string jsonPath)
        {
            try
            {
                var json = File.ReadAllText(jsonPath);
                var definition = JsonSerializer.Deserialize<SkinDefinition>(json);
                if (definition?.Transforms == null || definition.Transforms.Count == 0)
                {
                    Melon<CharacterSelectMod>.Logger.Warning($"Procedural skin has no transforms: {jsonPath}");
                    return null;
                }

                // Make sure we have a readable copy
                Texture2D src = original.isReadable ? original : CopyTextureToReadable(original);
                int w = src.width, h = src.height;
                var result = new Texture2D(w, h, TextureFormat.RGBA32, false);

                int totalPixels = w * h;
                int lastLogPercent = 0;

                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        Color c = src.GetPixel(x, y);
                        // Compute brightness from original (luminance)
                        float brightness = 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;

                        Color modified = c;
                        foreach (var transform in definition.Transforms)
                        {
                            if (transform.Where != null)
                            {
                                if (brightness < transform.Where.BrightnessMin || brightness > transform.Where.BrightnessMax)
                                    continue;
                            }

                            float blend = Math.Clamp(transform.Blend, 0f, 1f);

                            switch (transform.Action?.ToLower())
                            {
                                case "recolor":
                                    if (transform.Color is { Length: >= 3 })
                                    {
                                        var target = new Color(transform.Color[0], transform.Color[1], transform.Color[2], modified.a);
                                        modified = new Color(
                                            modified.r + (target.r - modified.r) * blend,
                                            modified.g + (target.g - modified.g) * blend,
                                            modified.b + (target.b - modified.b) * blend,
                                            modified.a);
                                    }
                                    break;

                                case "tint":
                                    if (transform.Color is { Length: >= 3 })
                                    {
                                        var tinted = new Color(
                                            modified.r * transform.Color[0],
                                            modified.g * transform.Color[1],
                                            modified.b * transform.Color[2],
                                            modified.a);
                                        modified = new Color(
                                            modified.r + (tinted.r - modified.r) * blend,
                                            modified.g + (tinted.g - modified.g) * blend,
                                            modified.b + (tinted.b - modified.b) * blend,
                                            modified.a);
                                    }
                                    break;

                                case "hue_shift":
                                    RgbToHsv(modified.r, modified.g, modified.b, out float hue, out float sat, out float val);
                                    hue = (hue + transform.Degrees * blend / 360f) % 1f;
                                    if (hue < 0) hue += 1f;
                                    HsvToRgb(hue, sat, val, out float nr, out float ng, out float nb);
                                    modified = new Color(nr, ng, nb, modified.a);
                                    break;
                            }
                        }

                        result.SetPixel(x, y, modified);
                    }

                    // Log progress at 25% intervals
                    int percent = (int)((y + 1) * 100f / h);
                    if (percent >= lastLogPercent + 25)
                    {
                        lastLogPercent = (percent / 25) * 25;
                    }
                }

                result.Apply();

                if (!original.isReadable && src != original)
                    Object.Destroy(src);

                return result;
            }
            catch (Exception ex)
            {
                Melon<CharacterSelectMod>.Logger.Error($"GenerateProceduralSkin error: {ex.Message}\n{ex.StackTrace}");
                return null;
            }
        }

        // Pure-math HSV helpers (no Il2Cpp dependency)
        private static void RgbToHsv(float r, float g, float b, out float h, out float s, out float v)
        {
            float max = Math.Max(r, Math.Max(g, b));
            float min = Math.Min(r, Math.Min(g, b));
            float delta = max - min;

            v = max;
            s = max > 0 ? delta / max : 0;

            if (delta == 0) { h = 0; return; }

            if (max == r) h = (g - b) / delta;
            else if (max == g) h = 2 + (b - r) / delta;
            else h = 4 + (r - g) / delta;

            h /= 6f;
            if (h < 0) h += 1f;
        }

        private static void HsvToRgb(float h, float s, float v, out float r, out float g, out float b)
        {
            if (s == 0) { r = g = b = v; return; }

            h *= 6f;
            int i = (int)Math.Floor(h);
            float f = h - i;
            float p = v * (1 - s);
            float q = v * (1 - s * f);
            float t = v * (1 - s * (1 - f));

            switch (i % 6)
            {
                case 0: r = v; g = t; b = p; return;
                case 1: r = q; g = v; b = p; return;
                case 2: r = p; g = v; b = t; return;
                case 3: r = p; g = q; b = v; return;
                case 4: r = t; g = p; b = v; return;
                default: r = v; g = p; b = q; return;
            }
        }

        private void ApplyReskin(int characterId, string skinPath)
        {
            try
            {
                var playerObj = GameObject.Find("Player Networked(Clone)");
                if (playerObj == null) return;

                CharacterSkinMaterials.TryGetValue(characterId, out var skinPrefix);

                var renderers = playerObj.GetComponentsInChildren<SkinnedMeshRenderer>();

                // If no custom skin, restore original texture
                if (string.IsNullOrEmpty(skinPath))
                {
                    if (_originalSkinTextures.TryGetValue(characterId, out var origTex) && origTex != null)
                    {
                        int restored = 0;
                        foreach (var r in renderers)
                        {
                            if (r == null) continue;
                            var mat = r.material;
                            if (mat == null) continue;
                            var shaderName = mat.shader?.name;
                            if (shaderName == null || shaderName == "Standard" || shaderName.Contains("Eyes")) continue;
                            var matName = mat.name ?? "";
                            if (skinPrefix != null && !matName.StartsWith(skinPrefix, StringComparison.OrdinalIgnoreCase)) continue;

                            if (mat.HasProperty("_BaseMap")) { mat.SetTexture("_BaseMap", origTex); restored++; }
                            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", origTex);
                        }
                    }
                    return;
                }

                // Check if this is a procedural (JSON) skin that needs generation
                SkinEntry? matchingEntry = FindSkinEntry(skinPath);
                if (matchingEntry != null && matchingEntry.Value.NeedsGeneration && matchingEntry.Value.JsonPath != null)
                {

                    // Get original texture
                    var origTex2D = GetOriginalTexture(characterId);
                    if (origTex2D == null)
                    {
                        // Try to capture it from current renderers
                        foreach (var r in renderers)
                        {
                            if (r == null) continue;
                            var mat = r.material;
                            if (mat == null) continue;
                            var sn = mat.shader?.name;
                            if (sn == null || sn == "Standard" || sn.Contains("Eyes")) continue;
                            var mn = mat.name ?? "";
                            if (skinPrefix != null && !mn.StartsWith(skinPrefix, StringComparison.OrdinalIgnoreCase)) continue;

                            Texture tex = null;
                            if (mat.HasProperty("_BaseMap")) tex = mat.GetTexture("_BaseMap");
                            else if (mat.HasProperty("_MainTex")) tex = mat.GetTexture("_MainTex");

                            if (tex != null)
                            {
                                _originalSkinTextures[characterId] = tex;
                                origTex2D = tex.TryCast<Texture2D>();
                                break;
                            }
                        }
                    }

                    if (origTex2D != null)
                    {
                        var generated = GenerateProceduralSkin(origTex2D, matchingEntry.Value.JsonPath);
                        if (generated != null)
                        {
                            // Cache the generated texture in PNG and save to disk
                            byte[] pngData = EncodeToPngManual(generated);
                            File.WriteAllBytes(skinPath, pngData);

                            _skinTextureCache[skinPath] = generated;

                            // Mark entry as no longer needing generation
                            MarkSkinGenerated(skinPath);
                        }
                    }
                    else
                    {
                        Melon<CharacterSelectMod>.Logger.Warning("Cannot generate procedural skin: original texture not available");
                    }
                }

                // Load custom skin texture
                if (!_skinTextureCache.TryGetValue(skinPath, out var texture))
                {
                    texture = LoadReskinTexture(skinPath);
                    if (texture != null) _skinTextureCache[skinPath] = texture;
                }
                if (texture == null) return;

                int swapped = 0;
                foreach (var r in renderers)
                {
                    if (r == null) continue;
                    var mat = r.material;
                    if (mat == null) continue;

                    var shaderName = mat.shader?.name;
                    if (shaderName == null || shaderName == "Standard") continue;
                    if (shaderName.Contains("Eyes")) continue;

                    var matName = mat.name ?? "";
                    if (skinPrefix != null && !matName.StartsWith(skinPrefix, StringComparison.OrdinalIgnoreCase)) continue;

                    // Save original texture before first swap
                    if (!_originalSkinTextures.ContainsKey(characterId))
                    {
                        if (mat.HasProperty("_BaseMap"))
                            _originalSkinTextures[characterId] = mat.GetTexture("_BaseMap");
                        else if (mat.HasProperty("_MainTex"))
                            _originalSkinTextures[characterId] = mat.GetTexture("_MainTex");
                    }

                    if (mat.HasProperty("_BaseMap"))
                    {
                        mat.SetTexture("_BaseMap", texture);
                        swapped++;
                    }
                    if (mat.HasProperty("_MainTex"))
                    {
                        mat.SetTexture("_MainTex", texture);
                    }
                }
            }
            catch (Exception ex)
            {
                Melon<CharacterSelectMod>.Logger.Error($"ApplyReskin error: {ex.Message}");
            }
        }

        private Texture2D LoadReskinTexture(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    Melon<CharacterSelectMod>.Logger.Warning($"Reskin file not found: {filePath}");
                    return null;
                }

                byte[] data = File.ReadAllBytes(filePath);
                var ext = Path.GetExtension(filePath).ToLower();

                // BMP needs manual parsing (ImageConversion only supports PNG/JPG)
                if (ext == ".bmp")
                {
                    var texture = LoadBmpData(data);
                    if (texture == null)
                        Melon<CharacterSelectMod>.Logger.Warning($"Failed to parse BMP: {filePath}");
                    return texture;
                }

                // PNG/JPG — use Unity's built-in loader
                var tex = new Texture2D(2, 2);
                if (ImageConversion.LoadImage(tex, data))
                {
                    return tex;
                }
                else
                {
                    Melon<CharacterSelectMod>.Logger.Warning($"Failed to load reskin image: {filePath}");
                    return null;
                }
            }
            catch (Exception ex)
            {
                Melon<CharacterSelectMod>.Logger.Error($"Error loading reskin texture {filePath}: {ex.Message}");
                return null;
            }
        }

        private Texture2D LoadBmpData(byte[] data)
        {
            if (data.Length < 54 || data[0] != 0x42 || data[1] != 0x4D)
                return null;

            int pixelOffset = BitConverter.ToInt32(data, 10);
            int width = BitConverter.ToInt32(data, 18);
            int height = BitConverter.ToInt32(data, 22);
            int bpp = BitConverter.ToInt16(data, 28);

            if (bpp != 32 || width <= 0 || height <= 0)
            {
                Melon<CharacterSelectMod>.Logger.Warning($"BMP loader only supports 32bpp, got {bpp}bpp ({width}x{height})");
                return null;
            }

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);

            // BMP pixel data is bottom-up, BGRA order
            int stride = width * 4;
            for (int y = 0; y < height; y++)
            {
                int rowStart = pixelOffset + y * stride;
                for (int x = 0; x < width; x++)
                {
                    int i = rowStart + x * 4;
                    float b = data[i] / 255f;
                    float g = data[i + 1] / 255f;
                    float r = data[i + 2] / 255f;
                    float a = data[i + 3] / 255f;
                    texture.SetPixel(x, y, new Color(r, g, b, a));
                }
            }
            texture.Apply();
            return texture;
        }

        private SkinEntry? FindSkinEntry(string filePath)
        {
            foreach (var kvp in _availableSkins)
            {
                for (int i = 0; i < kvp.Value.Count; i++)
                {
                    if (kvp.Value[i].FilePath == filePath)
                        return kvp.Value[i];
                }
            }
            return null;
        }

        private void MarkSkinGenerated(string filePath)
        {
            foreach (var kvp in _availableSkins)
            {
                for (int i = 0; i < kvp.Value.Count; i++)
                {
                    if (kvp.Value[i].FilePath == filePath)
                    {
                        var entry = kvp.Value[i];
                        entry.NeedsGeneration = false;
                        kvp.Value[i] = entry;
                        return;
                    }
                }
            }
        }

        private void SwitchCharacter(int characterId)
        {
            try
            {
                // Find local player
                var playerObj = GameObject.Find("Player Networked(Clone)");
                if (playerObj == null)
                {
                    Melon<CharacterSelectMod>.Logger.Warning("No local player found");
                    return;
                }

                // Find PlayerControl component using Il2Cpp type names
                Component playerControl = null;
                var allComponents = playerObj.GetComponents<Component>();

                foreach (var comp in allComponents)
                {
                    if (comp == null) continue;
                    string typeName = GetIl2CppTypeName(comp);
                    if (typeName == "PlayerControl")
                    {
                        playerControl = comp;
                        break;
                    }
                }

                if (playerControl == null)
                {
                    Melon<CharacterSelectMod>.Logger.Warning("PlayerControl not found");
                    return;
                }

                // Get the actual Il2Cpp type from the assembly
                var assembly = Assembly.Load("Assembly-CSharp");
                Type pcType = null;
                foreach (var type in assembly.GetTypes())
                {
                    if (type.Name == "PlayerControl" && type.Namespace == "Il2Cpp")
                    {
                        pcType = type;
                        break;
                    }
                }

                if (pcType == null)
                {
                    foreach (var type in assembly.GetTypes())
                    {
                        if (type.Name == "PlayerControl")
                        {
                            pcType = type;
                            break;
                        }
                    }
                }

                if (pcType == null)
                {
                    Melon<CharacterSelectMod>.Logger.Warning("PlayerControl type not found");
                    return;
                }

                // Find CmdSwitchCharacter method
                var switchMethod = pcType.GetMethod("CmdSwitchCharacter");
                if (switchMethod == null)
                {
                    Melon<CharacterSelectMod>.Logger.Warning("CmdSwitchCharacter method not found");
                    return;
                }

                var parameters = switchMethod.GetParameters();
                if (parameters.Length == 1)
                {
                    var paramType = parameters[0].ParameterType;
                    var enumValue = Enum.ToObject(paramType, characterId);

                    // Cast to the actual PlayerControl type and invoke
                    var castMethod = typeof(Il2CppObjectBase).GetMethod("Cast").MakeGenericMethod(pcType);
                    var typedPC = castMethod.Invoke(playerControl, null);

                    switchMethod.Invoke(typedPC, new object[] { enumValue });
                    Melon<CharacterSelectMod>.Logger.Msg($"Switched to {GetCharacterName(characterId)}!");
                }
            }
            catch (Exception ex)
            {
                Melon<CharacterSelectMod>.Logger.Error($"Failed to switch character: {ex.Message}");
            }
        }
    }
}
