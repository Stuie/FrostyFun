using System;
using System.Reflection;

namespace CharacterSelect.Infrastructure
{
    public interface ITypeResolver
    {
        bool IsInitialized { get; }
        void Initialize();
        Type GetPlayerControlType();
        MethodInfo GetCmdSwitchCharacterMethod();
        PropertyInfo GetSyncEquippedCharacterNameProperty();
    }
}
