using UnityEngine;
using YetiHunt.Yeti;

namespace YetiHunt.Combat
{
    /// <summary>
    /// Event data for yeti hit events.
    /// </summary>
    public class HitEventArgs
    {
        public HuntYeti Yeti { get; }
        public Vector3 HitPosition { get; }
        public string ThrowerName { get; }

        public HitEventArgs(HuntYeti yeti, Vector3 hitPosition, string throwerName)
        {
            Yeti = yeti;
            HitPosition = hitPosition;
            ThrowerName = throwerName;
        }
    }
}
