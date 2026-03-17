# CharacterSelect

Quick character switching via a visual UI, with session persistence.

## Features

- Press F6 to open a visual character selection grid
- 15 playable characters available (all except Turtle which doesn't work)
- Shows character icons where available, placeholder for others
- Remembers your mod-selected character across sessions
- Detects in-game character changes and respects them

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
The mod provides a custom IMGUI-based selection screen with a 5x3 grid of characters. Characters with game icons display them; others show a custom placeholder.

### Persistence
- When you select a character via F6, it's saved to Unity's PlayerPrefs
- On next session, the mod auto-applies your saved character ~1.5 seconds after spawning
- If you change character via the in-game menu, the mod detects this and clears its saved preference
- This means whichever method you used last (mod or in-game) is what persists

### Available Characters
| Has Icon | Name |
|----------|------|
| Yes | Frog, Penguin, Brown Bear, Baikal Seal, Orange Toad, Orange Fox |
| No | Harbor Seal, Polar Bear, Black Bear, Ringed Seal, Strawberry Frog, Tree Frog, Brown Toad, Arctic Fox, Panda |

## Custom Assets

The mod supports embedded custom icons. Place PNG files in `Assets/` and they'll be compiled into the DLL. Currently includes a placeholder icon for characters without game icons.

## Technical Details

- Uses IMGUI (OnGUI) for the selection interface
- Reads character state from `PlayerControl.sync_EquippedCharacterName`
- Switches characters via `PlayerControl.CmdSwitchCharacter()`
- Disables player input while UI is open to prevent camera movement
- Custom cursor drawn since the game's UI cursor isn't accessible during gameplay
