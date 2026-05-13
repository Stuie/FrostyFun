using MelonLoader;
using UnityEngine;
using System;
using System.Linq;
using System.Reflection;
using FrostyFun.Shared.Il2Cpp;
using FrostyFun.Shared.Logging;
using FrostyFun.Shared.Players;
using FrostyFun.Shared.UI;
using Il2Cpp;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Object = UnityEngine.Object;

namespace RacePicker
{
    public enum RouteChoice
    {
        Random,
        DoATrick,
        FrozenFeet
    }

    public enum GreenRouteChoice
    {
        Random,
        FullCourse,
        SplitSlopes
    }

    public class RacePickerMod : MelonMod
    {
        private const string PrefKey = "RacePicker_RouteChoice";
        private const string GreenPrefKey = "RacePicker_GreenRouteChoice";

        private RouteChoice _selectedRoute = RouteChoice.Random;
        private GreenRouteChoice _selectedGreenRoute = GreenRouteChoice.Random;
        private bool _showUI = false;
        private string _currentScene = "";

        private PlayerInputBlocker _inputBlocker;
        private CursorSnapshot _cursorSnapshot;

        // Race flag references (cached after scene load)
        private PlaceableRaceInteractable _startFlag;
        private PlaceableRaceInteractable _finishLine0;
        private PlaceableRaceInteractable _finishLine1;
        private Il2CppReferenceArray<PlaceableRaceInteractable> _originalFinishLines;
        private bool _needsFlagSearch = false;
        private bool _flagsInitialized = false;

        // Green race flag references
        private PlaceableRaceInteractable _greenStartFlag;
        private PlaceableRaceInteractable _greenFinishLine0;
        private PlaceableRaceInteractable _greenFinishLine1;
        private Il2CppReferenceArray<PlaceableRaceInteractable> _originalGreenFinishLines;
        private bool _greenFlagsInitialized = false;

        // UI textures
        private bool _texturesInitialized = false;
        private Texture2D _bgTexture;
        private Texture2D _buttonTexture;
        private Texture2D _buttonHoverTexture;
        private Texture2D _buttonSelectedTexture;
        private Texture2D _cursorTexture;

        public override void OnInitializeMelon()
        {
            _inputBlocker = new PlayerInputBlocker(new MelonLoggerAdapter(Melon<RacePickerMod>.Logger));
            LoadPreference();
            LoadGreenPreference();
            Melon<RacePickerMod>.Logger.Msg("Race Picker loaded!");
            Melon<RacePickerMod>.Logger.Msg($"  Yellow race: {_selectedRoute}");
            Melon<RacePickerMod>.Logger.Msg($"  Green race: {_selectedGreenRoute}");
            Melon<RacePickerMod>.Logger.Msg("  F5 = Toggle race picker UI");
            Melon<RacePickerMod>.Logger.Msg("  Ctrl+F5 = Dump debug info");
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            _currentScene = sceneName;
            _showUI = false;
            _inputBlocker.Reset();

            // Reset race flag state - will re-search on next Update
            _startFlag = null;
            _finishLine0 = null;
            _finishLine1 = null;
            _originalFinishLines = null;
            _flagsInitialized = false;

            _greenStartFlag = null;
            _greenFinishLine0 = null;
            _greenFinishLine1 = null;
            _originalGreenFinishLines = null;
            _greenFlagsInitialized = false;

            _needsFlagSearch = true;

            Melon<RacePickerMod>.Logger.Msg($"Scene loaded: {sceneName}");
        }

        public override void OnUpdate()
        {
            // Deferred flag initialization
            if (_needsFlagSearch && (!_flagsInitialized || !_greenFlagsInitialized))
                TryInitializeRaceFlags();

            // F5 = toggle UI
            if (Input.GetKeyDown(KeyCode.F5))
            {
                if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
                {
                    // Ctrl+F5 = dump debug info
                    Melon<RacePickerMod>.Logger.Msg("=== RacePicker Debug Dump ===");
                    Melon<RacePickerMod>.Logger.Msg($"  Current scene: {_currentScene}");
                    Melon<RacePickerMod>.Logger.Msg($"  Selected route: {_selectedRoute}");
                    Melon<RacePickerMod>.Logger.Msg($"  UI visible: {_showUI}");
                    DumpRaceTypes();
                    DumpRaceGameObjects();
                }
                else
                {
                    if (_showUI)
                        CloseUI();
                    else
                        OpenUI();
                }
            }

        }

