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
        // Auto-confirm feature - event-based approach
        private bool _autoConfirmEnabled = true;
        private bool _userClickedHost = false;  // Set by HOST button click, cleared after confirming
        private bool _hooked = false;  // Whether we've hooked the HOST button

        // Password QOL feature
        private bool _passwordQOLHooked = false;
        private TMP_InputField _passwordInput = null;
        private Toggle _passwordToggle = null;
        private Button _confirmHostButton = null;
        private const string PASSWORD_PREF_KEY = "MenuQOL_LastPassword";

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
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            // Reset state on scene load - need to re-hook button in new scene
            _hooked = false;
            _userClickedHost = false;
            _passwordQOLHooked = false;
            _passwordInput = null;
            _passwordToggle = null;
            _confirmHostButton = null;
            Melon<MenuQOLMod>.Logger.Msg($"Scene loaded: {sceneName}");
        }

        public override void OnUpdate()
        {
            if (Input.GetKeyDown(KeyCode.F7))
            {
                DumpUIElements();
            }

            if (Input.GetKeyDown(KeyCode.F6))
            {
                _autoConfirmEnabled = !_autoConfirmEnabled;
                Melon<MenuQOLMod>.Logger.Msg($"Auto-confirm host dialog: {(_autoConfirmEnabled ? "ON" : "OFF")}");
            }

            if (_autoConfirmEnabled)
            {
                // Try to hook HOST button if not already hooked
                if (!_hooked)
                {
                    TryHookHostButton();
                }

                // Only auto-confirm if user clicked HOST
                if (_userClickedHost)
                {
                    TryAutoConfirmHostDialog();
                }
            }

            // Try to hook password UI if not already hooked
            if (!_passwordQOLHooked)
            {
                TryHookPasswordUI();
            }
        }

        private void TryHookHostButton()
        {
            try
            {
                var hostButton = GameObject.Find("(Button) HOST");
                if (hostButton == null) return;

                var button = hostButton.GetComponent<Button>();
                if (button == null) return;

                // Add our listener using Il2Cpp compatible delegate
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

        private void TryHookPasswordUI()
        {
            try
            {
                // Find elements by name
                var inputObj = GameObject.Find("(Input) lobby setting password");
                var toggleObj = GameObject.Find("(Toggle) uses password");
                var buttonObj = GameObject.Find("(Button) CONFIRM HOST");

                if (inputObj == null || toggleObj == null || buttonObj == null) return;

                _passwordInput = inputObj.GetComponent<TMP_InputField>();
                _passwordToggle = toggleObj.GetComponent<Toggle>();
                _confirmHostButton = buttonObj.GetComponent<Button>();

                if (_passwordInput == null || _passwordToggle == null || _confirmHostButton == null) return;

                // Hook events
                _passwordInput.onSelect.AddListener((UnityAction<string>)OnPasswordFieldSelected);
                _passwordInput.onSubmit.AddListener((UnityAction<string>)OnPasswordFieldSubmit);
                _passwordToggle.onValueChanged.AddListener((UnityAction<bool>)OnPasswordToggleChanged);
                _confirmHostButton.onClick.AddListener((UnityAction)OnCreateLobbyClicked);

                // Restore saved password and check the toggle
                string savedPassword = PlayerPrefs.GetString(PASSWORD_PREF_KEY, "");
                if (!string.IsNullOrEmpty(savedPassword))
                {
                    _passwordInput.text = savedPassword;
                    _passwordToggle.isOn = true;
                }

                _passwordQOLHooked = true;
                Melon<MenuQOLMod>.Logger.Msg("Password QOL hooked");
            }
            catch (System.Exception ex)
            {
                Melon<MenuQOLMod>.Logger.Warning($"Failed to hook password UI: {ex.Message}");
            }
        }

        private void OnPasswordFieldSelected(string _)
        {
            // Auto-check the password toggle when field is selected
            if (_passwordToggle != null && !_passwordToggle.isOn)
            {
                _passwordToggle.isOn = true;
                Melon<MenuQOLMod>.Logger.Msg("Auto-enabled password toggle");
            }
        }

        private void OnPasswordFieldSubmit(string text)
        {
            // Save password before submitting
            PlayerPrefs.SetString(PASSWORD_PREF_KEY, text);
            PlayerPrefs.Save();

            // Click the CREATE button
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
            // Auto-focus password field when toggle is checked
            if (isOn && _passwordInput != null)
            {
                _passwordInput.Select();
                _passwordInput.ActivateInputField();
                Melon<MenuQOLMod>.Logger.Msg("Auto-focused password field");
            }
        }

        private void OnCreateLobbyClicked()
        {
            // Save password when CREATE button is clicked
            if (_passwordInput != null && _passwordToggle != null && _passwordToggle.isOn)
            {
                PlayerPrefs.SetString(PASSWORD_PREF_KEY, _passwordInput.text);
                PlayerPrefs.Save();
            }
        }

        private void TryAutoConfirmHostDialog()
        {
            try
            {
                // Find the popup by name
                var popup = GameObject.Find("UI_Popup_ConfirmGoodInternet");
                if (popup == null || !popup.activeInHierarchy)
                {
                    return;
                }

                // Find the confirm button
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

                // Click it!
                Melon<MenuQOLMod>.Logger.Msg("Auto-confirming host dialog...");
                button.onClick.Invoke();
                _userClickedHost = false;  // Reset flag after confirming
            }
            catch (System.Exception ex)
            {
                Melon<MenuQOLMod>.Logger.Warning($"Auto-confirm error: {ex.Message}");
            }
        }

        private Transform FindChildByName(Transform parent, string name)
        {
            // Check direct children first
            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (child.name == name) return child;
            }

            // Recursive search
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

                // Get hierarchy path first for filtering
                string path = GetHierarchyPath(obj);

                // Skip excluded paths
                if (ShouldExclude(path))
                {
                    excluded++;
                    continue;
                }

                // Get all components
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

                // Log basic info
                Melon<MenuQOLMod>.Logger.Msg($"[{path}] Components: {componentList}");

                // Extract text content from UI elements
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
            // Check for TextMeshPro components
            var tmpText = obj.GetComponent<TMP_Text>();
            if (tmpText != null)
            {
                string text = tmpText.text;
                if (!string.IsNullOrEmpty(text))
                {
                    // Truncate long text for readability
                    if (text.Length > 100)
                    {
                        text = text.Substring(0, 100) + "...";
                    }
                    text = text.Replace("\n", "\\n").Replace("\r", "\\r");
                    Melon<MenuQOLMod>.Logger.Msg($"  [TEXT] {path}: \"{text}\"");
                }
            }

            // Check for legacy Text component
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

                // Try to log onClick listener count
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
