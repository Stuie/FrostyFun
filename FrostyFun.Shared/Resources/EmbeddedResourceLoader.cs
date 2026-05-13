using System;
using System.Reflection;
using UnityEngine;
using FrostyFun.Shared.Logging;

namespace FrostyFun.Shared.Resources
{
    public static class EmbeddedResourceLoader
    {
        /// <summary>
        /// Loads an embedded PNG/JPG resource from the given assembly and decodes it into a Texture2D.
        /// Caller must pass its own Assembly explicitly — Assembly.GetCallingAssembly is unreliable
        /// after this code is ILRepack-internalized into another assembly.
        /// </summary>
        public static Texture2D LoadTexture(Assembly assembly, string resourceName, IModLogger logger = null)
        {
            try
            {
                using (var stream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream == null)
                    {
                        if (logger != null)
                        {
                            logger.Warning($"Embedded resource not found: {resourceName}");
                            var names = assembly.GetManifestResourceNames();
                            logger.Info($"Available resources: {string.Join(", ", names)}");
                        }
                        return null;
                    }

                    byte[] data = new byte[stream.Length];
                    stream.Read(data, 0, data.Length);

                    var texture = new Texture2D(2, 2);
                    if (ImageConversion.LoadImage(texture, data))
                        return texture;

                    logger?.Warning($"Failed to decode image: {resourceName}");
                    return null;
                }
            }
            catch (Exception ex)
            {
                logger?.Error($"EmbeddedResourceLoader.LoadTexture failed: {ex.Message}");
                return null;
            }
        }
    }
}