        public override void OnLateUpdate()
        {
            if (_showUI)
                CursorState.ShowFree();
        }

        public override void OnGUI()
        {
            if (!_showUI) return;

            // Consume Escape to prevent game pause menu
            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
            {
                CloseUI();
                Event.current.Use();
                return;
            }

            InitializeTextures();
            DrawRoutePickerUI();

            // Custom cursor
            if (_cursorTexture != null)
            {
                var mousePos = Event.current.mousePosition;
                GUI.DrawTexture(new Rect(mousePos.x, mousePos.y, 16, 16), _cursorTexture);
            }
        }

        private void InitializeTextures()
        {
            if (_texturesInitialized) return;

            _bgTexture = TextureFactory.MakeSolid(new Color(0.1f, 0.1f, 0.15f, 0.95f));
            _buttonTexture = TextureFactory.MakeSolid(new Color(0.25f, 0.25f, 0.35f, 1f));
            _buttonHoverTexture = TextureFactory.MakeSolid(new Color(0.35f, 0.35f, 0.5f, 1f));
            _buttonSelectedTexture = TextureFactory.MakeSolid(new Color(0.2f, 0.5f, 0.3f, 1f));
            _cursorTexture = CursorTextures.MakeArrowCursor();

            _texturesInitialized = true;
        }

        private void DrawRoutePickerUI()
        {
            float windowWidth = 340;
            float windowHeight = 460;
            float x = (Screen.width - windowWidth) / 2f;
            float y = (Screen.height - windowHeight) / 2f;
            float btnWidth = 300;
            float btnHeight = 38;
            float btnX = x + (windowWidth - btnWidth) / 2f;
            float btnSpacing = 8;

            // Background
            GUI.DrawTexture(new Rect(x, y, windowWidth, windowHeight), _bgTexture);

            // Title
            GUIStyle titleStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 18,
                fontStyle = FontStyle.Bold
            };
            titleStyle.normal.textColor = Color.white;
            GUI.Label(new Rect(x, y + 12, windowWidth, 30), "Race Picker", titleStyle);

            // Section header style
            GUIStyle sectionStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize = 14,
                fontStyle = FontStyle.Bold
            };
            sectionStyle.normal.textColor = new Color(0.7f, 0.7f, 0.9f);

            // === Yellow Race Section ===
            float curY = y + 50;
            GUI.Label(new Rect(btnX, curY, btnWidth, 22), "Yellow Race", sectionStyle);
            curY += 28;

            if (DrawSelectionButton(new Rect(btnX, curY, btnWidth, btnHeight), "Do A Trick", _selectedRoute == RouteChoice.DoATrick))
            { _selectedRoute = RouteChoice.DoATrick; SavePreference(); ApplyRouteChoice(_selectedRoute); }
            curY += btnHeight + btnSpacing;

            if (DrawSelectionButton(new Rect(btnX, curY, btnWidth, btnHeight), "Frozen Feet", _selectedRoute == RouteChoice.FrozenFeet))
            { _selectedRoute = RouteChoice.FrozenFeet; SavePreference(); ApplyRouteChoice(_selectedRoute); }
            curY += btnHeight + btnSpacing;

            if (DrawSelectionButton(new Rect(btnX, curY, btnWidth, btnHeight), "Random (Default)", _selectedRoute == RouteChoice.Random))
            { _selectedRoute = RouteChoice.Random; SavePreference(); ApplyRouteChoice(_selectedRoute); }
            curY += btnHeight + 16;

            // === Green Race Section ===
            GUI.Label(new Rect(btnX, curY, btnWidth, 22), "Green Race", sectionStyle);
            curY += 28;

