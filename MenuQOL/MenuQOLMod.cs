using MelonLoader;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using Il2CppTMPro;
using Object = UnityEngine.Object;

namespace MenuQOL
{
    public class MenuQOLMod : MelonMod
    {
        // Auto-confirm feature
        private bool _autoConfirmEnabled = true;
        private bool _userClickedHost = false;
        private bool _hooked = false;

        // Lobby settings UI references
        private bool _lobbySettingsHooked = false;
        private TMP_InputField _passwordInput = null;
        private Toggle _passwordToggle = null;
        private Button _confirmHostButton = null;
        private Toggle _publicPrivateToggle = null;
        private Toggle _inviteOnlyToggle = null;
        private Slider _playerCountSlider = null;
        private Toggle _peacefulToggle = null;
        private Toggle _disableVoiceToggle = null;

        // Quick Host
        private bool _quickHostButtonInjected = false;
        private TMP_Text _quickHostText = null;
        private QuickHostPhase _quickHostPhase = QuickHostPhase.Idle;
        private enum QuickHostPhase { Idle, WaitingForSettings }

        // PlayerPrefs keys
        private const string PREF_PASSWORD = "MenuQOL_LastPassword";
        private const string PREF_USE_PASSWORD = "MenuQOL_UsePassword";
        private const string PREF_PUBLIC = "MenuQOL_LobbyPublic";
        private const string PREF_INVITE_ONLY = "MenuQOL_InviteOnly";
        private const string PREF_PLAYER_COUNT = "MenuQOL_PlayerCount";
        private const string PREF_PEACEFUL = "MenuQOL_PeacefulMode";
        private const string PREF_VOICE_DISABLED = "MenuQOL_DisableVoice";
        private const string PREF_HAS_SAVED = "MenuQOL_HasSavedSettings";

        // Paths to exclude (world geometry, not UI)
        private static readonly string[] ExcludedPathPrefixes = new[]
        {
            "World/",
            "--------------- DEMO STUFF",
            "Directional Light",
            "EventSystem",
            "SceneCamera",
        };

        // Path segments to exclude
        private static readonly string[] ExcludedPathContains = new[]
        {
            "/Fence",
            "/Shear",
            "/Cocoa Cart",
            "/Fire Setup",
            "/Lodge/",
            "/Terrain",
            "/Fishing",
            "snow fence",
            "wooden fence",
            "Graham Cracker",
            "Styrofoam",
            "fairy_lights",
        };

        public override void OnInitializeMelon()
        {
            Melon<MenuQOLMod>.Logger.Msg("MenuQOL loaded!");
            Melon<MenuQOLMod>.Logger.Msg("  F7 = dump UI elements");
            Melon<MenuQOLMod>.Logger.Msg("  F6 = toggle auto-confirm host dialog (currently ON)");
            if (PlayerPrefs.HasKey(PREF_HAS_SAVED))
                Melon<MenuQOLMod>.Logger.Msg("  Quick Host button available on main menu");
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            _hooked = false;
            _userClickedHost = false;
            _lobbySettingsHooked = false;
            _passwordInput = null;
            _passwordToggle = null;
            _confirmHostButton = null;
            _publicPrivateToggle = null;
            _inviteOnlyToggle = null;
            _playerCountSlider = null;
            _peacefulToggle = null;
            _disableVoiceToggle = null;
            _quickHostButtonInjected = false;
            _quickHostText = null;
            _quickHostPhase = QuickHostPhase.Idle;
            Melon<MenuQOLMod>.Logger.Msg($"Scene loaded: {sceneName}");
        }

        public override void OnUpdate()
        {
            if (Input.GetKeyDown(KeyCode.F7))
                DumpUIElements();

            if (Input.GetKeyDown(KeyCode.F6))
            {
                _autoConfirmEnabled = !_autoConfirmEnabled;
                Melon<MenuQOLMod>.Logger.Msg($"Auto-confirm host dialog: {(_autoConfirmEnabled ? "ON" : "OFF")}");
            }

            if (_autoConfirmEnabled)
            {
                if (!_hooked) TryHookHostButton();
                if (_userClickedHost) TryAutoConfirmHostDialog();
            }

            if (!_lobbySettingsHooked)
                TryHookLobbySettings();

            if (!_quickHostButtonInjected)
                TryInjectQuickHostButton();

            // Keep Quick Host text consistent (game scripts can reset cloned button text)
            if (_quickHostText != null && _quickHostText.text != "QUICK HOST")
            {
                _quickHostText.text = "QUICK HOST";
                _quickHostText.ForceMeshUpdate();
            }

            if (_quickHostPhase == QuickHostPhase.WaitingForSettings && _lobbySettingsHooked)
                ApplySettingsAndCreate();
        }

