# FrostyFun - MelonLoader Mods for Sledding Game

## Project Structure

```
FrostyFun/
├── FrostyFun.sln              # Solution file
├── CLAUDE.md                  # This file
├── global.json                # .NET SDK version pinning
├── Directory.Build.props      # Shared project properties (target framework, NuGet refs)
├── Directory.Build.targets    # Auto-generates MelonLoader assembly attributes
├── FrostyFun.Shared/          # Shared code library (see "Shared Code" section below)
│   ├── FrostyFun.Shared.csproj
│   ├── FrostyFun.Shared.targets  # Consumers <Import> this; one-line opt-in
│   ├── README.md
│   ├── Logging/               # IModLogger + MelonLoggerAdapter
│   ├── Il2Cpp/                # Il2CppTypeResolver, GetIl2CppTypeName extension
│   ├── Players/               # PlayerLocator, PlayerTeleporter, PlayerInputBlocker
│   ├── UI/                    # TextureFactory, CursorTextures, CursorState
│   └── Resources/             # EmbeddedResourceLoader
├── SnowmanMod/                # Makes completed snowmen face the player
│   ├── SnowmanMod.csproj
│   └── SnowmanMod.cs
├── MenuQOL/                   # Menu quality-of-life improvements (F7 = dump UI elements)
│   ├── MenuQOL.csproj
│   └── MenuQOLMod.cs
├── CharacterSelect/           # Quick character switching (F6 = UI)
│   ├── CharacterSelect.csproj
│   ├── CharacterSelectMod.cs
│   └── Assets/
├── PushToTalk/                # Push-to-talk for Dissonance VOIP (V = talk, F9 = dump)
│   ├── PushToTalk.csproj
│   └── PushToTalkMod.cs
├── YetiHunt/                  # Battle Royale yeti hunting game mode (WIP)
│   ├── YetiHunt.csproj        # F10=start, F11=yeti dump, F12=network dump
│   ├── YetiHuntMod.cs
│   ├── IMPLEMENTATION_PLAN.md
│   ├── Assets/
│   ├── Boundary/              # Play area boundary logic
│   ├── Combat/                # Snowball hit detection
│   ├── Core/                  # Game state machine
│   ├── Debug/                 # Diagnostics and debug input
│   ├── Infrastructure/        # Il2Cpp type resolution, logging adapters
│   ├── Players/               # Player tracking and teleportation
│   ├── UI/                    # HUD, minimap rendering
│   └── Yeti/                  # Yeti spawning, AI behavior, movement
├── RacePicker/                # Pick race route at shared start flag (F5 = UI, Ctrl+F5 = dump)
│   ├── RacePicker.csproj
│   └── RacePickerMod.cs
├── RespawnFlags/              # Quick teleport to spawn points (F8 = UI, CapsLock x2 = respawn)
│   ├── RespawnFlags.csproj
│   ├── RespawnFlagsMod.cs
│   ├── Services/              # Teleport + spawn point management
│   └── UI/                    # IMGUI spawn point picker
├── YetiHunt.Tests/            # Unit tests for YetiHunt
│   ├── YetiHunt.Tests.csproj
│   ├── GameStateMachineTests.cs
│   ├── MinimapCoordinateTests.cs
│   └── YetiBehaviorTests.cs
└── TestMod/                   # MINIMAL TEST MOD - only verifies MelonLoader is working
    ├── TestMod.csproj
    └── TestMod.cs
```

## First-Time Setup (IMPORTANT)

This game uses **Il2Cpp**, which means Unity assemblies are generated at runtime by MelonLoader.

**Before building mods:**
1. Install MelonLoader on the game (already done)
2. **Run the game once** with MelonLoader installed
3. Wait for MelonLoader to generate Il2Cpp assemblies (console shows progress)
4. Close the game
5. Now you can build mods with proper Unity references

After the first run, assemblies will be at:
```
[GamePath]\MelonLoader\Il2CppAssemblies\
```

## Shared Code (`FrostyFun.Shared/`)