            if (DrawSelectionButton(new Rect(btnX, curY, btnWidth, btnHeight), "Full Course", _selectedGreenRoute == GreenRouteChoice.FullCourse))
            { _selectedGreenRoute = GreenRouteChoice.FullCourse; SaveGreenPreference(); ApplyGreenRouteChoice(_selectedGreenRoute); }
            curY += btnHeight + btnSpacing;

            if (DrawSelectionButton(new Rect(btnX, curY, btnWidth, btnHeight), "Split Slopes", _selectedGreenRoute == GreenRouteChoice.SplitSlopes))
            { _selectedGreenRoute = GreenRouteChoice.SplitSlopes; SaveGreenPreference(); ApplyGreenRouteChoice(_selectedGreenRoute); }
            curY += btnHeight + btnSpacing;

            if (DrawSelectionButton(new Rect(btnX, curY, btnWidth, btnHeight), "Random (Default)", _selectedGreenRoute == GreenRouteChoice.Random))
            { _selectedGreenRoute = GreenRouteChoice.Random; SaveGreenPreference(); ApplyGreenRouteChoice(_selectedGreenRoute); }

            // Close button
            float closeBtnWidth = 180;
            float closeBtnHeight = 34;
            float closeBtnX = x + (windowWidth - closeBtnWidth) / 2f;
            float closeBtnY = y + windowHeight - closeBtnHeight - 12;
            Rect closeRect = new Rect(closeBtnX, closeBtnY, closeBtnWidth, closeBtnHeight);

            bool closeHover = closeRect.Contains(Event.current.mousePosition);
            GUI.DrawTexture(closeRect, closeHover ? _buttonHoverTexture : _buttonTexture);