        private void TryHookHostButton()
        {
            try
            {
                var hostButton = GameObject.Find("(Button) HOST");
                if (hostButton == null) return;

                var button = hostButton.GetComponent<Button>();
                if (button == null) return;

                button.onClick.AddListener((UnityAction)OnHostButtonClicked);
                _hooked = true;
                Melon<MenuQOLMod>.Logger.Msg("Hooked HOST button for auto-confirm");
            }
            catch (System.Exception ex)
            {
                Melon<MenuQOLMod>.Logger.Warning($"Failed to hook HOST button: {ex.Message}");
            }
        }

        private void OnHostButtonClicked()
        {
            Melon<MenuQOLMod>.Logger.Msg("HOST button clicked - will auto-confirm popup");
            _userClickedHost = true;
        }

        private void TryHookLobbySettings()
        {
            try
            {
                var inputObj = GameObject.Find("(Input) lobby setting password");
                var toggleObj = GameObject.Find("(Toggle) uses password");
                var buttonObj = GameObject.Find("(Button) CONFIRM HOST");

                if (inputObj == null || toggleObj == null || buttonObj == null) return;

                _passwordInput = inputObj.GetComponent<TMP_InputField>();
                _passwordToggle = toggleObj.GetComponent<Toggle>();
                _confirmHostButton = buttonObj.GetComponent<Button>();

                if (_passwordInput == null || _passwordToggle == null || _confirmHostButton == null) return;

                // Additional lobby settings (optional)
                var publicObj = GameObject.Find("(Toggle) Game Type public/private");
                var inviteObj = GameObject.Find("(Toggle) invite only");
                var sliderObj = GameObject.Find("(Slider) player count slider");
                var peacefulObj = GameObject.Find("(Toggle) peaceful mode");
                var voiceObj = GameObject.Find("(Toggle) disable voice chat");

                _publicPrivateToggle = publicObj?.GetComponent<Toggle>();
                _inviteOnlyToggle = inviteObj?.GetComponent<Toggle>();
                _playerCountSlider = sliderObj?.GetComponent<Slider>();
                _peacefulToggle = peacefulObj?.GetComponent<Toggle>();
                _disableVoiceToggle = voiceObj?.GetComponent<Toggle>();

                // Hook events
                _passwordInput.onSelect.AddListener((UnityAction<string>)OnPasswordFieldSelected);
                _passwordInput.onSubmit.AddListener((UnityAction<string>)OnPasswordFieldSubmit);
                _passwordToggle.onValueChanged.AddListener((UnityAction<bool>)OnPasswordToggleChanged);
                _confirmHostButton.onClick.AddListener((UnityAction)OnCreateLobbyClicked);

                // Restore saved password and check the toggle
                string savedPassword = PlayerPrefs.GetString(PREF_PASSWORD, "");
                if (!string.IsNullOrEmpty(savedPassword))
                {
                    _passwordInput.text = savedPassword;
                    _passwordToggle.isOn = true;
                }

                _lobbySettingsHooked = true;
                Melon<MenuQOLMod>.Logger.Msg("Lobby settings hooked");
            }
            catch (System.Exception ex)
            {
                Melon<MenuQOLMod>.Logger.Warning($"Failed to hook lobby settings: {ex.Message}");
            }
        }

        private void OnPasswordFieldSelected(string _)
        {
            if (_passwordToggle != null && !_passwordToggle.isOn)
            {
                _passwordToggle.isOn = true;
                Melon<MenuQOLMod>.Logger.Msg("Auto-enabled password toggle");
            }
        }

