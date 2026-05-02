using System;
using UnityEngine;
using CharacterSelect.Infrastructure;
using Object = UnityEngine.Object;

namespace CharacterSelect.Imaging
{
    public static class TextureUtils
    {
        public static Texture2D CopyTextureToReadable(Texture2D source)
        {
            int w = source.width, h = source.height;
            var rt = new RenderTexture(w, h, 0);
            rt.Create();
            Graphics.Blit(source, rt);

            var prevRT = RenderTexture.active;
            RenderTexture.active = rt;

            var readable = new Texture2D(w, h, TextureFormat.RGBA32, false);
            readable.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            readable.Apply();

            RenderTexture.active = prevRT;
            rt.Release();
            Object.Destroy(rt);
            return readable;
        }

        public static Mesh BakeMeshReadable(SkinnedMeshRenderer smr, IModLogger logger)
        {
            try
            {
                var baked = new Mesh();
                smr.BakeMesh(baked);
                return baked;
            }
            catch (Exception ex)
            {
                logger.Warning($"BakeMesh failed for '{smr.gameObject.name}': {ex.Message}");
                return null;
            }
        }
    }
}
