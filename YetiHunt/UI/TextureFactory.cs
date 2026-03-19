using System;
using System.Reflection;
using UnityEngine;
using YetiHunt.Infrastructure;

namespace YetiHunt.UI
{
    /// <summary>
    /// Utility class for creating textures.
    /// </summary>
    public class TextureFactory
    {
        private readonly IModLogger _logger;

        public TextureFactory(IModLogger logger)
        {
            _logger = logger;
        }

        public Texture2D MakeTexture(int width, int height, Color color)
        {
            Color[] pixels = new Color[width * height];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = color;

            Texture2D texture = new Texture2D(width, height);
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        public Texture2D MakeCircleTexture(int size, Color color)
        {
            var texture = new Texture2D(size, size);
            float radius = size / 2f;
            float radiusSq = radius * radius;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - radius + 0.5f;
                    float dy = y - radius + 0.5f;
                    float distSq = dx * dx + dy * dy;

                    if (distSq <= radiusSq)
                    {
                        float dist = Mathf.Sqrt(distSq);
                        float alpha = Mathf.Clamp01((radius - dist) / 2f) * color.a;
                        texture.SetPixel(x, y, new Color(color.r, color.g, color.b, alpha));
                    }
                    else
                    {
                        texture.SetPixel(x, y, Color.clear);
                    }
                }
            }

            texture.Apply();
            return texture;
        }

        public Texture2D LoadEmbeddedTexture(string fileName)
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                var resourceName = $"YetiHunt.Assets.{fileName}";

                using (var stream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream == null)
                    {
                        _logger.Warning($"Embedded resource not found: {resourceName}");
                        var names = assembly.GetManifestResourceNames();
                        _logger.Info($"Available resources: {string.Join(", ", names)}");
                        return null;
                    }

                    byte[] data = new byte[stream.Length];
                    stream.Read(data, 0, data.Length);

                    var texture = new Texture2D(2, 2);
                    if (ImageConversion.LoadImage(texture, data))
                    {
                        return texture;
                    }
                    else
                    {
                        _logger.Warning($"Failed to decode image: {fileName}");
                        return null;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"LoadEmbeddedTexture failed: {ex.Message}");
                return null;
            }
        }
    }
}
