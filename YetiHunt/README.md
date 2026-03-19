# YetiHunt

A Battle Royale-style game mode where players hunt a yeti. Players are teleported to random sky positions, a yeti spawns in the play area, and the first player to hit it with a snowball wins the round.

## Features

- **Yeti spawning**: Spawns the game's built-in yeti NPC with custom wandering AI
- **Snowball detection**: Tracks snowball hits on the yeti with player attribution
- **Minimap overlay**: Shows player and yeti positions on a map in the corner
- **Countdown timer**: 3-second countdown before hunting begins
- **Winner announcement**: Displays who hit the yeti first
- **Boundary protection**: Disables the out-of-bounds yeti and fog for uninterrupted play
- **Random teleportation**: Drops players from the sky at random positions

## Controls

| Key | Action |
|-----|--------|
| Ctrl+1 | Start/stop a yeti hunt round |
| Ctrl+2 | Spawn a test yeti near the player |
| Ctrl+3 | Dump map/UI info (debug) |
| Ctrl+4 | Dump map coordinate debug |
| Ctrl+5 | Toggle boundary protection (disable OOB yetis + fog) |
| Ctrl+6 | Record corner coordinate (for calibration) |
| Ctrl+7 | Dump player info |
| Ctrl+8 | Show recorded corners |

## Installation

1. Build the mod: `dotnet build YetiHunt/YetiHunt.csproj -c Release`
2. Copy `YetiHunt/bin/Release/net6.0/YetiHunt.dll` to `[Game]/Mods/`
3. Launch the game

## How It Works

### Game Flow
1. Press **Ctrl+1** to start a round
2. 3-second countdown displays on screen
3. You're teleported to a random sky position
4. A yeti spawns in the play area and wanders around
5. Find the yeti on the minimap (red dot) and hit it with a snowball
6. Winner is announced, then returns to idle state

### Yeti Behavior
The yeti uses a simple state machine:
- **Moving**: Walks toward a target position
- **Pausing**: Stops briefly (1-3 seconds)
- **Turning**: Rotates toward new direction
- Repeats with random targets within its wander radius

### Hit Detection
- Uses Physics.OverlapCapsule to detect snowballs near yetis
- Tracks which snowballs have already been counted to avoid duplicates
- Identifies the thrower via FishNet's sync_PlayerThatPickedUpObject or OwnerId

### Minimap
- Embedded map.png texture rendered in bottom-right corner
- Coordinate transformation converts world positions to map pixels
- Player shown as green dot, yetis as red dots
- Only visible during countdown and hunting phases

## Project Structure

The mod uses a clean, modular architecture:

```
YetiHunt/
├── YetiHuntMod.cs              # Entry point, orchestration (~240 lines)
├── Core/
│   ├── GameState.cs            # Idle, Countdown, Hunting, RoundEnd
│   ├── IGameStateMachine.cs    # State management interface
│   └── GameStateMachine.cs     # State transitions and timing
├── Yeti/
│   ├── HuntYeti.cs             # Data class for tracked yetis
│   ├── YetiMovementState.cs    # Moving, Pausing, Turning
│   ├── IYetiManager.cs         # Spawning/tracking interface
│   ├── YetiManager.cs          # Yeti lifecycle management
│   ├── IYetiBehaviorController.cs
│   └── YetiBehaviorController.cs  # Wandering AI
├── Combat/
│   ├── HitEventArgs.cs         # Hit event data
│   ├── ISnowballDetector.cs
│   └── SnowballDetector.cs     # Physics-based hit detection
├── Players/
│   ├── IPlayerTracker.cs
│   ├── PlayerTracker.cs        # Username caching by OwnerId
│   ├── ITeleportationService.cs
│   └── TeleportationService.cs # Il2Cpp teleport invocation
├── Boundary/
│   ├── IBoundaryController.cs
│   └── BoundaryController.cs   # Fog and OOB yeti control
├── UI/
│   ├── TextureFactory.cs       # Texture creation utilities
│   ├── IHuntUI.cs
│   ├── HuntUI.cs               # State display, countdown, winner
│   ├── IMinimapRenderer.cs
│   └── MinimapRenderer.cs      # Map rendering and coordinates
├── Infrastructure/
│   ├── IModLogger.cs           # Logging abstraction
│   ├── MelonLoggerAdapter.cs   # MelonLoader logger wrapper
│   ├── ITypeResolver.cs        # Il2Cpp type discovery
│   └── Il2CppTypeResolver.cs   # Reflection cache
├── Debug/
│   ├── IDiagnosticsService.cs
│   ├── DiagnosticsService.cs   # Dump methods for debugging
│   └── InputHandler.cs         # Debug key bindings
└── Assets/
    └── map.png                 # Embedded minimap texture
```

## Testing

The mod includes a separate test project with unit tests for core logic:

```bash
dotnet test YetiHunt.Tests/YetiHunt.Tests.csproj
```

Tests cover:
- **GameStateMachine**: State transitions, timing, winner handling
- **YetiBehavior**: Movement state machine logic
- **MinimapCoordinates**: World-to-map coordinate transformation

## Technical Details

- **Framework**: .NET 6.0, MelonLoader 0.7.2
- **Game networking**: FishNet (found yeti RPC methods via reflection)
- **UI rendering**: Unity IMGUI (OnGUI)
- **Hit detection**: Physics.OverlapCapsule with 2m radius, 6.5m height
- **Yeti control**: Invokes native Yeti.Move() method via reflection

## Future Improvements

- Multi-round matches with scoring
- Shrinking play boundary (battle royale style)
- Power-ups (speed boost, triple shot, tracker)
- Networked state sync for multiplayer
