using UnityEditor;
using UnityEngine;
using UnityEditor.SceneManagement;
using System.IO;

public class BuildAssetBundles
{
    // Terrain parameters - sized to match game's actual play area.
    // Game data: playable radius ~1km, mountain peak 573m gain over 2km, fog distance 1200m.
    private const float TERRAIN_SIZE = 5000f;        // 5km mesh - 1km playable + ~1.5km wasteland buffer each side
    private const int TERRAIN_RES = 500;             // 10m vertex spacing - good detail at this scale
    private const float BOWL_PEAK_HEIGHT = 280f;     // less imposing rim - was 500m
    private const float BOWL_FLAT_RADIUS = 100f;     // small lodge-sized flat zone
    private const float DUNE_AMPLITUDE = 6f;         // gentle large-scale dunes
    private const float DUNE_FREQ = 0.012f;          // tighter freq for ~1km play area
    private const float BUMP_AMPLITUDE = 4f;         // sled-launch bumps (small undulations)
    private const float BUMP_FREQ = 0.045f;          // ~140m wavelength - sharper than dunes

    // Auto-deploy target - the game's CustomScenes folder
    private const string GAME_MODS_PATH = @"C:\Program Files (x86)\Steam\steamapps\common\Sledding Game\Mods\CustomScenes";

    [MenuItem("SceneLoader/Build && Deploy %&b")]
    public static void BuildAll()
    {
        Debug.Log("=== SceneLoader AssetBundle Builder ===");

        string outputPath = Path.Combine(Application.dataPath, "..", "Build");
        if (!Directory.Exists(outputPath))
            Directory.CreateDirectory(outputPath);

        string testScenePath = "Assets/Scenes/TestScene.unity";
        CreateTestScene(testScenePath);

        AssetDatabase.Refresh();
        AssetDatabase.SaveAssets();

        string scenesDir = Path.Combine(Application.dataPath, "Scenes");
        var sceneFiles = Directory.GetFiles(scenesDir, "*.unity");

        var builds = new AssetBundleBuild[sceneFiles.Length];
        for (int i = 0; i < sceneFiles.Length; i++)
        {
            string sceneName = Path.GetFileNameWithoutExtension(sceneFiles[i]);
            string assetPath = "Assets/Scenes/" + Path.GetFileName(sceneFiles[i]);
            builds[i] = new AssetBundleBuild
            {
                assetBundleName = sceneName.ToLower() + ".bundle",
                assetNames = new[] { assetPath }
            };
            Debug.Log($"  Bundle: {builds[i].assetBundleName} <- {assetPath}");
        }

        if (builds.Length == 0)
        {
            Debug.LogError("No scenes found in Assets/Scenes/");
            return;
        }

        BuildPipeline.BuildAssetBundles(outputPath, builds,
            BuildAssetBundleOptions.None, BuildTarget.StandaloneWindows64);

        Debug.Log($"=== Built {builds.Length} bundle(s) to {outputPath} ===");

        // Auto-deploy to game's Mods folder
        DeployBundles(outputPath, builds);
    }

    private static void DeployBundles(string outputPath, AssetBundleBuild[] builds)
    {
        if (!Directory.Exists(GAME_MODS_PATH))
        {
            Debug.LogWarning($"Game Mods folder not found, skipping deploy: {GAME_MODS_PATH}");
            return;
        }

        int copied = 0;
        foreach (var build in builds)
        {
            string srcPath = Path.Combine(outputPath, build.assetBundleName);
            string dstPath = Path.Combine(GAME_MODS_PATH, build.assetBundleName);

            if (!File.Exists(srcPath))
            {
                Debug.LogWarning($"Bundle not found: {srcPath}");
                continue;
            }

            try
            {
                File.Copy(srcPath, dstPath, overwrite: true);
                long sizeKb = new FileInfo(dstPath).Length / 1024;
                Debug.Log($"  Deployed: {build.assetBundleName} ({sizeKb} KB)");
                copied++;
            }
            catch (IOException ex)
            {
                Debug.LogError($"  Deploy failed for {build.assetBundleName} (game may be running, try closing it): {ex.Message}");
            }
        }
        Debug.Log($"=== Deployed {copied}/{builds.Length} bundle(s) to {GAME_MODS_PATH} ===");
    }

