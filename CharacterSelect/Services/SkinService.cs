using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using UnityEngine;
using CharacterSelect.Data;
using CharacterSelect.Imaging;
using CharacterSelect.Infrastructure;
using Object = UnityEngine.Object;

namespace CharacterSelect.Services
{
    public class SkinService : ISkinService
    {
        private readonly IModLogger _logger;

        private string _reskinsDir;
        private Dictionary<string, Texture2D> _skinTextureCache = new();
        private Dictionary<string, Texture2D> _skinIconCache = new();
        private Dictionary<int, Texture> _originalSkinTextures = new();
        private float _reskinApplyTime;
        private int _pendingReskinCharacterId = -1;
        private string _pendingReskinPath;

        public Dictionary<string, List<SkinEntry>> AvailableSkins { get; } = new();

        public SkinService(IModLogger logger)
        {
            _logger = logger;
        }

        public void DeployEmbeddedReskins()
        {
            try
            {
                var modsDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                var reskinsDir = Path.Combine(modsDir, "reskins");
                var assembly = Assembly.GetExecutingAssembly();
                var prefix = "CharacterSelect.Assets.reskins.";

                foreach (var resourceName in assembly.GetManifestResourceNames())
                {
                    if (!resourceName.StartsWith(prefix)) continue;

                    var relativeParts = resourceName.Substring(prefix.Length);
                    var dotIndex = relativeParts.IndexOf('.');
                    if (dotIndex < 0) continue;

                    var lastDot = relativeParts.LastIndexOf('.');
                    var ext = relativeParts.Substring(lastDot);
                    var pathPart = relativeParts.Substring(0, lastDot);
                    var charFolder = pathPart.Substring(0, dotIndex);
                    var fileName = pathPart.Substring(dotIndex + 1) + ext;

                    var targetDir = Path.Combine(reskinsDir, charFolder);
                    var targetPath = Path.Combine(targetDir, fileName);

                    if (File.Exists(targetPath)) continue;

                    Directory.CreateDirectory(targetDir);
                    using (var stream = assembly.GetManifestResourceStream(resourceName))
                    {
                        if (stream == null) continue;
                        var data = new byte[stream.Length];
                        stream.Read(data, 0, data.Length);
                        File.WriteAllBytes(targetPath, data);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Error deploying embedded reskins: {ex.Message}");
            }
        }

        public void ScanForReskins()
        {
            var modsDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            _reskinsDir = Path.Combine(modsDir, "reskins");

            if (!Directory.Exists(_reskinsDir))
            {
                Directory.CreateDirectory(_reskinsDir);
                return;
            }

            foreach (var charDir in Directory.GetDirectories(_reskinsDir))
            {
                var charKey = Path.GetFileName(charDir).ToLower();
                var skins = new List<SkinEntry>();

                foreach (var file in Directory.GetFiles(charDir))
                {
                    var ext = Path.GetExtension(file).ToLower();
                    var skinName = Path.GetFileNameWithoutExtension(file);

                    if (skinName.EndsWith("_icon", StringComparison.OrdinalIgnoreCase)) continue;
                    if (skinName.EndsWith("_generated", StringComparison.OrdinalIgnoreCase)) continue;

                    if (ext == ".json")
                    {
                        var cachePath = Path.Combine(charDir, skinName + "_generated.png");
                        bool needsGen = true;
                        if (File.Exists(cachePath))
                        {
                            var jsonTime = File.GetLastWriteTimeUtc(file);
                            var cacheTime = File.GetLastWriteTimeUtc(cachePath);
                            needsGen = jsonTime > cacheTime;
                        }

                        string iconPath = FindIconPath(charDir, skinName);
                        var displayName = FormatDisplayName(skinName);

                        skins.Add(new SkinEntry
                        {
                            DisplayName = displayName,
                            FilePath = cachePath,
                            IconPath = iconPath,
                            NeedsGeneration = needsGen,
                            JsonPath = file
                        });
                    }
                    else if (ext == ".png" || ext == ".jpg" || ext == ".bmp")
                    {
                        string iconPath = FindIconPath(charDir, skinName);
                        var displayName = FormatDisplayName(skinName);
                        skins.Add(new SkinEntry { DisplayName = displayName, FilePath = file, IconPath = iconPath });
                    }
                }

                if (skins.Count > 0)
                    AvailableSkins[charKey] = skins;
            }

            // Legacy flat files: reskins/{character}_skin.*
            foreach (var file in Directory.GetFiles(_reskinsDir, "*_skin.*"))
            {
                var ext = Path.GetExtension(file).ToLower();
                if (ext != ".png" && ext != ".jpg" && ext != ".bmp") continue;

                var nameWithoutExt = Path.GetFileNameWithoutExtension(file);
                if (!nameWithoutExt.EndsWith("_skin")) continue;
                var charKey = nameWithoutExt.Substring(0, nameWithoutExt.Length - 5);

                if (!AvailableSkins.ContainsKey(charKey))
                    AvailableSkins[charKey] = new List<SkinEntry>();

                var displayName = FormatDisplayName(charKey) + " (Custom)";
                AvailableSkins[charKey].Add(new SkinEntry { DisplayName = displayName, FilePath = file });
            }

            int totalSkins = AvailableSkins.Values.Sum(s => s.Count);
            _logger.Info($"Found {totalSkins} custom skin(s) for {AvailableSkins.Count} character(s)");
        }

        public void ScheduleReskin(int characterId, string skinPath)
        {
            _pendingReskinCharacterId = characterId;
            _pendingReskinPath = skinPath;
            _reskinApplyTime = Time.time + 0.5f;
        }

        public void ProcessPendingReskin()
        {
            if (_pendingReskinCharacterId >= 0 && Time.time >= _reskinApplyTime)
            {
                ApplyReskin(_pendingReskinCharacterId, _pendingReskinPath);
                _pendingReskinCharacterId = -1;
                _pendingReskinPath = null;
            }
        }

        public void ApplyReskin(int characterId, string skinPath)
        {
            try
            {
                var playerObj = GameObject.Find("Player Networked(Clone)");
                if (playerObj == null) return;

                CharacterData.CharacterSkinMaterials.TryGetValue(characterId, out var skinPrefix);
                var renderers = playerObj.GetComponentsInChildren<SkinnedMeshRenderer>();

                if (string.IsNullOrEmpty(skinPath))
                {
                    RestoreOriginal(characterId, renderers, skinPrefix);
                    return;
                }

                // Check if procedural skin needs generation
                SkinEntry? matchingEntry = FindSkinEntry(skinPath);
                if (matchingEntry != null && matchingEntry.Value.NeedsGeneration && matchingEntry.Value.JsonPath != null)
                {
                    var origTex2D = GetOriginalTexture(characterId, renderers, skinPrefix);
                    if (origTex2D != null)
                    {
                        var generated = GenerateProceduralSkin(origTex2D, matchingEntry.Value.JsonPath);
                        if (generated != null)
                        {
                            byte[] pngData = PngEncoder.EncodeToPngManual(generated);
                            File.WriteAllBytes(skinPath, pngData);
                            _skinTextureCache[skinPath] = generated;
                            MarkSkinGenerated(skinPath);
                        }
                    }
                    else
                    {
                        _logger.Warning("Cannot generate procedural skin: original texture not available");
                    }
                }

                // Load custom skin texture
                if (!_skinTextureCache.TryGetValue(skinPath, out var texture))
                {
                    texture = LoadReskinTexture(skinPath);
                    if (texture != null) _skinTextureCache[skinPath] = texture;
                }
                if (texture == null) return;

                int swapped = 0;
                foreach (var r in renderers)
                {
                    if (r == null) continue;
                    var mat = r.material;
                    if (mat == null) continue;
                    var shaderName = mat.shader?.name;
                    if (shaderName == null || shaderName == "Standard" || shaderName.Contains("Eyes")) continue;
                    var matName = mat.name ?? "";
                    if (skinPrefix != null && !matName.StartsWith(skinPrefix, StringComparison.OrdinalIgnoreCase)) continue;

                    if (!_originalSkinTextures.ContainsKey(characterId))
                    {
                        if (mat.HasProperty("_BaseMap"))
                            _originalSkinTextures[characterId] = mat.GetTexture("_BaseMap");
                        else if (mat.HasProperty("_MainTex"))
                            _originalSkinTextures[characterId] = mat.GetTexture("_MainTex");
                    }

                    if (mat.HasProperty("_BaseMap"))
                    {
                        mat.SetTexture("_BaseMap", texture);
                        swapped++;
                    }
                    if (mat.HasProperty("_MainTex"))
                        mat.SetTexture("_MainTex", texture);
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"ApplyReskin error: {ex.Message}");
            }
        }

        public void DumpCurrentSkinTexture(int currentCharacterId)
        {
            try
            {
                var playerObj = GameObject.Find("Player Networked(Clone)");
                if (playerObj == null)
                {
                    _logger.Warning("DumpSkinTexture: No player found");
                    return;
                }

                if (!CharacterData.CharacterSkinMaterials.TryGetValue(currentCharacterId, out var skinPrefix))
                {
                    _logger.Warning($"DumpSkinTexture: No skin prefix for character {currentCharacterId}");
                    return;
                }

                Texture skinTexture = null;
                var renderers = playerObj.GetComponentsInChildren<SkinnedMeshRenderer>();
                foreach (var r in renderers)
                {
                    if (r == null) continue;
                    var mat = r.material;
                    if (mat == null) continue;
                    var shaderName = mat.shader?.name;
                    if (shaderName == null || shaderName == "Standard" || shaderName.Contains("Eyes")) continue;
                    var matName = mat.name ?? "";
                    if (!matName.StartsWith(skinPrefix, StringComparison.OrdinalIgnoreCase)) continue;

                    if (mat.HasProperty("_BaseMap"))
                    {
                        skinTexture = mat.GetTexture("_BaseMap");
                        break;
                    }
                    if (mat.HasProperty("_MainTex"))
                    {
                        skinTexture = mat.GetTexture("_MainTex");
                        break;
                    }
                }

                if (skinTexture == null)
                {
                    _logger.Warning("DumpSkinTexture: No skin texture found on player");
                    return;
                }

                var skinTex2D = skinTexture.TryCast<Texture2D>();
                if (skinTex2D == null)
                {
                    _logger.Warning("DumpSkinTexture: Could not cast to Texture2D");
                    return;
                }

                Texture2D readableTex = skinTex2D.isReadable ? skinTex2D : TextureUtils.CopyTextureToReadable(skinTex2D);
                byte[] png = PngEncoder.EncodeToPngManual(readableTex);
                if (!skinTex2D.isReadable)
                    Object.Destroy(readableTex);

                var modsDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                var dumpDir = Path.Combine(modsDir, "skin_dumps");
                Directory.CreateDirectory(dumpDir);

                var charName = CharacterData.GetCharacterName(currentCharacterId).ToLower().Replace(" ", "_");
                var outputPath = Path.Combine(dumpDir, $"{charName}_skin.png");
                File.WriteAllBytes(outputPath, png);
                _logger.Info($"Saved skin texture to: {outputPath} ({png.Length} bytes)");

                ExportUVTemplate(playerObj, skinPrefix, charName, dumpDir);
            }
            catch (Exception ex)
            {
                _logger.Error($"DumpSkinTexture error: {ex.Message}\n{ex.StackTrace}");
            }
        }

        public void ExportUVTemplate(GameObject playerObj, string skinPrefix, string charName, string dumpDir)
        {
            try
            {
                int templateSize = 1024;
                var template = new Texture2D(templateSize, templateSize, TextureFormat.RGBA32, false);

                var clearPixels = new Color[templateSize * templateSize];
                for (int i = 0; i < clearPixels.Length; i++)
                    clearPixels[i] = new Color(0, 0, 0, 0);
                template.SetPixels(clearPixels);

                Color[] partColors = {
                    new Color(1f, 0.2f, 0.2f, 1f),
                    new Color(0.2f, 1f, 0.2f, 1f),
                    new Color(0.3f, 0.5f, 1f, 1f),
                    new Color(1f, 1f, 0.2f, 1f),
                    new Color(1f, 0.5f, 0f, 1f),
                    new Color(0.8f, 0.2f, 1f, 1f),
                    new Color(0f, 1f, 1f, 1f),
                    new Color(1f, 0.5f, 0.7f, 1f),
                };

                var renderers = playerObj.GetComponentsInChildren<SkinnedMeshRenderer>();
                int partIndex = 0;
                var legend = new List<string>();

                foreach (var r in renderers)
                {
                    if (r == null) continue;
                    var mat = r.material;
                    if (mat == null) continue;
                    var shaderName = mat.shader?.name;
                    if (shaderName == null || shaderName == "Standard" || shaderName.Contains("Eyes")) continue;
                    var matName = mat.name ?? "";
                    if (!matName.StartsWith(skinPrefix, StringComparison.OrdinalIgnoreCase)) continue;

                    var mesh = r.sharedMesh;
                    if (mesh == null) continue;

                    if (!mesh.isReadable)
                    {
                        mesh = TextureUtils.BakeMeshReadable(r, _logger);
                        if (mesh == null) continue;
                    }

                    var uvs = mesh.uv;
                    var triangles = mesh.triangles;
                    if (uvs == null || uvs.Length == 0 || triangles == null || triangles.Length == 0)
                        continue;

                    Color color = partColors[partIndex % partColors.Length];
                    string colorName = partIndex < partColors.Length
                        ? new[] { "Red", "Green", "Blue", "Yellow", "Orange", "Purple", "Cyan", "Pink" }[partIndex]
                        : $"Color {partIndex}";

                    legend.Add($"  {colorName} = {r.gameObject.name}");

                    for (int t = 0; t < triangles.Length; t += 3)
                    {
                        int i0 = triangles[t], i1 = triangles[t + 1], i2 = triangles[t + 2];
                        if (i0 >= uvs.Length || i1 >= uvs.Length || i2 >= uvs.Length) continue;

                        DrawLineUV(template, uvs[i0], uvs[i1], templateSize, color);
                        DrawLineUV(template, uvs[i1], uvs[i2], templateSize, color);
                        DrawLineUV(template, uvs[i2], uvs[i0], templateSize, color);
                    }

                    partIndex++;
                }

                if (partIndex == 0)
                {
                    Object.Destroy(template);
                    return;
                }

                template.Apply();
                byte[] pngData = PngEncoder.EncodeToPngManual(template);
                Object.Destroy(template);

                var outputPath = Path.Combine(dumpDir, $"{charName}_uv_template.png");
                File.WriteAllBytes(outputPath, pngData);

                var legendPath = Path.Combine(dumpDir, $"{charName}_uv_legend.txt");
                File.WriteAllText(legendPath, $"UV Template Legend - {charName}\n\n" + string.Join("\n", legend) + "\n");
            }
            catch (Exception ex)
            {
                _logger.Error($"ExportUVTemplate error: {ex.Message}\n{ex.StackTrace}");
            }
        }

        public Texture2D LoadReskinTexture(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    _logger.Warning($"Reskin file not found: {filePath}");
                    return null;
                }

                byte[] data = File.ReadAllBytes(filePath);
                var ext = Path.GetExtension(filePath).ToLower();

                if (ext == ".bmp")
                {
                    var texture = LoadBmpData(data);
                    if (texture == null)
                        _logger.Warning($"Failed to parse BMP: {filePath}");
                    return texture;
                }

                var tex = new Texture2D(2, 2);
                if (ImageConversion.LoadImage(tex, data))
                    return tex;

                _logger.Warning($"Failed to load reskin image: {filePath}");
                return null;
            }
            catch (Exception ex)
            {
                _logger.Error($"Error loading reskin texture {filePath}: {ex.Message}");
                return null;
            }
        }