            GUIStyle closeStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 14
            };
            closeStyle.normal.textColor = Color.white;
            GUI.Label(closeRect, "Close (F5)", closeStyle);

            if (closeHover && Event.current.type == EventType.MouseDown && Event.current.button == 0)
            {
                CloseUI();
                Event.current.Use();
            }
        }

        private bool DrawSelectionButton(Rect rect, string label, bool isSelected)
        {
            bool isHover = rect.Contains(Event.current.mousePosition);

            Texture2D btnTex = isSelected ? _buttonSelectedTexture : (isHover ? _buttonHoverTexture : _buttonTexture);
            GUI.DrawTexture(rect, btnTex);

            GUIStyle labelStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 15
            };
            labelStyle.normal.textColor = Color.white;

            string displayLabel = isSelected ? $"> {label} <" : label;
            GUI.Label(rect, displayLabel, labelStyle);

            if (isHover && Event.current.type == EventType.MouseDown && Event.current.button == 0)
            {
                Event.current.Use();
                return true;
            }
            return false;
        }

        private void LoadPreference()
        {
            try
            {
                int stored = PlayerPrefs.GetInt(PrefKey, (int)RouteChoice.Random);
                if (System.Enum.IsDefined(typeof(RouteChoice), stored))
                {
                    _selectedRoute = (RouteChoice)stored;
                }
            }
            catch (System.Exception ex)
            {
                Melon<RacePickerMod>.Logger.Warning($"Failed to load preference: {ex.Message}");
                _selectedRoute = RouteChoice.Random;
            }
        }

        private void LoadGreenPreference()
        {
            try
            {
                int stored = PlayerPrefs.GetInt(GreenPrefKey, (int)GreenRouteChoice.Random);
                if (Enum.IsDefined(typeof(GreenRouteChoice), stored))
                    _selectedGreenRoute = (GreenRouteChoice)stored;
            }
            catch (Exception ex)
            {
                Melon<RacePickerMod>.Logger.Warning($"Failed to load green preference: {ex.Message}");
                _selectedGreenRoute = GreenRouteChoice.Random;
            }
        }

        private void SaveGreenPreference()
        {
            try
            {
                PlayerPrefs.SetInt(GreenPrefKey, (int)_selectedGreenRoute);
                PlayerPrefs.Save();
            }
            catch (Exception ex)
            {
                Melon<RacePickerMod>.Logger.Warning($"Failed to save green preference: {ex.Message}");
            }
        }

        private void SavePreference()
        {
            try
            {
                PlayerPrefs.SetInt(PrefKey, (int)_selectedRoute);
                PlayerPrefs.Save();
                Melon<RacePickerMod>.Logger.Msg($"Saved route preference: {_selectedRoute}");
            }
            catch (System.Exception ex)
            {
                Melon<RacePickerMod>.Logger.Warning($"Failed to save preference: {ex.Message}");
            }
        }

        private void OpenUI()
        {
            _cursorSnapshot = CursorState.Snapshot();
            CursorState.ShowFree();
            _inputBlocker.Disable();
            _showUI = true;
            Melon<RacePickerMod>.Logger.Msg("UI opened");
        }

        private void CloseUI()
        {
            _inputBlocker.Restore();
            CursorState.Restore(_cursorSnapshot);
            _showUI = false;
            Melon<RacePickerMod>.Logger.Msg("UI closed");
        }

        private void TryInitializeRaceFlags()
        {
            // Yellow race
            if (!_flagsInitialized)
            {
                try
                {
                    var flagObj = GameObject.Find("World/Races/yellow race/RaceFlag");
                    if (flagObj != null)
                    {
                        var interactable = flagObj.GetComponent<PlaceableRaceInteractable>();
                        if (interactable != null)
                        {
                            var arr = interactable.assignedFinishLines;
                            if (arr != null && arr.Length >= 2)
                            {
                                _startFlag = interactable;
                                _originalFinishLines = arr;
                                _finishLine0 = arr[0];
                                _finishLine1 = arr[1];
                                _flagsInitialized = true;
                                Melon<RacePickerMod>.Logger.Msg($"Yellow [0]: {_finishLine0?.gameObject.name}  pos={_finishLine0?.transform.position}");
                                Melon<RacePickerMod>.Logger.Msg($"Yellow [1]: {_finishLine1?.gameObject.name}  pos={_finishLine1?.transform.position}");
                                Melon<RacePickerMod>.Logger.Msg("Yellow race flags initialized!");
                                ApplyRouteChoice(_selectedRoute);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Melon<RacePickerMod>.Logger.Error($"Error initializing yellow race flags: {ex.Message}");
                }
            }

            // Green race
            if (!_greenFlagsInitialized)
            {
                try
                {
                    var flagObj = GameObject.Find("World/Races/green race/RaceFlag");
                    if (flagObj != null)
                    {
                        var interactable = flagObj.GetComponent<PlaceableRaceInteractable>();
                        if (interactable != null)
                        {
                            var arr = interactable.assignedFinishLines;
                            if (arr != null && arr.Length >= 2)
                            {
                                _greenStartFlag = interactable;
                                _originalGreenFinishLines = arr;
                                _greenFinishLine0 = arr[0];
                                _greenFinishLine1 = arr[1];
                                _greenFlagsInitialized = true;
                                Melon<RacePickerMod>.Logger.Msg($"Green [0]: {_greenFinishLine0?.gameObject.name}  pos={_greenFinishLine0?.transform.position}");
                                Melon<RacePickerMod>.Logger.Msg($"Green [1]: {_greenFinishLine1?.gameObject.name}  pos={_greenFinishLine1?.transform.position}");
                                Melon<RacePickerMod>.Logger.Msg("Green race flags initialized!");
                                ApplyGreenRouteChoice(_selectedGreenRoute);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Melon<RacePickerMod>.Logger.Error($"Error initializing green race flags: {ex.Message}");
                }
            }

            if (_flagsInitialized && _greenFlagsInitialized)
                _needsFlagSearch = false;
        }

        private void ApplyRouteChoice(RouteChoice choice)
        {
            if (!_flagsInitialized || _startFlag == null) return;

            try
            {
                switch (choice)
                {
                    case RouteChoice.Random:
                        _startFlag.assignedFinishLines = _originalFinishLines;
                        _startFlag.finishLines = _originalFinishLines;
                        Melon<RacePickerMod>.Logger.Msg("Route set to RANDOM (both finish lines active)");
                        break;

                    case RouteChoice.DoATrick:
                        var doATrickArray = new Il2CppReferenceArray<PlaceableRaceInteractable>(1);
                        doATrickArray[0] = _finishLine1;
                        _startFlag.assignedFinishLines = doATrickArray;
                        _startFlag.finishLines = doATrickArray;
                        Melon<RacePickerMod>.Logger.Msg("Route set to DO A TRICK (finish line [1] only)");
                        break;

                    case RouteChoice.FrozenFeet:
                        var frozenFeetArray = new Il2CppReferenceArray<PlaceableRaceInteractable>(1);
                        frozenFeetArray[0] = _finishLine0;
                        _startFlag.assignedFinishLines = frozenFeetArray;
                        _startFlag.finishLines = frozenFeetArray;
                        Melon<RacePickerMod>.Logger.Msg("Route set to FROZEN FEET (finish line [0] only)");
                        break;
                }
            }
            catch (Exception ex)
            {
                Melon<RacePickerMod>.Logger.Error($"Failed to apply route choice: {ex.Message}");
            }
        }

        private void ApplyGreenRouteChoice(GreenRouteChoice choice)
        {
            if (!_greenFlagsInitialized || _greenStartFlag == null) return;

            try
            {
                switch (choice)
                {
                    case GreenRouteChoice.Random:
                        _greenStartFlag.assignedFinishLines = _originalGreenFinishLines;
                        _greenStartFlag.finishLines = _originalGreenFinishLines;
                        Melon<RacePickerMod>.Logger.Msg("Green route set to RANDOM");
                        break;

                    case GreenRouteChoice.FullCourse:
                        var fullArray = new Il2CppReferenceArray<PlaceableRaceInteractable>(1);
                        fullArray[0] = _greenFinishLine0;
                        _greenStartFlag.assignedFinishLines = fullArray;
                        _greenStartFlag.finishLines = fullArray;
                        Melon<RacePickerMod>.Logger.Msg("Green route set to FULL COURSE ([0])");
                        break;

                    case GreenRouteChoice.SplitSlopes:
                        var splitArray = new Il2CppReferenceArray<PlaceableRaceInteractable>(1);
                        splitArray[0] = _greenFinishLine1;
                        _greenStartFlag.assignedFinishLines = splitArray;
                        _greenStartFlag.finishLines = splitArray;
                        Melon<RacePickerMod>.Logger.Msg("Green route set to SPLIT SLOPES ([1])");
                        break;
                }
            }
            catch (Exception ex)
            {
                Melon<RacePickerMod>.Logger.Error($"Failed to apply green route choice: {ex.Message}");
            }
        }

        public override void OnApplicationQuit()
        {
            try
            {
                if (_flagsInitialized && _startFlag != null && _originalFinishLines != null)
                {
                    _startFlag.assignedFinishLines = _originalFinishLines;
                    _startFlag.finishLines = _originalFinishLines;
                }
                if (_greenFlagsInitialized && _greenStartFlag != null && _originalGreenFinishLines != null)
                {
                    _greenStartFlag.assignedFinishLines = _originalGreenFinishLines;
                    _greenStartFlag.finishLines = _originalGreenFinishLines;
                }
            }
            catch { }
        }

        private void DumpRaceTypes()
        {
            Melon<RacePickerMod>.Logger.Msg("--- Assembly-CSharp type scan ---");
            try
            {
                var assembly = Assembly.Load("Assembly-CSharp");
                var keywords = new[] {
                    "race", "track", "checkpoint", "flag", "start", "finish",
                    "endpoint", "trick", "route", "course", "goal", "waypoint",
                    "objective", "frozen", "feet"
                };

                foreach (var type in assembly.GetTypes())
                {
                    string typeLower = type.Name.ToLower();
                    if (!keywords.Any(k => typeLower.Contains(k))) continue;

                    Melon<RacePickerMod>.Logger.Msg($"  TYPE: {type.Namespace}.{type.Name}");

                    foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                    {
                        if (method.Name.StartsWith("get_") || method.Name.StartsWith("set_")) continue;
                        var parms = string.Join(", ", method.GetParameters().Select(p => p.ParameterType.Name));
                        Melon<RacePickerMod>.Logger.Msg($"    .{method.Name}({parms})");
                    }

                    foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                    {
                        Melon<RacePickerMod>.Logger.Msg($"    [{prop.PropertyType.Name}] {prop.Name}");
                    }

                    foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                    {
                        Melon<RacePickerMod>.Logger.Msg($"    <{field.FieldType.Name}> {field.Name}");
                    }
                }
            }
            catch (Exception ex)
            {
                Melon<RacePickerMod>.Logger.Error($"Type scan failed: {ex.Message}");
            }
        }

        private void DumpRaceGameObjects()
        {
            Melon<RacePickerMod>.Logger.Msg("--- Scene GameObject scan ---");
            try
            {
                var keywords = new[] {
                    "race", "trick", "flag", "checkpoint", "start", "finish",
                    "route", "frozen", "feet", "goal", "endpoint"
                };

                var allObjects = Object.FindObjectsOfType<GameObject>();
                foreach (var obj in allObjects)
                {
                    if (obj == null) continue;
                    string nameLower = obj.name.ToLower();
                    if (!keywords.Any(k => nameLower.Contains(k))) continue;

                    string parentPath = GetHierarchyPath(obj.transform);
                    Melon<RacePickerMod>.Logger.Msg($"  OBJ: {parentPath}  pos={obj.transform.position}");

                    var components = obj.GetComponents<Component>();
                    foreach (var comp in components)
                    {
                        if (comp == null) continue;
                        Melon<RacePickerMod>.Logger.Msg($"    comp: {comp.GetIl2CppTypeName()}");
                    }
                }
            }
            catch (Exception ex)
            {
                Melon<RacePickerMod>.Logger.Error($"GameObject scan failed: {ex.Message}");
            }
            // Targeted finish line state dump
            Melon<RacePickerMod>.Logger.Msg("--- Finish Line Array State ---");
            if (_flagsInitialized && _startFlag != null)
            {
                Melon<RacePickerMod>.Logger.Msg($"  Start flag: {_startFlag.gameObject.name}  pos={_startFlag.transform.position}");
                Melon<RacePickerMod>.Logger.Msg($"  selectedFinishLineIndex: {_startFlag.selectedFinishLineIndex}");

                var currentAssigned = _startFlag.assignedFinishLines;
                Melon<RacePickerMod>.Logger.Msg($"  assignedFinishLines.Length: {currentAssigned?.Length ?? -1}");
                if (currentAssigned != null)
                    for (int i = 0; i < currentAssigned.Length; i++)
                        Melon<RacePickerMod>.Logger.Msg(
                            $"    [{i}] {currentAssigned[i]?.gameObject.name}  pos={currentAssigned[i]?.transform.position}");

                var currentFinish = _startFlag.finishLines;
                Melon<RacePickerMod>.Logger.Msg($"  finishLines.Length: {currentFinish?.Length ?? -1}");
                if (currentFinish != null)
                    for (int i = 0; i < currentFinish.Length; i++)
                        Melon<RacePickerMod>.Logger.Msg(
                            $"    [{i}] {currentFinish[i]?.gameObject.name}  pos={currentFinish[i]?.transform.position}");

                Melon<RacePickerMod>.Logger.Msg($"  Cached [0]: {_finishLine0?.gameObject.name}  pos={_finishLine0?.transform.position}");
                Melon<RacePickerMod>.Logger.Msg($"  Cached [1]: {_finishLine1?.gameObject.name}  pos={_finishLine1?.transform.position}");
            }
            else
            {
                Melon<RacePickerMod>.Logger.Msg("  Flags not initialized yet");
            }
            Melon<RacePickerMod>.Logger.Msg("=== END RacePicker Debug Dump ===");
        }

        private static string GetHierarchyPath(Transform t)
        {
            string path = t.name;
            while (t.parent != null)
            {
                t = t.parent;
                path = t.name + "/" + path;
            }
            return path;
        }

    }
}
