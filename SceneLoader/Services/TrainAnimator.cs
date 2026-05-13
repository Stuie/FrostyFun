using System;
using System.Collections.Generic;
using System.Reflection;
using MelonLoader;
using UnityEngine;

namespace SceneLoader.Services
{
    /// <summary>
    /// Drives train motion in the loaded bundle scene by hijacking real Bench
    /// instances from the main scene. The Bench:
    ///   • inherits Seat (multi-passenger F-interaction with InteractionPopupElement)
    ///   • has a FishNet NetworkObject + NetworkTransform — when the SERVER
    ///     moves the bench transform, all clients receive the synced position
    ///     automatically; we get real server-authoritative networking with no
    ///     custom NetworkBehaviour.
    ///   • has 2 SeatPosition children — 2 passengers per bench / per train.
    ///
    /// Each train is composed of:
    ///   - A hijacked Bench (the networked, interactable seat layer).
    ///   - Train body / cabin / smokestack created as CHILDREN of the bench so
    ///     they follow the bench's synced transform on every client.
    ///
    /// Server detection: only the host runs the per-frame motion. Non-host
    /// clients let FishNet's NetworkTransform sync handle bench positions.
    ///
    /// On exit: reparent benches back to their original parent and re-enable
    /// any disabled components so the world restores cleanly.
    /// </summary>
    public class TrainAnimator
    {
        private readonly MelonLogger.Instance _logger;

        private struct TrainState
        {
            public Transform Bench;       // the hijacked bench (networked seat + transform anchor)
            public GameObject TrainBody;  // procedural train geometry parented under the bench
            public Vector3[] Waypoints;
            public int[] StationIndices;
            public int CurrentIdx;
            public float T;
            public float BaseSpeed;
        }

        private struct HijackedBenchRecord
        {
            public GameObject Obj;
            public Transform OriginalParent;
            public Vector3 OriginalLocalPos;
            public Quaternion OriginalLocalRot;
            public Vector3 OriginalLocalScale;
        }

        private readonly List<TrainState> _trains = new();
        private readonly List<HijackedBenchRecord> _hijackedBenches = new();

        private const int TrainsPerRoute = 10;
        public const int MaxRoutes = 2;
        public const int BenchesToHijack = TrainsPerRoute * MaxRoutes; // 20

        private const float BaseSpeed = 12f;
        private const float StationSlowMultiplier = 0.18f;
        private const int StationSlowRadius = 5;

        // Caps how fast the bench can rotate per second. Without this, rotation
        // snapped to each segment's direction every dense waypoint (~0.33s at
        // BaseSpeed/4m spacing), and terrain-sampled Y on each waypoint kept
        // tilting the look pitch step-by-step. Constant angular velocity reads
        // as a train-like bank rather than the exponential ease of a Slerp.
        private const float TurnRateDegPerSec = 90f;

        // Layout: the bench is a LOW TRAILER at the REAR of the train so short
        // characters can step onto it. Train geometry (engine body + cabin +
        // smokestack) is built FORWARD of the bench in bench-local space so
        // the train extends in the direction of motion, with the bench dragged
        // along behind. Articulating the trailer along the curved waypoint
        // path is left as future work — the bench position here matches the
        // train's nominal waypoint position (no separate trailer offset along
        // the path).
        //
        // Bench is placed 0.9m BELOW the bundle's waypoint Y (which sits at
        // terrain+1.4); that puts the bench pivot at terrain+0.5 — bench
        // trigger collider (1.31m tall, centered) reaches from terrain-0.16
        // to terrain+1.16 — directly walkable from ground.
        private static readonly Vector3 BenchWorldYOffset = new Vector3(0f, -0.9f, 0f);