    public static void CreateTestScene(string savePath)
    {
        Debug.Log("Creating sand dune scene with cacti...");

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

        // Procedural noise textures for terrain. Hue varies between two endpoint
        // colors driven by a low-frequency macro octave — this is what gives the
        // eye reference points at sledding distances. With material tiling=2 and
        // mesh UV scale=8, total tiling is 16 across the 5km mesh, so each tile
        // spans ~312m: a macro period-1 blob is ~150m wide and reads as a real
        // landmark at speed.
        var sandTex = GetOrCreateNoiseTexture("Assets/Materials/SandTexture.asset",
            colorA: new Color(0.96f, 0.86f, 0.58f),   // pale cream sand
            colorB: new Color(0.86f, 0.55f, 0.28f),   // dusty orange sand
            brightnessVariation: 0.30f, seed: 1);
        var sandstoneTex = GetOrCreateNoiseTexture("Assets/Materials/SandstoneTexture.asset",
            colorA: new Color(0.82f, 0.55f, 0.34f),   // tan sandstone
            colorB: new Color(0.62f, 0.30f, 0.16f),   // terracotta
            brightnessVariation: 0.35f, seed: 2);
        // Transition material for the borderline-slope band — sits visually
        // between sand and sandstone so the boundary doesn't slap the eye.
        var transitionTex = GetOrCreateNoiseTexture("Assets/Materials/TransitionTexture.asset",
            colorA: new Color(0.90f, 0.66f, 0.40f),   // warm sand
            colorB: new Color(0.74f, 0.42f, 0.22f),   // burnt orange
            brightnessVariation: 0.32f, seed: 3);

        // Materials - load existing or create. Reusing avoids re-import + shader recompile every build.
        // Material color is white because the texture itself carries the full color (via the
        // colorA↔colorB lerp); a non-white material color would tint the whole thing.
        // Smoothness: 0=matte, 1=mirror. Sand should be very matte to kill the sun glare.
        var sandMat = GetOrCreateMaterial("Assets/Materials/SandMaterial.mat", shader,
            Color.white, sandTex, tiling: new Vector2(2f, 2f), smoothness: 0.05f);
        var sandstoneMat = GetOrCreateMaterial("Assets/Materials/SandstoneMaterial.mat", shader,
            Color.white, sandstoneTex, tiling: new Vector2(2f, 2f), smoothness: 0.10f);
        var transitionMat = GetOrCreateMaterial("Assets/Materials/TransitionMaterial.mat", shader,
            Color.white, transitionTex, tiling: new Vector2(2f, 2f), smoothness: 0.07f);
        var cactusMat = GetOrCreateMaterial("Assets/Materials/CactusMaterial.mat", shader,
            new Color(0.18f, 0.62f, 0.22f), smoothness: 0.20f);
        var postMat = GetOrCreateMaterial("Assets/Materials/PostMaterial.mat", shader,
            new Color(0.50f, 0.36f, 0.22f), smoothness: 0.15f);
        var roofMat = GetOrCreateMaterial("Assets/Materials/RoofMaterial.mat", shader,
            new Color(0.32f, 0.20f, 0.10f), smoothness: 0.20f);
        var rockMat = GetOrCreateMaterial("Assets/Materials/RockMaterial.mat", shader,
            new Color(0.45f, 0.40f, 0.35f), smoothness: 0.05f); // dark grey-brown rock
        var railMat = GetOrCreateMaterial("Assets/Materials/RailMaterial.mat", shader,
            new Color(0.30f, 0.20f, 0.14f), smoothness: 0.15f); // dark wood rail
        var trainMat = GetOrCreateMaterial("Assets/Materials/TrainMaterial.mat", shader,
            new Color(0.78f, 0.20f, 0.18f), smoothness: 0.30f); // bright red so it stands out

        // Sun
        var lightObj = new GameObject("Directional Light");
        var light = lightObj.AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = new Color(1f, 0.95f, 0.85f);
        light.intensity = 1.8f;
        lightObj.transform.rotation = Quaternion.Euler(60, -30, 0);

        // Wavy sloped sand dunes - split into 3 submeshes by slope:
        //   flat   (normal.y > ~0.92) → sand
        //   middle (normal.y in 0.83..0.92) → transition (warm orange — bridges sand↔sandstone)
        //   steep  (normal.y < ~0.83) → sandstone
        var terrainObj = CreateSandDuneTerrain("SandDunes", sandMat, transitionMat, sandstoneMat);
        terrainObj.transform.position = Vector3.zero;

        // Spawn point at center of bowl (bottom of slope)
        float spawnY = GetTerrainHeight(0, 0) + 3f;
        var spawnPoint = new GameObject("SpawnPoint");
        spawnPoint.transform.position = new Vector3(0, spawnY, 0);
        Debug.Log($"SpawnPoint at {spawnPoint.transform.position}");

        // Cacti only inside the playable area (~1km).
        SpawnCactusRing(180f,   12, 10, 30,  0f, cactusMat);    // close ring
        SpawnCactusRing(330f,   18, 14, 50,  11f, cactusMat);
        SpawnCactusRing(500f,   24, 18, 70,  5f, cactusMat);
        SpawnCactusRing(680f,   30, 20, 90,  13f, cactusMat);
        SpawnCactusRing(870f,   32, 20, 110, 7f, cactusMat);
        SpawnCactusRing(1020f,  32, 18, 120, 3f, cactusMat);    // last ring just inside boundary
        // Beyond PLAYABLE_RADIUS (1100m): flat barren wasteland, no cacti.

        // Giant landmark cacti near spawn (immediately visible orientation markers)
        CreateSaguaro(GroundPos(0, 200), 26, 3, cactusMat);
        CreateSaguaro(GroundPos(0, -200), 26, 3, cactusMat);
        CreateSaguaro(GroundPos(200, 0), 26, 3, cactusMat);
        CreateSaguaro(GroundPos(-200, 0), 26, 3, cactusMat);

        // Lodge gazebo near spawn - 4 posts holding up a roof
        // Lodge in main scene is roughly 30m square; this is comparable.
        CreateGazebo(GroundPos(80f, 0f), size: 30f, pillarHeight: 7f, postMat, roofMat);

        // Rock barriers on the steep flanks of each peak - blocks easy climbing
        // up the steepest faces, channelling players to the gentler ridges.
        SpawnRocksAroundPeaks(rockMat);

        // Sparse mini-train routes from low ground up to mountain plateaus.
        // The mod animates trains along these waypoints at runtime.
        CreateTrainRoute("TrainRoute_North", GetNorthMountainRoute(), railMat, trainMat);
        CreateTrainRoute("TrainRoute_East", GetEastMountainRoute(), railMat, trainMat);

        EditorSceneManager.SaveScene(scene, savePath);
        AssetDatabase.SaveAssets();
        Debug.Log($"Test scene saved to {savePath}");
    }

    // Mix of standalone hills at all distances. Heights tuned so the flat-top
    // plateaus give usable mountaintop areas (~40% of each peak radius is flat).
    // Heights reduced overall and tallest peaks widened.
    private static readonly (float x, float z, float height, float radius)[] TEST_PEAKS = new[]
    {
        // Inner foothills 150-300m from spawn
        ( 250f,  250f,  55f, 150f),
        (-280f,  200f,  60f, 160f),
        ( 150f,  330f,  75f, 190f),
        (-180f,  300f,  70f, 180f),
        // Mid hills 400-600m - mixed heights for variety
        ( 50f,   450f, 145f, 290f),
        (-400f,  380f, 110f, 250f),
        ( 380f,  400f, 105f, 240f),
        ( 0f,    600f, 165f, 320f),
        ( 220f,  500f,  90f, 220f),
        (-260f,  520f,  80f, 210f),
        // Outer mountains 750-900m - lower & wider for usable summits
        ( 0f,    830f, 130f, 420f),     // tallest, with broad flat summit
        (-440f,  770f, 145f, 360f),     // NW shoulder
        ( 460f,  750f, 140f, 350f),     // NE shoulder
        // Lateral mid-range
        ( 700f,  150f, 130f, 330f),     // E
        (-720f,  100f, 130f, 330f),     // W
        ( 550f, -350f, 105f, 270f),     // SE-mid
        (-530f, -380f, 110f, 280f),     // SW-mid
        // South - smaller mounds
        (-300f, -400f,  70f, 200f),
        ( 320f, -420f,  75f, 210f),
        ( 0f,   -600f,  90f, 240f),
    };

    // Several depressions - some near spawn, some between hills, for varied terrain
    private static readonly (float x, float z, float depth, float radius)[] TEST_VALLEYS = new[]
    {
        ( 100f,  400f, -45f, 220f),     // dip in the front field (deeper now)
        (-80f,  -200f, -50f, 230f),     // depression to the south
        ( 350f,  600f, -40f, 200f),     // saddle between mid peaks (NE)
        (-360f,  580f, -35f, 200f),     // saddle between mid peaks (NW)
        ( 200f, -150f, -30f, 180f),     // small dip, S of spawn
    };

    // Matches the game's ~1km playable area (player was 974m from edge).
    // Beyond this: flat wasteland.
    private const float PLAYABLE_RADIUS = 1100f;

