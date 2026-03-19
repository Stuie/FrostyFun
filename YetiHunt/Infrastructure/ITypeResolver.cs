using System;
using System.Reflection;

namespace YetiHunt.Infrastructure
{
    /// <summary>
    /// Abstraction for Il2Cpp type discovery and caching.
    /// </summary>
    public interface ITypeResolver
    {
        bool IsInitialized { get; }

        void Initialize();

        Type GetYetiManagerType();
        Type GetYetiType();
        Type GetChatManagerType();
        Type GetPlayerControlType();
        Type GetPlayerTeleportControllerType();
        Type GetSnowballType();

        MethodInfo GetSpawnYetiMethod();
        PropertyInfo GetTeleportControllerProperty();
        MethodInfo GetTeleportPlayerMethod();
    }
}
