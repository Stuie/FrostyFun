using System;

namespace CharacterSelect.Imaging
{
    public static class ColorUtils
    {
        public static void RgbToHsv(float r, float g, float b, out float h, out float s, out float v)
        {
            float max = Math.Max(r, Math.Max(g, b));
            float min = Math.Min(r, Math.Min(g, b));
            float delta = max - min;

            v = max;
            s = max > 0 ? delta / max : 0;

            if (delta == 0) { h = 0; return; }

            if (max == r) h = (g - b) / delta;
            else if (max == g) h = 2 + (b - r) / delta;
            else h = 4 + (r - g) / delta;

            h /= 6f;
            if (h < 0) h += 1f;
        }

        public static void HsvToRgb(float h, float s, float v, out float r, out float g, out float b)
        {
            if (s == 0) { r = g = b = v; return; }

            h *= 6f;
            int i = (int)Math.Floor(h);
            float f = h - i;
            float p = v * (1 - s);
            float q = v * (1 - s * f);
            float t = v * (1 - s * (1 - f));

            switch (i % 6)
            {
                case 0: r = v; g = t; b = p; return;
                case 1: r = q; g = v; b = p; return;
                case 2: r = p; g = v; b = t; return;
                case 3: r = p; g = q; b = v; return;
                case 4: r = t; g = p; b = v; return;
                default: r = v; g = p; b = q; return;
            }
        }
    }
}
