using System.Reflection;
using Il2CppDissonance;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace PushToTalk;

public class PushToTalkMod : MelonMod
{
    private bool _inGame;
    private DissonanceComms _dissonanceComms;
    private Toggle _muteToggle;

    public override void OnInitializeMelon()
    {
        Melon<PushToTalkMod>.Logger.Msg("PushToTalk initialized");
        Melon<PushToTalkMod>.Logger.Msg("  V (hold) = Push-to-talk (unmutes while held)");
        Melon<PushToTalkMod>.Logger.Msg("  F9 = Dump Dissonance voice system info");
    }

    public override void OnSceneWasLoaded(int buildIndex, string sceneName)
    {
        _dissonanceComms = null;
        _muteToggle = null;

        var lower = sceneName.ToLower();
        _inGame = !lower.Contains("boot") && !lower.Contains("menu") && !lower.Contains("loading");

        if (_inGame)
            Melon<PushToTalkMod>.Logger.Msg($"Scene: {sceneName} - push-to-talk active");
    }

    public override void OnUpdate()
    {
        if (!_inGame) return;

        if (_dissonanceComms == null)
            TryFindDissonanceComms();

        if (Input.GetKeyDown(KeyCode.F9))
            DumpDissonanceInfo();

        if (_dissonanceComms == null) return;

        // V key - Push-to-talk: unmute while held
        if (Input.GetKeyDown(KeyCode.V))
            SetMuted(false);
        else if (Input.GetKeyUp(KeyCode.V))
            SetMuted(true);
    }

    private void TryFindDissonanceComms()
    {
        try
        {
            _dissonanceComms = Object.FindObjectOfType<DissonanceComms>();
            if (_dissonanceComms != null)
            {
                Melon<PushToTalkMod>.Logger.Msg($"Found DissonanceComms on '{_dissonanceComms.gameObject.name}'");
            }
        }
        catch (Exception ex)
        {
            Melon<PushToTalkMod>.Logger.Error($"Error finding DissonanceComms: {ex.Message}");
        }
    }

    private void TryFindMuteToggle()
    {
        try
        {
            var toggles = Object.FindObjectsOfType<Toggle>(true);
            foreach (var toggle in toggles)
            {
                if (toggle != null && toggle.gameObject.name == "(Toggle) voice on")
                {
                    _muteToggle = toggle;
                    Melon<PushToTalkMod>.Logger.Msg("Found settings voice toggle");
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            Melon<PushToTalkMod>.Logger.Error($"Error finding mute toggle: {ex.Message}");
        }
    }

    private void SetMuted(bool muted)
    {
        if (_dissonanceComms == null) return;

        try
        {
            _dissonanceComms.IsMuted = muted;
            Melon<PushToTalkMod>.Logger.Msg($"Mic {(muted ? "MUTED" : "UNMUTED")}");

            SyncMuteToggle(muted);
        }
        catch (Exception ex)
        {
            Melon<PushToTalkMod>.Logger.Error($"Failed to set mute: {ex.Message}");
        }
    }

    private void SyncMuteToggle(bool muted)
    {
        // Lazily find the toggle
        if (_muteToggle == null)
            TryFindMuteToggle();

        if (_muteToggle == null) return;

        try
        {
            // "voice on" toggle: isOn=true means unmuted, so invert
            _muteToggle.SetIsOnWithoutNotify(!muted);
        }
        catch
        {
            // Toggle may have been destroyed (scene change, menu closed)
            _muteToggle = null;
        }
    }

    private void DumpDissonanceInfo()
    {
        Melon<PushToTalkMod>.Logger.Msg("=== DISSONANCE VOICE SYSTEM DUMP ===");

        // DissonanceComms
        try
        {
            var comms = Object.FindObjectOfType<DissonanceComms>();
            if (comms != null)
            {
                Melon<PushToTalkMod>.Logger.Msg($"DissonanceComms on '{comms.gameObject.name}':");
                Melon<PushToTalkMod>.Logger.Msg($"  IsMuted: {comms.IsMuted}");
                var props = comms.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
                foreach (var prop in props)
                {
                    try
                    {
                        if (!prop.CanRead) continue;
                        Melon<PushToTalkMod>.Logger.Msg($"  [P] {prop.Name} ({prop.PropertyType.Name}) = {prop.GetValue(comms)}");
                    }
                    catch
                    {
                        Melon<PushToTalkMod>.Logger.Msg($"  [P] {prop.Name} ({prop.PropertyType.Name}) = <error>");
                    }
                }
            }
            else
            {
                Melon<PushToTalkMod>.Logger.Warning("DissonanceComms NOT found");
            }
        }
        catch (Exception ex)
        {
            Melon<PushToTalkMod>.Logger.Error($"DissonanceComms error: {ex.Message}");
        }

        // Search for mute-related toggles
        Melon<PushToTalkMod>.Logger.Msg("--- Mute-related Toggles ---");
        try
        {
            var toggles = Object.FindObjectsOfType<Toggle>(true);
            foreach (var toggle in toggles)
            {
                if (toggle == null) continue;
                var name = toggle.gameObject.name.ToLower();
                if (name.Contains("mute") || name.Contains("mic") || name.Contains("voice") || name.Contains("sound"))
                {
                    Melon<PushToTalkMod>.Logger.Msg($"  Toggle: '{toggle.gameObject.name}' isOn={toggle.isOn} interactable={toggle.interactable}");
                }
            }
        }
        catch (Exception ex)
        {
            Melon<PushToTalkMod>.Logger.Error($"Toggle search error: {ex.Message}");
        }

        // Full scene scan for Dissonance components
        Melon<PushToTalkMod>.Logger.Msg("--- Dissonance Components in Scene ---");
        try
        {
            var allObjects = Object.FindObjectsOfType<GameObject>();
            foreach (var go in allObjects)
            {
                if (go == null) continue;
                var components = go.GetComponents<Component>();
                foreach (var comp in components)
                {
                    if (comp == null) continue;
                    var typeName = comp.GetType().FullName;
                    if (typeName != null && typeName.Contains("Dissonance"))
                        Melon<PushToTalkMod>.Logger.Msg($"  '{go.name}' -> {typeName}");
                }
            }
        }
        catch (Exception ex)
        {
            Melon<PushToTalkMod>.Logger.Error($"Scene scan error: {ex.Message}");
        }

        Melon<PushToTalkMod>.Logger.Msg("=== END DUMP ===");
    }
}
