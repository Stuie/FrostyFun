using System;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;

namespace RespawnFlags.Services
{
    public enum SpawnPointType
    {
        RaceStart,
        Lodge,
        UserMarker
    }

    public struct SpawnPoint
    {
        public string Name;
        public Vector3 Position;
        public SpawnPointType Type;
    }

    public class SpawnPointService
    {
        private readonly MelonLogger.Instance _logger;
        private readonly MarkerStore _store = new();

        private const string HistoryPrefKey = "RespawnFlags_History";
        private const string HistoryCountKey = "RespawnFlags_HistoryCount";
        private const string LastUsedPrefKey = "RespawnFlags_LastUsed";
        private const float MarkerCheckInterval = 1.0f;

        private readonly List<SpawnPoint> _raceStarts = new();
        private SpawnPoint? _lodge;
        private SpawnPoint? _lastUsedPoint;

        private bool _fixedPointsScanned;
        private Vector3 _lastKnownMarkerPos;
        private float _lastMarkerCheckTime;

        public bool IsScanned => _fixedPointsScanned;
        public bool HasPendingEviction => _store.HasPendingEviction;
        public string PendingEvictName => _store.PendingEvictName;
        public int MarkerCount => _store.Count;

        public SpawnPointService(MelonLogger.Instance logger)
        {
            _logger = logger;
        }

        public void Reset()
        {
            _raceStarts.Clear();
            _lodge = null;
            _fixedPointsScanned = false;
            _lastKnownMarkerPos = Vector3.zero;
        }

        public bool TryScanFixedPoints()
        {
            if (_fixedPointsScanned) return true;

            var racesObj = GameObject.Find("World/Races");
            if (racesObj == null) return false;

            var races = new[]
            {
                ("yellow", "Yellow: Do A Trick! / Frozen Feet"),
                ("green",  "Green: Full Course / Split Slopes"),
                ("red",    "Red: Black Hole"),
                ("blue",   "Blue: Waterfall"),
                ("purple", "Purple: Beeline"),
                ("wooden", "Wooden Shack: Bunny Run"),
                ("uphill", "Uphill: Long Trek"),
            };
            foreach (var (color, displayName) in races)
            {
                var flagObj = GameObject.Find($"World/Races/{color} race/RaceFlag");
                if (flagObj != null)
                {
                    _raceStarts.Add(new SpawnPoint
                    {
                        Name = displayName,
                        Position = flagObj.transform.position,
                        Type = SpawnPointType.RaceStart
                    });
                }
            }

            var lodgeObj = GameObject.Find("World/Lodge");
            if (lodgeObj != null)
            {
                _lodge = new SpawnPoint
                {
                    Name = "Lodge",
                    Position = lodgeObj.transform.position,
                    Type = SpawnPointType.Lodge
                };
            }

            _fixedPointsScanned = true;
            _logger.Msg($"Scanned {_raceStarts.Count} race starts, lodge: {(_lodge != null ? "found" : "not found")}");
            return true;
        }

        public void UpdateUserMarker()
        {
            if (Time.time - _lastMarkerCheckTime < MarkerCheckInterval) return;
            _lastMarkerCheckTime = Time.time;

            var markerObj = GameObject.Find("Respawn Marker Flag(Clone)");
            if (markerObj == null) return;

            var pos = markerObj.transform.position;

            if (Vector3.Distance(pos, _lastKnownMarkerPos) > 5f)
            {
                _lastKnownMarkerPos = pos;

                // Add new marker position at front of the unified list
                var result = _store.TryAdd(pos.x, pos.y, pos.z);
                if (result == MarkerStore.AddResult.Added)
                    SaveHistory();

                // New marker automatically becomes the quick-respawn point
                SetLastUsedPoint(new SpawnPoint
                {
                    Name = _store.Markers[0].Name,
                    Position = pos,
                    Type = SpawnPointType.UserMarker
                });
            }
        }

        public void ConfirmEviction()
        {
            _store.ConfirmEviction();
            SaveHistory();
        }

        public void CancelEviction() => _store.CancelEviction();

        public void RemoveMarker(int index)
        {
            _store.Remove(index);
            SaveHistory();
        }

