using System;
using UnityEngine;

namespace CharacterSelect.Imaging
{
    public static class BmpEncoder
    {
        public static byte[] EncodeToBmp(Texture2D tex)
        {
            int w = tex.width, h = tex.height;
            int pixelDataSize = w * h * 4;
            int fileSize = 54 + pixelDataSize;
            byte[] bmp = new byte[fileSize];

            // BMP file header
            bmp[0] = 0x42; bmp[1] = 0x4D;
            BitConverter.GetBytes(fileSize).CopyTo(bmp, 2);
            BitConverter.GetBytes(54).CopyTo(bmp, 10);

            // DIB header
            BitConverter.GetBytes(40).CopyTo(bmp, 14);
            BitConverter.GetBytes(w).CopyTo(bmp, 18);
            BitConverter.GetBytes(h).CopyTo(bmp, 22);
            bmp[26] = 1; bmp[28] = 32;

            int offset = 54;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    var c = tex.GetPixel(x, y);
                    bmp[offset++] = (byte)(c.b * 255f);
                    bmp[offset++] = (byte)(c.g * 255f);
                    bmp[offset++] = (byte)(c.r * 255f);
                    bmp[offset++] = (byte)(c.a * 255f);
                }
            }
            return bmp;
        }
    }
}
