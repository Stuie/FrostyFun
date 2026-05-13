using System;
using System.Collections.Generic;
using System.Reflection;
using FrostyFun.Shared.Logging;

namespace FrostyFun.Shared.Il2Cpp
{
    /// <summary>
    /// Caches Type lookups against Assembly-CSharp by simple type name.
    /// The first call triggers a one-shot scan of every type in the assembly.
    /// </summary>
    public class Il2CppTypeResolver
    {
        public const string GameAssemblyName = "Assembly-CSharp";

        private readonly IModLogger _logger;
        private readonly Dictionary<string, Type> _byName = new Dictionary<string, Type>();
        private bool _scanned;

        public Il2CppTypeResolver(IModLogger logger)
        {
            _logger = logger;
        }

        public Type GetType(string simpleName)
        {
            if (string.IsNullOrEmpty(simpleName)) return null;
            EnsureScanned();
            _byName.TryGetValue(simpleName, out var type);
            return type;
        }

        private void EnsureScanned()
        {
            if (_scanned) return;
            _scanned = true;
            try
            {
                var assembly = Assembly.Load(GameAssemblyName);
                foreach (var type in assembly.GetTypes())
                {
                    // Last writer wins on collisions; Assembly-CSharp can have duplicate simple names
                    // across namespaces (rare in this game). Consumers needing namespace disambiguation
                    // should reach for assembly.GetType(fullName) directly.
                    _byName[type.Name] = type;
                }
            }
            catch (Exception ex)
            {
                _logger?.Error($"Il2CppTypeResolver scan failed: {ex.Message}");
            }
        }
    }
}