        public void RenameMarker(int index, string newName)
        {
            _store.Rename(index, newName);
            SaveHistory();

            // Update lastUsedPoint name if it matches this marker's position
            if (_lastUsedPoint != null && index < _store.Count)
            {
                var m = _store.Markers[index];
                var lastPos = _lastUsedPoint.Value.Position;
                if (Vector3.Distance(lastPos, new Vector3(m.X, m.Y, m.Z)) < 5f)
                {
                    SetLastUsedPoint(new SpawnPoint
                    {
                        Name = m.Name,
                        Position = lastPos,
                        Type = SpawnPointType.UserMarker
                    });
                }
            }
        }

        public List<SpawnPoint> GetAllSpawnPoints()
        {
            var points = new List<SpawnPoint>();
            points.AddRange(_raceStarts);
            if (_lodge != null) points.Add(_lodge.Value);

            foreach (var m in _store.Markers)
            {
                points.Add(new SpawnPoint
                {
                    Name = m.Name,
                    Position = new Vector3(m.X, m.Y, m.Z),
                    Type = SpawnPointType.UserMarker
                });
            }
            return points;
        }

        public SpawnPoint? GetLastUsedPoint() => _lastUsedPoint;

        public SpawnPoint? GetQuickRespawnPoint()
        {
            if (_lastUsedPoint != null) return _lastUsedPoint;
            return _lodge;
        }

        public void SetLastUsedPoint(SpawnPoint point)
        {
            _lastUsedPoint = point;
            try
            {
                PlayerPrefs.SetFloat(LastUsedPrefKey + "_x", point.Position.x);
                PlayerPrefs.SetFloat(LastUsedPrefKey + "_y", point.Position.y);
                PlayerPrefs.SetFloat(LastUsedPrefKey + "_z", point.Position.z);
                PlayerPrefs.SetString(LastUsedPrefKey + "_name", point.Name);
                PlayerPrefs.Save();
            }
            catch { }
        }

        public void LoadHistory()
        {
            _store.Clear();
            try
            {
                int count = PlayerPrefs.GetInt(HistoryCountKey, 0);
                for (int i = 0; i < count; i++)
                {
                    string key = $"{HistoryPrefKey}_{i}";
                    if (PlayerPrefs.HasKey(key + "_x"))
                    {
                        _store.AddRaw(
                            PlayerPrefs.GetString(key + "_name", $"Marker {i + 1}"),
                            PlayerPrefs.GetFloat(key + "_x"),
                            PlayerPrefs.GetFloat(key + "_y"),
                            PlayerPrefs.GetFloat(key + "_z"));
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Failed to load marker history: {ex.Message}");
            }

            try
            {
                if (PlayerPrefs.HasKey(LastUsedPrefKey + "_x"))
                {
                    _lastUsedPoint = new SpawnPoint
                    {
                        Name = PlayerPrefs.GetString(LastUsedPrefKey + "_name", "Last Used"),
                        Position = new Vector3(
                            PlayerPrefs.GetFloat(LastUsedPrefKey + "_x"),
                            PlayerPrefs.GetFloat(LastUsedPrefKey + "_y"),
                            PlayerPrefs.GetFloat(LastUsedPrefKey + "_z")),
                        Type = SpawnPointType.UserMarker
                    };
                }
            }
            catch { }

            _logger.Msg($"Loaded {_store.Count} saved markers");
        }

        private void SaveHistory()
        {
            try
            {
                var markers = _store.Markers;
                PlayerPrefs.SetInt(HistoryCountKey, markers.Count);

                for (int i = 0; i < markers.Count; i++)
                {
                    string key = $"{HistoryPrefKey}_{i}";
                    var m = markers[i];
                    PlayerPrefs.SetFloat(key + "_x", m.X);
                    PlayerPrefs.SetFloat(key + "_y", m.Y);
                    PlayerPrefs.SetFloat(key + "_z", m.Z);
                    PlayerPrefs.SetString(key + "_name", m.Name);
                }

                for (int i = markers.Count; i < MarkerStore.MaxMarkers; i++)
                {
                    string key = $"{HistoryPrefKey}_{i}";
                    if (!PlayerPrefs.HasKey(key + "_x")) break;
                    PlayerPrefs.DeleteKey(key + "_x");
                    PlayerPrefs.DeleteKey(key + "_y");
                    PlayerPrefs.DeleteKey(key + "_z");
                    PlayerPrefs.DeleteKey(key + "_name");
                }

                PlayerPrefs.Save();
            }
            catch (Exception ex)
            {
                _logger.Warning($"Failed to save marker history: {ex.Message}");
            }
        }
    }
}
