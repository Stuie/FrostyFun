using UnityEngine;

namespace FrostyFun.Shared.UI
{
    public struct CursorSnapshot
    {
        public bool Visible;
        public CursorLockMode LockState;
    }

    public static class CursorState
    {
        public static CursorSnapshot Snapshot() => new CursorSnapshot
        {
            Visible = Cursor.visible,
            LockState = Cursor.lockState,
        };

        public static void Restore(CursorSnapshot snapshot)
        {
            Cursor.visible = snapshot.Visible;
            Cursor.lockState = snapshot.LockState;
        }

        public static void ShowFree()
        {
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
            Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
        }
    }
}
