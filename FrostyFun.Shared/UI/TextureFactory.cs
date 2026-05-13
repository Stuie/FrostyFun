using UnityEngine;

namespace FrostyFun.Shared.UI
{
    public static class TextureFactory
    {
        public static Texture2D MakeSolid(int width, int height, Color color)
        {
            Color[] pixels = new Color[width * height];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = color;

            var texture = new Texture2D(width, height);
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        public static Texture2D MakeSolid(Color color) => MakeSolid(2, 2, color);

        public static Texture2D MakeCircle(int size, Color color)
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
    }
}
