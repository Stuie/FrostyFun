# SnowmanMod

Makes completed snowmen turn their faces to track the player automatically.

## Features

- Automatically detects completed snowmen (2+ stacked snowballs)
- Uses the "Face" child object to determine where the snowman's face is pointing
- Smoothly rotates snowmen to face the player as they move around
- No configuration needed - just build snowmen and they'll watch you!

## Installation

1. Build the mod: `dotnet build -c Release`
2. Copy `bin/Release/net6.0/SnowmanMod.dll` to `[Game]/Mods/`
3. Launch the game

## How It Works

Each snowman ball has a "Face" child object containing the carrot nose, coal eyes, and mouth. The face features are positioned at +Z in local space relative to the Face object.

The mod automatically:
1. Scans for `Snowman Ball(Clone)` objects every second
2. Groups them by position to identify complete snowmen (2+ balls stacked)
3. Finds the head (highest Y position) of each snowman
4. Uses the Face child's world-space forward direction to determine where the face is pointing
5. Smoothly rotates the head so the face points toward the player

## Technical Details

- Only the head (topmost ball) is rotated, not the body
- Rotation is applied to the ball's Y-axis only (keeps snowmen upright)
- Smooth interpolation prevents jarring rotation
- Face detection works regardless of how the snowball was rolled
- Destroyed snowmen are automatically cleaned up from tracking

## API for Future Integration

The mod exposes public methods for potential mod menu or command integration:

- `CaptureSnowmen()` - Force rescan and capture all snowmen
- `StopTracking()` - Stop all snowman tracking
- `ShowDebugInfo()` - Log debug information about tracked snowmen
- `InspectSnowballHierarchy()` - Log the hierarchy of all snowballs