        public SkinEntry? FindSkinEntry(string filePath)
        {
            foreach (var kvp in AvailableSkins)
            {
                for (int i = 0; i < kvp.Value.Count; i++)
                {
                    if (kvp.Value[i].FilePath == filePath)
                        return kvp.Value[i];
                }
            }
            return null;
        }

        public string GetCharacterReskinKey(int characterId)
        {
            if (CharacterData.CharacterSkinMaterials.TryGetValue(characterId, out var prefix))
                return CharacterData.GetModelKey(prefix);
            return CharacterData.GetCharacterName(characterId).ToLower().Replace(" ", "_");
        }

        public Texture2D GetSkinIcon(string iconPath)
        {
            if (iconPath == null) return null;
            if (!_skinIconCache.TryGetValue(iconPath, out var icon))
            {
                icon = LoadReskinTexture(iconPath);
                _skinIconCache[iconPath] = icon;
            }
            return icon;
        }

        private void RestoreOriginal(int characterId, SkinnedMeshRenderer[] renderers, string skinPrefix)
        {
            if (_originalSkinTextures.TryGetValue(characterId, out var origTex) && origTex != null)
            {
                foreach (var r in renderers)
                {
                    if (r == null) continue;
                    var mat = r.material;
                    if (mat == null) continue;
                    var shaderName = mat.shader?.name;
                    if (shaderName == null || shaderName == "Standard" || shaderName.Contains("Eyes")) continue;
                    var matName = mat.name ?? "";
                    if (skinPrefix != null && !matName.StartsWith(skinPrefix, StringComparison.OrdinalIgnoreCase)) continue;

                    if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", origTex);
                    if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", origTex);
                }
            }
        }

