# Scene Loader

Loads custom Unity AssetBundle scenes inside the Sledding Game, hijacks real networked Bench instances to give the scene rideable, multi-passenger trains, and migrates the player between the main world and the custom scene cleanly.

> **Status:** Work in progress. Single-player works; multiplayer hijack of FishNet-synced benches is plumbed but untested with multiple clients.

## Features

- **AssetBundle scene picker (F4):** drop any `*.bundle` into `Mods/CustomScenes/` and pick it from an in-game IMGUI list.
- **Player migration:** hides main-world geometry, repositions the player at the bundle's SpawnPoint, then reverses everything on exit.
- **Custom desert test scene** (`UnitySceneProject/`): 5×5 km terrain with sand-dune height field, slope-classified sand / transition / sandstone materials, cacti, a lodge gazebo, peak rocks, and two looping train routes.
- **Networked trains:** hijacks real `Bench` GameObjects from the main scene. Bench already inherits `Seat` (multi-passenger F-interact) and has a FishNet `NetworkTransform`, so when the host moves the bench, every client sees the synced position automatically. Train body / cabin / smokestack / trailer are built as children of the bench so they ride along on the synced transform.

## Controls

| Key | Action |
|-----|--------|
| F4 | Open scene picker (or return to main world) |
| Ctrl+F4 | Dump scene / player / network diagnostics |
| Shift+F4 | Dump world dimensions / custom-scene state |
| F (in scene) | Sit on a passing train (game's standard Seat prompt) |

## Installation

1. Install [MelonLoader](https://melonwiki.xyz/) on Sledding Game.
2. Build or download `SceneLoader.dll` (see below).
3. Drop the DLL in the game's `Mods/` folder.
4. Drop one or more `*.bundle` files in `Mods/CustomScenes/` (the folder is created on first run if missing).

## Building the mod

```bash
dotnet build SceneLoader/SceneLoader.csproj -c Release
```

DLL lands at `SceneLoader/bin/Release/net6.0/SceneLoader.dll`.

Deploy:

```bash
dotnet build SceneLoader/SceneLoader.csproj -c Release && cp 'SceneLoader/bin/Release/net6.0/SceneLoader.dll' 'C:\Program Files (x86)\Steam\steamapps\common\Sledding Game\Mods\'
```

## Building the custom scene bundle

The `UnitySceneProject/` directory is a Unity 6 (URP) project that builds the desert/train test scene. It is separate from the .NET solution and only needed if you want to edit or rebuild the bundle.

1. Open `UnitySceneProject/` in Unity Editor **6000.3.14f1** (or any 6000.3.x).
2. Let it import. The first open will take a minute — the generated material / mesh / scene assets are not committed; they regenerate from `Assets/Editor/BuildAssetBundles.cs`.
3. Run the menu **SceneLoader → Build && Deploy** (shortcut `Ctrl+Alt+B`). This regenerates all procedural materials / textures / meshes, builds the scene, packs it into `Build/testscene.bundle`, and copies the bundle to `<game>/Mods/CustomScenes/`.

## Architecture

- `SceneLoaderMod.cs` — MelonMod entry point, IMGUI scene-picker, F4 state machine, orchestrates the services below.
- `Services/AssetBundleLoader.cs` — scans `Mods/CustomScenes/`, loads bundles via `Il2CppAssetBundle` (the standard wrapper is broken on Il2Cpp; this one bypasses interop via native ICalls).
- `Services/PlayerMigration.cs` — hides main-world geometry on entry, restores on exit. Supports excluding specific GameObjects (used to keep hijacked benches active).
- `Services/TrainAnimator.cs` — hijacks live Bench instances, parents bundle-defined `TrainRoute_*` waypoint geometry to them, and drives motion on the host. Clients get sync for free via FishNet's `NetworkTransform`.
- `Services/SceneDiagnostics.cs` — Ctrl+F4 / Shift+F4 hierarchy + property dumps. Indispensable for figuring out how the live game wires things.
- `Networking/ClassInjectorProbe.cs` — research spike (kept for future reference) that confirmed `Il2CppInterop.ClassInjector` + FishNet's prefab registration is **not** a viable path for custom `NetworkBehaviour` subclasses, which is why the mod hijacks existing networked objects instead.