        private void OnPasswordFieldSubmit(string text)
        {
            PlayerPrefs.SetString(PREF_PASSWORD, text);
            PlayerPrefs.Save();

            if (_confirmHostButton != null && _confirmHostButton.interactable)
            {
                _confirmHostButton.onClick.Invoke();
                Melon<MenuQOLMod>.Logger.Msg("Submitted lobby via Enter key");
            }
            else
            {
                Melon<MenuQOLMod>.Logger.Msg($"Cannot submit - button null: {_confirmHostButton == null}, interactable: {_confirmHostButton?.interactable}");
            }
        }

        private void OnPasswordToggleChanged(bool isOn)
        {
            if (isOn && _passwordInput != null)
            {
                _passwordInput.Select();
                _passwordInput.ActivateInputField();
                Melon<MenuQOLMod>.Logger.Msg("Auto-focused password field");
            }
        }

        private void OnCreateLobbyClicked()
        {
            SaveAllLobbySettings();
        }

        private void SaveAllLobbySettings()
        {
            try
            {
                if (_passwordToggle != null && _passwordInput != null)
                {
                    PlayerPrefs.SetInt(PREF_USE_PASSWORD, _passwordToggle.isOn ? 1 : 0);
                    if (_passwordToggle.isOn)
                        PlayerPrefs.SetString(PREF_PASSWORD, _passwordInput.text);
                }

                if (_publicPrivateToggle != null)
                    PlayerPrefs.SetInt(PREF_PUBLIC, _publicPrivateToggle.isOn ? 1 : 0);
                if (_inviteOnlyToggle != null)
                    PlayerPrefs.SetInt(PREF_INVITE_ONLY, _inviteOnlyToggle.isOn ? 1 : 0);
                if (_playerCountSlider != null)
                    PlayerPrefs.SetFloat(PREF_PLAYER_COUNT, _playerCountSlider.value);
                if (_peacefulToggle != null)
                    PlayerPrefs.SetInt(PREF_PEACEFUL, _peacefulToggle.isOn ? 1 : 0);
                if (_disableVoiceToggle != null)
                    PlayerPrefs.SetInt(PREF_VOICE_DISABLED, _disableVoiceToggle.isOn ? 1 : 0);

                PlayerPrefs.SetInt(PREF_HAS_SAVED, 1);
                PlayerPrefs.Save();
                Melon<MenuQOLMod>.Logger.Msg("Saved all lobby settings");
            }
            catch (System.Exception ex)
            {
                Melon<MenuQOLMod>.Logger.Warning($"Failed to save lobby settings: {ex.Message}");
            }
        }

        private void TryInjectQuickHostButton()
        {
            if (!PlayerPrefs.HasKey(PREF_HAS_SAVED)) return;

            try
            {
                var hostObj = GameObject.Find("(Button) HOST");
                if (hostObj == null || !hostObj.activeInHierarchy) return;

                // Clone the entire horizontal layout row to preserve layout, sizing and background
                var originalRow = hostObj.transform.parent;
                var rowClone = Object.Instantiate(originalRow.gameObject, originalRow.parent);
                if (rowClone == null) return;

                rowClone.name = "horizontal layout (quick host)";
                rowClone.transform.SetSiblingIndex(originalRow.GetSiblingIndex() + 1);

                // Keep only the first child (HOST clone), destroy the rest (Join, Join text-only)
                var buttonObj = rowClone.transform.GetChild(0).gameObject;
                for (int i = rowClone.transform.childCount - 1; i > 0; i--)
                    Object.DestroyImmediate(rowClone.transform.GetChild(i).gameObject);

                buttonObj.name = "(Button) Quick Host";

                // Change button text
                var textChild = buttonObj.transform.Find("Text (TMP)");
                if (textChild != null)
                {
                    _quickHostText = textChild.GetComponent<TMP_Text>();
                    if (_quickHostText != null)
                    {
                        _quickHostText.text = "QUICK HOST";
                        _quickHostText.ForceMeshUpdate();
                    }
                }

                // Tint background green to distinguish from HOST
                var image = buttonObj.GetComponent<Image>();
                if (image != null)
                    image.color = new Color(0.3f, 0.7f, 0.3f, 1f);

                var button = buttonObj.GetComponent<Button>();
                if (button != null)
                {
                    button.onClick.RemoveAllListeners();
                    button.onClick.AddListener((UnityAction)OnQuickHostClicked);
                }

                _quickHostButtonInjected = true;
                Melon<MenuQOLMod>.Logger.Msg("Quick Host button injected on main menu");
            }
            catch (System.Exception ex)
            {
                Melon<MenuQOLMod>.Logger.Warning($"Failed to inject Quick Host button: {ex.Message}");
            }
        }

