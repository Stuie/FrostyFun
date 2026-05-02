using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace RespawnFlags.Services
{
    /// <summary>
    /// Pure logic for managing a list of named markers with positions.
    /// No Unity dependencies — fully unit-testable.
    /// </summary>
    public class MarkerStore
    {
        public const int MaxMarkers = 100;
        public const float DuplicateDistance = 5f;

        private static readonly Regex AutoNamePattern = new(@"^Marker \(-?\d+, -?\d+\)$");

        private readonly List<MarkerEntry> _markers = new();

        // Pending eviction state
        private float[] _pendingPosition;
        private int _pendingEvictIndex = -1;

        public IReadOnlyList<MarkerEntry> Markers => _markers;
        public int Count => _markers.Count;

        public bool HasPendingEviction => _pendingEvictIndex >= 0;
        public string PendingEvictName => _pendingEvictIndex >= 0 && _pendingEvictIndex < _markers.Count
            ? _markers[_pendingEvictIndex].Name : null;

        public struct MarkerEntry
        {
            public string Name;
            public float X, Y, Z;
        }

        public enum AddResult
        {
            Added,
            Duplicate,
            EvictionRequired
        }

        /// <summary>
        /// Try to add a marker. Returns the result status.
        /// </summary>
        public AddResult TryAdd(float x, float y, float z)
        {
            // Check duplicates
            foreach (var m in _markers)
            {
                float dx = m.X - x, dy = m.Y - y, dz = m.Z - z;
                if (Math.Sqrt(dx * dx + dy * dy + dz * dz) < DuplicateDistance)
                    return AddResult.Duplicate;
            }

            // Evict if at capacity
            if (_markers.Count >= MaxMarkers)
            {
                int evictIndex = FindOldestUnnamed();
                if (evictIndex >= 0)
                {
                    _markers.RemoveAt(evictIndex);
                }
                else
                {
                    // All named — need user confirmation
                    _pendingPosition = new[] { x, y, z };
                    _pendingEvictIndex = _markers.Count - 1;
                    return AddResult.EvictionRequired;
                }
            }

            _markers.Insert(0, new MarkerEntry
            {
                Name = $"Marker ({x:F0}, {z:F0})",
                X = x, Y = y, Z = z
            });
            return AddResult.Added;
        }

        public void ConfirmEviction()
        {
            if (_pendingEvictIndex < 0 || _pendingPosition == null) return;

            var pos = _pendingPosition;
            _markers.RemoveAt(_pendingEvictIndex);
            _pendingEvictIndex = -1;
            _pendingPosition = null;

            _markers.Insert(0, new MarkerEntry
            {
                Name = $"Marker ({pos[0]:F0}, {pos[2]:F0})",
                X = pos[0], Y = pos[1], Z = pos[2]
            });
        }

        public void CancelEviction()
        {
            _pendingEvictIndex = -1;
            _pendingPosition = null;
        }

        public void Remove(int index)
        {
            if (index >= 0 && index < _markers.Count)
                _markers.RemoveAt(index);
        }

        public void Rename(int index, string newName)
        {
            if (index < 0 || index >= _markers.Count) return;
            if (string.IsNullOrWhiteSpace(newName)) return;

            var entry = _markers[index];
            entry.Name = newName.Trim();
            _markers[index] = entry;
        }

        public void Clear() => _markers.Clear();

        public void AddRaw(string name, float x, float y, float z)
        {
            _markers.Add(new MarkerEntry { Name = name, X = x, Y = y, Z = z });
        }

        public static bool IsAutoNamed(string name) => AutoNamePattern.IsMatch(name);

        private int FindOldestUnnamed()
        {
            for (int i = _markers.Count - 1; i >= 0; i--)
            {
                if (IsAutoNamed(_markers[i].Name))
                    return i;
            }
            return -1;
        }
    }
}
