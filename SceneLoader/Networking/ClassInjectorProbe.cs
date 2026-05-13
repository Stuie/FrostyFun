using System;
using System.Reflection;
using Il2CppInterop.Runtime.Injection;
using MelonLoader;
using UnityEngine;

namespace SceneLoader.Networking
{
    /// <summary>
    /// Phase A5 research spike. The plan is to determine whether we can:
    ///   1. Register a managed C# subclass of FishNet's NetworkBehaviour
    ///      with Il2Cpp's class registry via ClassInjector.
    ///   2. AddComponent that subclass to a runtime-created GameObject.
    ///   3. AddComponent FishNet's NetworkObject (existing Il2Cpp class) too.
    ///   4. Have FishNet's ServerManager.Spawn() accept the resulting object.
    ///
    /// Each step is wrapped in try/catch with full exception logging so we
    /// can see exactly which step (if any) breaks. Output drives the Phase B
    /// branch decision: success ⇒ proper custom Train NetworkBehaviour,
    /// failure ⇒ deterministic-only train sync.
    ///
    /// Run this probe ON DEMAND from the diagnostic hotkey, NOT in
    /// OnInitializeMelon — touching FishNet too early may upset the game's
    /// own startup. Only safe to call once the player is in a lobby (so
    /// NetworkManager exists).
    /// </summary>
    public static class ClassInjectorProbe
    {
        // Test stub - a trivial NetworkBehaviour subclass we'll attempt to
        // register and instantiate. Empty body to minimise risk of the
        // injector tripping over any of our methods.
        public class TestNetBehaviour : Il2CppFishNet.Object.NetworkBehaviour
        {
            // Required IL2CPP-injected types need a ptr-constructor.
            public TestNetBehaviour(IntPtr ptr) : base(ptr) { }
        }

        private static bool _typeRegistered = false;

