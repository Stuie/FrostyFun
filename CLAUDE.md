# FrostyFun - MelonLoader Mods for Sledding Game

## Project Structure

```
FrostyFun/
├── FrostyFun.sln           # Solution file
├── CLAUDE.md               # This file
├── global.json             # .NET SDK version pinning
├── FrostyFun/              # Original project (placeholder)
│   └── FrostyFun.csproj
├── SnowmanMod/             # Makes completed snowmen face the player
│   ├── SnowmanMod.csproj
│   ├── SnowmanMod.cs
│   └── Properties/
│       └── AssemblyInfo.cs
├── MenuQOL/                # Menu quality-of-life improvements (F7 = dump UI elements)
│   ├── MenuQOL.csproj
│   ├── MenuQOLMod.cs
│   └── Properties/
│       └── AssemblyInfo.cs
├── CharacterSelect/        # Quick character switching (F6 = UI)
│   ├── CharacterSelect.csproj
│   ├── CharacterSelectMod.cs
│   └── Assets/
├── YetiHunt/               # Battle Royale yeti hunting game mode (WIP)
│   ├── YetiHunt.csproj     # F10=start, F11=yeti dump, F12=network dump
│   ├── YetiHuntMod.cs
│   └── IMPLEMENTATION_PLAN.md
└── TestMod/                # MINIMAL TEST MOD - only verifies MelonLoader is working
    ├── TestMod.csproj      # No gameplay features, just logs to confirm mod loading
    ├── TestMod.cs
    └── Properties/
        └── AssemblyInfo.cs
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

## MelonLoader Mod Development

### Project Setup
- Target: `net6.0`
- NuGet: `LavaGang.MelonLoader` v0.7.2
- Set `GenerateAssemblyInfo=false` to use manual assembly attributes
- Reference assemblies from `MelonLoader\Il2CppAssemblies\` (conditional in .csproj)

### Required Assembly Attributes
```csharp
[assembly: MelonInfo(typeof(ModClass), "ModName", "1.0.0", "Author")]
[assembly: MelonGame("The Sledding Corporation", "Sledding Game Demo")]
```

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
dotnet build ModName/ModName.csproj -c Release && cp 'ModName/bin/Release/net6.0/ModName.dll' 'C:\Program Files (x86)\Steam\steamapps\common\Sledding Game Demo\Mods\'
```

**Game Path:** `C:\Program Files (x86)\Steam\steamapps\common\Sledding Game Demo`

#### Quick Deploy Commands
```bash
# SnowmanMod
dotnet build SnowmanMod/SnowmanMod.csproj -c Release && cp 'SnowmanMod/bin/Release/net6.0/SnowmanMod.dll' 'C:\Program Files (x86)\Steam\steamapps\common\Sledding Game Demo\Mods\'

# MenuQOL
dotnet build MenuQOL/MenuQOL.csproj -c Release && cp 'MenuQOL/bin/Release/net6.0/MenuQOL.dll' 'C:\Program Files (x86)\Steam\steamapps\common\Sledding Game Demo\Mods\'

# TestMod
dotnet build TestMod/TestMod.csproj -c Release && cp 'TestMod/bin/Release/net6.0/TestMod.dll' 'C:\Program Files (x86)\Steam\steamapps\common\Sledding Game Demo\Mods\'

# CharacterSelect
dotnet build CharacterSelect/CharacterSelect.csproj -c Release && cp 'CharacterSelect/bin/Release/net6.0/CharacterSelect.dll' 'C:\Program Files (x86)\Steam\steamapps\common\Sledding Game Demo\Mods\'

# YetiHunt
dotnet build YetiHunt/YetiHunt.csproj -c Release && cp 'YetiHunt/bin/Release/net6.0/YetiHunt.dll' 'C:\Program Files (x86)\Steam\steamapps\common\Sledding Game Demo\Mods\'
```

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
