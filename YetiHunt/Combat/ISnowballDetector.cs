using System;
using System.Collections.Generic;
using YetiHunt.Yeti;

namespace YetiHunt.Combat
{
    /// <summary>
    /// Interface for detecting snowball hits on yetis.
    /// </summary>
    public interface ISnowballDetector
    {
        void CheckForHits(IReadOnlyList<HuntYeti> yetis);
        void ClearTracking();

        event Action<HitEventArgs> OnSnowballHit;
    }
}
