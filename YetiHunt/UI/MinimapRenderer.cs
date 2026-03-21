using System;
using UnityEngine;
using YetiHunt.Infrastructure;
using YetiHunt.Players;
using YetiHunt.Yeti;

namespace YetiHunt.UI
{
    /// <summary>
    /// Renders the minimap with player position and yeti region indicator.
    /// The game map is rotated ~48° relative to Unity world axes, so we project
    /// world coordinates onto the map's own east/north axes derived from two
    /// calibrated corner points on the south edge of the map.
    /// </summary>
    public class MinimapRenderer : IMinimapRenderer
    {
        private const float YETI_REGION_RADIUS = 50f;

        // Two known corners on the south edge of the in-game map (world X, Z)
        private static readonly Vector3 SW_CORNER = new Vector3(-1062.69f, 0f, 557.69f);
        private static readonly Vector3 SE_CORNER = new Vector3(272.82f, 0f, -926.87f);

        private readonly IModLogger _logger;
        private readonly TextureFactory _textureFactory;
        private readonly IPlayerTracker _playerTracker;
        private readonly IYetiManager _yetiManager;

        private Texture2D _minimapTexture;
        private Texture2D _playerMarkerTexture;
        private Texture2D _yetiRegionTexture;
        private Texture2D _bgTexture;

        // Map projection axes (computed from corners + image aspect ratio)
        private Vector2 _eastDir;   // unit vector: map-east in world XZ
        private Vector2 _northDir;  // unit vector: map-north in world XZ
        private float _eastExtent;  // world-space length of south edge
        private float _northExtent; // world-space length of west edge (derived from aspect ratio)
        private Vector2 _origin;    // world XZ of the SW corner (bottom-left of image)

        private float _minimapWidth = 250f;
        private float _minimapHeight = 360f;
        private bool _initialized;
        private bool _visible = false;

        public bool Visible
        {
            get => _visible;
            set => _visible = value;
        }

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
                float imageAspect;
                if (_minimapTexture != null)
                {
                    _logger.Info($"Loaded minimap texture: {_minimapTexture.width}x{_minimapTexture.height}");
                    imageAspect = (float)_minimapTexture.width / _minimapTexture.height;
                    _minimapHeight = 360f;
                    _minimapWidth = _minimapHeight * imageAspect;
                    _logger.Info($"Minimap display size: {_minimapWidth}x{_minimapHeight}");
                }
                else
                {
                    _logger.Warning("Could not load map.png, using placeholder");
                    _minimapTexture = _textureFactory.MakeTexture(256, 180, new Color(0.2f, 0.3f, 0.2f, 0.8f));
                    _minimapWidth = 256f;
                    _minimapHeight = 360f;
                    imageAspect = 256f / 360f;
                }

                // Compute rotated map projection from the two known south-edge corners
                ComputeMapProjection(imageAspect);

                _initialized = true;
            }
            catch (Exception ex)
            {
                _logger.Error($"InitMinimap failed: {ex.Message}");
                _minimapTexture = _textureFactory.MakeTexture(256, 180, new Color(0.2f, 0.3f, 0.2f, 0.8f));
                _minimapWidth = 256f;
                _minimapHeight = 180f;
                ComputeMapProjection(256f / 360f);
                _initialized = true;
            }
        }

        private void ComputeMapProjection(float imageAspectRatio)
        {
            // East direction: SW -> SE along the south edge of the map
            Vector2 sw = new Vector2(SW_CORNER.x, SW_CORNER.z);
            Vector2 se = new Vector2(SE_CORNER.x, SE_CORNER.z);
            Vector2 eastVec = se - sw;

            _eastExtent = eastVec.magnitude;
            _eastDir = eastVec / _eastExtent;

            // North is perpendicular to east, rotated 90° CCW in XZ plane
            _northDir = new Vector2(-_eastDir.y, _eastDir.x);

            // Derive north-south extent from image aspect ratio (uniform scale assumption)
            _northExtent = _eastExtent / imageAspectRatio;

            // Origin is the SW corner (bottom-left of image)
            _origin = sw;

            _logger.Info($"Map projection: east=({_eastDir.x:F3}, {_eastDir.y:F3}), north=({_northDir.x:F3}, {_northDir.y:F3})");
            _logger.Info($"Map extents: east={_eastExtent:F1}, north={_northExtent:F1}");
            _logger.Info($"Map origin (SW): ({_origin.x:F1}, {_origin.y:F1})");
        }

        public void Draw()
        {
            // Il2Cpp textures can get destroyed on scene change; reinitialize if needed
            if (_initialized && _minimapTexture == null)
                _initialized = false;

            if (!_initialized)
                Initialize();

            if (!_visible || _minimapTexture == null) return;

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

                float regionSize = _minimapWidth * (YETI_REGION_RADIUS / _eastExtent) * 2f;
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

        /// <summary>
        /// Projects a world position onto the rotated map axes and returns
        /// normalized coordinates (0-1) where (0,0) = SW and (1,1) = NE.
        /// </summary>
        private Vector2 WorldToNormalized(Vector3 worldPos)
        {
            Vector2 worldXZ = new Vector2(worldPos.x, worldPos.z);
            Vector2 offset = worldXZ - _origin;

            // Dot product with map axes to get projection distances
            float eastProj = offset.x * _eastDir.x + offset.y * _eastDir.y;
            float northProj = offset.x * _northDir.x + offset.y * _northDir.y;

            float normX = Mathf.Clamp01(eastProj / _eastExtent);
            float normY = Mathf.Clamp01(northProj / _northExtent);

            return new Vector2(normX, normY);
        }

        public Vector2 WorldToMapCoordinates(Vector3 worldPos)
        {
            Vector2 norm = WorldToNormalized(worldPos);
            return new Vector2(norm.x * _minimapWidth, (1f - norm.y) * _minimapHeight);
        }

        private Vector2 WorldToMinimapPos(Vector3 worldPos, float minimapX, float minimapY)
        {
            Vector2 norm = WorldToNormalized(worldPos);
            float mapX = minimapX + norm.x * _minimapWidth;
            float mapY = minimapY + (1f - norm.y) * _minimapHeight;
            return new Vector2(mapX, mapY);
        }
    }
}
