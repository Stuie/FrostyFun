# TestMod

Developer utility for testing chat integration in Sledding Game Demo.

## Features

- Sends a local system chat message when F7 is pressed
- Uses reflection to find and invoke the game's ChatManager

## Controls

| Key | Action |
|-----|--------|
| F7 | Send "Hello from TestMod!" to local chat |

## Installation

1. Build the mod: `dotnet build -c Release`
2. Copy `bin/Release/net6.0/TestMod.dll` to `[Game]/Mods/`
3. Launch the game

## How It Works

The mod searches loaded assemblies for `ChatManager`, gets the singleton instance, and invokes `SendLocalSystemChatMessage` to display messages in the local chat window.