**Before implementing anything that touches the player, custom UI, Il2Cpp reflection, or embedded resources, check `FrostyFun.Shared/` first.** If the functionality already exists there, use it. If you're about to write something a future mod will likely want, **put it in `FrostyFun.Shared/`** rather than duplicating it in your mod.

See `FrostyFun.Shared/README.md` for the inventory. Highlights:
- `PlayerTeleporter` — teleport via reflected `PlayerControl.teleportationController.TeleportPlayer` (with optional "leave race first")
- `PlayerInputBlocker` — `Disable()` / `Restore()` for `PlayerLocalInput` + `PlayerCameraControl` + Cinemachine while a mod UI is open
- `Il2CppTypeResolver` + `GetIl2CppTypeName(this Component)` extension — replaces ad-hoc `Assembly.Load("Assembly-CSharp").GetTypes()` scans
- `TextureFactory.MakeSolid` / `MakeCircle`, `CursorTextures.MakeArrowCursor`, `CursorState.Snapshot/Restore/ShowFree` — IMGUI helpers
- `MelonLoggerAdapter` — wraps `MelonLogger.Instance` as an `IModLogger` so shared code can log without depending on a specific mod's logger
- `EmbeddedResourceLoader.LoadTexture(Assembly, name, logger)` — decodes embedded PNG/JPG into `Texture2D`

### How a mod consumes Shared

One line in the mod's `.csproj`:

```xml
<Import Project="..\FrostyFun.Shared\FrostyFun.Shared.targets" />
```

