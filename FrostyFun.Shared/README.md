# FrostyFun.Shared

Shared code library for FrostyFun mods. Compiled directly into each consuming mod's DLL — there is **no `FrostyFun.Shared.dll` at runtime**.

## How to use it from a mod

Add **one line** to the mod's `.csproj`:

```xml
<Import Project="..\FrostyFun.Shared\FrostyFun.Shared.targets" />
```

That import pulls in the shared `.cs` files (as `<Compile>` items) **and** the game/Unity references those files need (`Il2CppInterop.Runtime`/`Common`, `Il2Cppmscorlib`, `UnityEngine.CoreModule`, `UnityEngine.ImageConversionModule`). The mod's own csproj only needs to declare references that are specific to *that mod*.

That's it — your mod now has access to everything in this folder. Build, deploy, and ship a single self-contained DLL with no extra files in the Mods directory.

## What's in here

| Namespace | Type | Purpose |
|-----------|------|---------|
| `FrostyFun.Shared.Logging` | `IModLogger` | Interface decoupling shared code from `MelonLogger` |
| `FrostyFun.Shared.Logging` | `MelonLoggerAdapter` | Wraps `MelonLogger.Instance` as an `IModLogger` |
| `FrostyFun.Shared.Il2Cpp` | `Il2CppTypeResolver` | Caches `Type` lookups against `Assembly-CSharp` by simple name |
| `FrostyFun.Shared.Il2Cpp` | `Il2CppExtensions.GetIl2CppTypeName(Component)` | Safe wrapper for `comp.GetIl2CppType()?.Name` |
| `FrostyFun.Shared.Players` | `PlayerLocator.FindLocal()` | `GameObject.Find("Player Networked(Clone)")` |
| `FrostyFun.Shared.Players` | `PlayerTeleporter` | Teleport via reflected `PlayerControl.teleportationController.TeleportPlayer`, with optional "leave race first" |
| `FrostyFun.Shared.Players` | `PlayerInputBlocker` | `Disable()` / `Restore()` for `PlayerLocalInput` + `PlayerCameraControl` + Cinemachine; used while a mod UI is open |
| `FrostyFun.Shared.UI` | `TextureFactory` | `MakeSolid(color)` and `MakeCircle(size, color)` |
| `FrostyFun.Shared.UI` | `CursorTextures.MakeArrowCursor()` | 16×16 pixel-art mouse cursor for IMGUI overlays |
| `FrostyFun.Shared.UI` | `CursorState` | `Snapshot()` / `Restore()` / `ShowFree()` for `Cursor.visible` + `lockState` |
| `FrostyFun.Shared.Resources` | `EmbeddedResourceLoader.LoadTexture(Assembly, name, logger)` | Decodes embedded PNG/JPG into `Texture2D`; takes the assembly explicitly (don't trust `GetCallingAssembly` here) |

## When to add to Shared vs. keep in a mod

**Add to Shared** when:
- A second mod is about to copy/paste an existing implementation
- The functionality is plausibly useful to future mods (e.g. anything that touches `PlayerControl`, IMGUI helpers, common Il2Cpp reflection patterns)

**Keep in the mod** when:
- The behaviour is unique to one game system the mod owns (e.g. RacePicker's race-flag swapping, RespawnFlags' spawn-point storage)
- It's experimental and not yet stable

## Adding a new shared file

1. Drop the `.cs` file in the appropriate `FrostyFun.Shared/<area>/` folder.
2. If the file needs a Unity/Il2Cpp reference that isn't already in `FrostyFun.Shared.targets`, add it there — every consuming mod will inherit it automatically.
3. Build any consuming mod to confirm it compiles.

## Why source-level inclusion, not a DLL?

Two reasons:

1. **No runtime DLL dependency.** Each mod ships as a single self-contained DLL. Users only need to drop the mod they want into `Mods/` — no shared library to keep in sync.
2. **Per-mod version isolation.** Each mod's compiled-in copy of Shared lives in *its own* assembly. Two mods built against different revisions of Shared can coexist in the same MelonLoader process without type-identity conflicts, because their `PlayerTeleporter` types are literally in different assemblies.

The `FrostyFun.Shared.csproj` in this folder exists purely as an IDE/Rider convenience — it lets Rider show this code as a project. Its built DLL (in `FrostyFun.Shared/bin/`) is never deployed or referenced. Consumers reach Shared via `FrostyFun.Shared.targets`, not via the csproj.

## Consumers

Currently using FrostyFun.Shared (look for `<Import Project="..\FrostyFun.Shared\FrostyFun.Shared.targets" />` in the .csproj):
- RespawnFlags
- RacePicker

YetiHunt, CharacterSelect, and others have local copies of similar utilities; migrating them to Shared is a follow-up.
