using System;
using UnityEngine;
using YetiHunt.Infrastructure;
using YetiHunt.Players;
using YetiHunt.Yeti;

namespace YetiHunt.UI
{
    /// <summary>
    /// Renders the minimap with player position and yeti region indicator.
    /// </summary>
    public class MinimapRenderer : IMinimapRenderer
    {
        private const float YETI_REGION_RADIUS = 50f;

        // Calibrated values based on spawn point mapping
        private static readonly Vector2 DEFAULT_MAP_BOUNDS = new Vector2(500f, 262f);
        private static readonly Vector3 DEFAULT_MAP_CENTER = new Vector3(26.5f, 200f, 206.4f);

        private readonly IModLogger _logger;
        private readonly TextureFactory _textureFactory;
        private readonly IPlayerTracker _playerTracker;
        private readonly IYetiManager _yetiManager;

        private Texture2D _minimapTexture;
        private Texture2D _playerMarkerTexture;
        private Texture2D _yetiRegionTexture;
        private Texture2D _bgTexture;

        private Vector2 _mapBounds;
        private Vector3 _mapCenter;
        private float _minimapWidth = 250f;
        private float _minimapHeight = 180f;
        private bool _initialized;

        public MinimapRenderer(IModLogger logger, TextureFactory textureFactory, IPlayerTracker playerTracker, IYetiManager yetiManager)
        {
            _logger = logger;
            _textureFactory = textureFactory;
            _playerTracker = playerTracker;
            _yetiManager = yetiManager;
        }

        public void Initialize()
        {
            if (_initialized) return;

            try
            {
                // Create textures
                _bgTexture = _textureFactory.MakeTexture(2, 2, new Color(0.1f, 0.1f, 0.15f, 0.8f));
                _playerMarkerTexture = _textureFactory.MakeCircleTexture(12, new Color(0.2f, 0.5f, 1f, 1f));
                _yetiRegionTexture = _textureFactory.MakeCircleTexture(64, new Color(1f, 0.3f, 0.1f, 0.4f));

                // Load map texture
                _minimapTexture = _textureFactory.LoadEmbeddedTexture("map.png");
                if (_minimapTexture != null)
                {
                    _logger.Info($"Loaded minimap texture: {_minimapTexture.width}x{_minimapTexture.height}");

                    float aspectRatio = (float)_minimapTexture.width / _minimapTexture.height;
                    _minimapHeight = 180f;
                    _minimapWidth = _minimapHeight * aspectRatio;
                    _logger.Info($"Minimap display size: {_minimapWidth}x{_minimapHeight}");
                }
                else
                {
                    _logger.Warning("Could not load map.png, using placeholder");
                    _minimapTexture = _textureFactory.MakeTexture(256, 180, new Color(0.2f, 0.3f, 0.2f, 0.8f));
                    _minimapWidth = 256f;
                    _minimapHeight = 180f;
                }

                // Try to get bounds from game, fall back to calibrated values
                _mapBounds = GetMapBoundsFromGame() ?? DEFAULT_MAP_BOUNDS;
                _mapCenter = DEFAULT_MAP_CENTER;
                _logger.Info($"Using map bounds: {_mapBounds}, center: {_mapCenter}");

                _initialized = true;
            }
            catch (Exception ex)
            {
                _logger.Error($"InitMinimap failed: {ex.Message}");
                _minimapTexture = _textureFactory.MakeTexture(256, 180, new Color(0.2f, 0.3f, 0.2f, 0.8f));
                _minimapWidth = 256f;
                _minimapHeight = 180f;
                _mapBounds = DEFAULT_MAP_BOUNDS;
                _mapCenter = DEFAULT_MAP_CENTER;
                _initialized = true;
            }
        }

