using UnityEngine;

namespace YetiHunt.UI
{
    /// <summary>
    /// Interface for minimap rendering.
    /// </summary>
    public interface IMinimapRenderer
    {
        void Initialize();
        void Draw();
        Vector2 WorldToMapCoordinates(Vector3 worldPos);
    }
}