        private Texture2D GetOriginalTexture(int characterId, SkinnedMeshRenderer[] renderers, string skinPrefix)
        {
            if (_originalSkinTextures.TryGetValue(characterId, out var cached) && cached != null)
            {
                var tex2d = cached.TryCast<Texture2D>();
                if (tex2d != null) return tex2d;
            }

            foreach (var r in renderers)
            {
                if (r == null) continue;
                var mat = r.material;
                if (mat == null) continue;
                var sn = mat.shader?.name;
                if (sn == null || sn == "Standard" || sn.Contains("Eyes")) continue;
                var mn = mat.name ?? "";
                if (skinPrefix != null && !mn.StartsWith(skinPrefix, StringComparison.OrdinalIgnoreCase)) continue;

                Texture tex = null;
                if (mat.HasProperty("_BaseMap")) tex = mat.GetTexture("_BaseMap");
                else if (mat.HasProperty("_MainTex")) tex = mat.GetTexture("_MainTex");

                if (tex != null)
                {
                    _originalSkinTextures[characterId] = tex;
                    return tex.TryCast<Texture2D>();
                }
            }
            return null;
        }

        private Texture2D GenerateProceduralSkin(Texture2D original, string jsonPath)
        {
            try
            {
                var json = File.ReadAllText(jsonPath);
                var definition = JsonSerializer.Deserialize<SkinDefinition>(json);
                if (definition?.Transforms == null || definition.Transforms.Count == 0)
                {
                    _logger.Warning($"Procedural skin has no transforms: {jsonPath}");
                    return null;
                }

                Texture2D src = original.isReadable ? original : TextureUtils.CopyTextureToReadable(original);
                int w = src.width, h = src.height;
                var result = new Texture2D(w, h, TextureFormat.RGBA32, false);

                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        Color c = src.GetPixel(x, y);
                        float brightness = 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;

                        Color modified = c;
                        foreach (var transform in definition.Transforms)
                        {
                            if (transform.Where != null)
                            {
                                if (brightness < transform.Where.BrightnessMin || brightness > transform.Where.BrightnessMax)
                                    continue;
                            }

                            float blend = Math.Clamp(transform.Blend, 0f, 1f);

                            switch (transform.Action?.ToLower())
                            {
                                case "recolor":
                                    if (transform.Color is { Length: >= 3 })
                                    {
                                        var target = new Color(transform.Color[0], transform.Color[1], transform.Color[2], modified.a);
                                        modified = new Color(
                                            modified.r + (target.r - modified.r) * blend,
                                            modified.g + (target.g - modified.g) * blend,
                                            modified.b + (target.b - modified.b) * blend,
                                            modified.a);
                                    }
                                    break;

                                case "tint":
                                    if (transform.Color is { Length: >= 3 })
                                    {
                                        var tinted = new Color(
                                            modified.r * transform.Color[0],
                                            modified.g * transform.Color[1],
                                            modified.b * transform.Color[2],
                                            modified.a);
                                        modified = new Color(
                                            modified.r + (tinted.r - modified.r) * blend,
                                            modified.g + (tinted.g - modified.g) * blend,
                                            modified.b + (tinted.b - modified.b) * blend,
                                            modified.a);
                                    }
                                    break;

                                case "hue_shift":
                                    ColorUtils.RgbToHsv(modified.r, modified.g, modified.b, out float hue, out float sat, out float val);
                                    hue = (hue + transform.Degrees * blend / 360f) % 1f;
                                    if (hue < 0) hue += 1f;
                                    ColorUtils.HsvToRgb(hue, sat, val, out float nr, out float ng, out float nb);
                                    modified = new Color(nr, ng, nb, modified.a);
                                    break;
                            }
                        }

                        result.SetPixel(x, y, modified);
                    }
                }

