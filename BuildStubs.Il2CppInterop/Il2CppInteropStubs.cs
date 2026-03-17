// Il2CppInterop stubs for CI builds
// At runtime, the real Il2CppInterop.Runtime provides these
// These stubs just allow compilation - the code paths using them won't execute in CI

// ReSharper disable All
#pragma warning disable

using System;
using UnityEngine;

namespace Il2CppInterop.Runtime
{
    public static class Il2CppObjectExtensions
    {
        // Stub for TryCast<T> extension method
        public static T TryCast<T>(this UnityEngine.Object obj) where T : class
        {
            return obj as T;
        }

        // Stub for GetIl2CppType extension method
        public static Il2CppType GetIl2CppType(this object obj)
        {
            return new Il2CppType(obj?.GetType()?.Name ?? "Unknown");
        }
    }

    // Minimal Il2CppType stub
    public class Il2CppType
    {
        public string Name { get; }

        public Il2CppType(string name)
        {
            Name = name;
        }
    }
}

