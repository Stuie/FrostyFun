using MelonLoader;
using UnityEngine;

namespace TestMod;

/// <summary>
/// Minimal test mod to verify MelonLoader is working correctly.
/// This mod serves no gameplay purpose - it only exists to confirm that:
/// 1. MelonLoader is installed and loading mods
/// 2. The mod DLL is being picked up from the Mods folder
/// 3. Basic MelonMod lifecycle methods are being called
///
/// If you see "TestMod initialized!" in the MelonLoader console, mods are loading correctly.
/// </summary>
public class TestModMain : MelonMod
{
    public override void OnInitializeMelon()
    {
        Melon<TestModMain>.Logger.Msg("TestMod initialized! MelonLoader is working correctly.");
        Melon<TestModMain>.Logger.Msg("This is a minimal test mod with no gameplay features.");
    }

    public override void OnSceneWasLoaded(int buildIndex, string sceneName)
    {
        Melon<TestModMain>.Logger.Msg($"Scene loaded: {sceneName}");
    }
}
