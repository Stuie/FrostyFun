using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using CharacterSelect.Data;
using CharacterSelect.Services;

namespace CharacterSelect.UI
{
    public class CharacterSelectUI : ICharacterSelectUI
    {
        private readonly TextureLoader _textureLoader;
        private readonly ISkinService _skinService;

        private bool _showUI;
        private bool _stylesInitialized;
        private int _expandedGroupIndex = -1;
        private bool _showToolsPanel;
        private int _hoverCharacterId = -1;

        private Texture2D _bgTexture;
        private Texture2D _buttonTexture;
        private Texture2D _buttonHoverTexture;
        private Texture2D _buttonSelectedTexture;
        private Texture2D _cursorTexture;

        public bool IsVisible => _showUI;

        public CharacterSelectUI(TextureLoader textureLoader, ISkinService skinService)
        {
            _textureLoader = textureLoader;
            _skinService = skinService;
        }

        public void Open()
        {
            _showUI = true;
            _expandedGroupIndex = -1;
            _showToolsPanel = false;
        }

        public void Close()
        {
            _showUI = false;
        }

        public void Draw(int currentCharacterId, string activeSkinPath, Action<int, string> onSelect)
        {
            if (!_showUI) return;

            InitTextures();
            _textureLoader.LoadCharacterTextures();
            DrawCharacterSelectWindow(currentCharacterId, activeSkinPath, onSelect);
        }

        private void InitTextures()
        {
            if (_stylesInitialized) return;

            _bgTexture = TextureFactory.MakeTexture(2, 2, new Color(0.1f, 0.1f, 0.15f, 0.95f));
            _buttonTexture = TextureFactory.MakeTexture(2, 2, new Color(0.25f, 0.25f, 0.35f, 1f));
            _buttonHoverTexture = TextureFactory.MakeTexture(2, 2, new Color(0.35f, 0.35f, 0.5f, 1f));
            _buttonSelectedTexture = TextureFactory.MakeTexture(2, 2, new Color(0.2f, 0.5f, 0.3f, 1f));
            _cursorTexture = TextureFactory.MakeCursorTexture();

            _stylesInitialized = true;
        }

