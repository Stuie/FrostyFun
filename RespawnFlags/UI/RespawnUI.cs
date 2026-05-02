using System;
using System.Collections.Generic;
using UnityEngine;
using RespawnFlags.Services;

namespace RespawnFlags.UI
{
    public class RespawnUI
    {
        private bool _showUI;
        private bool _texturesInitialized;
        private Texture2D _bgTexture;
        private Texture2D _buttonTexture;
        private Texture2D _buttonHoverTexture;
        private Texture2D _buttonDangerTexture;
        private Texture2D _cursorTexture;
        private Vector2 _scrollPosition;

        // Rename state
        private int _renamingIndex = -1;
        private string _renameText = "";

        // Eviction confirmation state
        private bool _showEvictionConfirm;
        private string _evictionMarkerName;
        private Action _onEvictionConfirm;
        private Action _onEvictionCancel;

        public bool IsVisible => _showUI;

        public void Open()
        {
            _showUI = true;
            _renamingIndex = -1;
        }

        public void Close()
        {
            _showUI = false;
            _renamingIndex = -1;
        }

        public void ShowEvictionConfirmation(string markerName, Action onConfirm, Action onCancel)
        {
            _showEvictionConfirm = true;
            _evictionMarkerName = markerName;
            _onEvictionConfirm = onConfirm;
            _onEvictionCancel = onCancel;
        }

        public void Draw(List<SpawnPoint> spawnPoints, SpawnPoint? lastUsed,
            Action<SpawnPoint> onSelect, Action onClose,
            Action<int> onDeleteMarker, Action<int, string> onRenameMarker,
            int markerCount)
        {
            if (!_showUI) return;

            if (_showEvictionConfirm)
            {
                DrawEvictionDialog();
                return;
            }

            InitTextures();

            float colWidth = 340;
            float padding = 16;
            float windowWidth = colWidth * 2 + padding * 3;
            float windowHeight = 560;
            float x = (Screen.width - windowWidth) / 2f;
            float y = (Screen.height - windowHeight) / 2f;
            float btnHeight = 34;
            float btnSpacing = 6;
            float deleteWidth = 30;

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
            GUI.Label(new Rect(x, y + 12, windowWidth, 30), "Respawn Flags", titleStyle);

            // Last used indicator
            if (lastUsed != null)
            {
                GUIStyle hintStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 13
                };
                hintStyle.normal.textColor = new Color(0.8f, 0.8f, 0.95f);
                GUI.Label(new Rect(x, y + 40, windowWidth, 18),
                    $"CapsLock x2 = {lastUsed.Value.Name}", hintStyle);
            }

            // Section header style
            GUIStyle sectionStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize = 14,
                fontStyle = FontStyle.Bold
            };
            sectionStyle.normal.textColor = new Color(0.7f, 0.7f, 0.9f);

            float contentTop = y + 60;
            float contentBottom = y + windowHeight - 56;
            float contentHeight = contentBottom - contentTop;

            // Split points by type
            var raceStarts = spawnPoints.FindAll(p => p.Type == SpawnPointType.RaceStart);
            var lodgePoints = spawnPoints.FindAll(p => p.Type == SpawnPointType.Lodge);
            var userMarkers = spawnPoints.FindAll(p => p.Type == SpawnPointType.UserMarker);

            // === LEFT COLUMN: Race starts + Lodge ===
            float leftX = x + padding;
            float curY = contentTop;
            float btnWidth = colWidth - 8;

            if (raceStarts.Count > 0)
            {
                GUI.Label(new Rect(leftX, curY, btnWidth, 22), "Race Starts", sectionStyle);
                curY += 28;
                foreach (var point in raceStarts)
                {
                    if (DrawButton(new Rect(leftX, curY, btnWidth, btnHeight), point.Name))
                        onSelect(point);
                    curY += btnHeight + btnSpacing;
                }
            }

            if (lodgePoints.Count > 0)
            {
                curY += 8;
                GUI.Label(new Rect(leftX, curY, btnWidth, 22), "Locations", sectionStyle);
                curY += 28;
                foreach (var point in lodgePoints)
                {
                    if (DrawButton(new Rect(leftX, curY, btnWidth, btnHeight), point.Name))
                        onSelect(point);
                    curY += btnHeight + btnSpacing;
                }
            }

            // === RIGHT COLUMN: User markers (unified list, scrollable) ===
            float rightX = x + padding + colWidth + padding;
            float scrollBtnWidth = colWidth - 24 - deleteWidth - 4;
            float fullRowWidth = colWidth - 24;

