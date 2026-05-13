using System;
using System.Reflection;
using MelonLoader;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SceneLoader.Services
{
    public class SceneDiagnostics
    {
        private readonly MelonLogger.Instance _logger;

        public SceneDiagnostics(MelonLogger.Instance logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Dumps measurements useful for sizing a custom scene to match the game world:
        /// boundary extents, lodge bounds, terrain dimensions, ski lift heights, etc.
        /// </summary>
        public void DumpWorldDimensions()
        {
            _logger.Msg("=== WORLD DIMENSIONS DUMP ===");

            DumpBoundaryDimensions();
            DumpObjectBoundsByPath("Lodge", "World/Lodge");
            DumpObjectBoundsByPath("World root", "World");
            DumpObjectBoundsByPath("Terrains root", "Terrains");
            DumpSkiLifts();
            DumpBenches();
            DumpSleds();
            DumpFishNetState();
            SceneLoader.Networking.ClassInjectorProbe.Run(_logger);
            DumpSpawnPointsRoot();
            DumpPlayerPosition();
            DumpSledZoneSearch();
            DumpSafeZones();
            DumpLodgeInteractables();

            _logger.Msg("=== END WORLD DIMENSIONS ===");
        }

        private void DumpBoundaryDimensions()
        {
            _logger.Msg("--- Boundary ---");
            try
            {
                var allBehaviours = UnityEngine.Object.FindObjectsOfType<Behaviour>();
                int found = 0;
                foreach (var b in allBehaviours)
                {
                    if (b == null) continue;
                    string typeName;
                    try { typeName = b.GetIl2CppType()?.Name ?? ""; } catch { continue; }
                    if (typeName != "MapBoundaryController") continue;
                    found++;

                    _logger.Msg($"  MapBoundaryController on \"{b.gameObject.name}\"");
                    _logger.Msg($"    Position: {b.transform.position}");
                    _logger.Msg($"    Active: {b.isActiveAndEnabled}");

                    // Combined collider bounds for this boundary controller (all descendants)
                    var colliders = b.gameObject.GetComponentsInChildren<Collider>(true);
                    if (colliders.Length > 0)
                    {
                        Bounds combined = colliders[0].bounds;
                        bool first = true;
                        int triggerCount = 0, solidCount = 0;
                        foreach (var col in colliders)
                        {
                            if (col == null) continue;
                            if (first) { combined = col.bounds; first = false; }
                            else combined.Encapsulate(col.bounds);
                            if (col.isTrigger) triggerCount++; else solidCount++;
                        }
                        _logger.Msg($"    Colliders: {colliders.Length} ({triggerCount} triggers, {solidCount} solid)");
                        _logger.Msg($"    Combined bounds: center={combined.center} size={combined.size}");
                        _logger.Msg($"      X span: {combined.min.x:F1} -> {combined.max.x:F1}  (width {combined.size.x:F1})");
                        _logger.Msg($"      Y span: {combined.min.y:F1} -> {combined.max.y:F1}  (height {combined.size.y:F1})");
                        _logger.Msg($"      Z span: {combined.min.z:F1} -> {combined.max.z:F1}  (depth {combined.size.z:F1})");
                    }
                    else
                    {
                        _logger.Msg("    No colliders on boundary or descendants");
                    }

                    // Dump fields/properties via .NET reflection - the boundary is
                    // probably defined logically (radius, center, etc.), not by colliders
                    DumpBoundaryFieldsAndProperties(b);
                }
                if (found == 0)
                    _logger.Msg("  No MapBoundaryController instances found");
            }
            catch (Exception ex)
            {
                _logger.Warning($"  Boundary scan failed: {ex.Message}");
            }
        }

        private void DumpObjectBoundsByPath(string label, string path)
        {
            _logger.Msg($"--- {label} ---");
            var obj = GameObject.Find(path);
            if (obj == null)
            {
                _logger.Msg($"  Not found at \"{path}\"");
                return;
            }

            _logger.Msg($"  Position: {obj.transform.position}");
            var renderers = obj.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                _logger.Msg("  No renderers found");
                return;
            }

            Bounds combined = renderers[0].bounds;
            foreach (var r in renderers)
            {
                if (r != null) combined.Encapsulate(r.bounds);
            }
            _logger.Msg($"  Renderers: {renderers.Length}");
            _logger.Msg($"  Combined bounds: center={combined.center} size={combined.size}");
            _logger.Msg($"    X span: {combined.min.x:F1} -> {combined.max.x:F1}  (width {combined.size.x:F1})");
            _logger.Msg($"    Y span: {combined.min.y:F1} -> {combined.max.y:F1}  (height {combined.size.y:F1})");
            _logger.Msg($"    Z span: {combined.min.z:F1} -> {combined.max.z:F1}  (depth {combined.size.z:F1})");
        }

        private void DumpBoundaryFieldsAndProperties(Behaviour comp)
        {
            try
            {
                // Use the Il2Cpp type to find the actual MapBoundaryController C# type via Assembly-CSharp
                var assembly = Assembly.Load("Assembly-CSharp");
                Type t = null;
                foreach (var type in assembly.GetTypes())
                {
                    if (type.Name == "MapBoundaryController") { t = type; break; }
                }
                if (t == null)
                {
                    _logger.Msg("    (managed MapBoundaryController type not resolved for reflection)");
                    return;
                }

                // Cast the component to the resolved Il2Cpp type
                var castMethod = typeof(Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase).GetMethod("Cast")
                    .MakeGenericMethod(t);
                var typedInstance = castMethod.Invoke(comp, null);

                _logger.Msg("    Fields/Properties:");
                var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
                foreach (var f in t.GetFields(flags))
                {
                    if (f.Name.Contains("k__") || f.Name.StartsWith("_")) continue;
                    try
                    {
                        var val = f.GetValue(typedInstance);
                        _logger.Msg($"      {f.FieldType.Name} {f.Name} = {val}");

                        // Special case: enumerate boundary points (the polygon vertices)
                        if (f.Name == "mapBoundaryPoints" && val != null)
                        {
                            DumpBoundaryPoints(val);
                        }
                        // Also enumerate the parent Transform's children (alternate location)
                        if (f.Name == "mapBoundaryPointsParent" && val is Transform parentTransform)
                        {
                            DumpBoundaryPointsFromParent(parentTransform);
                        }
                    }
                    catch { }
                }
                foreach (var p in t.GetProperties(flags))
                {
                    if (!p.CanRead) continue;
                    if (p.Name.Contains("RpcWriter") || p.Name.Contains("RpcReader")) continue;
                    try
                    {
                        var val = p.GetValue(typedInstance);
                        _logger.Msg($"      {p.PropertyType.Name} {p.Name} = {val}");
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"    Reflection dump failed: {ex.Message}");
            }
        }

        private void DumpBoundaryPoints(object pointsArray)
        {
            try
            {
                // It's an Il2CppReferenceArray<Transform>. Use reflection to iterate.
                var arrayType = pointsArray.GetType();
                var lengthProp = arrayType.GetProperty("Length");
                if (lengthProp == null) return;
                int length = (int)lengthProp.GetValue(pointsArray);
                _logger.Msg($"      [{length} boundary points]:");
                var indexer = arrayType.GetMethod("get_Item", new[] { typeof(int) });
                if (indexer == null) return;
                float minX = float.MaxValue, maxX = float.MinValue;
                float minZ = float.MaxValue, maxZ = float.MinValue;
                for (int i = 0; i < length; i++)
                {
                    var transform = indexer.Invoke(pointsArray, new object[] { i }) as Transform;
                    if (transform == null) continue;
                    var p = transform.position;
                    if (p.x < minX) minX = p.x;
                    if (p.x > maxX) maxX = p.x;
                    if (p.z < minZ) minZ = p.z;
                    if (p.z > maxZ) maxZ = p.z;
                    if (i < 24)
                        _logger.Msg($"        [{i}] {p}");
                }
                if (length > 24)
                    _logger.Msg($"        ... ({length - 24} more boundary points omitted)");
                _logger.Msg($"      Boundary X span: {minX:F1} -> {maxX:F1}  (width {maxX - minX:F1})");
                _logger.Msg($"      Boundary Z span: {minZ:F1} -> {maxZ:F1}  (depth {maxZ - minZ:F1})");
            }
            catch (Exception ex)
            {
                _logger.Warning($"      Boundary points enumeration failed: {ex.Message}");
            }
        }

        private void DumpBoundaryPointsFromParent(Transform parent)
        {
            try
            {
                int count = parent.childCount;
                _logger.Msg($"      mapBoundaryPointsParent has {count} children:");
                float minX = float.MaxValue, maxX = float.MinValue;
                float minZ = float.MaxValue, maxZ = float.MinValue;
                for (int i = 0; i < count; i++)
                {
                    var child = parent.GetChild(i);
                    var p = child.position;
                    if (p.x < minX) minX = p.x;
                    if (p.x > maxX) maxX = p.x;
                    if (p.z < minZ) minZ = p.z;
                    if (p.z > maxZ) maxZ = p.z;
                    if (i < 24)
                        _logger.Msg($"        [{i}] \"{child.name}\" at {p}");
                }
                if (count > 24)
                    _logger.Msg($"        ... ({count - 24} more)");
                _logger.Msg($"      Boundary X span: {minX:F1} -> {maxX:F1}  (width {maxX - minX:F1})");
                _logger.Msg($"      Boundary Z span: {minZ:F1} -> {maxZ:F1}  (depth {maxZ - minZ:F1})");
            }
            catch (Exception ex)
            {
                _logger.Warning($"      Boundary parent enumeration failed: {ex.Message}");
            }
        }

        private void DumpSkiLifts()
        {
            _logger.Msg("--- Ski Lifts ---");
            try
            {
                var allObjs = UnityEngine.Object.FindObjectsOfType<GameObject>();
                int chairCount = 0;
                float minY = float.MaxValue, maxY = float.MinValue;
                float minX = float.MaxValue, maxX = float.MinValue;
                float minZ = float.MaxValue, maxZ = float.MinValue;

                Vector3 playerPos = GetPlayerPositionOrZero();
                GameObject closestChair = null;
                float closestDist = float.MaxValue;

                foreach (var obj in allObjs)
                {
                    if (obj == null) continue;
                    if (!obj.name.Contains("Ski Lift Chair")) continue;
                    chairCount++;
                    var p = obj.transform.position;
                    if (p.y < minY) minY = p.y;
                    if (p.y > maxY) maxY = p.y;
                    if (p.x < minX) minX = p.x;
                    if (p.x > maxX) maxX = p.x;
                    if (p.z < minZ) minZ = p.z;
                    if (p.z > maxZ) maxZ = p.z;
                    float d = Vector3.Distance(p, playerPos);
                    if (d < closestDist) { closestDist = d; closestChair = obj; }
                }
                if (chairCount == 0)
                {
                    _logger.Msg("  No \"Ski Lift Chair\" objects found");
                }
                else
                {
                    _logger.Msg($"  Chair count: {chairCount}");
                    _logger.Msg($"  Y range: {minY:F1} -> {maxY:F1}  (elevation gain {maxY - minY:F1})");
                    _logger.Msg($"  X range: {minX:F1} -> {maxX:F1}  (run length X {maxX - minX:F1})");
                    _logger.Msg($"  Z range: {minZ:F1} -> {maxZ:F1}  (run length Z {maxZ - minZ:F1})");
                }

                // Deep-dump the closest chair so the player can stand near one and
                // press Shift+F4 to capture the exact components/fields involved
                // in the interaction.
                if (closestChair != null)
                {
                    _logger.Msg($"  --- Closest chair to player ({closestDist:F1}m): \"{GetHierarchyPath(closestChair.transform)}\"");
                    DumpInteractableTarget(closestChair, includeParents: true, childDepth: 2);
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"  Ski lift scan failed: {ex.Message}");
            }

            DumpInteractionSystemTypes();
            DumpNearestInteractables();
        }

        private Vector3 GetPlayerPositionOrZero()
        {
            var player = GameObject.Find("Player Networked(Clone)");
            return player != null ? player.transform.position : Vector3.zero;
        }

        /// <summary>
        /// Dumps full component listing, parent chain, child subtree, fields,
        /// and trigger colliders for a target GameObject. Used to reverse-engineer
        /// rideable interactables (ski lift chairs, sled benches, etc).
        /// </summary>
        private void DumpInteractableTarget(GameObject go, bool includeParents, int childDepth)
        {
            if (go == null) return;

            // Parent chain — the lift cable / system manager probably lives on a parent
            if (includeParents)
            {
                _logger.Msg("    Parent chain:");
                var t = go.transform.parent;
                int depth = 0;
                while (t != null && depth < 6)
                {
                    string compStr = ListComponentTypeNames(t.gameObject);
                    _logger.Msg($"      [{depth}] \"{t.name}\" components=[{compStr}]");
                    t = t.parent;
                    depth++;
                }
            }

            // Self components + fields
            _logger.Msg("    Self components:");
            DumpComponentsOn(go, indent: "      ");

            // Children (recurse)
            if (childDepth > 0)
            {
                _logger.Msg("    Child subtree:");
                DumpChildrenRecursive(go.transform, childDepth, indent: "      ");
            }

            // Colliders (anywhere in subtree) — interaction triggers are typically here
            _logger.Msg("    Colliders in subtree (interaction zones often have isTrigger=true):");
            var cols = go.GetComponentsInChildren<Collider>(true);
            int colCount = 0;
            foreach (var c in cols)
            {
                if (c == null) continue;
                _logger.Msg($"      {c.GetType().Name} on \"{c.gameObject.name}\" trigger={c.isTrigger} bounds={c.bounds.size} layer={LayerMask.LayerToName(c.gameObject.layer)}({c.gameObject.layer})");
                colCount++;
                if (colCount >= 12) { _logger.Msg("      ... (cap)"); break; }
            }
        }

        private void DumpComponentsOn(GameObject go, string indent)
        {
            var comps = go.GetComponents<Component>();
            foreach (var comp in comps)
            {
                if (comp == null) continue;
                string typeName;
                Type managedType = null;
                try
                {
                    var il2cppType = comp.GetIl2CppType();
                    typeName = il2cppType?.FullName ?? comp.GetType().FullName;
                    managedType = ResolveManagedType(il2cppType?.Name);
                }
                catch { typeName = comp.GetType().FullName; }

                _logger.Msg($"{indent}{typeName}");

                // Try reflecting on the managed C# type if we found one
                if (managedType != null)
                {
                    try
                    {
                        var castMethod = typeof(Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase).GetMethod("Cast")
                            ?.MakeGenericMethod(managedType);
                        var typed = castMethod?.Invoke(comp, null);
                        if (typed != null)
                        {
                            DumpInstanceState(managedType, typed, indent + "  ");
                        }
                    }
                    catch { }
                }
            }
        }

        // Boring Il2CppObjectBase / Component / MonoBehaviour members that show up
        // on every component and add no signal — filter them out so the actual
        // game state stands out.
        private static readonly System.Collections.Generic.HashSet<string> BoringMemberNames = new()
        {
            "isWrapped", "pooledPtr", "m_CachedPtr", "Pointer", "ObjectClass", "WasCollected",
            "transform", "gameObject", "tag", "name", "hideFlags", "useGUILayout",
            "enabled", "isActiveAndEnabled", "didStart", "didAwake",
            "transformHandle", "destroyCancellationToken", "m_CancellationTokenSource",
        };

        /// <summary>
        /// Dumps fields AND properties on an Il2Cpp wrapper. Il2Cpp game state
        /// is usually exposed via properties (the wrapper calls into native
        /// Il2Cpp to read backing fields), not C# fields, so we have to
        /// reflect over both.
        /// </summary>
        private void DumpInstanceState(Type managedType, object typed, string indent)
        {
            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            int count = 0;

            foreach (var f in managedType.GetFields(flags))
            {
                if (count >= 30) { _logger.Msg($"{indent}... (member cap)"); return; }
                string n = f.Name;
                if (n.Contains("k__") || n.StartsWith("<")) continue;
                if (BoringMemberNames.Contains(n)) continue;
                try
                {
                    object v = f.GetValue(typed);
                    string vs = FormatValue(v);
                    _logger.Msg($"{indent}{f.FieldType.Name} {n} = {vs}");
                    count++;
                }
                catch { }
            }

            foreach (var p in managedType.GetProperties(flags))
            {
                if (count >= 30) { _logger.Msg($"{indent}... (member cap)"); return; }
                string n = p.Name;
                if (n.Contains("k__") || n.StartsWith("<")) continue;
                if (BoringMemberNames.Contains(n)) continue;
                if (!p.CanRead) continue;
                if (p.GetIndexParameters().Length > 0) continue;
                try
                {
                    object v = p.GetValue(typed);
                    string vs = FormatValue(v);
                    _logger.Msg($"{indent}{p.PropertyType.Name} {n} (prop) = {vs}");
                    count++;
                }
                catch { }
            }
        }

        private static string FormatValue(object v)
        {
            if (v == null) return "null";
            string s = v.ToString();
            if (string.IsNullOrEmpty(s)) return "(empty)";
            if (s.Length > 100) s = s.Substring(0, 97) + "...";
            return s;
        }

        private static Type ResolveManagedType(string simpleName)
        {
            if (string.IsNullOrEmpty(simpleName)) return null;
            try
            {
                var asm = Assembly.Load("Assembly-CSharp");
                foreach (var t in asm.GetTypes())
                    if (t.Name == simpleName) return t;
            }
            catch { }
            return null;
        }

        private static string ListComponentTypeNames(GameObject go)
        {
            var comps = go.GetComponents<Component>();
            string s = "";
            foreach (var c in comps)
            {
                if (c == null) continue;
                try { s += c.GetIl2CppType()?.Name + " "; }
                catch { s += c.GetType().Name + " "; }
            }
            return s.TrimEnd();
        }

        private void DumpChildrenRecursive(Transform t, int depth, string indent)
        {
            int count = t.childCount;
            for (int i = 0; i < count; i++)
            {
                var child = t.GetChild(i);
                string compStr = ListComponentTypeNames(child.gameObject);
                _logger.Msg($"{indent}\"{child.name}\" [{compStr}]");
                if (depth > 1)
                    DumpChildrenRecursive(child, depth - 1, indent + "  ");
            }
        }

        /// <summary>
        /// Searches Assembly-CSharp for types whose names contain interaction-related
        /// keywords. The point is to discover what implements the F prompt / mounting.
        /// </summary>
        private void DumpInteractionSystemTypes()
        {
            _logger.Msg("--- Interaction System Types ---");
            try
            {
                var asm = Assembly.Load("Assembly-CSharp");
                var keywords = new[] {
                    "Lift", "Chair", "Bench", "Mount", "Ride", "Rider", "Seat",
                    "Vehicle", "Carry", "Carrier", "Cable",
                    "Interact", "Interactable", "Prompt", "InputPrompt",
                    "Hint", "ContextAction", "UseAction"
                };
                int total = 0;
                foreach (var t in asm.GetTypes())
                {
                    if (t == null) continue;
                    string n = t.Name;
                    if (n.Contains("d__") || n.StartsWith("<")) continue;
                    foreach (var k in keywords)
                    {
                        if (n.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            _logger.Msg($"  {t.FullName}  (base: {t.BaseType?.Name})");
                            total++;
                            break;
                        }
                    }
                    if (total >= 80) { _logger.Msg("  ... (type cap)"); break; }
                }
                _logger.Msg($"  ({total} interaction-related types listed)");
            }
            catch (Exception ex)
            {
                _logger.Warning($"  Interaction type scan failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Lists every Behaviour in the scene whose Il2Cpp type name matches an
        /// interaction-related keyword, plus its hierarchy path. This catches the
        /// runtime instances of whatever drives the F-prompt (chair, bench, etc).
        /// </summary>
        private void DumpNearestInteractables()
        {
            _logger.Msg("--- Nearby Interactable Components (within 30m of player) ---");
            try
            {
                Vector3 playerPos = GetPlayerPositionOrZero();
                var allBehaviours = UnityEngine.Object.FindObjectsOfType<Behaviour>();
                var keywords = new[] {
                    "Lift", "Chair", "Bench", "Mount", "Ride", "Seat",
                    "Vehicle", "Carry", "Cable",
                    "Interact", "Interactable", "Prompt", "Hint"
                };
                int count = 0;
                foreach (var b in allBehaviours)
                {
                    if (b == null) continue;
                    string typeName;
                    try { typeName = b.GetIl2CppType()?.Name ?? ""; } catch { continue; }
                    if (string.IsNullOrEmpty(typeName)) continue;
                    bool matched = false;
                    foreach (var k in keywords)
                    {
                        if (typeName.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0)
                        { matched = true; break; }
                    }
                    if (!matched) continue;
                    float d = Vector3.Distance(b.transform.position, playerPos);
                    if (d > 30f) continue;
                    _logger.Msg($"  [{d:F1}m] {typeName} on \"{GetHierarchyPath(b.transform)}\" enabled={b.isActiveAndEnabled}");
                    count++;
                    if (count >= 30) { _logger.Msg("  ... (cap)"); break; }
                }
                if (count == 0) _logger.Msg("  (none within 30m — try standing closer to a ski lift or sled bench)");
            }
            catch (Exception ex)
            {
                _logger.Warning($"  Nearby interactable scan failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Phase A1: scans for any Bench instance in the scene. Benches are
        /// candidate seat-providers for our trains because Bench : Seat is
        /// simpler than Sled (no SledChange/sled-physics overhead).
        /// </summary>
        private void DumpBenches()
        {
            _logger.Msg("--- Benches ---");
            try
            {
                var allBehaviours = UnityEngine.Object.FindObjectsOfType<Behaviour>();
                int benchCount = 0;
                Vector3 playerPos = GetPlayerPositionOrZero();
                Behaviour closestBench = null;
                float closestDist = float.MaxValue;

                foreach (var b in allBehaviours)
                {
                    if (b == null) continue;
                    string typeName;
                    try { typeName = b.GetIl2CppType()?.Name ?? ""; } catch { continue; }
                    if (typeName != "Bench") continue;
                    benchCount++;
                    var path = GetHierarchyPath(b.transform);
                    var p = b.transform.position;
                    int seatChildCount = CountChildSeatPositions(b.transform);
                    _logger.Msg($"  Bench at {p} (seatPositions={seatChildCount}) on \"{path}\"");
                    float d = Vector3.Distance(p, playerPos);
                    if (d < closestDist) { closestDist = d; closestBench = b; }
                }
                if (benchCount == 0)
                {
                    _logger.Msg("  No Bench components found in scene.");

                    // Also scan GameObject names as a fallback (in case the
                    // component type detection misses anything).
                    _logger.Msg("  Fallback: GameObjects with 'bench' in name:");
                    var allObjs = UnityEngine.Object.FindObjectsOfType<GameObject>();
                    int matched = 0;
                    foreach (var o in allObjs)
                    {
                        if (o == null) continue;
                        if (o.name.IndexOf("bench", StringComparison.OrdinalIgnoreCase) < 0) continue;
                        _logger.Msg($"    \"{GetHierarchyPath(o.transform)}\" at {o.transform.position} components=[{ListComponentTypeNames(o)}]");
                        matched++;
                        if (matched >= 20) { _logger.Msg("    ... (cap)"); break; }
                    }
                    return;
                }

                _logger.Msg($"  Total benches: {benchCount}");
                if (closestBench != null)
                {
                    _logger.Msg($"  --- Closest bench ({closestDist:F1}m) deep-dump ---");
                    DumpInteractableTarget(closestBench.gameObject, includeParents: true, childDepth: 3);
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"  Bench scan failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Phase A3: deep-dumps the closest Sled so we can confirm how
        /// SledSeat tracks its multi-passenger state.
        /// </summary>
        private void DumpSleds()
        {
            _logger.Msg("--- Sleds ---");
            try
            {
                var allObjs = UnityEngine.Object.FindObjectsOfType<GameObject>();
                Vector3 playerPos = GetPlayerPositionOrZero();
                GameObject closest = null;
                float closestDist = float.MaxValue;
                int sledCount = 0;

                foreach (var o in allObjs)
                {
                    if (o == null) continue;
                    if (!o.name.StartsWith("Sled(")) continue;
                    sledCount++;
                    float d = Vector3.Distance(o.transform.position, playerPos);
                    if (d < closestDist) { closestDist = d; closest = o; }
                }

                if (sledCount == 0) { _logger.Msg("  No Sled instances in scene"); return; }
                _logger.Msg($"  Total sleds: {sledCount}");
                if (closest != null)
                {
                    _logger.Msg($"  --- Closest sled ({closestDist:F1}m) deep-dump ---");
                    DumpInteractableTarget(closest, includeParents: true, childDepth: 4);
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"  Sled scan failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Phase A4: enumerates the FishNet NetworkManager so we can see
        /// what's registered, what authority this client has, and whether
        /// runtime prefab registration is exposed.
        /// </summary>
        private void DumpFishNetState()
        {
            _logger.Msg("--- FishNet State ---");
            try
            {
                Behaviour networkManager = null;
                string nmTypeName = null;
                var allBehaviours = UnityEngine.Object.FindObjectsOfType<Behaviour>();
                foreach (var b in allBehaviours)
                {
                    if (b == null) continue;
                    string typeName;
                    try { typeName = b.GetIl2CppType()?.Name ?? ""; } catch { continue; }
                    if (typeName == "NetworkManager")
                    {
                        networkManager = b;
                        nmTypeName = typeName;
                        break;
                    }
                }

                if (networkManager == null)
                {
                    _logger.Msg("  NetworkManager not found");
                    return;
                }

                _logger.Msg($"  NetworkManager on \"{GetHierarchyPath(networkManager.transform)}\"");
                Type managedType = ResolveManagedType(nmTypeName);
                if (managedType != null)
                {
                    var castMethod = typeof(Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase).GetMethod("Cast")
                        ?.MakeGenericMethod(managedType);
                    var typed = castMethod?.Invoke(networkManager, null);
                    if (typed != null)
                    {
                        _logger.Msg("  NetworkManager state:");
                        DumpInstanceState(managedType, typed, "    ");
                    }
                }

                // Drill into ServerManager / ClientManager / SpawnablePrefabs
                var children = networkManager.GetComponentsInChildren<Behaviour>(true);
                foreach (var c in children)
                {
                    if (c == null) continue;
                    string tn;
                    try { tn = c.GetIl2CppType()?.Name ?? ""; } catch { continue; }
                    if (tn == "ServerManager" || tn == "ClientManager" || tn == "PredictionManager" ||
                        tn == "TransportManager" || tn == "SceneManager")
                    {
                        _logger.Msg($"  {tn} on \"{c.gameObject.name}\":");
                        var mt = ResolveManagedType(tn);
                        if (mt != null)
                        {
                            var cast = typeof(Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase).GetMethod("Cast")
                                ?.MakeGenericMethod(mt);
                            var t2 = cast?.Invoke(c, null);
                            if (t2 != null) DumpInstanceState(mt, t2, "    ");
                        }
                    }
                }

                // Check for any "PrefabObjects" / "DefaultPrefabObjects" referenced anywhere
                _logger.Msg("  Searching for *PrefabObjects ScriptableObjects in scene:");
                var asm = Assembly.Load("Assembly-CSharp");
                Type prefabObjType = null;
                foreach (var t in asm.GetTypes())
                {
                    if (t.Name == "PrefabObjects" || t.Name == "DefaultPrefabObjects" || t.Name == "SinglePrefabObjects")
                    {
                        _logger.Msg($"    Type: {t.FullName} (base: {t.BaseType?.Name})");
                        prefabObjType = t;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"  FishNet introspection failed: {ex.Message}");
            }
        }

        private static int CountChildSeatPositions(Transform t)
        {
            int count = 0;
            int n = t.childCount;
            for (int i = 0; i < n; i++)
            {
                var ch = t.GetChild(i);
                var comps = ch.GetComponents<Component>();
                foreach (var c in comps)
                {
                    if (c == null) continue;
                    string tn;
                    try { tn = c.GetIl2CppType()?.Name ?? ""; } catch { continue; }
                    if (tn == "SeatPosition") { count++; break; }
                }
            }
            return count;
        }

        private void DumpSpawnPointsRoot()
        {
            _logger.Msg("--- Spawn Points ---");
            var root = GameObject.Find("SPAWN POINTS");
            if (root == null)
            {
                _logger.Msg("  \"SPAWN POINTS\" root not found");
                return;
            }
            int count = root.transform.childCount;
            _logger.Msg($"  Children: {count}");
            float minY = float.MaxValue, maxY = float.MinValue;
            for (int i = 0; i < count; i++)
            {
                var child = root.transform.GetChild(i);
                var p = child.position;
                if (p.y < minY) minY = p.y;
                if (p.y > maxY) maxY = p.y;
                if (i < 12)
                    _logger.Msg($"    [{i}] \"{child.name}\" at {p}");
            }
            if (count > 0)
                _logger.Msg($"  Y range across all spawn points: {minY:F1} -> {maxY:F1}  (delta {maxY - minY:F1})");
            if (count > 12)
                _logger.Msg($"    ... and {count - 12} more (omitted from log)");
        }

        /// <summary>
        /// Searches the scene for anything that could implement the lodge no-sled
        /// zone: types/objects/colliders with names mentioning Sled, Lodge, NoSled,
        /// Restrict, Dismount, Eject, etc.
        /// </summary>
        private void DumpSledZoneSearch()
        {
            _logger.Msg("--- Sled Zone Search ---");
            try
            {
                // 1) Assembly-CSharp types containing relevant keywords
                _logger.Msg("  Types containing sled/lodge/restrict/dismount keywords:");
                var assembly = Assembly.Load("Assembly-CSharp");
                var keywords = new[] { "NoSled", "SledZone", "DismountSled", "EjectSled",
                    "LodgeZone", "LodgeRestrict", "RestrictedZone", "SledRestrict",
                    "ForceDismount", "SafeZone", "SledOff", "OffSled" };
                foreach (var type in assembly.GetTypes())
                {
                    if (type == null || type.Name.StartsWith("_") || type.Name.Contains("d__")) continue;
                    foreach (var k in keywords)
                    {
                        if (type.Name.Contains(k))
                        {
                            _logger.Msg($"    Type: {type.FullName} (base: {type.BaseType?.Name})");
                            break;
                        }
                    }
                }

                // 2) Components in the World/Lodge hierarchy with sled-related types
                _logger.Msg("  Components in 'World/Lodge' subtree with sled-related type names:");
                var lodge = GameObject.Find("World/Lodge");
                if (lodge != null)
                {
                    var allComps = lodge.GetComponentsInChildren<Component>(true);
                    int matched = 0;
                    foreach (var c in allComps)
                    {
                        if (c == null) continue;
                        string typeName;
                        try { typeName = c.GetIl2CppType()?.Name ?? ""; } catch { continue; }
                        string lower = typeName.ToLower();
                        if (lower.Contains("sled") || lower.Contains("dismount") ||
                            lower.Contains("eject") || lower.Contains("restrict") ||
                            lower.Contains("nozone") || lower.Contains("safezone"))
                        {
                            string path = GetHierarchyPath(c.transform);
                            _logger.Msg($"    {typeName} on \"{path}\" at {c.transform.position}");
                            matched++;
                            if (matched >= 30) { _logger.Msg("    ... (cap reached)"); break; }
                        }
                    }
                    if (matched == 0) _logger.Msg("    (none found in Lodge subtree)");
                }
                else
                {
                    _logger.Msg("    \"World/Lodge\" not found");
                }

                // 3) ALL colliders near the spawn point (within 60m) - these are
                // candidates for the lodge protection zone.
                _logger.Msg("  Colliders within 60m of spawn (35.5, 30.3, 135.4):");
                Vector3 spawn = new Vector3(35.49f, 30.26f, 135.40f);
                var allColliders = UnityEngine.Object.FindObjectsOfType<Collider>();
                int nearCount = 0;
                foreach (var c in allColliders)
                {
                    if (c == null) continue;
                    float d = Vector3.Distance(c.bounds.center, spawn);
                    if (d > 60f) continue;
                    string path = GetHierarchyPath(c.transform);
                    string compTypes = "";
                    foreach (var comp in c.gameObject.GetComponents<Component>())
                    {
                        if (comp == null) continue;
                        try { compTypes += comp.GetIl2CppType()?.Name + " "; } catch { }
                    }
                    _logger.Msg($"    {c.GetType().Name} (trigger={c.isTrigger}) bounds={c.bounds} dist={d:F1}m");
                    _logger.Msg($"      path: {path}");
                    _logger.Msg($"      components: {compTypes}");
                    nearCount++;
                    if (nearCount >= 25) { _logger.Msg("    ... (cap reached)"); break; }
                }
                if (nearCount == 0) _logger.Msg("    (no colliders within 60m of spawn)");
            }
            catch (Exception ex)
            {
                _logger.Warning($"  Sled zone search failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Searches for SafeZone components - the lodge no-sled zone is likely
        /// implemented as one of these. Dumps each instance's fields.
        /// </summary>
        private void DumpSafeZones()
        {
            _logger.Msg("--- SafeZones ---");
            try
            {
                var assembly = Assembly.Load("Assembly-CSharp");
                var allBehaviours = UnityEngine.Object.FindObjectsOfType<Behaviour>();
                var typesByName = new System.Collections.Generic.Dictionary<string, Type>();
                foreach (var t in assembly.GetTypes())
                {
                    if (t.Name == "SafeZone" || t.Name == "SafeZoneManager" || t.Name == "SafeZoneAntiZone")
                        typesByName[t.Name] = t;
                }

                int found = 0;
                foreach (var b in allBehaviours)
                {
                    if (b == null) continue;
                    string typeName;
                    try { typeName = b.GetIl2CppType()?.Name ?? ""; } catch { continue; }
                    if (typeName != "SafeZone" && typeName != "SafeZoneManager" && typeName != "SafeZoneAntiZone")
                        continue;

                    found++;
                    string path = GetHierarchyPath(b.transform);
                    _logger.Msg($"  {typeName} on \"{path}\" at {b.transform.position}");

                    if (typesByName.TryGetValue(typeName, out var managedType))
                    {
                        try
                        {
                            var castMethod = typeof(Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase).GetMethod("Cast")
                                .MakeGenericMethod(managedType);
                            var typedInstance = castMethod.Invoke(b, null);
                            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
                            foreach (var f in managedType.GetFields(flags))
                            {
                                if (f.Name.Contains("k__") || f.Name.StartsWith("_")) continue;
                                try
                                {
                                    var v = f.GetValue(typedInstance);
                                    _logger.Msg($"    {f.FieldType.Name} {f.Name} = {v}");
                                }
                                catch { }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.Warning($"    reflection failed: {ex.Message}");
                        }
                    }

                    // Also list any colliders on this GO
                    var colliders = b.gameObject.GetComponents<Collider>();
                    foreach (var c in colliders)
                    {
                        if (c == null) continue;
                        _logger.Msg($"    Collider: {c.GetType().Name} bounds={c.bounds} isTrigger={c.isTrigger}");
                    }
                }
                if (found == 0) _logger.Msg("  No SafeZone instances found in scene");
            }
            catch (Exception ex)
            {
                _logger.Warning($"  SafeZone scan failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Lists all direct children of World/Lodge, plus any descendants whose
        /// name starts with "(Interact)" or contains "Customization", "Shop", etc.
        /// Used to find the chest, stores, and other functional items for the gazebo.
        /// </summary>
        private void DumpLodgeInteractables()
        {
            _logger.Msg("--- Lodge Interactables ---");
            var lodge = GameObject.Find("World/Lodge");
            if (lodge == null)
            {
                _logger.Msg("  World/Lodge not found");
                return;
            }

            // 1) Top-level direct children of Lodge
            _logger.Msg("  Direct children of World/Lodge:");
            int childCount = lodge.transform.childCount;
            for (int i = 0; i < childCount; i++)
            {
                var child = lodge.transform.GetChild(i);
                _logger.Msg($"    [{i}] \"{child.name}\" at {child.position} active={child.gameObject.activeSelf}");
            }

            // 2) Deep search: anything starting with "(Interact)" or containing functional keywords
            _logger.Msg("  Descendants matching interactable patterns:");
            string[] keywords = { "(Interact)", "Customization", "Shop", "Store", "Chest", "Vending", "Booth", "Counter" };
            var allTransforms = lodge.GetComponentsInChildren<Transform>(true);
            int matched = 0;
            foreach (var t in allTransforms)
            {
                if (t == null) continue;
                bool match = false;
                foreach (var k in keywords)
                {
                    if (t.name.Contains(k)) { match = true; break; }
                }
                if (!match) continue;

                // List the components on this object so we know what makes it functional
                string compTypes = "";
                foreach (var comp in t.gameObject.GetComponents<Component>())
                {
                    if (comp == null) continue;
                    try
                    {
                        string n = comp.GetIl2CppType()?.Name ?? comp.GetType().Name;
                        if (n != "Transform" && n != "MeshFilter" && n != "MeshRenderer")
                            compTypes += n + " ";
                    }
                    catch { }
                }

                _logger.Msg($"    \"{GetHierarchyPath(t)}\" at {t.position}");
                if (!string.IsNullOrEmpty(compTypes))
                    _logger.Msg($"      components: {compTypes}");
                matched++;
                if (matched >= 60)
                {
                    _logger.Msg("    ... (cap reached at 60)");
                    break;
                }
            }
            _logger.Msg($"  ({matched} interactable-like objects found)");

            // 3) Also dump any *Interactable component instances anywhere in scene
            _logger.Msg("  All *Interactable components in scene:");
            var allBehaviours = UnityEngine.Object.FindObjectsOfType<Behaviour>();
            int interactableCount = 0;
            foreach (var b in allBehaviours)
            {
                if (b == null) continue;
                string typeName;
                try { typeName = b.GetIl2CppType()?.Name ?? ""; } catch { continue; }
                if (!typeName.EndsWith("Interactable")) continue;
                _logger.Msg($"    {typeName} on \"{GetHierarchyPath(b.transform)}\" at {b.transform.position}");
                interactableCount++;
                if (interactableCount >= 50) { _logger.Msg("    ... (cap reached at 50)"); break; }
            }
        }

        /// <summary>
        /// Context-aware dump for when we're already in a custom scene. Shows
        /// what's loaded, where the gazebo is, what lodge furniture got moved,
        /// and where the player is relative to all of it.
        /// </summary>
        public void DumpCustomSceneState(PlayerMigration migration)
        {
            _logger.Msg("=== CUSTOM SCENE STATE ===");

            // 1) All loaded scenes
            try
            {
                int sceneCount = SceneManager.sceneCount;
                _logger.Msg($"Loaded scenes: {sceneCount}");
                for (int i = 0; i < sceneCount; i++)
                {
                    var s = SceneManager.GetSceneAt(i);
                    _logger.Msg($"  [{i}] \"{s.name}\" rootCount={s.rootCount} isLoaded={s.isLoaded}");
                }
            }
            catch { }

            // 2) Player position
            DumpPlayerPosition();

            // 3) Gazebo & terrain in the additive bundle scene
            _logger.Msg("--- Bundle scene roots ---");
            try
            {
                int sceneCount = SceneManager.sceneCount;
                for (int i = 0; i < sceneCount; i++)
                {
                    var s = SceneManager.GetSceneAt(i);
                    if (s.name == "Main Mountain Scene" || !s.isLoaded) continue;
                    _logger.Msg($"  Scene \"{s.name}\":");
                    var roots = s.GetRootGameObjects();
                    foreach (var root in roots)
                    {
                        if (root == null) continue;
                        int rendererCount = root.GetComponentsInChildren<Renderer>(true).Length;
                        _logger.Msg($"    \"{root.name}\" at {root.transform.position} renderers={rendererCount} active={root.activeSelf}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Bundle scene dump failed: {ex.Message}");
            }

            // 4) Lodge furniture status - we expect them to be detached as scene roots
            _logger.Msg("--- Lodge furniture ---");
            _logger.Msg($"  Captured count: {migration.CapturedFurnitureCount}");
            // Find the items we know we moved by their distinctive names - they are
            // detached so they're scene roots now.
            string[] knownNames = {
                "(Interact) Inventory Chest (2)",
                "(Interact) Inventory Chest (3)",
                "(Interact) Sled Customization",
                "Shop (sleds)",
                "Shop (hats)",
                "Shop (props)",
            };
            try
            {
                var activeScene = SceneManager.GetActiveScene();
                var roots = activeScene.GetRootGameObjects();
                foreach (var name in knownNames)
                {
                    GameObject found = null;
                    foreach (var r in roots)
                    {
                        if (r != null && r.name == name) { found = r; break; }
                    }
                    if (found != null)
                        _logger.Msg($"  \"{name}\" at {found.transform.position} active={found.activeSelf}");
                    else
                        _logger.Msg($"  \"{name}\" NOT FOUND as active-scene root");
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Furniture status dump failed: {ex.Message}");
            }

            // 5) Cameras and their occlusion state (so we can verify it's still off)
            _logger.Msg("--- Cameras ---");
            try
            {
                var cams = UnityEngine.Object.FindObjectsOfType<Camera>();
                foreach (var c in cams)
                {
                    if (c == null) continue;
                    _logger.Msg($"  \"{c.name}\" at {c.transform.position} useOcclusionCulling={c.useOcclusionCulling}");
                }
            }
            catch { }

            _logger.Msg("=== END CUSTOM SCENE STATE ===");
        }

        private void DumpPlayerPosition()
        {
            _logger.Msg("--- Player ---");
            var player = GameObject.Find("Player Networked(Clone)");
            if (player == null)
            {
                _logger.Msg("  Player not found");
                return;
            }
            _logger.Msg($"  Player position: {player.transform.position}");
        }

        public void DumpAllScenes()
        {
            _logger.Msg("=== SCENE DUMP ===");
            try
            {
                int count = SceneManager.sceneCount;
                _logger.Msg($"Total loaded scenes: {count}");

                for (int i = 0; i < count; i++)
                {
                    var scene = SceneManager.GetSceneAt(i);
                    _logger.Msg($"  [{i}] name=\"{scene.name}\" buildIndex={scene.buildIndex} " +
                                $"isLoaded={scene.isLoaded} rootCount={scene.rootCount}");

                    if (scene.isLoaded)
                    {
                        try
                        {
                            var rootObjects = scene.GetRootGameObjects();
                            foreach (var obj in rootObjects)
                            {
                                if (obj == null) continue;
                                _logger.Msg($"    root: \"{obj.name}\" active={obj.activeSelf}");
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.Warning($"    Could not enumerate root objects: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Scene dump failed: {ex.Message}");
            }
        }

        public void DumpPlayerHierarchy()
        {
            _logger.Msg("=== PLAYER HIERARCHY DUMP ===");

            DumpGameObjectComponents("Player Networked(Clone)");
            DumpGameObjectComponents("Player Input");
            DumpGameObjectComponents("CinemachineCamera (makes parent null on start)");
        }

        public void DumpNetworkState()
        {
            _logger.Msg("=== NETWORK STATE ===");
            try
            {
                var assembly = Assembly.Load("Assembly-CSharp");
                var nmType = FindType(assembly, "FishNet.Managing.NetworkManager");
                if (nmType == null)
                    nmType = FindType(assembly, "NetworkManager");

                if (nmType == null)
                {
                    // Try the FishNet runtime assembly
                    try
                    {
                        var fishNetAssembly = Assembly.Load("FishNet.Runtime");
                        nmType = FindType(fishNetAssembly, "FishNet.Managing.NetworkManager");
                    }
                    catch { }
                }

                if (nmType == null)
                {
                    _logger.Msg("  NetworkManager type not found (trying GameObject search)");
                    // Try finding by GameObject
                    var nmObj = GameObject.Find("NetworkManager");
                    if (nmObj != null)
                    {
                        _logger.Msg($"  Found NetworkManager GameObject: {nmObj.name}");
                        var comps = nmObj.GetComponents<Component>();
                        foreach (var comp in comps)
                        {
                            if (comp == null) continue;
                            string typeName = GetIl2CppTypeName(comp);
                            _logger.Msg($"    Component: {typeName}");

                            // Try to read IsServerStarted, IsClientStarted
                            try
                            {
                                var type = comp.GetIl2CppType();
                                if (type != null && type.Name.Contains("NetworkManager"))
                                {
                                    DumpProperties(comp, type);
                                }
                            }
                            catch { }
                        }
                    }
                    else
                    {
                        _logger.Msg("  NetworkManager GameObject not found either");
                    }
                    return;
                }

                _logger.Msg($"  NetworkManager type: {nmType.FullName}");
            }
            catch (Exception ex)
            {
                _logger.Error($"Network state dump failed: {ex.Message}");
            }
        }

        public void DumpBoundaryInfo()
        {
            _logger.Msg("=== BOUNDARY / OUT-OF-BOUNDS SEARCH ===");

            // Search Assembly-CSharp for types related to boundaries
            try
            {
                var assembly = Assembly.Load("Assembly-CSharp");
                var boundaryKeywords = new[] { "Bound", "Border", "Limit", "OutOf", "Respawn", "Kill", "Teleport", "Reset" };
                foreach (var type in assembly.GetTypes())
                {
                    foreach (var keyword in boundaryKeywords)
                    {
                        if (type.Name.Contains(keyword) && !type.Name.StartsWith("_") &&
                            !type.Name.Contains("d__") && !type.Name.Contains("RpcWriter") &&
                            !type.Name.Contains("RpcReader"))
                        {
                            _logger.Msg($"  Type: {type.Name} (base: {type.BaseType?.Name})");
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Type search failed: {ex.Message}");
            }

            // Search scene for GameObjects with boundary-related names or colliders set as triggers
            _logger.Msg("--- Searching scene for boundary objects ---");
            try
            {
                var allObjects = UnityEngine.Object.FindObjectsOfType<Collider>();
                foreach (var col in allObjects)
                {
                    if (col == null) continue;
                    string objName = col.gameObject.name.ToLower();
                    if (col.isTrigger && (objName.Contains("bound") || objName.Contains("border") ||
                        objName.Contains("kill") || objName.Contains("limit") ||
                        objName.Contains("barrier") || objName.Contains("reset") ||
                        objName.Contains("out") || objName.Contains("fall")))
                    {
                        string path = GetHierarchyPath(col.transform);
                        _logger.Msg($"  Trigger: \"{path}\" bounds={col.bounds} layer={col.gameObject.layer}");

                        // Dump components on this object
                        var comps = col.gameObject.GetComponents<Component>();
                        foreach (var comp in comps)
                        {
                            if (comp == null) continue;
                            _logger.Msg($"    {GetIl2CppTypeName(comp)}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Boundary object search failed: {ex.Message}");
            }

            // Also check "Managers / Handlers" children for boundary managers
            _logger.Msg("--- Managers / Handlers children ---");
            var managers = GameObject.Find("Managers / Handlers");
            if (managers != null)
            {
                int childCount = managers.transform.childCount;
                for (int i = 0; i < childCount; i++)
                {
                    var child = managers.transform.GetChild(i);
                    _logger.Msg($"  [{i}] \"{child.name}\" active={child.gameObject.activeSelf}");
                }
            }

            _logger.Msg("=== END BOUNDARY SEARCH ===");
        }

        private static string GetHierarchyPath(Transform t)
        {
            string path = t.name;
            while (t.parent != null)
            {
                t = t.parent;
                path = t.name + "/" + path;
            }
            return path;
        }

        private void DumpGameObjectComponents(string name)
        {
            _logger.Msg($"--- {name} ---");
            var obj = GameObject.Find(name);
            if (obj == null)
            {
                _logger.Msg("  (not found)");
                return;
            }

            _logger.Msg($"  Position: {obj.transform.position}");
            _logger.Msg($"  Active: {obj.activeSelf}");
            _logger.Msg($"  Scene: {obj.scene.name}");

            // List children
            int childCount = obj.transform.childCount;
            if (childCount > 0)
            {
                _logger.Msg($"  Children ({childCount}):");
                for (int i = 0; i < childCount && i < 20; i++)
                {
                    var child = obj.transform.GetChild(i);
                    _logger.Msg($"    [{i}] \"{child.name}\" active={child.gameObject.activeSelf}");
                }
                if (childCount > 20)
                    _logger.Msg($"    ... and {childCount - 20} more");
            }

            // List components
            var components = obj.GetComponents<Component>();
            _logger.Msg($"  Components ({components.Length}):");
            foreach (var comp in components)
            {
                if (comp == null)
                {
                    _logger.Msg("    [null component]");
                    continue;
                }

                string typeName = GetIl2CppTypeName(comp);
                string enabledStr = "";
                try
                {
                    var behaviour = comp.TryCast<Behaviour>();
                    if (behaviour != null)
                        enabledStr = $" enabled={behaviour.enabled}";
                }
                catch { }

                _logger.Msg($"    {typeName}{enabledStr}");
            }
        }

        private void DumpProperties(Component comp, Il2CppSystem.Type il2cppType)
        {
            // Use C# reflection on the interop type
            var managedType = comp.GetType();
            var props = managedType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            foreach (var prop in props)
            {
                if (prop.Name.Contains("Server") || prop.Name.Contains("Client") ||
                    prop.Name.Contains("Host") || prop.Name.Contains("Started") ||
                    prop.Name.Contains("Connected"))
                {
                    try
                    {
                        var val = prop.GetValue(comp);
                        _logger.Msg($"    {prop.Name} = {val}");
                    }
                    catch { }
                }
            }
        }

        private static Type FindType(Assembly assembly, string fullName)
        {
            try
            {
                foreach (var type in assembly.GetTypes())
                {
                    if (type.FullName == fullName || type.Name == fullName)
                        return type;
                }
            }
            catch { }
            return null;
        }

        private static string GetIl2CppTypeName(Component comp)
        {
            try
            {
                var il2cppType = comp.GetIl2CppType();
                return il2cppType?.Name ?? comp.GetType().Name;
            }
            catch
            {
                return comp.GetType().Name;
            }
        }
    }
}