        private void DrawCharacterSelectWindow(int currentCharacterId, string activeSkinPath, Action<int, string> onSelect)
        {
            int columns = 4;
            int groupCount = CharacterData.ModelGroups.Length;
            int rows = (groupCount + columns - 1) / columns;

            float buttonWidth = 130;
            float buttonHeight = 115;
            float spacing = 8;
            float windowPadding = 20;

            float windowWidth = columns * buttonWidth + (columns - 1) * spacing + windowPadding * 2;

            float panelHeight = 0;
            int panelItemCount = 0;
            List<SkinEntry> activeSkins = null;
            int[] expandedVariants = null;
            string expandedModelKey = null;

            if (_showToolsPanel)
            {
                panelHeight = 150;
            }
            else if (_expandedGroupIndex >= 0 && _expandedGroupIndex < CharacterData.ModelGroups.Length)
            {
                expandedVariants = CharacterData.ModelGroups[_expandedGroupIndex].GameIds;
                expandedModelKey = CharacterData.ModelGroups[_expandedGroupIndex].ModelKey;
                _skinService.AvailableSkins.TryGetValue(expandedModelKey, out activeSkins);

                panelItemCount = expandedVariants.Length + (activeSkins?.Count ?? 0);
                int panelRows = (panelItemCount + columns - 1) / columns;
                panelHeight = 30 + panelRows * (buttonHeight + spacing);
            }

            float windowHeight = rows * buttonHeight + (rows - 1) * spacing + 120 + panelHeight;

            float x = (Screen.width - windowWidth) / 2;
            float y = (Screen.height - windowHeight) / 2;

            GUI.DrawTexture(new Rect(x, y, windowWidth, windowHeight), _bgTexture);

            // Title
            GUIStyle titleStyle = new GUIStyle(GUI.skin.label);
            titleStyle.alignment = TextAnchor.MiddleCenter;
            titleStyle.fontSize = 18;
            titleStyle.fontStyle = FontStyle.Bold;
            GUI.Label(new Rect(x, y + 10, windowWidth, 30), "Select Character", titleStyle);

            // Wrench/settings button
            float gearSize = 30;
            Rect gearRect = new Rect(x + windowWidth - gearSize - 8, y + 8, gearSize, gearSize);
            bool gearHover = gearRect.Contains(Event.current.mousePosition);
            GUI.DrawTexture(gearRect, _showToolsPanel ? _buttonSelectedTexture : gearHover ? _buttonHoverTexture : _buttonTexture);
            if (_textureLoader.WrenchTexture != null)
            {
                float iconPad = 5;
                Rect iconRect = new Rect(gearRect.x + iconPad, gearRect.y + iconPad, gearSize - iconPad * 2, gearSize - iconPad * 2);
                GUI.DrawTexture(iconRect, _textureLoader.WrenchTexture, ScaleMode.ScaleToFit);
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

            // Model group buttons
            for (int gi = 0; gi < CharacterData.ModelGroups.Length; gi++)
            {
                int row = gi / columns;
                int col = gi % columns;

                float btnX = startX + col * (buttonWidth + spacing);
                float btnY = startY + row * (buttonHeight + spacing);
                Rect buttonRect = new Rect(btnX, btnY, buttonWidth, buttonHeight);

                var group = CharacterData.ModelGroups[gi];
                bool isExpanded = (gi == _expandedGroupIndex);
                bool isCurrentGroup = group.GameIds.Contains(currentCharacterId);
                bool hasCustomSkins = _skinService.AvailableSkins.ContainsKey(group.ModelKey) && _skinService.AvailableSkins[group.ModelKey].Count > 0;
                bool isExpandable = group.GameIds.Length > 1 || hasCustomSkins;
                bool isHover = buttonRect.Contains(Event.current.mousePosition);

                Texture2D btnTex = isExpanded ? _buttonSelectedTexture
                    : (isCurrentGroup && _expandedGroupIndex < 0) ? _buttonSelectedTexture
                    : isHover ? _buttonHoverTexture
                    : _buttonTexture;
                GUI.DrawTexture(buttonRect, btnTex);

                int firstId = group.GameIds[0];
                Texture2D icon = _textureLoader.GetCharacterIcon(firstId);
                if (icon != null)
                {
                    Rect imgRect = new Rect(btnX + imgPadding, btnY + 8, imgSize, imgSize);
                    GUI.DrawTexture(imgRect, icon, ScaleMode.ScaleToFit);
                }

                if (isExpandable)
                    GUI.Label(new Rect(btnX + buttonWidth - 22, btnY + 2, 20, 20), "\u2605", badgeStyle);

                Rect labelRect = new Rect(btnX, btnY + buttonHeight - labelHeight - 5, buttonWidth, labelHeight);
                GUI.Label(labelRect, group.GroupName, labelStyle);

                if (isHover && Event.current.type == EventType.MouseDown && Event.current.button == 0)
                {
                    _showToolsPanel = false;
                    if (!isExpandable)
                    {
                        onSelect(group.GameIds[0], null);
                    }
                    else
                    {
                        _expandedGroupIndex = isExpanded ? -1 : gi;
                    }
                    Event.current.Use();
                }

                if (isHover) _hoverCharacterId = firstId;
            }

            // Tools panel
            if (_showToolsPanel)
            {
                DrawToolsPanel(x, startY + rows * (buttonHeight + spacing) + 5, windowWidth, windowPadding, spacing, currentCharacterId);
            }

            // Expanded panel: variants + skins
            if (!_showToolsPanel && _expandedGroupIndex >= 0 && expandedVariants != null)
            {
                DrawExpandedPanel(x, startY + rows * (buttonHeight + spacing) + 5, windowWidth, windowPadding,
                    startX, columns, buttonWidth, buttonHeight, spacing, imgSize, imgPadding, labelHeight,
                    labelStyle, currentCharacterId, activeSkinPath, expandedVariants, activeSkins, onSelect);
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
                Close();
                Event.current.Use();
            }

            // Custom cursor
            if (_cursorTexture != null)
            {
                Vector2 mousePos = Event.current.mousePosition;
                GUI.DrawTexture(new Rect(mousePos.x, mousePos.y, 16, 16), _cursorTexture);
            }
        }

        private void DrawToolsPanel(float x, float panelY, float windowWidth, float windowPadding, float spacing, int currentCharacterId)
        {
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
                _skinService.DumpCurrentSkinTexture(currentCharacterId);
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

        private void DrawExpandedPanel(float x, float panelY, float windowWidth, float windowPadding,
            float startX, int columns, float buttonWidth, float buttonHeight, float spacing,
            float imgSize, float imgPadding, float labelHeight,
            GUIStyle labelStyle, int currentCharacterId, string activeSkinPath,
            int[] expandedVariants, List<SkinEntry> activeSkins, Action<int, string> onSelect)
        {
            var group = CharacterData.ModelGroups[_expandedGroupIndex];

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
                bool selected = (gameId == currentCharacterId && activeSkinPath == null);

                GUI.DrawTexture(btnRect, selected ? _buttonSelectedTexture : hover ? _buttonHoverTexture : _buttonTexture);

                Texture2D varIcon = _textureLoader.GetCharacterIcon(gameId);
                if (varIcon != null)
                {
                    Rect imgRect = new Rect(btnX + imgPadding, btnY + 8, imgSize, imgSize);
                    GUI.DrawTexture(imgRect, varIcon, ScaleMode.ScaleToFit);
                }

                Rect lblRect = new Rect(btnX, btnY + buttonHeight - labelHeight - 5, buttonWidth, labelHeight);
                GUI.Label(lblRect, CharacterData.GetCharacterName(gameId), labelStyle);

                if (hover && Event.current.type == EventType.MouseDown && Event.current.button == 0)
                {
                    onSelect(gameId, null);
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
                    bool selected = (activeSkinPath == entry.FilePath);

                    GUI.DrawTexture(btnRect, selected ? _buttonSelectedTexture : hover ? _buttonHoverTexture : _buttonTexture);

                    Texture2D skinIcon = null;
                    if (entry.IconPath != null)
                        skinIcon = ((SkinService)_skinService).GetSkinIcon(entry.IconPath);

                    if (skinIcon != null)
                    {
                        Rect imgRect = new Rect(btnX + imgPadding, btnY + 8, imgSize, imgSize);
                        GUI.DrawTexture(imgRect, skinIcon, ScaleMode.ScaleToFit);
                    }

                    Rect lblRect = new Rect(btnX, btnY + buttonHeight - labelHeight - 5, buttonWidth, labelHeight);
                    GUI.Label(lblRect, entry.DisplayName, labelStyle);

                    if (hover && Event.current.type == EventType.MouseDown && Event.current.button == 0)
                    {
                        int targetId = group.GameIds.Contains(currentCharacterId) ? currentCharacterId : group.GameIds[0];
                        onSelect(targetId, entry.FilePath);
                        Event.current.Use();
                    }

                    itemIndex++;
                }
            }
        }
    }
}