    /// <summary>
    /// Computes a peak's height contribution at distance d from its center.
    /// Uses a "plateau Gaussian": the inner ~40% of the radius is flat (a usable
    /// mountaintop area for races, structures, etc.) while the outer falls off
    /// like a normal mountain face.
    /// </summary>
    private static float PeakContribution(float d, float radius, float peakHeight)
    {
        float r2 = radius * radius;
        float gaussian = Mathf.Exp(-(d * d) / r2 * 2f);
        // Clip the top of the gaussian. Values >= 0.65 of peak get flattened to 1.
        // gaussian = 0.65 at d/r ≈ 0.46, so the inner 46% of the radius is a flat plateau.
        float plateau = Mathf.Min(1f, gaussian / 0.65f);
        return peakHeight * plateau;
    }

    /// <summary>
    /// Bowl-shape terrain height at (x, z). Spawn is at origin (0, 0, 0).
    /// Inside PLAYABLE_RADIUS: rolling hills, dunes, test peaks.
    /// Beyond PLAYABLE_RADIUS: flat barren wasteland (signals "not playable").
    /// </summary>
    private static float GetTerrainHeight(float x, float z)
    {
        float dist = Mathf.Sqrt(x * x + z * z);

        // Outside the playable area: dead flat wasteland. No bowl walls, no dunes,
        // no peaks. The far-out terrain is a featureless desert.
        if (dist > PLAYABLE_RADIUS)
            return 0f;

        // Smoothly fade base/dune contributions toward 0 in the last 20% of the
        // playable area so there's no abrupt cliff at the edge.
        float playableT = Mathf.Clamp01((PLAYABLE_RADIUS - dist) / (PLAYABLE_RADIUS * 0.20f));

        // Bowl walls - flat near spawn, gentle middle, mountain rim at the edge
        float baseHeight = 0f;
        if (dist > BOWL_FLAT_RADIUS)
        {
            float t = (dist - BOWL_FLAT_RADIUS) / (PLAYABLE_RADIUS - BOWL_FLAT_RADIUS);
            t = Mathf.Clamp01(t);
            baseHeight = Mathf.Pow(t, 2.4f) * BOWL_PEAK_HEIGHT * playableT;
        }

        // === Localized test peaks (plateau-Gaussian for flat tops) ===
        foreach (var peak in TEST_PEAKS)
        {
            float dx = x - peak.x;
            float dz = z - peak.z;
            float d = Mathf.Sqrt(dx * dx + dz * dz);
            baseHeight += PeakContribution(d, peak.radius, peak.height);
        }

        // === Localized valleys (negative Gaussians) ===
        foreach (var valley in TEST_VALLEYS)
        {
            float dx = x - valley.x;
            float dz = z - valley.z;
            float d2 = dx * dx + dz * dz;
            float r2 = valley.radius * valley.radius;
            float falloff = Mathf.Exp(-d2 / r2 * 2f);
            baseHeight += valley.depth * falloff;
        }

        // Radial ridges (sled paths) - 8 ridges for more varied lines
        float angle = Mathf.Atan2(z, x);
        float ridgeNoise = Mathf.Cos(angle * 8f) * 0.5f + 0.5f;
        float ridgeAmp = Mathf.Min(baseHeight * 0.20f, 300f);
        float ridges = ridgeNoise * ridgeAmp;

        // Smooth rolling dunes (also faded toward boundary)
        float duneAmp = (DUNE_AMPLITUDE + baseHeight * 0.08f) * playableT;
        float largeDunes = Mathf.Sin(x * DUNE_FREQ) * Mathf.Cos(z * DUNE_FREQ) * duneAmp;
        float medDunes = (Mathf.Sin(x * DUNE_FREQ * 2.3f + 1.3f) * Mathf.Cos(z * DUNE_FREQ * 1.9f)
                       + Mathf.Sin(x * DUNE_FREQ * 3.1f) * Mathf.Cos(z * DUNE_FREQ * 3.7f + 0.7f)) * duneAmp * 0.35f;

        // Sled-jump bumps - small higher-frequency undulations for catching air.
        // Modulated by a low-freq "bumpiness mask" so we get clusters of bumps
        // separated by smoother stretches (more interesting than uniform chop).
        float bumpAmp = BUMP_AMPLITUDE * playableT;
        float bumpMask = 0.5f + 0.5f * Mathf.Sin(x * 0.003f + 1.7f) * Mathf.Cos(z * 0.0026f - 0.4f);
        float bumps = (Mathf.Sin(x * BUMP_FREQ) * Mathf.Cos(z * BUMP_FREQ * 1.3f)
                    + Mathf.Sin(x * BUMP_FREQ * 1.7f + 0.5f) * Mathf.Cos(z * BUMP_FREQ * 1.1f) * 0.6f
                    + Mathf.Sin(x * BUMP_FREQ * 2.4f) * Mathf.Cos(z * BUMP_FREQ * 2.7f + 0.9f) * 0.4f
                   ) * bumpAmp * bumpMask;

        return baseHeight + ridges + largeDunes + medDunes + bumps;
    }

    private static Vector3 GroundPos(float x, float z)
    {
        return new Vector3(x, GetTerrainHeight(x, z), z);
    }

