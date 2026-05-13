using UnityEngine;

namespace FrostyFun.Shared.Il2Cpp
{
    public static class Il2CppExtensions
    {
        public static string GetIl2CppTypeName(this Component comp)
        {
            if (comp == null) return null;
            try
            {
                var il2cppType = comp.GetIl2CppType();
                return il2cppType?.Name ?? comp.GetType().Name;
            }
            catch
            {
                return comp.GetType().Name;
            }
        }
    }
}
