using MelonLoader;
using UnityEngine;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
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

        // Persistence - remember character across sessions
        private const string PREF_KEY = "CharacterSelect_SavedCharacterId";
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
            Melon<CharacterSelectMod>.Logger.Msg("CharacterSelect loaded!");
            Melon<CharacterSelectMod>.Logger.Msg("  Press F6 to open character selection");

            // Load saved character preference
            LoadSavedCharacter();
        }

        private void LoadSavedCharacter()
        {
            _savedCharacterId = PlayerPrefs.GetInt(PREF_KEY, 0);
            if (_savedCharacterId > 0)
            {
                Melon<CharacterSelectMod>.Logger.Msg($"Loaded saved character: {GetCharacterName(_savedCharacterId)}");
            }
        }

        private void SaveCharacterPreference(int characterId)
        {
            _savedCharacterId = characterId;
            _lastModSetCharacterId = characterId;
            PlayerPrefs.SetInt(PREF_KEY, characterId);
            PlayerPrefs.Save();
            Melon<CharacterSelectMod>.Logger.Msg($"Saved character preference: {GetCharacterName(characterId)}");
        }

        private void ClearCharacterPreference()
        {
            if (_savedCharacterId != 0)
            {
                Melon<CharacterSelectMod>.Logger.Msg("Cleared saved character (changed via in-game menu)");
                _savedCharacterId = 0;
                _lastModSetCharacterId = 0;
                PlayerPrefs.SetInt(PREF_KEY, 0);
                PlayerPrefs.Save();
            }
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            _currentScene = sceneName;
            _sceneLoadTime = Time.time;
            _appliedSavedCharacter = false;
            _lastDetectedCharacterId = 0;
            Melon<CharacterSelectMod>.Logger.Msg($"Scene loaded: {sceneName}");
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

            // F8 debug disabled - conflicts with YetiHunt teleport
            // if (Input.GetKeyDown(KeyCode.F8))
            // {
            //     DumpCharacterAssets();
            // }

            // Detect player spawn/despawn
            var playerObj = GameObject.Find("Player Networked(Clone)");
            bool playerPresent = playerObj != null;

            if (playerPresent && !_playerWasPresent)
            {
                // Player just spawned
                _playerSpawnTime = Time.time;
                _appliedSavedCharacter = false;
                _lastDetectedCharacterId = 0;
                Melon<CharacterSelectMod>.Logger.Msg($"Player spawned, saved character: {(_savedCharacterId > 0 ? GetCharacterName(_savedCharacterId) : "none")}");
            }
            else if (!playerPresent && _playerWasPresent)
            {
                // Player despawned
                Melon<CharacterSelectMod>.Logger.Msg("Player despawned");
            }
            _playerWasPresent = playerPresent;

            // Auto-apply saved character after player spawn (with delay)
            if (!_appliedSavedCharacter && _savedCharacterId > 0 && playerPresent)
            {
                float timeSinceSpawn = Time.time - _playerSpawnTime;
                if (timeSinceSpawn > 1.5f && timeSinceSpawn < 15.0f) // Apply 1.5-15s after spawn
                {
                    Melon<CharacterSelectMod>.Logger.Msg($"Auto-applying saved character: {GetCharacterName(_savedCharacterId)} ({timeSinceSpawn:F1}s after spawn)");
                    _lastModSetCharacterId = _savedCharacterId;
                    SwitchCharacter(_savedCharacterId);
                    _currentCharacterId = _savedCharacterId;
                    _lastDetectedCharacterId = _savedCharacterId;
                    _appliedSavedCharacter = true;
                }
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
                Melon<CharacterSelectMod>.Logger.Msg($"Initial character detected: {GetCharacterName(currentChar)}");
                return;
            }

            // If character changed and it wasn't us who changed it
            if (currentChar != _lastDetectedCharacterId)
            {
                Melon<CharacterSelectMod>.Logger.Msg($"Character change detected: {GetCharacterName(_lastDetectedCharacterId)} -> {GetCharacterName(currentChar)}");

                // Did we recently set this character?
                if (currentChar == _lastModSetCharacterId)
                {
                    // This is our change taking effect - just update tracking
                    _lastDetectedCharacterId = currentChar;
                    Melon<CharacterSelectMod>.Logger.Msg("  (This was our mod's change)");
                }
                else
                {
                    // User changed character via in-game menu - clear our preference
                    Melon<CharacterSelectMod>.Logger.Msg("  User changed via in-game menu - clearing saved preference");
                    ClearCharacterPreference();
                    _lastDetectedCharacterId = currentChar;
                    _currentCharacterId = currentChar;
                }
            }
        }

        private bool _dumpedPlayerControlMembers = false;

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

                // One-time dump of PlayerControl members to find the right property
                if (!_dumpedPlayerControlMembers)
                {
                    _dumpedPlayerControlMembers = true;
                    Melon<CharacterSelectMod>.Logger.Msg("=== PlayerControl members (looking for character property) ===");
                    foreach (var prop in pcType.GetProperties())
                    {
                        Melon<CharacterSelectMod>.Logger.Msg($"  Property: {prop.Name} : {prop.PropertyType.Name}");
                    }
                    foreach (var field in pcType.GetFields())
                    {
                        Melon<CharacterSelectMod>.Logger.Msg($"  Field: {field.Name} : {field.FieldType.Name}");
                    }
                    Melon<CharacterSelectMod>.Logger.Msg("=== End PlayerControl members ===");
                }

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
            Melon<CharacterSelectMod>.Logger.Msg("Character selection opened");
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
                                Melon<CharacterSelectMod>.Logger.Msg("Disabled PlayerLocalInput");
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
                                Melon<CharacterSelectMod>.Logger.Msg("Disabled PlayerCameraControl");
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
                                Melon<CharacterSelectMod>.Logger.Msg($"Disabled {typeName}");
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

            // Load placeholder from embedded resources
            _placeholderTexture = LoadEmbeddedTexture("character_placeholder.png");
            if (_placeholderTexture != null)
            {
                Melon<CharacterSelectMod>.Logger.Msg("Loaded custom placeholder icon from embedded resources");
            }

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
                            Melon<CharacterSelectMod>.Logger.Msg($"Loaded game icon for {Characters[i].Name}: {iconName}");
                            break;
                        }
                    }
                }

                // If no game icon found, try custom embedded icon
                if (_characterTextures[i] == null && CustomIcons.TryGetValue(Characters[i].GameId, out var customFile))
                {
                    _characterTextures[i] = LoadEmbeddedTexture(customFile);
                    if (_characterTextures[i] != null)
                    {
                        Melon<CharacterSelectMod>.Logger.Msg($"Loaded custom icon for {Characters[i].Name}: {customFile}");
                    }
                }

                // Still nothing? Use placeholder
                if (_characterTextures[i] == null && _placeholderTexture != null)
                {
                    _characterTextures[i] = _placeholderTexture;
                    Melon<CharacterSelectMod>.Logger.Msg($"Using placeholder for {Characters[i].Name}");
                }
            }

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
                    {
                        Melon<CharacterSelectMod>.Logger.Warning($"Embedded resource not found: {resourceName}");
                        // List available resources for debugging
                        var names = assembly.GetManifestResourceNames();
                        Melon<CharacterSelectMod>.Logger.Msg($"Available resources: {string.Join(", ", names)}");
                        return null;
                    }

                    byte[] data = new byte[stream.Length];
                    stream.Read(data, 0, data.Length);

                    // Create texture and load image data
                    var texture = new Texture2D(2, 2);
                    if (ImageConversion.LoadImage(texture, data))
                    {
                        return texture;
                    }
                    else
                    {
                        Melon<CharacterSelectMod>.Logger.Warning($"Failed to load image data from {fileName}");
                        return null;
                    }
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

        private void DrawCharacterSelectUI()
        {
            // Window dimensions
            int charCount = Characters.Length; // 15 characters (Turtle removed)
            int columns = 5;
            int rows = (charCount + columns - 1) / columns; // 3 rows for 15 chars

            float buttonWidth = 130;
            float buttonHeight = 115;
            float spacing = 8;
            float windowPadding = 20;

            float windowWidth = columns * buttonWidth + (columns - 1) * spacing + windowPadding * 2;
            float windowHeight = rows * buttonHeight + (rows - 1) * spacing + 120; // Extra for title and close button

            float x = (Screen.width - windowWidth) / 2;
            float y = (Screen.height - windowHeight) / 2;

            // Draw background
            GUI.DrawTexture(new Rect(x, y, windowWidth, windowHeight), _bgTexture);

            // Title (centered)
            GUIStyle titleStyle = new GUIStyle(GUI.skin.label);
            titleStyle.alignment = TextAnchor.MiddleCenter;
            titleStyle.fontSize = 18;
            titleStyle.fontStyle = FontStyle.Bold;
            GUI.Label(new Rect(x, y + 10, windowWidth, 30), "Select Character", titleStyle);

            float startX = x + windowPadding;
            float startY = y + 50;

            // Image dimensions within button
            float imgSize = 70;
            float imgPadding = (buttonWidth - imgSize) / 2;
            float labelHeight = 25;

            _hoverCharacterId = -1;

            GUIStyle labelStyle = new GUIStyle(GUI.skin.label);
            labelStyle.alignment = TextAnchor.MiddleCenter;
            labelStyle.fontSize = 11;

            for (int i = 0; i < Characters.Length; i++)
            {
                int row = i / columns;
                int col = i % columns;

                float btnX = startX + col * (buttonWidth + spacing);
                float btnY = startY + row * (buttonHeight + spacing);
                Rect buttonRect = new Rect(btnX, btnY, buttonWidth, buttonHeight);

                int gameId = Characters[i].GameId;

                // Check hover
                bool isHover = buttonRect.Contains(Event.current.mousePosition);
                bool isSelected = (gameId == _currentCharacterId);

                // Draw button background
                Texture2D btnTex = isSelected ? _buttonSelectedTexture : (isHover ? _buttonHoverTexture : _buttonTexture);
                GUI.DrawTexture(buttonRect, btnTex);

                // Draw character image if available
                if (_characterTextures != null && i < _characterTextures.Length && _characterTextures[i] != null)
                {
                    Rect imgRect = new Rect(btnX + imgPadding, btnY + 8, imgSize, imgSize);
                    GUI.DrawTexture(imgRect, _characterTextures[i], ScaleMode.ScaleToFit);
                }

                // Draw character name at bottom of button
                Rect labelRect = new Rect(btnX, btnY + buttonHeight - labelHeight - 5, buttonWidth, labelHeight);
                GUI.Label(labelRect, Characters[i].Name, labelStyle);

                // Handle click
                if (isHover && Event.current.type == EventType.MouseDown && Event.current.button == 0)
                {
                    SelectCharacter(gameId);
                    Event.current.Use();
                }

                if (isHover) _hoverCharacterId = gameId;
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

            // Draw custom cursor (since game uses UI cursor that we can't easily access)
            if (_cursorTexture != null)
            {
                Vector2 mousePos = Event.current.mousePosition;
                GUI.DrawTexture(new Rect(mousePos.x, mousePos.y, 16, 16), _cursorTexture);
            }
        }

        private void SelectCharacter(int characterId)
        {
            _currentCharacterId = characterId;
            _lastDetectedCharacterId = characterId;
            _appliedSavedCharacter = true; // Don't auto-apply again this session
            SaveCharacterPreference(characterId);
            SwitchCharacter(characterId);
            CloseUI();  // Close UI after selection (restores cursor)
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
