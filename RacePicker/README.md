# Race Picker

A MelonLoader mod for Sledding Game that lets you choose which race to run from the shared start flag — "Do A Trick" or "Frozen Feet" — instead of being randomly assigned.

## Features

- Select a specific race (Do A Trick or Frozen Feet) or keep it random
- Preference persists across game sessions
- Debug dump for discovering race-related game types

## Controls

| Key | Action |
|-----|--------|
| F5 | Toggle race picker UI |
| Ctrl+F5 | Dump race-related types and scene objects to MelonLoader log |

## Installation

1. Install [MelonLoader](https://melonwiki.xyz/) on **Sledding Game**
2. Run the game once with MelonLoader to generate Il2Cpp assemblies
3. Download `RacePicker.dll` from the [Releases](../../releases) page
4. Place the DLL in the `Mods` folder inside the game directory

## Building

```bash
dotnet build RacePicker/RacePicker.csproj -c Release
```

## Deploy

```bash
dotnet build RacePicker/RacePicker.csproj -c Release && cp 'RacePicker/bin/Release/net6.0/RacePicker.dll' 'C:\Program Files (x86)\Steam\steamapps\common\Sledding Game\Mods\'
```
