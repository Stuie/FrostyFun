using System;
using System.IO;
using SysCompressionLevel = System.IO.Compression.CompressionLevel;
using System.IO.Compression;
using UnityEngine;

namespace CharacterSelect.Imaging
{
    public static class PngEncoder
    {
        public static byte[] EncodeToPngManual(Texture2D tex)
        {
            int w = tex.width, h = tex.height;

            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);

            // PNG signature
            bw.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

            // IHDR chunk
            WriteChunk(bw, "IHDR", writer =>
            {
                writer.Write(ToBigEndian(w));
                writer.Write(ToBigEndian(h));
                writer.Write((byte)8);  // bit depth
                writer.Write((byte)6);  // color type: RGBA
                writer.Write((byte)0);  // compression
                writer.Write((byte)0);  // filter
                writer.Write((byte)0);  // interlace
            });

            // IDAT chunk
            byte[] rawData;
            using (var rawMs = new MemoryStream())
            {
                for (int y = h - 1; y >= 0; y--)
                {
                    rawMs.WriteByte(0); // filter: None
                    for (int x = 0; x < w; x++)
                    {
                        var c = tex.GetPixel(x, y);
                        rawMs.WriteByte((byte)(c.r * 255f));
                        rawMs.WriteByte((byte)(c.g * 255f));
                        rawMs.WriteByte((byte)(c.b * 255f));
                        rawMs.WriteByte((byte)(c.a * 255f));
                    }
                }
                rawData = rawMs.ToArray();
            }

            byte[] compressedData;
            using (var compMs = new MemoryStream())
            {
                compMs.WriteByte(0x78);
                compMs.WriteByte(0x01);
                using (var deflate = new DeflateStream(compMs, SysCompressionLevel.Fastest, leaveOpen: true))
                {
                    deflate.Write(rawData, 0, rawData.Length);
                }
                uint adler = Adler32(rawData);
                compMs.WriteByte((byte)((adler >> 24) & 0xFF));
                compMs.WriteByte((byte)((adler >> 16) & 0xFF));
                compMs.WriteByte((byte)((adler >> 8) & 0xFF));
                compMs.WriteByte((byte)(adler & 0xFF));
                compressedData = compMs.ToArray();
            }

            WriteChunk(bw, "IDAT", writer => writer.Write(compressedData));
            WriteChunk(bw, "IEND", _ => { });

            return ms.ToArray();
        }

        private static void WriteChunk(BinaryWriter bw, string type, Action<BinaryWriter> writeData)
        {
            using var dataMs = new MemoryStream();
            using (var dataWriter = new BinaryWriter(dataMs, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                writeData(dataWriter);
            }
            byte[] data = dataMs.ToArray();
            byte[] typeBytes = System.Text.Encoding.ASCII.GetBytes(type);

            bw.Write(ToBigEndian(data.Length));
            bw.Write(typeBytes);
            bw.Write(data);

            uint crc = Crc32(typeBytes, data);
            bw.Write(ToBigEndian((int)crc));
        }

        private static byte[] ToBigEndian(int value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
            return bytes;
        }

        private static uint Adler32(byte[] data)
        {
            uint a = 1, b = 0;
            foreach (byte d in data)
            {
                a = (a + d) % 65521;
                b = (b + a) % 65521;
            }
            return (b << 16) | a;
        }

        private static uint Crc32(byte[] typeBytes, byte[] data)
        {
            uint crc = 0xFFFFFFFF;
            foreach (byte b in typeBytes) crc = Crc32Update(crc, b);
            foreach (byte b in data) crc = Crc32Update(crc, b);
            return crc ^ 0xFFFFFFFF;
        }

        private static uint Crc32Update(uint crc, byte b)
        {
            crc ^= b;
            for (int i = 0; i < 8; i++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
            return crc;
        }
    }
}