        // All in BENCH-LOCAL space. +Z = motion direction (LookRotation makes
        // bench.forward = motion vector). Bench pivot is local origin.
        private static readonly Vector3 TrailerBaseLocalPos   = new Vector3(0f, -0.05f, 0f);   // a flat plank UNDER the bench
        private static readonly Vector3 TrailerBaseLocalScale = new Vector3(2.6f, 0.2f, 3.0f);
        private static readonly Vector3 BodyLocalPos          = new Vector3(0f,  0.9f, 4.5f);  // engine body 4.5m ahead, raised
        private static readonly Vector3 BodyLocalScale        = new Vector3(2.6f, 1.2f, 5.0f);
        private static readonly Vector3 CabinLocalPos         = new Vector3(0f,  2.0f, 3.0f);  // cabin on top toward back of body
        private static readonly Vector3 CabinLocalScale       = new Vector3(2.0f, 1.4f, 2.2f);
        private static readonly Vector3 StackLocalPos         = new Vector3(0f,  2.0f, 6.0f);  // stack at front of body
        private static readonly Vector3 StackLocalScale       = new Vector3(0.5f, 1.0f, 0.5f);

        private bool _isServer;
        private bool _serverChecked;

        // Runtime URP materials so primitives actually render in URP (the
        // built-in default material that GameObject.CreatePrimitive assigns
        // is invisible/magenta in URP). Cached so we don't allocate per call.
        private Material _trainBodyMaterial;
        private Material _trailerBaseMaterial;
        private Material _stackMaterial;

        public bool HasTrains => _trains.Count > 0;

        public IReadOnlyList<GameObject> HijackedBenchObjects
        {
            get
            {
                var list = new List<GameObject>(_hijackedBenches.Count);
                foreach (var r in _hijackedBenches) list.Add(r.Obj);
                return list;
            }
        }

