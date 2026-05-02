using UnityEngine;

namespace CharacterSelect.UI
{
    public static class TextureFactory
    {
        public static Texture2D MakeTexture(int width, int height, Color color)
        {
            Color[] pixels = new Color[width * height];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = color;
            Texture2D texture = new Texture2D(width, height);
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        public static Texture2D MakeCursorTexture()
        {
            int size = 16;
            Texture2D tex = new Texture2D(size, size);
            Color transparent = new Color(0, 0, 0, 0);
            Color white = Color.white;

            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    tex.SetPixel(x, y, transparent);

            tex.SetPixel(0, 15, white);
            tex.SetPixel(0, 14, white); tex.SetPixel(1, 14, white);
            tex.SetPixel(0, 13, white); tex.SetPixel(1, 13, white); tex.SetPixel(2, 13, white);
            tex.SetPixel(0, 12, white); tex.SetPixel(1, 12, white); tex.SetPixel(2, 12, white); tex.SetPixel(3, 12, white);
            tex.SetPixel(0, 11, white); tex.SetPixel(1, 11, white); tex.SetPixel(2, 11, white); tex.SetPixel(3, 11, white); tex.SetPixel(4, 11, white);
            tex.SetPixel(0, 10, white); tex.SetPixel(1, 10, white); tex.SetPixel(2, 10, white); tex.SetPixel(3, 10, white); tex.SetPixel(4, 10, white); tex.SetPixel(5, 10, white);
            tex.SetPixel(0, 9, white); tex.SetPixel(1, 9, white); tex.SetPixel(2, 9, white); tex.SetPixel(3, 9, white); tex.SetPixel(4, 9, white); tex.SetPixel(5, 9, white); tex.SetPixel(6, 9, white);
            tex.SetPixel(0, 8, white); tex.SetPixel(1, 8, white); tex.SetPixel(2, 8, white); tex.SetPixel(3, 8, white); tex.SetPixel(4, 8, white);
            tex.SetPixel(0, 7, white); tex.SetPixel(1, 7, white); tex.SetPixel(2, 7, white); tex.SetPixel(4, 7, white); tex.SetPixel(5, 7, white);
            tex.SetPixel(0, 6, white); tex.SetPixel(1, 6, white); tex.SetPixel(5, 6, white); tex.SetPixel(6, 6, white);
            tex.SetPixel(0, 5, white); tex.SetPixel(6, 5, white); tex.SetPixel(7, 5, white);
            tex.SetPixel(7, 4, white); tex.SetPixel(8, 4, white);

            tex.Apply();
            return tex;
        }

        public static Texture2D MakeWrenchTexture()
        {
            int size = 16;
            var tex = new Texture2D(size, size);
            var clear = new Color(0, 0, 0, 0);
            var white = Color.white;

            for (int py = 0; py < size; py++)
                for (int px = 0; px < size; px++)
                    tex.SetPixel(px, py, clear);

            int[][] handle = { new[]{2,1}, new[]{3,2}, new[]{4,3}, new[]{5,4}, new[]{6,5}, new[]{7,6}, new[]{8,7},
                               new[]{3,1}, new[]{4,2}, new[]{5,3}, new[]{6,4}, new[]{7,5}, new[]{8,6}, new[]{9,7} };
            foreach (var p in handle) tex.SetPixel(p[0], p[1], white);

            int[][] head = { new[]{9,8}, new[]{10,9}, new[]{11,10}, new[]{12,11}, new[]{13,12},
                             new[]{10,8}, new[]{11,9}, new[]{12,10}, new[]{13,11}, new[]{14,12},
                             new[]{13,13}, new[]{14,13},
                             new[]{11,12}, new[]{10,11}, new[]{9,10}, new[]{9,9},
                             new[]{12,13}, new[]{11,13}, new[]{10,12}, new[]{10,10} };
            foreach (var p in head) tex.SetPixel(p[0], p[1], white);

            tex.SetPixel(1, 1, white);
            tex.SetPixel(2, 0, white);
            tex.SetPixel(1, 0, white);

            tex.Apply();
            return tex;
        }
    }
}