        public void Draw()
        {
            if (_minimapTexture == null) return;

            float padding = 20f;
            float minimapX = Screen.width - _minimapWidth - padding;
            float minimapY = padding;

            // Draw background
            GUI.DrawTexture(new Rect(minimapX - 2, minimapY - 2, _minimapWidth + 4, _minimapHeight + 4), _bgTexture);

            // Draw map texture
            GUI.DrawTexture(new Rect(minimapX, minimapY, _minimapWidth, _minimapHeight), _minimapTexture);

            // Draw yeti region
            var yetis = _yetiManager.ActiveYetis;
            if (yetis.Count > 0 && yetis[0].GameObject != null)
            {
                Vector3 yetiWorldPos = yetis[0].GameObject.transform.position;
                Vector2 yetiMapPos = WorldToMinimapPos(yetiWorldPos, minimapX, minimapY);

                // Add wobble for vagueness
                float wobble = Mathf.Sin(Time.time * 0.5f) * 10f;
                float wobble2 = Mathf.Cos(Time.time * 0.7f) * 10f;
                yetiMapPos.x += wobble;
                yetiMapPos.y += wobble2;

                float regionSize = _minimapWidth * (YETI_REGION_RADIUS / _mapBounds.x) * 2f;
                regionSize = Mathf.Max(regionSize, 40f);
                GUI.DrawTexture(new Rect(yetiMapPos.x - regionSize / 2, yetiMapPos.y - regionSize / 2, regionSize, regionSize), _yetiRegionTexture);
            }

            // Draw player position
            var playerTransform = _playerTracker.LocalPlayerTransform;
            if (playerTransform != null)
            {
                Vector3 playerWorldPos = playerTransform.position;
                Vector2 playerMapPos = WorldToMinimapPos(playerWorldPos, minimapX, minimapY);

                float markerSize = 12f;
                GUI.DrawTexture(new Rect(playerMapPos.x - markerSize / 2, playerMapPos.y - markerSize / 2, markerSize, markerSize), _playerMarkerTexture);
            }

            // Draw label
            GUIStyle minimapLabel = new GUIStyle(GUI.skin.label);
            minimapLabel.fontSize = 10;
            minimapLabel.alignment = TextAnchor.MiddleCenter;
            minimapLabel.normal.textColor = new Color(1f, 1f, 1f, 0.7f);
            GUI.Label(new Rect(minimapX, minimapY + _minimapHeight + 2, _minimapWidth, 16), "MINIMAP", minimapLabel);
        }

        public Vector2 WorldToMapCoordinates(Vector3 worldPos)
        {
            float relX = worldPos.x - _mapCenter.x;
            float relZ = worldPos.z - _mapCenter.z;

            float normX = (relX / _mapBounds.x) + 0.5f;
            float normZ = (relZ / _mapBounds.y) + 0.5f;

            normX = Mathf.Clamp01(normX);
            normZ = Mathf.Clamp01(normZ);

            return new Vector2(normX * _minimapWidth, (1f - normZ) * _minimapHeight);
        }

        private Vector2 WorldToMinimapPos(Vector3 worldPos, float minimapX, float minimapY)
        {
            float relX = worldPos.x - _mapCenter.x;
            float relZ = worldPos.z - _mapCenter.z;

            float normX = (relX / _mapBounds.x) + 0.5f;
            float normZ = (relZ / _mapBounds.y) + 0.5f;

            normX = Mathf.Clamp01(normX);
            normZ = Mathf.Clamp01(normZ);

            float mapX = minimapX + normX * _minimapWidth;
            float mapY = minimapY + (1f - normZ) * _minimapHeight;

            return new Vector2(mapX, mapY);
        }

        private Vector2? GetMapBoundsFromGame()
        {
            try
            {
                var mapSizeHandler = GameObject.Find("Map Size Handler");
                if (mapSizeHandler != null)
                {
                    var components = mapSizeHandler.GetComponents<Component>();
                    foreach (var comp in components)
                    {
                        if (comp == null) continue;
                        var il2cppType = comp.GetIl2CppType();
                        if (il2cppType?.Name == "MapSizeHandler")
                        {
                            var boundsProp = comp.GetType().GetProperty("mapBounds");
                            if (boundsProp != null)
                            {
                                var boundsVal = boundsProp.GetValue(comp);
                                if (boundsVal is Vector2 v2 && v2 != Vector2.zero)
                                {
                                    _logger.Info($"Map bounds from property: {v2}");
                                    return v2;
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            return null;
        }
    }
}