        public TrainAnimator(MelonLogger.Instance logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Captures up to <see cref="BenchesToHijack"/> existing Bench
        /// instances from the active scene and reparents them to the scene
        /// root so they survive world-hide. Bench is preferred over chair
        /// because it has NetworkTransform (chairs don't), giving us real
        /// server-auth position sync.
        ///
        /// Must be called BEFORE PlayerMigration.HideWorldObjects.
        /// </summary>
        public void HijackBenches()
        {
            ReleaseHijackedBenches();
            _serverChecked = false;
            try
            {
                var allBehaviours = UnityEngine.Object.FindObjectsOfType<Behaviour>();
                foreach (var b in allBehaviours)
                {
                    if (b == null) continue;
                    string typeName;
                    try { typeName = b.GetIl2CppType()?.Name ?? ""; } catch { continue; }
                    if (typeName != "Bench") continue;

                    var go = b.gameObject;
                    var t = go.transform;
                    var record = new HijackedBenchRecord
                    {
                        Obj = go,
                        OriginalParent = t.parent,
                        OriginalLocalPos = t.localPosition,
                        OriginalLocalRot = t.localRotation,
                        OriginalLocalScale = t.localScale,
                    };

                    // Detach to scene root so HideWorldObjects (which deactivates
                    // the World subtree) cannot reach it.
                    t.SetParent(null, worldPositionStays: true);

                    _hijackedBenches.Add(record);
                    if (_hijackedBenches.Count >= BenchesToHijack) break;
                }
                _logger.Msg($"HijackBenches: captured {_hijackedBenches.Count} / {BenchesToHijack} benches (reparented to scene root)");
            }
            catch (Exception ex)
            {
                _logger.Warning($"HijackBenches failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Walks bundle scene roots, finds TrainRoute_* containers, and assigns
        /// one hijacked bench per train. Train geometry (body/cabin/stack) is
        /// built as children of the bench so it rides along with the synced
        /// bench transform on every client.
        /// </summary>
        public void RegisterTrainsFromRoots(GameObject[] roots)
        {
            _trains.Clear();
            if (roots == null) return;

            int benchIdx = 0;
            int routesSeen = 0;

            foreach (var root in roots)
            {
                if (root == null) continue;
                if (!root.name.StartsWith("TrainRoute_")) continue;
                routesSeen++;
                _logger.Msg($"Found train route root: \"{root.name}\" ({root.transform.childCount} children)");

                var waypointList = new List<(int idx, Vector3 pos)>();
                int childWaypoints = 0;

                int count = root.transform.childCount;
                GameObject legacyTrainCar = null;
                for (int c = 0; c < count; c++)
                {
                    var child = root.transform.GetChild(c);
                    if (child == null) continue;
                    string name = child.name;
                    if (name.StartsWith("Waypoint_"))
                    {
                        childWaypoints++;
                        var suffix = name.Substring("Waypoint_".Length);
                        if (!int.TryParse(suffix, out int idx)) continue;
                        waypointList.Add((idx, child.position));
                    }
                    else if (name == "TrainCar")
                    {
                        // Legacy bundle visual at waypoint 0. Replaced by runtime
                        // bench-attached geometry; otherwise it sits stationary
                        // at the start of every route as a dead decoration.
                        legacyTrainCar = child.gameObject;
                    }
                }
                if (legacyTrainCar != null)
                {
                    UnityEngine.Object.Destroy(legacyTrainCar);
                    _logger.Msg($"  Destroyed legacy bundle TrainCar at route start");
                }

                if (waypointList.Count < 2)
                {
                    _logger.Warning($"Train route \"{root.name}\" has <2 waypoints");
                    continue;
                }

                waypointList.Sort((a, b) => a.idx.CompareTo(b.idx));
                var positions = new Vector3[waypointList.Count];
                for (int i = 0; i < waypointList.Count; i++)
                    positions[i] = waypointList[i].pos;

                var stationIndices = new[] { 0 };

                for (int t = 0; t < TrainsPerRoute; t++)
                {
                    if (benchIdx >= _hijackedBenches.Count)
                    {
                        _logger.Warning($"  Out of hijacked benches at train {t} of {TrainsPerRoute}");
                        break;
                    }

                    var bench = _hijackedBenches[benchIdx++].Obj;
                    if (bench == null) continue;
                    var benchT = bench.transform;

                    int startIdx = (positions.Length * t) / TrainsPerRoute;
                    benchT.position = positions[startIdx] + BenchWorldYOffset;
                    benchT.rotation = Quaternion.identity;

                    // Build train geometry as children of the bench so it
                    // rides along when FishNet syncs the bench's transform.
                    var trainBody = BuildTrainGeometry(benchT, $"TrainCar_{root.name}_{t}");

                    _trains.Add(new TrainState
                    {
                        Bench = benchT,
                        TrainBody = trainBody,
                        Waypoints = positions,
                        StationIndices = stationIndices,
                        CurrentIdx = startIdx,
                        T = 0f,
                        BaseSpeed = BaseSpeed,
                    });
                }
                _logger.Msg($"  Registered {_trains.Count} trains for \"{root.name}\" (waypoints={positions.Length})");
            }
            _logger.Msg($"Total trains: {_trains.Count} | hijacked benches used: {benchIdx} / {_hijackedBenches.Count}");

            // Determine and cache server status now (NetworkManager exists; we're
            // already inside the lobby).
            _isServer = QueryIsServer();
            _serverChecked = true;
            _logger.Msg($"Network role: {(_isServer ? "SERVER (will drive train motion)" : "CLIENT (FishNet syncs bench positions from host)")}");
        }

        /// <summary>
        /// Builds train geometry as children of the bench (which is the rear
        /// trailer). Bench pivot = local origin; train extends in +Z (motion
        /// direction). Adds a flat trailer base under the bench, then engine
        /// body / cabin / stack ahead of it.
        /// </summary>
        private GameObject BuildTrainGeometry(Transform parent, string name)
        {
            EnsureMaterialsCreated();

            var trainRoot = new GameObject(name);
            trainRoot.transform.SetParent(parent, worldPositionStays: false);
            trainRoot.transform.localPosition = Vector3.zero;
            trainRoot.transform.localRotation = Quaternion.identity;

            CreateChildPrimitive(trainRoot.transform, PrimitiveType.Cube,    "TrailerBase", TrailerBaseLocalPos, TrailerBaseLocalScale, _trailerBaseMaterial);
            CreateChildPrimitive(trainRoot.transform, PrimitiveType.Cube,    "Body",        BodyLocalPos,        BodyLocalScale,        _trainBodyMaterial);
            CreateChildPrimitive(trainRoot.transform, PrimitiveType.Cube,    "Cabin",       CabinLocalPos,       CabinLocalScale,       _trainBodyMaterial);
            CreateChildPrimitive(trainRoot.transform, PrimitiveType.Cylinder,"Stack",       StackLocalPos,       StackLocalScale,       _stackMaterial);

            return trainRoot;
        }

        /// <summary>
        /// Lazily creates the URP/Lit materials we apply to runtime primitives.
        /// Without this, GameObject.CreatePrimitive's default material renders
        /// as magenta or invisible in URP. Falls back to default shader if URP
        /// shader can't be resolved (shouldn't happen — the bundle uses URP).
        /// </summary>
        private void EnsureMaterialsCreated()
        {
            if (_trainBodyMaterial != null) return;

            var urp = Shader.Find("Universal Render Pipeline/Lit");
            if (urp == null)
            {
                _logger.Warning("URP/Lit shader not found — train will render with default shader");
                urp = Shader.Find("Standard");
            }

            _trainBodyMaterial = new Material(urp) { color = new Color(0.78f, 0.20f, 0.18f) }; // train red
            _trailerBaseMaterial = new Material(urp) { color = new Color(0.35f, 0.20f, 0.10f) }; // dark wood
            _stackMaterial = new Material(urp) { color = new Color(0.20f, 0.20f, 0.20f) }; // dark grey smokestack
            _logger.Msg($"Train materials created (shader=\"{urp.name}\")");
        }

        private static void CreateChildPrimitive(Transform parent, PrimitiveType type, string name, Vector3 localPos, Vector3 localScale, Material material)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            // Strip the auto-collider so train geometry never blocks player movement
            var col = go.GetComponent<Collider>();
            if (col != null) UnityEngine.Object.Destroy(col);

            // CreatePrimitive assigns the built-in pipeline default material
            // which is invisible in URP. Replace with our cached URP material.
            if (material != null)
            {
                var renderer = go.GetComponent<MeshRenderer>();
                if (renderer != null) renderer.sharedMaterial = material;
            }

            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = localScale;
        }

        /// <summary>
        /// Per-frame update; advances each train along its loop. Only runs on
        /// the server — clients receive bench positions via FishNet's
        /// NetworkTransform sync.
        /// </summary>
        public void Update(float deltaTime)
        {
            if (!_serverChecked || !_isServer) return;

            for (int i = 0; i < _trains.Count; i++)
            {
                var s = _trains[i];
                if (s.Bench == null) continue;

                int next = (s.CurrentIdx + 1) % s.Waypoints.Length;
                Vector3 from = s.Waypoints[s.CurrentIdx];
                Vector3 to = s.Waypoints[next];
                float dist = Vector3.Distance(from, to);
                if (dist < 0.001f)
                {
                    s.CurrentIdx = next;
                    _trains[i] = s;
                    continue;
                }

                float speed = ComputeSpeed(s);
                s.T += speed * deltaTime / dist;
                while (s.T >= 1f)
                {
                    s.T -= 1f;
                    s.CurrentIdx = next;
                    next = (s.CurrentIdx + 1) % s.Waypoints.Length;
                    from = s.Waypoints[s.CurrentIdx];
                    to = s.Waypoints[next];
                    dist = Vector3.Distance(from, to);
                    if (dist < 0.001f) break;
                }

                Vector3 pos = Vector3.Lerp(from, to, s.T) + BenchWorldYOffset;
                s.Bench.position = pos;

                Vector3 dir = to - from;
                if (dir.sqrMagnitude > 0.001f)
                {
                    Quaternion target = Quaternion.LookRotation(dir.normalized);
                    s.Bench.rotation = Quaternion.RotateTowards(s.Bench.rotation, target, TurnRateDegPerSec * deltaTime);
                }

                _trains[i] = s;
            }
        }

        private float ComputeSpeed(in TrainState s)
        {
            float speed = s.BaseSpeed;
            if (s.StationIndices == null || s.StationIndices.Length == 0) return speed;

            int n = s.Waypoints.Length;
            float bestT = 1f;
            foreach (int sidx in s.StationIndices)
            {
                int diff = Mathf.Abs(s.CurrentIdx - sidx);
                int wrapped = Mathf.Min(diff, n - diff);
                if (wrapped <= StationSlowRadius)
                {
                    float t = (float)wrapped / StationSlowRadius;
                    if (t < bestT) bestT = t;
                }
            }
            if (bestT < 1f)
            {
                float mult = Mathf.Lerp(StationSlowMultiplier, 1f, bestT);
                speed = s.BaseSpeed * mult;
            }
            return speed;
        }

        /// <summary>
        /// Reflects on FishNet's NetworkManager.IsServer (or fallback property
        /// names) to decide whether to drive train motion locally.
        /// </summary>
        private bool QueryIsServer()
        {
            try
            {
                var allBehaviours = UnityEngine.Object.FindObjectsOfType<Behaviour>();
                foreach (var b in allBehaviours)
                {
                    if (b == null) continue;
                    string tn;
                    try { tn = b.GetIl2CppType()?.Name ?? ""; } catch { continue; }
                    if (tn != "NetworkManager") continue;

                    var asm = Assembly.Load("Il2CppFishNet.Runtime");
                    foreach (var managedType in asm.GetTypes())
                    {
                        if (managedType.Name != "NetworkManager") continue;
                        var castMethod = typeof(Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase)
                            .GetMethod("Cast")?.MakeGenericMethod(managedType);
                        var typed = castMethod?.Invoke(b, null);
                        if (typed == null) continue;

                        foreach (var pn in new[] { "IsServer", "IsServerStarted", "IsServerInitialized" })
                        {
                            var prop = managedType.GetProperty(pn);
                            if (prop == null) continue;
                            try
                            {
                                var v = prop.GetValue(typed);
                                if (v is bool result)
                                {
                                    _logger.Msg($"  NetworkManager.{pn} = {result}");
                                    return result;
                                }
                            }
                            catch { }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"QueryIsServer failed: {ex.Message} — assuming SERVER (singleplayer fallback)");
                return true; // singleplayer effectively is the server
            }
            _logger.Msg("  NetworkManager not found — assuming SERVER (singleplayer fallback)");
            return true;
        }

        public void Reset()
        {
            // Destroy procedural train geometry first (children of benches).
            foreach (var s in _trains)
            {
                if (s.TrainBody != null) UnityEngine.Object.Destroy(s.TrainBody);
            }
            _trains.Clear();
            ReleaseHijackedBenches();
        }

        /// <summary>
        /// Reparents benches to their original parent + restores local
        /// transforms. Lets FishNet's NetworkTransform settle them at the
        /// host's authoritative position naturally on the next sync tick.
        /// </summary>
        private void ReleaseHijackedBenches()
        {
            foreach (var r in _hijackedBenches)
            {
                if (r.Obj == null) continue;
                var t = r.Obj.transform;
                if (r.OriginalParent != null)
                {
                    t.SetParent(r.OriginalParent, worldPositionStays: false);
                    t.localPosition = r.OriginalLocalPos;
                    t.localRotation = r.OriginalLocalRot;
                    t.localScale = r.OriginalLocalScale;
                }
            }
            _hijackedBenches.Clear();
        }
    }
}
