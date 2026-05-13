using System;
using System.Collections.Generic;
using System.IO;
using MelonLoader;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SceneLoader.Services
{
    public class BundleInfo
    {
        public string FilePath { get; set; }
        public string FileName { get; set; }
        public string[] ScenePaths { get; set; }
    }

    public class AssetBundleLoader
    {
        private readonly MelonLogger.Instance _logger;
        private readonly string _customScenesPath;

        // Use Il2CppAssetBundle from MelonLoader's Il2CppAssetBundleManager
        // which bypasses the broken Il2Cpp interop wrappers via direct native ICalls
        private Il2CppAssetBundle _loadedBundle;
        private Scene _loadedScene;
        private bool _sceneLoaded;

        public bool IsSceneLoaded => _sceneLoaded;
        public Scene LoadedScene => _loadedScene;

        public AssetBundleLoader(MelonLogger.Instance logger)
        {
            _logger = logger;

            var gamePath = Path.GetDirectoryName(Application.dataPath);
            _customScenesPath = Path.Combine(gamePath, "Mods", "CustomScenes");
            _logger.Msg($"Custom scenes path: {_customScenesPath}");
        }

        public List<BundleInfo> ScanForBundles()
        {
            var bundles = new List<BundleInfo>();

            if (!Directory.Exists(_customScenesPath))
            {
                _logger.Msg($"Creating CustomScenes directory: {_customScenesPath}");
                Directory.CreateDirectory(_customScenesPath);
                return bundles;
            }

            foreach (var file in Directory.GetFiles(_customScenesPath, "*", SearchOption.TopDirectoryOnly))
            {
                var ext = Path.GetExtension(file).ToLower();
                if (ext == ".meta" || ext == ".txt" || ext == ".md" || ext == ".manifest") continue;

                var info = new BundleInfo
                {
                    FilePath = file,
                    FileName = Path.GetFileName(file),
                    ScenePaths = null
                };
                bundles.Add(info);
                _logger.Msg($"  Found bundle: {info.FileName}");
            }

            _logger.Msg($"Found {bundles.Count} bundle file(s)");
            return bundles;
        }

        public bool LoadBundle(BundleInfo bundle)
        {
            if (_loadedBundle != null)
            {
                _logger.Warning("A bundle is already loaded - unload first");
                return false;
            }

            try
            {
                _logger.Msg($"Loading bundle via Il2CppAssetBundleManager.LoadFromStream: {bundle.FilePath}");

                // Both LoadFromFile (ReadOnlySpan in Unity 6) and LoadFromMemory
                // (IL2CPP GC collects the Il2CppStructArray) are broken.
                // LoadFromStream with an Il2CppSystem.IO.FileStream avoids both:
                // the file handle is owned by IL2CPP so no GC issues,
                // and we use a string path (no ReadOnlySpan).
                var il2cppStream = Il2CppSystem.IO.File.OpenRead(bundle.FilePath);
                _logger.Msg($"  Opened Il2Cpp file stream");
                _loadedBundle = Il2CppAssetBundleManager.LoadFromStream(il2cppStream);
                il2cppStream?.Close();

                if (_loadedBundle == null)
                {
                    _logger.Error("Il2CppAssetBundleManager.LoadFromFile returned null");
                    return false;
                }

                _logger.Msg("Bundle loaded successfully via Il2CppAssetBundleManager");

                // Get scene paths using the Il2Cpp wrapper's method
                var scenePaths = _loadedBundle.GetAllScenePaths();
                if (scenePaths != null)
                {
                    bundle.ScenePaths = new string[scenePaths.Length];
                    for (int i = 0; i < scenePaths.Length; i++)
                    {
                        bundle.ScenePaths[i] = scenePaths[i];
                        _logger.Msg($"  Scene: {scenePaths[i]}");
                    }
                }
                else
                {
                    bundle.ScenePaths = Array.Empty<string>();
                }

                if (bundle.ScenePaths.Length == 0)
                    _logger.Warning("Bundle contains no scenes");

                _logger.Msg($"Bundle ready ({bundle.ScenePaths.Length} scene(s))");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to load bundle: {ex.GetType().Name}: {ex.Message}");
                if (ex.InnerException != null)
                    _logger.Error($"  Inner: {ex.InnerException.Message}");
                _loadedBundle = null;
                return false;
            }
        }

        public bool LoadSceneAdditive(string scenePath)
        {
            if (_loadedBundle == null)
            {
                _logger.Error("No bundle loaded");
                return false;
            }

            if (_sceneLoaded)
            {
                _logger.Warning("A scene is already loaded");
                return false;
            }

            try
            {
                var sceneName = Path.GetFileNameWithoutExtension(scenePath);
                _logger.Msg($"Loading scene additively: {sceneName}");

                SceneManager.LoadScene(sceneName, LoadSceneMode.Additive);

                for (int i = 0; i < SceneManager.sceneCount; i++)
                {
                    var scene = SceneManager.GetSceneAt(i);
                    if (scene.name == sceneName)
                    {
                        _loadedScene = scene;
                        _sceneLoaded = true;
                        _logger.Msg($"Scene loaded: \"{scene.name}\" (rootCount={scene.rootCount})");

                        if (scene.isLoaded)
                        {
                            var roots = scene.GetRootGameObjects();
                            foreach (var root in roots)
                            {
                                if (root != null)
                                    _logger.Msg($"  Root: \"{root.name}\" active={root.activeSelf}");
                            }
                        }

                        return true;
                    }
                }

                _logger.Error($"Scene \"{sceneName}\" not found after loading");
                return false;
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to load scene: {ex.GetType().Name}: {ex.Message}");
                return false;
            }
        }

        public Vector3? FindSpawnPoint()
        {
            if (!_sceneLoaded || !_loadedScene.isLoaded) return null;

            var roots = _loadedScene.GetRootGameObjects();
            foreach (var root in roots)
            {
                if (root == null) continue;

                if (root.name == "SpawnPoint" || root.name == "Spawn Point" || root.name == "PlayerSpawn")
                {
                    _logger.Msg($"Found spawn point: {root.transform.position}");
                    return root.transform.position;
                }

                var spawn = FindChildByName(root.transform, "SpawnPoint")
                         ?? FindChildByName(root.transform, "Spawn Point")
                         ?? FindChildByName(root.transform, "PlayerSpawn");
                if (spawn != null)
                {
                    _logger.Msg($"Found spawn point (child): {spawn.position}");
                    return spawn.position;
                }
            }

            if (roots.Length > 0 && roots[0] != null)
            {
                var fallback = roots[0].transform.position + Vector3.up * 3;
                _logger.Warning($"No SpawnPoint found, using fallback: {fallback}");
                return fallback;
            }

            return null;
        }

        private static Transform FindChildByName(Transform parent, string name)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (child.name == name) return child;

                var found = FindChildByName(child, name);
                if (found != null) return found;
            }
            return null;
        }

        public void UnloadAll()
        {
            if (_sceneLoaded && _loadedScene.IsValid() && _loadedScene.isLoaded)
            {
                try
                {
                    _logger.Msg($"Unloading scene: {_loadedScene.name}");
                    SceneManager.UnloadSceneAsync(_loadedScene);
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Scene unload failed: {ex.Message}");
                }
            }
            _sceneLoaded = false;
            _loadedScene = default;

            if (_loadedBundle != null)
            {
                try
                {
                    _logger.Msg("Unloading bundle");
                    _loadedBundle.Unload(true);
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Bundle unload failed: {ex.Message}");
                }
                _loadedBundle = null;
            }
        }

        public void Reset()
        {
            UnloadAll();
        }
    }
}
