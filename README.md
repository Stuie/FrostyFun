# FrostyFun

MelonLoader mods for **Sledding Game Demo**.

## Mods

### SnowmanMod

Makes completed snowmen turn their faces to track the player automatically.

**How it works:** Each snowman ball has a "Face" child object that contains the carrot nose, coal eyes, and mouth. The mod automatically detects completed snowmen (2+ stacked balls) and rotates them so the face always points toward the player. No configuration needed - just build snowmen and they'll watch you!

### MenuQOL

Menu quality-of-life improvements for the hosting flow.

**Features:**
- Auto-confirms the "good internet connection" popup when hosting
- Password field improvements: auto-checks toggle when clicking field, auto-focuses field when checking toggle, Enter key submits, remembers last password

**Controls:**
| Key | Action |
|-----|--------|
| F7 | Dump UI elements to log (development) |

### CharacterSelect

Quick character switching via a visual UI, with custom skins support.

**Features:**
- Press F6 to open a visual character selection grid with 15 characters
- All characters have custom embedded icons
- Remembers your selection across sessions
- Detects in-game character changes and respects them
- Custom skin system: place textures in `Mods/reskins/{character}/`
- Skin tools panel (gear button) for exporting and managing character textures

**Controls:**
| Key | Action |
|-----|--------|
| F6 | Open/close character selection UI |

### TestMod

Minimal developer utility for verifying MelonLoader is working. Logs a message on pressing F7, no gameplay features.

## Requirements

These are either requirements, or the only versions I tested with.

- [MelonLoader](https://melonwiki.xyz/) v0.7+ installed on Sledding Game Demo
- .NET 6.0 SDK for building

## Building

```bash
# Build all mods
dotnet build -c Release

# Build a specific mod
dotnet build SnowmanMod/SnowmanMod.csproj -c Release
```

## Installation

Copy the mod DLL to the game's Mods folder:

```
[ModName]/bin/Release/net6.0/[ModName].dll -> [GamePath]/Mods/
```

**Game Path:** `C:\Program Files (x86)\Steam\steamapps\common\Sledding Game Demo`

### Quick Install (PowerShell/Bash)

```bash
# SnowmanMod
cp SnowmanMod/bin/Release/net6.0/SnowmanMod.dll "C:\Program Files (x86)\Steam\steamapps\common\Sledding Game Demo\Mods\"

# MenuQOL
cp MenuQOL/bin/Release/net6.0/MenuQOL.dll "C:\Program Files (x86)\Steam\steamapps\common\Sledding Game Demo\Mods\"

# CharacterSelect
cp CharacterSelect/bin/Release/net6.0/CharacterSelect.dll "C:\Program Files (x86)\Steam\steamapps\common\Sledding Game Demo\Mods\"
```

## First-Time Setup

This game uses Il2Cpp, so Unity assemblies are generated at runtime:

1. Install MelonLoader on the game
2. Run the game once (MelonLoader generates assemblies)
3. Close the game
4. Now you can build and install mods

## License

MIT, see LICENSE file.
