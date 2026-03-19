using System;
using UnityEngine;

namespace YetiHunt.Debug
{
    /// <summary>
    /// Handles debug key inputs (Ctrl+1-8).
    /// </summary>
    public class InputHandler
    {
        public event Action OnStartStopRound;
        public event Action OnTestSpawnYeti;
        public event Action OnDumpMapInfo;
        public event Action OnDumpMapCoordinateDebug;
        public event Action OnToggleBoundaryProtection;
        public event Action OnRecordCorner;
        public event Action OnDumpPlayerInfo;
        public event Action OnShowRecordedCorners;

        public void HandleInput()
        {
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            if (!ctrl) return;

            if (Input.GetKeyDown(KeyCode.Alpha1))
                OnStartStopRound?.Invoke();

            if (Input.GetKeyDown(KeyCode.Alpha2))
                OnTestSpawnYeti?.Invoke();

            if (Input.GetKeyDown(KeyCode.Alpha3))
                OnDumpMapInfo?.Invoke();

            if (Input.GetKeyDown(KeyCode.Alpha4))
                OnDumpMapCoordinateDebug?.Invoke();

            if (Input.GetKeyDown(KeyCode.Alpha5))
                OnToggleBoundaryProtection?.Invoke();

            if (Input.GetKeyDown(KeyCode.Alpha6))
                OnRecordCorner?.Invoke();

            if (Input.GetKeyDown(KeyCode.Alpha7))
                OnDumpPlayerInfo?.Invoke();

            if (Input.GetKeyDown(KeyCode.Alpha8))
                OnShowRecordedCorners?.Invoke();
        }
    }
}
