# CharacterSelect

Quick character switching via a visual UI, with custom skins and session persistence.

## Features

- Press F6 to open a visual character selection grid
- 15 playable characters available (all except Turtle which doesn't work)
- All characters have custom embedded icons
- Remembers your mod-selected character across sessions
- Detects in-game character changes and respects them
- Custom skin (reskin) system for applying alternate textures
- Skin tools panel with gear button for exporting and managing skins
- "Open Skin Dumps Folder" button for quick access to exported textures

## Controls

| Key | Action |
|-----|--------|
| F6 | Open/close character selection UI |
| Escape | Close character selection UI |
| Click | Select a character |

## Installation

1. Build the mod: `dotnet build -c Release`
2. Copy `bin/Release/net6.0/CharacterSelect.dll` to `[Game]/Mods/`
3. Launch the game

## How It Works

### Character Selection
The mod provides a custom IMGUI-based selection screen with a 5x3 grid of characters. All characters display custom embedded icons.

### Persistence
- When you select a character via F6, it's saved to Unity's PlayerPrefs
- On next session, the mod auto-applies your saved character ~1.5 seconds after spawning
- If you change character via the in-game menu, the mod detects this and clears its saved preference
- This means whichever method you used last (mod or in-game) is what persists

### Custom Skins (Reskins)
The mod supports custom texture skins for characters. Place skin files in `Mods/reskins/{character}/` where `{character}` is a model key like `panda`, `frog`, `bear`, etc.

Supported skin formats:
- **PNG/BMP textures** - applied directly as skin replacements
- **JSON procedural skins** - define color transforms applied to the base texture

Each skin can include an optional `_icon.png` file for display in the UI.

### Skin Tools Panel
Click the gear button in the character select UI to open the skin tools panel. From here you can:
- Export character textures (BMP-to-PNG conversion)
- Open the skin dumps folder directly

## Custom Assets

The mod embeds custom icons and reskin assets into the DLL. Place PNG/JSON files in `Assets/` and they'll be compiled into the DLL.

## Technical Details

- Uses IMGUI (OnGUI) for the selection interface
- Reads character state from `PlayerControl.sync_EquippedCharacterName`
- Switches characters via `PlayerControl.CmdSwitchCharacter()`
- Disables player input while UI is open to prevent camera movement
- Custom cursor drawn since the game's UI cursor isn't accessible during gameplay
- Wrench icon loaded from game assets for the skin tools button
