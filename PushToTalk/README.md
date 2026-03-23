# PushToTalk

Adds push-to-talk functionality to Sledding Game Demo's Dissonance VOIP system.

## Features

- Hold V to unmute your microphone (mutes again on release)
- Automatically finds and hooks into the Dissonance voice system
- Syncs the in-game voice toggle UI to reflect mute state
- Only active in gameplay scenes (not menus or loading screens)

## Installation

1. Build the mod: `dotnet build -c Release`
2. Copy `bin/Release/net6.0/PushToTalk.dll` to `[Game]/Mods/`
3. Launch the game

## Controls

| Key | Action |
|-----|--------|
| V (hold) | Push-to-talk (unmutes while held) |
| F9 | Dump Dissonance voice system info to log |

## How It Works

The game uses [Dissonance Voice Chat](https://placeholder-software.co.uk/dissonance/) for multiplayer VOIP. The mod locates the `DissonanceComms` component at runtime and toggles `IsMuted` when the V key is held. It also finds the settings menu's voice toggle and keeps it in sync so the UI accurately reflects the current mute state.