        public static void Run(MelonLogger.Instance logger)
        {
            logger.Msg("--- ClassInjector Probe ---");

            // Step 1: register our managed type with Il2Cpp.
            bool step1Ok = TryStep(logger, "1. RegisterTypeInIl2Cpp<TestNetBehaviour>", () =>
            {
                if (!_typeRegistered)
                {
                    ClassInjector.RegisterTypeInIl2Cpp<TestNetBehaviour>();
                    _typeRegistered = true;
                }
                logger.Msg("    OK (already registered or just registered)");
            });
            if (!step1Ok) { logger.Msg("    Step 1 failed → custom NetworkBehaviour path is BLOCKED. Stopping."); return; }

            // Probe GameObject + cleanup tracker.
            GameObject probeGO = null;
            try
            {
                probeGO = new GameObject("__SceneLoader_Probe");
                logger.Msg($"    Created probe GameObject (instanceId={probeGO.GetInstanceID()})");
            }
            catch (Exception ex)
            {
                logger.Warning($"    Could not create probe GameObject: {ex.Message}");
                return;
            }

            try
            {
                // Step 2: AddComponent the injected NetworkBehaviour subclass.
                TestNetBehaviour testComp = null;
                bool step2Ok = TryStep(logger, "2. AddComponent<TestNetBehaviour>", () =>
                {
                    testComp = probeGO.AddComponent<TestNetBehaviour>();
                    logger.Msg(testComp != null ? "    OK" : "    AddComponent returned null");
                });
                if (!step2Ok || testComp == null) { logger.Msg("    Step 2 failed → custom NetworkBehaviour path is BLOCKED."); return; }

                // Step 3: AddComponent the existing FishNet NetworkObject (Il2Cpp class).
                Il2CppFishNet.Object.NetworkObject netObj = null;
                bool step3Ok = TryStep(logger, "3. AddComponent<NetworkObject>", () =>
                {
                    netObj = probeGO.AddComponent<Il2CppFishNet.Object.NetworkObject>();
                    logger.Msg(netObj != null ? "    OK" : "    AddComponent returned null");
                });
                if (!step3Ok || netObj == null) { logger.Msg("    Step 3 failed → standard NetworkObject path is BLOCKED."); return; }

                // Step 4: try to spawn via NetworkManager.ServerManager. This
                // can only work on the server/host; on a client connection it
                // is expected to refuse, which is informative either way.
                TryStep(logger, "4. NetworkManager.ServerManager.Spawn", () =>
                {
                    var nm = FindNetworkManager(logger);
                    if (nm == null) { logger.Warning("    NetworkManager not found — must be in a lobby to test Spawn."); return; }

                    bool isServer = TryGetBoolProperty(nm, "IsServer") ?? false;
                    bool isHost   = TryGetBoolProperty(nm, "IsHost")   ?? false;
                    logger.Msg($"    NetworkManager: IsServer={isServer} IsHost={isHost}");

                    if (!isServer)
                    {
                        logger.Msg("    Skipping Spawn — not server/host. Step 4 result is inconclusive.");
                        return;
                    }

                    // Reflect ServerManager off NetworkManager, then call Spawn(GameObject).
                    var nmType = nm.GetType();
                    var smProp = nmType.GetProperty("ServerManager");
                    var sm = smProp?.GetValue(nm);
                    if (sm == null) { logger.Warning("    ServerManager property unavailable"); return; }

                    var spawnMethods = sm.GetType().GetMethods();
                    MethodInfo spawnGO = null;
                    foreach (var m in spawnMethods)
                    {
                        if (m.Name != "Spawn") continue;
                        var ps = m.GetParameters();
                        if (ps.Length >= 1 && ps[0].ParameterType.Name == "GameObject")
                        {
                            spawnGO = m;
                            break;
                        }
                    }
                    if (spawnGO == null) { logger.Warning("    No Spawn(GameObject ...) overload found"); return; }

                    var args = new object[spawnGO.GetParameters().Length];
                    args[0] = probeGO;
                    spawnGO.Invoke(sm, args);
                    logger.Msg("    Spawn() invoked without throwing — check log for FishNet messages");
                });
            }
            finally
            {
                if (probeGO != null) UnityEngine.Object.Destroy(probeGO);
            }

            logger.Msg("--- ClassInjector Probe complete ---");
        }

        private static bool TryStep(MelonLogger.Instance logger, string label, Action action)
        {
            logger.Msg($"  Step {label}");
            try { action(); return true; }
            catch (Exception ex)
            {
                logger.Warning($"    THREW {ex.GetType().Name}: {ex.Message}");
                if (ex.InnerException != null)
                    logger.Warning($"    inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
                return false;
            }
        }

        private static object FindNetworkManager(MelonLogger.Instance logger)
        {
            try
            {
                var allBehaviours = UnityEngine.Object.FindObjectsOfType<Behaviour>();
                foreach (var b in allBehaviours)
                {
                    if (b == null) continue;
                    string typeName;
                    try { typeName = b.GetIl2CppType()?.Name ?? ""; } catch { continue; }
                    if (typeName != "NetworkManager") continue;

                    // Cast Il2Cpp wrapper to managed type if possible
                    var asm = Assembly.Load("Il2CppFishNet.Runtime");
                    foreach (var t in asm.GetTypes())
                    {
                        if (t.Name == "NetworkManager")
                        {
                            var castMethod = typeof(Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase)
                                .GetMethod("Cast")?.MakeGenericMethod(t);
                            return castMethod?.Invoke(b, null);
                        }
                    }
                    return b;
                }
            }
            catch (Exception ex) { logger.Warning($"    FindNetworkManager: {ex.Message}"); }
            return null;
        }

        private static bool? TryGetBoolProperty(object instance, string name)
        {
            try
            {
                var p = instance.GetType().GetProperty(name);
                var v = p?.GetValue(instance);
                if (v is bool b) return b;
            }
            catch { }
            return null;
        }
    }
}