            float rightContentHeight = 24 + (userMarkers.Count > 0 ? 20 : 0)
                + userMarkers.Count * (btnHeight + btnSpacing) + 16;

            Rect scrollViewRect = new Rect(rightX, contentTop, colWidth - 8, contentHeight);
            Rect scrollContentRect = new Rect(0, 0, colWidth - 24, rightContentHeight);
            _scrollPosition = GUI.BeginScrollView(scrollViewRect, _scrollPosition, scrollContentRect);

            float scrollY = 0;

            string headerText = userMarkers.Count > 0
                ? $"Markers ({userMarkers.Count}/{MarkerStore.MaxMarkers})"
                : "Markers";
            GUI.Label(new Rect(0, scrollY, fullRowWidth, 22), headerText, sectionStyle);
            scrollY += 24;

            if (userMarkers.Count > 0)
            {
                GUIStyle renameHint = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleLeft,
                    fontSize = 12
                };
                renameHint.normal.textColor = new Color(0.7f, 0.7f, 0.8f);
                GUI.Label(new Rect(0, scrollY, fullRowWidth, 16), "Right-click to rename", renameHint);
                scrollY += 20;
            }

            int deleteIndex = -1;

            for (int i = 0; i < userMarkers.Count; i++)
            {
                var point = userMarkers[i];
                bool isFirst = (i == 0); // First marker = current, not deletable

                if (_renamingIndex == i)
                {
                    // Rename mode
                    float okWidth = 36;
                    float fieldWidth = fullRowWidth - okWidth - 4;

                    GUI.SetNextControlName($"rename_{i}");
                    _renameText = GUI.TextField(new Rect(0, scrollY, fieldWidth, btnHeight), _renameText);

                    if (DrawButton(new Rect(fieldWidth + 4, scrollY, okWidth, btnHeight), "OK"))
                    {
                        if (!string.IsNullOrWhiteSpace(_renameText))
                            onRenameMarker(i, _renameText);
                        _renamingIndex = -1;
                    }

                    if (Event.current.type == EventType.KeyUp &&
                        (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter))
                    {
                        if (!string.IsNullOrWhiteSpace(_renameText))
                            onRenameMarker(i, _renameText);
                        _renamingIndex = -1;
                        Event.current.Use();
                    }
                }
                else
                {
                    // Normal mode
                    if (isFirst)
                    {
                        // First entry: full width, no delete button
                        if (DrawButton(new Rect(0, scrollY, fullRowWidth, btnHeight), point.Name))
                            onSelect(point);
                    }
                    else
                    {
                        // Other entries: teleport + delete
                        if (DrawButton(new Rect(0, scrollY, scrollBtnWidth, btnHeight), point.Name))
                            onSelect(point);

                        if (DrawDangerButton(new Rect(scrollBtnWidth + 4, scrollY, deleteWidth, btnHeight), "X"))
                            deleteIndex = i;
                    }

                    // Right-click to rename (all markers)
                    float rowWidth = isFirst ? fullRowWidth : scrollBtnWidth;
                    Rect rowRect = new Rect(0, scrollY, rowWidth, btnHeight);
                    if (rowRect.Contains(Event.current.mousePosition) &&
                        Event.current.type == EventType.MouseDown && Event.current.button == 1)
                    {
                        _renamingIndex = i;
                        _renameText = point.Name;
                        Event.current.Use();
                    }
                }

                scrollY += btnHeight + btnSpacing;
            }

            if (deleteIndex >= 0)
            {
                onDeleteMarker(deleteIndex);
                if (_renamingIndex == deleteIndex) _renamingIndex = -1;
                else if (_renamingIndex > deleteIndex) _renamingIndex--;
            }

            GUI.EndScrollView();

            // Close button
            float closeBtnWidth = 180;
            float closeBtnHeight = 34;
            float closeBtnX = x + (windowWidth - closeBtnWidth) / 2f;
            float closeBtnY = y + windowHeight - closeBtnHeight - 14;
            Rect closeRect = new Rect(closeBtnX, closeBtnY, closeBtnWidth, closeBtnHeight);

            bool closeHover = closeRect.Contains(Event.current.mousePosition);
            GUI.DrawTexture(closeRect, closeHover ? _buttonHoverTexture : _buttonTexture);

            GUIStyle closeStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 14
            };
            closeStyle.normal.textColor = Color.white;
            GUI.Label(closeRect, "Close (F8)", closeStyle);

            if (closeHover && Event.current.type == EventType.MouseDown && Event.current.button == 0)
            {
                onClose();
                Event.current.Use();
            }

            // Custom cursor
            if (_cursorTexture != null)
            {
                var mousePos = Event.current.mousePosition;
                GUI.DrawTexture(new Rect(mousePos.x, mousePos.y, 16, 16), _cursorTexture);
            }
        }

        private void DrawEvictionDialog()
        {
            InitTextures();

            float dlgWidth = 420;
            float dlgHeight = 160;
            float dlgX = (Screen.width - dlgWidth) / 2f;
            float dlgY = (Screen.height - dlgHeight) / 2f;

            GUI.DrawTexture(new Rect(dlgX, dlgY, dlgWidth, dlgHeight), _bgTexture);

            GUIStyle titleStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 15,
                fontStyle = FontStyle.Bold
            };
            titleStyle.normal.textColor = Color.white;
            GUI.Label(new Rect(dlgX, dlgY + 12, dlgWidth, 24), "Marker Limit Reached", titleStyle);

            GUIStyle msgStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 12,
                wordWrap = true
            };
            msgStyle.normal.textColor = new Color(0.85f, 0.85f, 0.85f);
            GUI.Label(new Rect(dlgX + 16, dlgY + 42, dlgWidth - 32, 44),
                $"All marker slots are named. To add a new marker,\n\"{_evictionMarkerName}\" will be deleted.", msgStyle);

            float btnW = 130;
            float btnH = 34;
            float gap = 16;
            float totalW = btnW * 2 + gap;
            float startX = dlgX + (dlgWidth - totalW) / 2f;
            float btnY = dlgY + dlgHeight - btnH - 18;

            if (DrawButton(new Rect(startX, btnY, btnW, btnH), "Delete & Add"))
            {
                _showEvictionConfirm = false;
                _onEvictionConfirm?.Invoke();
            }

            if (DrawButton(new Rect(startX + btnW + gap, btnY, btnW, btnH), "Cancel"))
            {
                _showEvictionConfirm = false;
                _onEvictionCancel?.Invoke();
            }

            if (_cursorTexture != null)
            {
                var mousePos = Event.current.mousePosition;
                GUI.DrawTexture(new Rect(mousePos.x, mousePos.y, 16, 16), _cursorTexture);
            }
        }

        private bool DrawButton(Rect rect, string label)
        {
            bool isHover = rect.Contains(Event.current.mousePosition);
            GUI.DrawTexture(rect, isHover ? _buttonHoverTexture : _buttonTexture);

            GUIStyle labelStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 13
            };
            labelStyle.normal.textColor = Color.white;
            GUI.Label(rect, label, labelStyle);

            if (isHover && Event.current.type == EventType.MouseDown && Event.current.button == 0)
            {
                Event.current.Use();
                return true;
            }
            return false;
        }

        private bool DrawDangerButton(Rect rect, string label)
        {
            bool isHover = rect.Contains(Event.current.mousePosition);
            GUI.DrawTexture(rect, isHover ? _buttonHoverTexture : _buttonDangerTexture);

            GUIStyle labelStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 13,
                fontStyle = FontStyle.Bold
            };
            labelStyle.normal.textColor = isHover ? Color.white : new Color(0.9f, 0.4f, 0.4f);
            GUI.Label(rect, label, labelStyle);

            if (isHover && Event.current.type == EventType.MouseDown && Event.current.button == 0)
            {
                Event.current.Use();
                return true;
            }
            return false;
        }

        private void InitTextures()
        {
            if (_texturesInitialized) return;

            _bgTexture = MakeTexture(new Color(0.1f, 0.1f, 0.15f, 0.95f));
            _buttonTexture = MakeTexture(new Color(0.25f, 0.25f, 0.35f, 1f));
            _buttonHoverTexture = MakeTexture(new Color(0.35f, 0.35f, 0.5f, 1f));
            _buttonDangerTexture = MakeTexture(new Color(0.35f, 0.2f, 0.2f, 1f));
            _cursorTexture = MakeCursorTexture();

            _texturesInitialized = true;
        }

        private static Texture2D MakeTexture(Color color)
        {
            var tex = new Texture2D(2, 2);
            var pixels = new Color[] { color, color, color, color };
            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }

        private static Texture2D MakeCursorTexture()
        {
            int size = 16;
            var tex = new Texture2D(size, size);
            var transparent = new Color(0, 0, 0, 0);
            var white = Color.white;

            for (int cy = 0; cy < size; cy++)
                for (int cx = 0; cx < size; cx++)
                    tex.SetPixel(cx, cy, transparent);

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
