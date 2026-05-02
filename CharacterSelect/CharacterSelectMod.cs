using MelonLoader;
using UnityEngine;
using System;
using CharacterSelect.Data;
using CharacterSelect.Infrastructure;
using CharacterSelect.Services;
using CharacterSelect.UI;

namespace CharacterSelect
{
    public class CharacterSelectMod : MelonMod
    {
        // Services
        private IModLogger _logger;
        private ITypeResolver _typeResolver;
        private ICharacterService _characterService;
        private IPreferenceService _preferenceService;
        private ISkinService _skinService;
        private TextureLoader _textureLoader;
        private ICharacterSelectUI _ui;

        // Scene & state
        private string _currentScene = "";

        // Cursor state to restore when closing
        private bool _previousCursorVisible;
        private CursorLockMode _previousLockState;

        // Components to disable while UI is open
        private Component _playerLocalInput;
        private Component _playerCameraControl;
        private bool _playerLocalInputWasEnabled;
        private bool _playerCameraControlWasEnabled;

        // Character tracking
        private int _currentCharacterId = 1;

        // Auto-apply saved character after spawn
        private int _lastModSetCharacterId = 0;
        private int _lastDetectedCharacterId = 0;
        private float _lastCharacterCheckTime = 0f;
        private const float CHARACTER_CHECK_INTERVAL = 1.0f;
        private bool _appliedSavedCharacter = false;
        private float _sceneLoadTime = 0f;
        private bool _playerWasPresent = false;
        private float _playerSpawnTime = 0f;

        public override void OnInitializeMelon()
        {
            _logger = new MelonLoggerAdapter(Melon<CharacterSelectMod>.Logger);
            _typeResolver = new Il2CppTypeResolver(_logger);
            _typeResolver.Initialize();
            _characterService = new CharacterService(_logger, _typeResolver);
            _preferenceService = new PreferenceService(_logger);
            _skinService = new SkinService(_logger);
            _textureLoader = new TextureLoader(_logger);
            _ui = new CharacterSelectUI(_textureLoader, _skinService);

            _logger.Info("CharacterSelect loaded! Press F6 to open.");

            _skinService.DeployEmbeddedReskins();
            _preferenceService.Load();
            _skinService.ScanForReskins();
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
                if (_ui.IsVisible)
                    CloseUI();
                else
                    OpenUI();
            }

            // Escape closes the UI
            if (_ui.IsVisible && Input.GetKeyDown(KeyCode.Escape))
                CloseUI();

            // Detect player spawn/despawn
            var playerObj = GameObject.Find("Player Networked(Clone)");
            bool playerPresent = playerObj != null;

            if (playerPresent && !_playerWasPresent)
            {
                _playerSpawnTime = Time.time;
                _appliedSavedCharacter = false;
                _lastDetectedCharacterId = 0;
            }
            _playerWasPresent = playerPresent;

            // Auto-apply saved character after player spawn (with delay)
            if (!_appliedSavedCharacter && _preferenceService.SavedCharacterId > 0 && playerPresent)
            {
                float timeSinceSpawn = Time.time - _playerSpawnTime;
                if (timeSinceSpawn > 1.5f && timeSinceSpawn < 15.0f)
                {
                    int savedId = _preferenceService.SavedCharacterId;
                    _logger.Info($"Auto-applying saved character: {CharacterData.GetCharacterName(savedId)}");
                    _lastModSetCharacterId = savedId;
                    _characterService.SwitchCharacter(savedId);
                    _skinService.ScheduleReskin(savedId, _preferenceService.ActiveSkinPath);
                    _currentCharacterId = savedId;
                    _lastDetectedCharacterId = savedId;
                    _appliedSavedCharacter = true;
                }
            }

            // Apply pending reskin after delay
            _skinService.ProcessPendingReskin();

            // Periodically check current character to detect in-game changes
            if (Time.time - _lastCharacterCheckTime > CHARACTER_CHECK_INTERVAL)
            {
                _lastCharacterCheckTime = Time.time;
                CheckForExternalCharacterChange();
            }
        }

        private void CheckForExternalCharacterChange()
        {
            if (_preferenceService.SavedCharacterId == 0) return;

            int currentChar = _characterService.GetCurrentCharacterId();
            if (currentChar <= 0) return;

            if (_lastDetectedCharacterId == 0)
            {
                _lastDetectedCharacterId = currentChar;
                return;
            }

            if (currentChar != _lastDetectedCharacterId)
            {
                if (currentChar == _lastModSetCharacterId)
                {
                    _lastDetectedCharacterId = currentChar;
                }
                else
                {
                    _preferenceService.Clear();
                    _lastDetectedCharacterId = currentChar;
                    _currentCharacterId = currentChar;
                }
            }
        }

        private void SelectCharacter(int characterId, string skinPath)
        {
            _currentCharacterId = characterId;
            _lastModSetCharacterId = characterId;
            _lastDetectedCharacterId = characterId;
            _appliedSavedCharacter = true;
            _preferenceService.Save(characterId, skinPath);
            _characterService.SwitchCharacter(characterId);
            _skinService.ScheduleReskin(characterId, skinPath);
            CloseUI();
        }

        private void OpenUI()
        {
            _previousCursorVisible = Cursor.visible;
            _previousLockState = Cursor.lockState;

            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;

            DisablePlayerInput();
            _ui.Open();
        }

        private void CloseUI()
        {
            EnablePlayerInput();

            Cursor.visible = _previousCursorVisible;
            Cursor.lockState = _previousLockState;

            _ui.Close();
        }

        public override void OnLateUpdate()
        {
            if (_ui.IsVisible)
            {
                Cursor.visible = true;
                Cursor.lockState = CursorLockMode.None;
                Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
            }
        }

        public override void OnGUI()
        {
            _ui.Draw(_currentCharacterId, _preferenceService.ActiveSkinPath, SelectCharacter);
        }

        private void DisablePlayerInput()
        {
            try
            {
                var playerInputObj = GameObject.Find("Player Input");
                if (playerInputObj != null)
                {
                    var components = playerInputObj.GetComponents<Component>();
                    foreach (var comp in components)
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
                                behaviour.enabled = false;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Error disabling input: {ex.Message}");
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
                                behaviour.enabled = true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Error enabling input: {ex.Message}");
            }
        }

        private static string GetIl2CppTypeName(Component comp)
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
    }
}