                result.Apply();

                if (!original.isReadable && src != original)
                    Object.Destroy(src);

                return result;
            }
            catch (Exception ex)
            {
                _logger.Error($"GenerateProceduralSkin error: {ex.Message}\n{ex.StackTrace}");
                return null;
            }
        }

        private void MarkSkinGenerated(string filePath)
        {
            foreach (var kvp in AvailableSkins)
            {
                for (int i = 0; i < kvp.Value.Count; i++)
                {
                    if (kvp.Value[i].FilePath == filePath)
                    {
                        var entry = kvp.Value[i];
                        entry.NeedsGeneration = false;
                        kvp.Value[i] = entry;
                        return;
                    }
                }
            }
        }

        private Texture2D LoadBmpData(byte[] data)
        {
            if (data.Length < 54 || data[0] != 0x42 || data[1] != 0x4D)
                return null;

            int pixelOffset = BitConverter.ToInt32(data, 10);
            int width = BitConverter.ToInt32(data, 18);
            int height = BitConverter.ToInt32(data, 22);
            int bpp = BitConverter.ToInt16(data, 28);

            if (bpp != 32 || width <= 0 || height <= 0)
            {
                _logger.Warning($"BMP loader only supports 32bpp, got {bpp}bpp ({width}x{height})");
                return null;
            }

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);

            int stride = width * 4;
            for (int y = 0; y < height; y++)
            {
                int rowStart = pixelOffset + y * stride;
                for (int x = 0; x < width; x++)
                {
                    int i = rowStart + x * 4;
                    float b = data[i] / 255f;
                    float g = data[i + 1] / 255f;
                    float r = data[i + 2] / 255f;
                    float a = data[i + 3] / 255f;
                    texture.SetPixel(x, y, new Color(r, g, b, a));
                }
            }
            texture.Apply();
            return texture;
        }

        private static string FindIconPath(string charDir, string skinName)
        {
            foreach (var iconExt in new[] { ".png", ".jpg", ".bmp" })
            {
                var candidate = Path.Combine(charDir, skinName + "_icon" + iconExt);
                if (File.Exists(candidate)) return candidate;
            }
            return null;
        }

        private static string FormatDisplayName(string name)
        {
            return string.Join(" ",
                name.Split('_').Select(w => w.Length > 0 ? char.ToUpper(w[0]) + w.Substring(1) : w));
        }

        private static void DrawLineUV(Texture2D tex, Vector2 uv0, Vector2 uv1, int size, Color color)
        {
            int x0 = (int)(uv0.x * (size - 1));
            int y0 = (int)(uv0.y * (size - 1));
            int x1 = (int)(uv1.x * (size - 1));
            int y1 = (int)(uv1.y * (size - 1));

            x0 = Math.Clamp(x0, 0, size - 1); y0 = Math.Clamp(y0, 0, size - 1);
            x1 = Math.Clamp(x1, 0, size - 1); y1 = Math.Clamp(y1, 0, size - 1);

            int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
            int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
            int err = dx + dy;

            while (true)
            {
                tex.SetPixel(x0, y0, color);
                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err;
                if (e2 >= dy) { err += dy; x0 += sx; }
                if (e2 <= dx) { err += dx; y0 += sy; }
            }
        }
    }
}