That import provides both the shared source files (compiled into the mod's DLL) and the references those files need. The mod's csproj only declares references that are *specific to that mod* (e.g. `Il2CppFishNet.Runtime`, `Unity.TextMeshPro`).

**No `FrostyFun.Shared.dll` ships at runtime** — the shared types are compiled into each mod's own assembly. This gives per-mod version isolation: two mods built against different revisions of Shared can coexist without type-identity conflicts.

The `FrostyFun.Shared/` folder also contains a `FrostyFun.Shared.csproj` for Rider/IDE convenience (so the shared code shows up as a project in the solution). Its built DLL is never deployed or referenced by anyone — don't ProjectReference it.

### When to add to Shared vs. keep in a mod

- **Add to Shared:** anything plausibly useful to another mod (player manipulation, IMGUI helpers, Il2Cpp reflection patterns). If you're tempted to copy a method from one mod into another, that method belongs in Shared.
- **Keep in the mod:** behaviour unique to one game system the mod owns (RacePicker's race-flag swapping, RespawnFlags' spawn-point storage, YetiHunt's yeti AI).

If you add a new shared file that uses a reference not yet in `FrostyFun.Shared.targets`, add the `<Reference>` to that targets file (not to each consumer's csproj). Every consumer inherits it automatically.

## MelonLoader Mod Development

### Project Setup
- Target: `net6.0`
- NuGet: `LavaGang.MelonLoader` v0.7.2
- Shared settings in `Directory.Build.props`; assembly attributes (`MelonInfo`, `MelonGame`) are auto-generated by `Directory.Build.targets`
- Reference assemblies from `MelonLoader\Il2CppAssemblies\` (conditional in .csproj)
- Consume shared utilities by adding `<Import Project="..\FrostyFun.Shared\FrostyFun.Shared.targets" />` (see Shared Code section above)

### MelonMod Lifecycle Methods
| Method | When Called |
|--------|-------------|
| `OnInitializeMelon()` | Mod loaded, Unity ready |
| `OnUpdate()` | Every frame |
| `OnLateUpdate()` | After all Update calls |
| `OnFixedUpdate()` | Physics tick |
| `OnSceneWasLoaded(int buildIndex, string sceneName)` | Scene transitions |
| `OnApplicationQuit()` | Game closing |

### Input Detection (Il2Cpp)
For Il2Cpp games, use reflection to access Unity types:
```csharp
// Types have Il2Cpp prefix: Il2CppUnityEngine.Input, Il2CppUnityEngine.KeyCode
// Or access via reflection for compile-time independence
```

### Logging
```csharp
Melon<TestModMain>.Logger.Msg("Message");
Melon<TestModMain>.Logger.Warning("Warning");
Melon<TestModMain>.Logger.Error("Error");
```

## Build & Deploy

### Build
```bash
dotnet build ModName/ModName.csproj -c Release
```

### Deploy
**IMPORTANT FOR CLAUDE:** Always use SINGLE QUOTES for paths in Bash commands (Cygwin/Windows issue with spaces in paths). Double quotes will fail.

```bash
# Build and deploy a mod (replace ModName with actual mod name)
dotnet build ModName/ModName.csproj -c Release && cp 'ModName/bin/Release/net6.0/ModName.dll' 'C:\Program Files (x86)\Steam\steamapps\common\Sledding Game\Mods\'
```

**Game Path:** `C:\Program Files (x86)\Steam\steamapps\common\Sledding Game`

#### Quick Deploy Commands
```bash
# SnowmanMod
dotnet build SnowmanMod/SnowmanMod.csproj -c Release && cp 'SnowmanMod/bin/Release/net6.0/SnowmanMod.dll' 'C:\Program Files (x86)\Steam\steamapps\common\Sledding Game\Mods\'

# MenuQOL
dotnet build MenuQOL/MenuQOL.csproj -c Release && cp 'MenuQOL/bin/Release/net6.0/MenuQOL.dll' 'C:\Program Files (x86)\Steam\steamapps\common\Sledding Game\Mods\'

# TestMod
dotnet build TestMod/TestMod.csproj -c Release && cp 'TestMod/bin/Release/net6.0/TestMod.dll' 'C:\Program Files (x86)\Steam\steamapps\common\Sledding Game\Mods\'

# CharacterSelect
dotnet build CharacterSelect/CharacterSelect.csproj -c Release && cp 'CharacterSelect/bin/Release/net6.0/CharacterSelect.dll' 'C:\Program Files (x86)\Steam\steamapps\common\Sledding Game\Mods\'

# PushToTalk
dotnet build PushToTalk/PushToTalk.csproj -c Release && cp 'PushToTalk/bin/Release/net6.0/PushToTalk.dll' 'C:\Program Files (x86)\Steam\steamapps\common\Sledding Game\Mods\'

# YetiHunt
dotnet build YetiHunt/YetiHunt.csproj -c Release && cp 'YetiHunt/bin/Release/net6.0/YetiHunt.dll' 'C:\Program Files (x86)\Steam\steamapps\common\Sledding Game\Mods\'

# RacePicker
dotnet build RacePicker/RacePicker.csproj -c Release && cp 'RacePicker/bin/Release/net6.0/RacePicker.dll' 'C:\Program Files (x86)\Steam\steamapps\common\Sledding Game\Mods\'

# RespawnFlags
dotnet build RespawnFlags/RespawnFlags.csproj -c Release && cp 'RespawnFlags/bin/Release/net6.0/RespawnFlags.dll' 'C:\Program Files (x86)\Steam\steamapps\common\Sledding Game\Mods\'
```

**Mods that consume `FrostyFun.Shared`** (RespawnFlags, RacePicker) still build and deploy as a single DLL — the shared source is compiled into the mod's own assembly, so there is no `FrostyFun.Shared.dll` to copy alongside.

### Verify
1. Launch game with MelonLoader installed
2. Check `MelonLoader/Latest.log` for mod loading

## Chat Integration

The game uses `ChatManager.Instance` singleton pattern (discovered from CrossChat mod).
Use reflection to find and invoke chat methods since exact API may vary.

## Il2Cpp Assembly Locations

| Folder | Contents |
|--------|----------|
| `MelonLoader\net6\` | MelonLoader runtime, Il2CppInterop (always present) |
| `MelonLoader\Il2CppAssemblies\` | Generated Unity/game assemblies (after first run) |

## References
- [MelonLoader Wiki](https://melonwiki.xyz/)
- [MelonLoader NuGet](https://www.nuget.org/packages/LavaGang.MelonLoader)
- [BobisBilly/Sledding-Game-Mods](https://github.com/BobisBilly/Sledding-Game-Mods)
- [MelonLoader Quick Start](https://github.com/LavaGang/MelonWiki/blob/master/docs/modders/quickstart.md)
