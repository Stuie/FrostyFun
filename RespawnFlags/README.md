# Respawn Flags

Quick teleportation to spawn points in the Sledding Game. Browse discovered spawn points in a UI overlay or double-tap CapsLock for an instant respawn to the last used spawn point.

## Features

- Browse and teleport to any spawn point on the current map
- Quick respawn via CapsLock double-tap
- Automatic spawn point discovery on scene load
- Full cursor and input management (player input disabled while UI is open)

## Controls

| Key | Action |
|-----|--------|
| F8 | Toggle spawn point UI |
| CapsLock (double-tap) | Quick respawn to last spawn point |
| Escape | Close UI |

## Installation

1. Install [MelonLoader](https://melonwiki.xyz/) on Sledding Game
2. Download `RespawnFlags.dll` from the [Releases](../../releases) page
3. Place the DLL in the `Mods` folder

## Building

```bash
dotnet build RespawnFlags/RespawnFlags.csproj -c Release
```

## Deploy

```bash
dotnet build RespawnFlags/RespawnFlags.csproj -c Release && cp 'RespawnFlags/bin/Release/net6.0/RespawnFlags.dll' 'C:\Program Files (x86)\Steam\steamapps\common\Sledding Game\Mods\'
```