    /// <summary>
    /// Loads existing material if present, else creates a new one. Avoids forcing
    /// asset re-imports (which trigger shader recompilation) on every build.
    /// Optional texture parameter assigns mainTexture; tiling controls UV repeat.
    /// </summary>
    private static Material GetOrCreateMaterial(string path, Shader shader, Color color,
        Texture texture = null, Vector2? tiling = null, float smoothness = 0.5f)
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        bool dirty = false;
        if (mat == null)
        {
            mat = new Material(shader);
            mat.color = color;
            AssetDatabase.CreateAsset(mat, path);
            Debug.Log($"  Created material: {path}");
        }
        else if (mat.color != color)
        {
            mat.color = color;
            dirty = true;
        }
        // Always (re)assign texture and tiling - cheap and ensures bundle has them
        if (texture != null && mat.mainTexture != texture)
        {
            mat.mainTexture = texture;
            dirty = true;
        }
        if (tiling.HasValue && mat.mainTextureScale != tiling.Value)
        {
            mat.mainTextureScale = tiling.Value;
            dirty = true;
        }
        // Smoothness (0 = matte, 1 = mirror). URP/Lit uses _Smoothness.
        if (mat.HasProperty("_Smoothness"))
        {
            mat.SetFloat("_Smoothness", smoothness);
            dirty = true;
        }
        // Always non-metallic for our terrain/cactus/wood materials
        if (mat.HasProperty("_Metallic"))
        {
            mat.SetFloat("_Metallic", 0f);
        }
        if (dirty) EditorUtility.SetDirty(mat);
        return mat;
    }

    /// <summary>
    /// Builds (or reuses) a procedural noise texture that varies in HUE between
    /// two endpoint colors (driven by a very-low-frequency macro octave) on top
    /// of brightness grain (driven by mid + fine octaves). The macro lerp is
    /// what gives the eye visible reference points at distance — without it
    /// the terrain reads as one uniform color until the slope changes.
    /// All octaves tile seamlessly via bilinear-corner Perlin.
    /// </summary>
    private static Texture2D GetOrCreateNoiseTexture(string path, Color colorA, Color colorB,
        float brightnessVariation, int seed)
    {
        const int size = 2048; // 4x pixel count vs 1024 — needed because we tile less, so each pixel covers more world
        var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (existing != null && existing.width == size && existing.height == size)
            return existing;

        // Wrong size or missing - regenerate. Delete old asset if it's a stale size.
        if (existing != null)
        {
            AssetDatabase.DeleteAsset(path);
            Debug.Log($"  Deleted stale texture (different size): {path}");
        }

        var tex = new Texture2D(size, size, TextureFormat.RGBA32, true);
        var rand = new System.Random(seed);
        float seedOffset = (float)(rand.NextDouble() * 10000.0);
        float macroSeedOffset = seedOffset + 137.7f;

        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            float v = (float)y / size;
            for (int x = 0; x < size; x++)
            {
                float u = (float)x / size;

                // Macro octave: 1 cycle per texture (huge blobs), with a period-3
                // overlay for asymmetry. Drives the colorA↔colorB hue lerp.
                // Extreme bias via SmoothStep so we get distinct color regions
                // rather than a muddy average.
                float macro = TileablePerlin(u, v, 1, macroSeedOffset)
                            + TileablePerlin(u, v, 3, macroSeedOffset + 7f) * 0.6f;
                macro /= 1.6f;
                macro = Mathf.SmoothStep(0.18f, 0.82f, macro);

                // Mid + fine octaves: brightness grain for "this surface has texture".
                float fine = TileablePerlin(u, v, 8, seedOffset);
                fine += TileablePerlin(u, v, 24, seedOffset + 17f) * 0.5f;
                fine += TileablePerlin(u, v, 64, seedOffset + 41f) * 0.25f;
                fine /= 1.75f;

                Color baseTint = Color.Lerp(colorA, colorB, macro);
                float brightness = 1f + (fine - 0.5f) * brightnessVariation * 2f;
                Color c = new Color(
                    Mathf.Clamp01(baseTint.r * brightness),
                    Mathf.Clamp01(baseTint.g * brightness),
                    Mathf.Clamp01(baseTint.b * brightness),
                    1f);
                pixels[y * size + x] = c;
            }
        }
        tex.SetPixels(pixels);
        tex.wrapMode = TextureWrapMode.Repeat;
        tex.filterMode = FilterMode.Trilinear;
        tex.anisoLevel = 8; // sharper at glancing angles (helps with sledding view)
        tex.Apply(updateMipmaps: true, makeNoLongerReadable: false);

        AssetDatabase.CreateAsset(tex, path);
        Debug.Log($"  Created tileable noise texture: {path} ({colorA} ↔ {colorB})");
        return tex;
    }

    /// <summary>
    /// Returns a Perlin sample at (u, v) in [0,1]^2 that wraps seamlessly.
    /// Achieved by sampling at 4 wraparound corners and bilinear blending,
    /// so the contributions at u=0 vs u=1 (and v=0 vs v=1) match exactly.
    /// </summary>
    private static float TileablePerlin(float u, float v, int period, float seedOffset)
    {
        float x = u * period + seedOffset;
        float y = v * period + seedOffset;
        float w00 = Mathf.PerlinNoise(x, y);
        float w10 = Mathf.PerlinNoise(x - period, y);
        float w01 = Mathf.PerlinNoise(x, y - period);
        float w11 = Mathf.PerlinNoise(x - period, y - period);
        return Mathf.Lerp(
            Mathf.Lerp(w00, w10, u),
            Mathf.Lerp(w01, w11, u),
            v);
    }

    /// <summary>
    /// Classifies a triangle into one of three submeshes based on its slope:
    ///   flat       (normal.y >  transitionThresh) → sand
    ///   transition (normal.y in steepThresh..transitionThresh) → warm orange
    ///   steep      (normal.y <  steepThresh) → sandstone
    /// Both thresholds are perturbed by the same low-frequency noise so the
    /// material boundaries wander organically rather than cutting along clean
    /// slope contour lines.
    /// </summary>
    private static void AddTri(Vector3[] verts, int a, int b, int c,
        System.Collections.Generic.List<int> flatTris,
        System.Collections.Generic.List<int> transitionTris,
        System.Collections.Generic.List<int> steepTris,
        float steepNormalY, float transitionNormalY, int gridX, int gridZ)
    {
        // Triangle face normal
        Vector3 va = verts[a];
        Vector3 vb = verts[b];
        Vector3 vc = verts[c];
        Vector3 normal = Vector3.Cross(vb - va, vc - va).normalized;

        // Center of triangle in world space - used for spatial noise lookup
        float cx = (va.x + vb.x + vc.x) / 3f;
        float cz = (va.z + vb.z + vc.z) / 3f;

        // Low-frequency noise modulates the slope thresholds organically.
        float thresholdNoise = Mathf.Sin(cx * 0.008f + 1.7f) * Mathf.Cos(cz * 0.011f + 0.4f) * 0.6f
                             + Mathf.Sin(cx * 0.025f - 0.5f) * Mathf.Cos(cz * 0.019f + 1.1f) * 0.4f;
        float effSteep      = steepNormalY      + thresholdNoise * 0.05f;
        float effTransition = transitionNormalY + thresholdNoise * 0.04f;

        if (normal.y < effSteep)
        {
            steepTris.Add(a); steepTris.Add(b); steepTris.Add(c);
        }
        else if (normal.y < effTransition)
        {
            transitionTris.Add(a); transitionTris.Add(b); transitionTris.Add(c);
        }
        else
        {
            flatTris.Add(a); flatTris.Add(b); flatTris.Add(c);
        }
    }

    private static GameObject CreateSandDuneTerrain(string name, Material flatMat, Material transitionMat, Material steepMat)
    {
        var go = new GameObject(name);
        var mf = go.AddComponent<MeshFilter>();
        var mr = go.AddComponent<MeshRenderer>();
        var mc = go.AddComponent<MeshCollider>();
        // Three materials: 0 = sand (flat), 1 = transition (warm orange), 2 = sandstone (steep)
        mr.sharedMaterials = new[] { flatMat, transitionMat, steepMat };
        go.layer = 10; // Terrain

        // Reuse existing mesh asset if present (avoids re-importing 1M-vertex asset).
        // We modify it in-place rather than calling CreateAsset every build.
        const string meshPath = "Assets/Materials/SandDuneMesh.asset";
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
        bool isNewMesh = (mesh == null);
        if (isNewMesh)
        {
            mesh = new Mesh();
            mesh.name = "SandDuneMesh";
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        }
        else
        {
            mesh.Clear();
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        }

        int vertCount = (TERRAIN_RES + 1) * (TERRAIN_RES + 1);
        var vertices = new Vector3[vertCount];
        var uvs = new Vector2[vertCount];

        float step = TERRAIN_SIZE / TERRAIN_RES;
        float halfSize = TERRAIN_SIZE / 2f;

        int idx = 0;
        for (int z = 0; z <= TERRAIN_RES; z++)
        {
            for (int x = 0; x <= TERRAIN_RES; x++)
            {
                float wx = x * step - halfSize;
                float wz = z * step - halfSize;
                float wy = GetTerrainHeight(wx, wz);
                vertices[idx] = new Vector3(wx, wy, wz);
                uvs[idx] = new Vector2((float)x / TERRAIN_RES * 8f, (float)z / TERRAIN_RES * 8f);
                idx++;
            }
        }

        // Build all triangles, then split them into three submeshes by slope:
        //   flat       (normal.y > ~0.92)       → sand
        //   transition (normal.y ∈ 0.83..0.92)  → warm orange (visual heads-up: a steeper face is near)
        //   steep      (normal.y < ~0.83)       → sandstone
        // Both thresholds wander +/- a few hundredths via low-frequency noise so the
        // bands aren't perfect contour lines.
        var flatTris = new System.Collections.Generic.List<int>(TERRAIN_RES * TERRAIN_RES * 6);
        var transitionTris = new System.Collections.Generic.List<int>(TERRAIN_RES * TERRAIN_RES);
        var steepTris = new System.Collections.Generic.List<int>(TERRAIN_RES * TERRAIN_RES);
        const float STEEP_NORMAL_Y = 0.83f;       // cos(~34°) - cliff face
        const float TRANSITION_NORMAL_Y = 0.92f;  // cos(~23°) - "getting steep" warning band

        for (int z = 0; z < TERRAIN_RES; z++)
        {
            for (int x = 0; x < TERRAIN_RES; x++)
            {
                int v0 = z * (TERRAIN_RES + 1) + x;
                int v1 = v0 + 1;
                int v2 = v0 + (TERRAIN_RES + 1);
                int v3 = v2 + 1;

                AddTri(vertices, v0, v2, v1, flatTris, transitionTris, steepTris, STEEP_NORMAL_Y, TRANSITION_NORMAL_Y, x, z);
                AddTri(vertices, v1, v2, v3, flatTris, transitionTris, steepTris, STEEP_NORMAL_Y, TRANSITION_NORMAL_Y, x, z);
            }
        }

        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.subMeshCount = 3;
        mesh.SetTriangles(flatTris, 0);
        mesh.SetTriangles(transitionTris, 1);
        mesh.SetTriangles(steepTris, 2);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        if (isNewMesh)
        {
            AssetDatabase.CreateAsset(mesh, meshPath);
            Debug.Log($"  Created mesh asset: {meshPath}");
        }
        else
        {
            EditorUtility.SetDirty(mesh);
        }

        mf.sharedMesh = mesh;
        mc.sharedMesh = mesh;
        return go;
    }

    /// <summary>
    /// Spawns a ring of cactus clusters around the bowl at a given radius.
    /// </summary>
    private static void SpawnCactusRing(float ringRadius, int clusterCount, int cactiPerCluster, float clusterRadius, float angleOffsetDeg, Material mat)
    {
        for (int i = 0; i < clusterCount; i++)
        {
            float angle = (i * 360f / clusterCount + angleOffsetDeg) * Mathf.Deg2Rad;
            float cx = Mathf.Cos(angle) * ringRadius;
            float cz = Mathf.Sin(angle) * ringRadius;
            SpawnCactusCluster(new Vector3(cx, 0, cz), cactiPerCluster, clusterRadius, mat);
        }
    }

    /// <summary>
    /// Spawns a cluster of saguaro cacti in a circular area around center.
    /// </summary>
    private static void SpawnCactusCluster(Vector3 center, int count, float radius, Material mat)
    {
        var clusterParent = new GameObject($"CactusCluster_{(int)center.x}_{(int)center.z}");
        for (int i = 0; i < count; i++)
        {
            float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
            float dist = Random.Range(0f, radius);
            float x = center.x + Mathf.Cos(angle) * dist;
            float z = center.z + Mathf.Sin(angle) * dist;
            float y = GetTerrainHeight(x, z);

            float height = Random.Range(5f, 11f);
            int armCount = Random.Range(0, 3);
            var saguaro = CreateSaguaro(new Vector3(x, y, z), height, armCount, mat);
            saguaro.transform.SetParent(clusterParent.transform);
        }
    }

    /// <summary>
    /// Builds a saguaro cactus from primitive cylinders: vertical trunk plus arms
    /// that grow horizontally then bend upward.
    /// </summary>
    /// <summary>
    /// Builds a simple gazebo: 4 corner posts + flat roof + peaked top.
    /// Open on all sides so player can walk in.
    /// </summary>
    /// <summary>
    /// Scatters rocky obstacles around each peak's steepest face. Mixes cube,
    /// capsule, and sphere primitives for visual variety; aggressive scale &
    /// rotation jitter makes them look less uniform / less "cubey".
    /// </summary>
    private static void SpawnRocksAroundPeaks(Material rockMat)
    {
        var parent = new GameObject("PeakRocks");
        var rng = new System.Random(42);
        foreach (var peak in TEST_PEAKS)
        {
            // Number of rocks scales with peak radius
            int rockCount = Mathf.Clamp(Mathf.RoundToInt(peak.radius / 12f), 6, 32);
            for (int i = 0; i < rockCount; i++)
            {
                // Place at radius 0.55-0.85 of peak (the steep face zone)
                float t = (float)rng.NextDouble();
                float ringR = peak.radius * (0.55f + t * 0.30f);
                float angle = (float)rng.NextDouble() * Mathf.PI * 2f;
                float wx = peak.x + Mathf.Cos(angle) * ringR;
                float wz = peak.z + Mathf.Sin(angle) * ringR;
                float wy = GetTerrainHeight(wx, wz);

                // Pick primitive type randomly for variety
                PrimitiveType prim;
                int pick = rng.Next(10);
                if (pick < 5) prim = PrimitiveType.Cube;
                else if (pick < 8) prim = PrimitiveType.Capsule;
                else prim = PrimitiveType.Sphere;

                var rock = GameObject.CreatePrimitive(prim);
                rock.name = $"Rock_{(int)peak.x}_{(int)peak.z}_{i}";
                rock.transform.SetParent(parent.transform);

                // Aggressive non-uniform scale - looks like weathered stone
                float baseSize = 1.6f + (float)rng.NextDouble() * 5f;
                float sx = baseSize * (0.55f + (float)rng.NextDouble() * 0.95f);
                float sy = baseSize * (0.40f + (float)rng.NextDouble() * 0.85f);
                float sz = baseSize * (0.55f + (float)rng.NextDouble() * 0.95f);
                rock.transform.localScale = new Vector3(sx, sy, sz);

                // Sink into ground a bit so they look embedded
                rock.transform.position = new Vector3(wx, wy + sy * 0.25f - 0.3f, wz);

                // Random tilt + Y rotation - cubes don't read as "boxes" anymore
                rock.transform.rotation = Quaternion.Euler(
                    (float)rng.NextDouble() * 50f - 25f,
                    (float)rng.NextDouble() * 360f,
                    (float)rng.NextDouble() * 50f - 25f);
                rock.layer = 0; // default - blocks player
                var r = rock.GetComponent<MeshRenderer>();
                if (r != null) r.sharedMaterial = rockMat;
            }
        }
    }

    /// <summary>
    /// Centripetal Catmull-Rom (alpha = 0.5) sample on the segment p1→p2,
    /// using p0 and p3 as outer control points to define tangents. t in
    /// [0,1] interpolates between p1 (t=0) and p2 (t=1).
    /// Centripetal parameterisation is robust against cusps/self-loops at
    /// sharp control-point angles, which uniform Catmull-Rom can produce.
    /// Falls back to linear lerp if any adjacent pair coincides (zero-length
    /// chord -> NaN in the t_i computation).
    /// </summary>
    private static Vector2 CatmullRomCentripetal(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
    {
        const float alpha = 0.5f;
        float t0 = 0f;
        float t1 = t0 + Mathf.Pow(Vector2.Distance(p0, p1), alpha);
        float t2 = t1 + Mathf.Pow(Vector2.Distance(p1, p2), alpha);
        float t3 = t2 + Mathf.Pow(Vector2.Distance(p2, p3), alpha);
        if (t1 <= t0 || t2 <= t1 || t3 <= t2)
            return Vector2.Lerp(p1, p2, t);
        float u = Mathf.Lerp(t1, t2, t);
        Vector2 a1 = (t1 - u) / (t1 - t0) * p0 + (u - t0) / (t1 - t0) * p1;
        Vector2 a2 = (t2 - u) / (t2 - t1) * p1 + (u - t1) / (t2 - t1) * p2;
        Vector2 a3 = (t3 - u) / (t3 - t2) * p2 + (u - t2) / (t3 - t2) * p3;
        Vector2 b1 = (t2 - u) / (t2 - t0) * a1 + (u - t0) / (t2 - t0) * a2;
        Vector2 b2 = (t3 - u) / (t3 - t1) * a2 + (u - t1) / (t3 - t1) * a3;
        return (t2 - u) / (t2 - t1) * b1 + (u - t1) / (t2 - t1) * b2;
    }

    /// <summary>
    /// Builds a looping train route. Creates waypoint markers (empty GameObjects)
    /// for the runtime animator, plus visual rail segments (no colliders), plus
    /// a TrainCar cube. The mod's TrainAnimator finds these on bundle align.
    /// </summary>
    private static void CreateTrainRoute(string name, System.Collections.Generic.List<Vector2> xzPath,
        Material railMat, Material trainMat)
    {
        var route = new GameObject(name);

        // Build a dense polyline by sampling each xzPath segment every ~4m.
        // The same point set drives both the rails and the runtime train waypoints,
        // so the train follows the exact curve of the tracks instead of cutting
        // across terrain between the original sparse control points.
        const float trainOffsetY = 1.4f;   // train body sits ~0.15m above ground (rails+sleepers ~32cm tall)
        const float subdivisionMeters = 4f;
        const float gauge = 1.2f;          // distance between the two parallel rails
        const float sleeperHeight = 0.14f;
        const float railHeight = 0.18f;
        const float railWidth  = 0.14f;
        // Rails rest ON TOP OF sleepers: rail bottom = sleeper top.
        // Sleeper sits on ground (bottom at terrain). Rail center = sleeperHeight + railHeight/2.
        const float railOffsetY  = sleeperHeight + railHeight * 0.5f;
        const float sleeperOffsetY = sleeperHeight * 0.5f;

        // Centripetal Catmull-Rom (alpha = 0.5) through the closed loop of
        // control points. Replaces straight-line interpolation so sharp
        // angles between adjacent control points become smooth curves —
        // both the rails and the runtime train waypoints inherit the curve
        // since they share `dense`. Centripetal alpha avoids cusps/loops
        // at tight corners (uniform alpha=0 can overshoot).
        var dense = new System.Collections.Generic.List<Vector2>();
        int cpCount = xzPath.Count;
        for (int i = 0; i < cpCount; i++)
        {
            Vector2 p0 = xzPath[(i - 1 + cpCount) % cpCount];
            Vector2 p1 = xzPath[i];
            Vector2 p2 = xzPath[(i + 1) % cpCount];
            Vector2 p3 = xzPath[(i + 2) % cpCount];
            float segDist = Vector2.Distance(p1, p2);
            int subs = Mathf.Max(1, Mathf.CeilToInt(segDist / subdivisionMeters));
            for (int s = 0; s < subs; s++) // exclusive end avoids duplicate at junctions
            {
                float t = (float)s / subs;
                dense.Add(CatmullRomCentripetal(p0, p1, p2, p3, t));
            }
        }
        int n = dense.Count;

        // Train waypoints: ride at trainOffsetY above terrain.
        // Naming preserves Waypoint_NNN convention the TrainAnimator already reads.
        for (int i = 0; i < n; i++)
        {
            float wx = dense[i].x;
            float wz = dense[i].y;
            float wy = GetTerrainHeight(wx, wz) + trainOffsetY;
            var wp = new GameObject($"Waypoint_{i:D3}");
            wp.transform.SetParent(route.transform);
            wp.transform.position = new Vector3(wx, wy, wz);
        }

        // Two parallel rails + a sleeper per dense segment. Rails sample terrain
        // at railOffsetY; the loop closes back to dense[0].
        int segIdx = 0;
        for (int i = 0; i < n; i++)
        {
            Vector2 axz = dense[i];
            Vector2 bxz = dense[(i + 1) % n];
            Vector3 a = new Vector3(axz.x, GetTerrainHeight(axz.x, axz.y) + railOffsetY, axz.y);
            Vector3 b = new Vector3(bxz.x, GetTerrainHeight(bxz.x, bxz.y) + railOffsetY, bxz.y);
            Vector3 dir = b - a;
            float segLen = dir.magnitude;
            if (segLen < 0.001f) continue;
            Vector3 dirN = dir.normalized;
            Vector3 perp = Vector3.Cross(Vector3.up, dirN).normalized;
            Vector3 mid = (a + b) * 0.5f;
            Quaternion rot = Quaternion.LookRotation(dirN);

            // Two parallel rails on either side
            for (int side = 0; side < 2; side++)
            {
                float offset = (side == 0) ? -gauge * 0.5f : gauge * 0.5f;
                var rail = GameObject.CreatePrimitive(PrimitiveType.Cube);
                rail.name = $"Rail_{segIdx:D3}_{(side == 0 ? "L" : "R")}";
                rail.transform.SetParent(route.transform);
                rail.transform.position = mid + perp * offset;
                rail.transform.rotation = rot;
                rail.transform.localScale = new Vector3(railWidth, railHeight, segLen + 0.1f); // slight overlap to hide seams
                rail.layer = 0;
                var col = rail.GetComponent<Collider>();
                if (col != null) Object.DestroyImmediate(col);
                var rr = rail.GetComponent<MeshRenderer>();
                if (rr != null) rr.sharedMaterial = railMat;
            }

            // Sleeper (cross beam) every segment, centered, sitting on terrain.
            // Rail bottoms rest on sleeper top (sleeperHeight = 0.14m).
            var sleeper = GameObject.CreatePrimitive(PrimitiveType.Cube);
            sleeper.name = $"Sleeper_{segIdx:D3}";
            sleeper.transform.SetParent(route.transform);
            float midGroundY = GetTerrainHeight(mid.x, mid.z);
            sleeper.transform.position = new Vector3(mid.x, midGroundY + sleeperOffsetY, mid.z);
            sleeper.transform.rotation = rot;
            // Local scale: x=length perpendicular to track, y=thickness, z=length along track
            sleeper.transform.localScale = new Vector3(gauge + 0.4f, sleeperHeight, 0.35f);
            sleeper.layer = 0;
            var scol = sleeper.GetComponent<Collider>();
            if (scol != null) Object.DestroyImmediate(scol);
            var sr = sleeper.GetComponent<MeshRenderer>();
            if (sr != null) sr.sharedMaterial = railMat;

            segIdx++;
        }

        // Train car - keeps its collider so player can land on top.
        // Empty parent so we can attach visible body + smokestack as children.
        var train = new GameObject("TrainCar");
        train.transform.SetParent(route.transform);
        float t0gy = GetTerrainHeight(dense[0].x, dense[0].y) + trainOffsetY;
        train.transform.position = new Vector3(dense[0].x, t0gy, dense[0].y);

        // Main body (red boxcar)
        var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
        body.name = "TrainBody";
        body.transform.SetParent(train.transform, worldPositionStays: false);
        body.transform.localPosition = new Vector3(0f, 0f, 0f);
        body.transform.localScale = new Vector3(3f, 2.5f, 6f);
        body.layer = 10;
        var br = body.GetComponent<MeshRenderer>();
        if (br != null) br.sharedMaterial = trainMat;

        // Cabin on top (smaller, also red) for silhouette
        var cabin = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cabin.name = "TrainCabin";
        cabin.transform.SetParent(train.transform, worldPositionStays: false);
        cabin.transform.localPosition = new Vector3(0f, 1.8f, -1.5f);
        cabin.transform.localScale = new Vector3(2.4f, 1.6f, 2.5f);
        cabin.layer = 10;
        var cabR = cabin.GetComponent<MeshRenderer>();
        if (cabR != null) cabR.sharedMaterial = trainMat;

        // Smokestack (cylinder) - tall and obvious from a distance
        var stack = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        stack.name = "TrainStack";
        stack.transform.SetParent(train.transform, worldPositionStays: false);
        stack.transform.localPosition = new Vector3(0f, 2.5f, 1.6f);
        stack.transform.localScale = new Vector3(0.6f, 1.2f, 0.6f);
        stack.layer = 10;
        var stR = stack.GetComponent<MeshRenderer>();
        if (stR != null) stR.sharedMaterial = trainMat;
    }

    /// <summary>
    /// Looping route from west of the gazebo up to the tallest mountain plateau (0, 830).
    /// Avoids the gazebo (which sits at +80, 0).
    /// </summary>
    private static System.Collections.Generic.List<Vector2> GetNorthMountainRoute()
    {
        return new System.Collections.Generic.List<Vector2>
        {
            new Vector2(-180f,  -60f),    // start station (clear of gazebo)
            new Vector2(-150f,   80f),
            new Vector2(-100f,  220f),
            new Vector2( -60f,  400f),
            new Vector2( -30f,  600f),
            new Vector2( -10f,  780f),
            new Vector2(   0f,  830f),    // top plateau (highest point)
            new Vector2(  50f,  720f),
            new Vector2(  90f,  500f),
            new Vector2( 100f,  300f),
            new Vector2(  60f,  140f),
            new Vector2( -30f,   20f),    // back loop, clear of gazebo
            new Vector2(-130f,  -90f),    // returns toward starting station
        };
    }

    /// <summary>
    /// Smaller looping route around the eastern foothills.
    /// </summary>
    private static System.Collections.Generic.List<Vector2> GetEastMountainRoute()
    {
        return new System.Collections.Generic.List<Vector2>
        {
            new Vector2( 350f,  100f),
            new Vector2( 550f,  150f),
            new Vector2( 700f,  130f),    // up E peak (700, 150)
            new Vector2( 850f,    0f),
            new Vector2( 700f, -150f),
            new Vector2( 550f, -100f),
            new Vector2( 350f,    0f),
        };
    }

    private static GameObject CreateGazebo(Vector3 basePos, float size, float pillarHeight, Material postMat, Material roofMat)
    {
        var gazebo = new GameObject("LodgeGazebo");
        gazebo.transform.position = basePos;

        float postThickness = 1.0f;
        float halfSize = size / 2f;

        // 4 corner posts - bottom rests on floor top (0.5m), extends pillarHeight up
        for (int i = 0; i < 4; i++)
        {
            float px = ((i & 1) == 0 ? -1 : 1) * halfSize;
            float pz = ((i & 2) == 0 ? -1 : 1) * halfSize;

            var post = GameObject.CreatePrimitive(PrimitiveType.Cube);
            post.name = $"GazeboPost_{i}";
            post.transform.SetParent(gazebo.transform);
            post.transform.localPosition = new Vector3(px, 0.5f + pillarHeight / 2f, pz);
            post.transform.localScale = new Vector3(postThickness, pillarHeight, postThickness);
            post.layer = 0;
            var r = post.GetComponent<MeshRenderer>();
            if (r != null) r.sharedMaterial = postMat;
        }

        // Floor platform - thick block buried partially in the ground so dunes
        // never expose a gap beneath it. Top surface is slightly above ground.
        const float floorThickness = 8f;     // very thick - extends well below ground level
        const float floorTopHeight = 0.5f;   // floor surface 0.5m above ground origin
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "GazeboFloor";
        floor.transform.SetParent(gazebo.transform);
        floor.transform.localPosition = new Vector3(0, floorTopHeight - floorThickness / 2f, 0);
        floor.transform.localScale = new Vector3(size + 2f, floorThickness, size + 2f);
        floor.layer = 10; // Terrain layer so player can walk on it
        var fr = floor.GetComponent<MeshRenderer>();
        if (fr != null) fr.sharedMaterial = postMat;

        // Sloped ramps on all 4 sides. Long + deep so even on the lower side of
        // a dune (where ground may be 6m below floor level), the outer edge is
        // still well below the surrounding terrain - no ledges.
        // Total drop 7.5m over 18m = ~23 degrees - still walkable, plus the upper
        // portion of the ramp is much gentler in practice because it intersects
        // the actual terrain at a higher point on the slope.
        const float rampLength = 18f;
        const float rampThickness = 0.6f;
        const float rampOuterY = -7f;
        for (int side = 0; side < 4; side++)
        {
            var ramp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ramp.name = $"GazeboRamp_{side}";
            ramp.transform.SetParent(gazebo.transform);

            // Compute outward direction & rotation per side
            Vector3 dir = side switch
            {
                0 => Vector3.forward,   // +Z
                1 => Vector3.right,     // +X
                2 => Vector3.back,      // -Z
                _ => Vector3.left,      // -X
            };
            // Ramp center: just outside the floor edge, vertical center between
            // floor surface (0.5) and outer end (-3).
            float outOffset = halfSize + 1f + rampLength / 2f;
            float rampCenterY = (floorTopHeight + rampOuterY) / 2f;
            ramp.transform.localPosition = new Vector3(dir.x * outOffset, rampCenterY, dir.z * outOffset);
            // Tilt: total vertical drop = floorTopHeight - rampOuterY across rampLength horizontal
            float pitch = Mathf.Atan2(floorTopHeight - rampOuterY, rampLength) * Mathf.Rad2Deg;
            // For Z-axis ramps: positive X rotation tilts +Z end DOWN (away from gazebo).
            // For X-axis ramps: negative Z rotation tilts +X end DOWN (away from gazebo).
            if (dir.z != 0) ramp.transform.localEulerAngles = new Vector3(dir.z * pitch, 0, 0);
            else ramp.transform.localEulerAngles = new Vector3(0, 0, -dir.x * pitch);
            // Scale: width matches gazebo size, length is rampLength, thin
            if (dir.z != 0)
                ramp.transform.localScale = new Vector3(size + 2f, rampThickness, rampLength);
            else
                ramp.transform.localScale = new Vector3(rampLength, rampThickness, size + 2f);
            ramp.layer = 10; // Terrain - walkable
            var rampR = ramp.GetComponent<MeshRenderer>();
            if (rampR != null) rampR.sharedMaterial = postMat;
        }

        // Roof - flat slab on top of posts, slightly overhanging.
        // Posts now sit on the elevated floor (top at y=0.5), so add 0.5 to roof height.
        float roofY = 0.5f + pillarHeight + 0.5f;
        var roof = GameObject.CreatePrimitive(PrimitiveType.Cube);
        roof.name = "GazeboRoof";
        roof.transform.SetParent(gazebo.transform);
        roof.transform.localPosition = new Vector3(0, roofY, 0);
        roof.transform.localScale = new Vector3(size + 4f, 1f, size + 4f);
        roof.layer = 0;
        var rr = roof.GetComponent<MeshRenderer>();
        if (rr != null) rr.sharedMaterial = roofMat;

        // Peaked center
        var roofCap = GameObject.CreatePrimitive(PrimitiveType.Cube);
        roofCap.name = "GazeboRoofCap";
        roofCap.transform.SetParent(gazebo.transform);
        roofCap.transform.localPosition = new Vector3(0, roofY + 2f, 0);
        roofCap.transform.localScale = new Vector3(size * 0.7f, 3f, size * 0.7f);
        roofCap.transform.localEulerAngles = new Vector3(0, 45f, 0);
        roofCap.layer = 0;
        var capR = roofCap.GetComponent<MeshRenderer>();
        if (capR != null) capR.sharedMaterial = roofMat;

        return gazebo;
    }

    private static GameObject CreateSaguaro(Vector3 basePos, float height, int armCount, Material mat)
    {
        var saguaro = new GameObject("Saguaro");
        saguaro.transform.position = basePos;

        float trunkRadius = height * 0.07f;

        // Main vertical trunk
        var trunk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        trunk.name = "Trunk";
        trunk.transform.SetParent(saguaro.transform);
        // Cylinder primitive: radius 0.5, height 2 at scale (1,1,1).
        // To get radius R and height H: scale = (R*2, H/2, R*2)
        trunk.transform.localPosition = new Vector3(0, height / 2f, 0);
        trunk.transform.localScale = new Vector3(trunkRadius * 2f, height / 2f, trunkRadius * 2f);
        trunk.layer = 0; // Default - blocks player like an obstacle
        var tr = trunk.GetComponent<MeshRenderer>();
        if (tr != null) tr.sharedMaterial = mat;

        // Arms - grow out horizontally, then bend up
        for (int i = 0; i < armCount; i++)
        {
            float armAngle = (360f / Mathf.Max(armCount, 1)) * i + Random.Range(-25f, 25f);
            float armRad = armAngle * Mathf.Deg2Rad;
            Vector3 outDir = new Vector3(Mathf.Cos(armRad), 0, Mathf.Sin(armRad));

            float armStartHeight = Random.Range(0.45f, 0.7f) * height;
            float horizLen = Random.Range(0.18f, 0.32f) * height;
            float upLen = Random.Range(0.30f, 0.50f) * height;
            float armRadius = trunkRadius * 0.7f;

            // Horizontal arm segment - cylinder rotated to align with outDir
            var armH = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            armH.name = $"Arm{i}_Horiz";
            armH.transform.SetParent(saguaro.transform);
            // Center the horizontal segment between trunk surface and bend point
            float horizCenterDist = trunkRadius + horizLen / 2f;
            armH.transform.localPosition = new Vector3(outDir.x * horizCenterDist, armStartHeight, outDir.z * horizCenterDist);
            armH.transform.localRotation = Quaternion.FromToRotation(Vector3.up, outDir);
            armH.transform.localScale = new Vector3(armRadius * 2f, horizLen / 2f, armRadius * 2f);
            armH.layer = 0;
            var ahr = armH.GetComponent<MeshRenderer>();
            if (ahr != null) ahr.sharedMaterial = mat;

            // Vertical arm segment at the end of horizontal
            var armV = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            armV.name = $"Arm{i}_Vert";
            armV.transform.SetParent(saguaro.transform);
            float endDist = trunkRadius + horizLen;
            armV.transform.localPosition = new Vector3(outDir.x * endDist, armStartHeight + upLen / 2f, outDir.z * endDist);
            armV.transform.localScale = new Vector3(armRadius * 2f, upLen / 2f, armRadius * 2f);
            armV.layer = 0;
            var avr = armV.GetComponent<MeshRenderer>();
            if (avr != null) avr.sharedMaterial = mat;
        }

        return saguaro;
    }
}
