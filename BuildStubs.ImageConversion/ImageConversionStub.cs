// ImageConversion stub for CI builds
// At runtime, the real Unity ImageConversion module is used

// ReSharper disable All
#pragma warning disable

namespace UnityEngine
{
    public static class ImageConversion
    {
        public static bool LoadImage(Texture2D tex, byte[] data) => false;
        public static bool LoadImage(Texture2D tex, byte[] data, bool markNonReadable) => false;
    }
}