        private void OnQuickHostClicked()
        {
            Melon<MenuQOLMod>.Logger.Msg("Quick Host initiated");

            // Reset lobby hooks so we re-detect when the settings screen opens
            _lobbySettingsHooked = false;
            _passwordInput = null;
            _passwordToggle = null;
            _confirmHostButton = null;
            _publicPrivateToggle = null;
            _inviteOnlyToggle = null;
            _playerCountSlider = null;
            _peacefulToggle = null;
            _disableVoiceToggle = null;

            _quickHostPhase = QuickHostPhase.WaitingForSettings;

            // Trigger the normal HOST flow (auto-confirm will handle the popup)
            var hostObj = GameObject.Find("(Button) HOST");
            var hostButton = hostObj?.GetComponent<Button>();
            if (hostButton != null)
            {
                hostButton.onClick.Invoke();
            }
            else
            {
                Melon<MenuQOLMod>.Logger.Warning("Quick Host: Could not find HOST button");
                _quickHostPhase = QuickHostPhase.Idle;
            }
        }

        private void ApplySettingsAndCreate()
        {
            try
            {
                if (_confirmHostButton == null || !_confirmHostButton.gameObject.activeInHierarchy)
                {
                    _quickHostPhase = QuickHostPhase.Idle;
                    return;
                }

                Melon<MenuQOLMod>.Logger.Msg("Quick Host: Applying saved settings...");

                // Lobby type
                if (_publicPrivateToggle != null && PlayerPrefs.HasKey(PREF_PUBLIC))
                    _publicPrivateToggle.isOn = PlayerPrefs.GetInt(PREF_PUBLIC, 1) == 1;
                if (_inviteOnlyToggle != null && PlayerPrefs.HasKey(PREF_INVITE_ONLY))
                    _inviteOnlyToggle.isOn = PlayerPrefs.GetInt(PREF_INVITE_ONLY, 0) == 1;

                // Player count
                if (_playerCountSlider != null && PlayerPrefs.HasKey(PREF_PLAYER_COUNT))
                    _playerCountSlider.value = PlayerPrefs.GetFloat(PREF_PLAYER_COUNT, 8f);

                // Other toggles
                if (_peacefulToggle != null && PlayerPrefs.HasKey(PREF_PEACEFUL))
                    _peacefulToggle.isOn = PlayerPrefs.GetInt(PREF_PEACEFUL, 0) == 1;
                if (_disableVoiceToggle != null && PlayerPrefs.HasKey(PREF_VOICE_DISABLED))
                    _disableVoiceToggle.isOn = PlayerPrefs.GetInt(PREF_VOICE_DISABLED, 0) == 1;

                // Password
                if (_passwordToggle != null && _passwordInput != null)
                {
                    bool usePassword = PlayerPrefs.GetInt(PREF_USE_PASSWORD, 0) == 1;
                    _passwordToggle.isOn = usePassword;
                    if (usePassword)
                        _passwordInput.text = PlayerPrefs.GetString(PREF_PASSWORD, "");
                }

                // Create the lobby
                if (_confirmHostButton.interactable)
                {
                    _confirmHostButton.onClick.Invoke();
                    Melon<MenuQOLMod>.Logger.Msg("Quick Host: Lobby created!");
                }

                _quickHostPhase = QuickHostPhase.Idle;
            }
            catch (System.Exception ex)
            {
                Melon<MenuQOLMod>.Logger.Warning($"Quick Host error: {ex.Message}");
                _quickHostPhase = QuickHostPhase.Idle;
            }
        }

