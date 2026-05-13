using UnityEngine;

namespace FrostyFun.Shared.Players
{
    public static class PlayerLocator
    {
        public const string LocalPlayerObjectName = "Player Networked(Clone)";

        public static GameObject FindLocal() => GameObject.Find(LocalPlayerObjectName);
    }
}