        private void TryAutoConfirmHostDialog()
        {
            try
            {
                var popup = GameObject.Find("UI_Popup_ConfirmGoodInternet");
                if (popup == null || !popup.activeInHierarchy)
                {
                    return;
                }

                var confirmButton = FindChildByName(popup.transform, "(Button) Settings Button (Confirm)");
                if (confirmButton == null)
                {
                    return;
                }

                var button = confirmButton.GetComponent<Button>();
                if (button == null || !button.interactable)
                {
                    return;
                }

                Melon<MenuQOLMod>.Logger.Msg("Auto-confirming host dialog...");
                button.onClick.Invoke();
                _userClickedHost = false;
            }
            catch (System.Exception ex)
            {
                Melon<MenuQOLMod>.Logger.Warning($"Auto-confirm error: {ex.Message}");
            }
        }

        private Transform FindChildByName(Transform parent, string name)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (child.name == name) return child;
            }

            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                var found = FindChildByName(child, name);
                if (found != null) return found;
            }

            return null;
        }

        private bool ShouldExclude(string path)
        {
            foreach (var prefix in ExcludedPathPrefixes)
            {
                if (path.StartsWith(prefix)) return true;
            }
            foreach (var segment in ExcludedPathContains)
            {
                if (path.Contains(segment)) return true;
            }
            return false;
        }

        private void DumpUIElements()
        {
            Melon<MenuQOLMod>.Logger.Msg("=== UI DUMP START ===");

            var allObjects = Object.FindObjectsOfType<GameObject>();
            int count = 0;
            int excluded = 0;

            foreach (var obj in allObjects)
            {
                if (obj == null) continue;

                string path = GetHierarchyPath(obj);

                if (ShouldExclude(path))
                {
                    excluded++;
                    continue;
                }

                var components = obj.GetComponents<Component>();
                var componentNames = new List<string>();
                foreach (var comp in components)
                {
                    if (comp != null)
                    {
                        componentNames.Add(comp.GetType().Name);
                    }
                }
                string componentList = string.Join(", ", componentNames);

                Melon<MenuQOLMod>.Logger.Msg($"[{path}] Components: {componentList}");

                LogTextContent(obj, path);
                LogButtonInfo(obj, path);

                count++;
            }

            Melon<MenuQOLMod>.Logger.Msg($"=== UI DUMP END === (Shown: {count}, Excluded: {excluded})");
        }

        private string GetHierarchyPath(GameObject obj)
        {
            var path = new List<string>();
            var current = obj.transform;

            while (current != null)
            {
                path.Insert(0, current.name);
                current = current.parent;
            }

            return string.Join("/", path);
        }

        private void LogTextContent(GameObject obj, string path)
        {
            var tmpText = obj.GetComponent<TMP_Text>();
            if (tmpText != null)
            {
                string text = tmpText.text;
                if (!string.IsNullOrEmpty(text))
                {
                    if (text.Length > 100)
                    {
                        text = text.Substring(0, 100) + "...";
                    }
                    text = text.Replace("\n", "\\n").Replace("\r", "\\r");
                    Melon<MenuQOLMod>.Logger.Msg($"  [TEXT] {path}: \"{text}\"");
                }
            }

            var legacyText = obj.GetComponent<Text>();
            if (legacyText != null)
            {
                string text = legacyText.text;
                if (!string.IsNullOrEmpty(text))
                {
                    if (text.Length > 100)
                    {
                        text = text.Substring(0, 100) + "...";
                    }
                    text = text.Replace("\n", "\\n").Replace("\r", "\\r");
                    Melon<MenuQOLMod>.Logger.Msg($"  [LEGACY TEXT] {path}: \"{text}\"");
                }
            }
        }

        private void LogButtonInfo(GameObject obj, string path)
        {
            var button = obj.GetComponent<Button>();
            if (button != null)
            {
                bool interactable = button.interactable;
                Melon<MenuQOLMod>.Logger.Msg($"  [BUTTON] {path}: interactable={interactable}");

                var onClick = button.onClick;
                if (onClick != null)
                {
                    int listenerCount = onClick.GetPersistentEventCount();
                    Melon<MenuQOLMod>.Logger.Msg($"    onClick persistent listeners: {listenerCount}");

                    for (int i = 0; i < listenerCount; i++)
                    {
                        var target = onClick.GetPersistentTarget(i);
                        var methodName = onClick.GetPersistentMethodName(i);
                        string targetName = target != null ? target.name : "null";
                        Melon<MenuQOLMod>.Logger.Msg($"    [{i}] Target: {targetName}, Method: {methodName}");
                    }
                }
            }
        }
    }
}
