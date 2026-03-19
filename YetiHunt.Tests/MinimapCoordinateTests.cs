using Xunit;

namespace YetiHunt.Tests
{
    /// <summary>
    /// Tests for minimap coordinate transformation logic.
    /// These tests verify the pure math without Unity dependencies.
    /// </summary>
    public class MinimapCoordinateTests
    {
        // Testable coordinate transformer extracted from MinimapRenderer
        public class MinimapCoordTransformer
        {
            private readonly float _mapBoundsX;
            private readonly float _mapBoundsY;
            private readonly float _mapCenterX;
            private readonly float _mapCenterZ;
            private readonly float _minimapWidth;
            private readonly float _minimapHeight;

            public MinimapCoordTransformer(
                float mapBoundsX, float mapBoundsY,
                float mapCenterX, float mapCenterZ,
                float minimapWidth, float minimapHeight)
            {
                _mapBoundsX = mapBoundsX;
                _mapBoundsY = mapBoundsY;
                _mapCenterX = mapCenterX;
                _mapCenterZ = mapCenterZ;
                _minimapWidth = minimapWidth;
                _minimapHeight = minimapHeight;
            }

            /// <summary>
            /// Converts world position to normalized minimap coordinates (0-1 range).
            /// </summary>
            public (float normX, float normZ) WorldToNormalized(float worldX, float worldZ)
            {
                float relX = worldX - _mapCenterX;
                float relZ = worldZ - _mapCenterZ;

                float normX = (relX / _mapBoundsX) + 0.5f;
                float normZ = (relZ / _mapBoundsY) + 0.5f;

                // Clamp to 0-1
                normX = Math.Clamp(normX, 0f, 1f);
                normZ = Math.Clamp(normZ, 0f, 1f);

                return (normX, normZ);
            }

            /// <summary>
            /// Converts world position to minimap pixel coordinates.
            /// </summary>
            public (float mapX, float mapY) WorldToMinimap(float worldX, float worldZ, float minimapScreenX, float minimapScreenY)
            {
                var (normX, normZ) = WorldToNormalized(worldX, worldZ);

                // GUI Y is inverted (0 at top)
                float mapX = minimapScreenX + normX * _minimapWidth;
                float mapY = minimapScreenY + (1f - normZ) * _minimapHeight;

                return (mapX, mapY);
            }
        }

        [Fact]
        public void WorldToNormalized_AtMapCenter_ReturnsMiddle()
        {
            var transformer = new MinimapCoordTransformer(
                mapBoundsX: 500f, mapBoundsY: 262f,
                mapCenterX: 26.5f, mapCenterZ: 206.4f,
                minimapWidth: 250f, minimapHeight: 180f
            );

            var (normX, normZ) = transformer.WorldToNormalized(26.5f, 206.4f);

            Assert.Equal(0.5, (double)normX, precision: 3);
            Assert.Equal(0.5, (double)normZ, precision: 3);
        }

        [Fact]
        public void WorldToNormalized_AtMinCorner_ReturnsZero()
        {
            var transformer = new MinimapCoordTransformer(
                mapBoundsX: 500f, mapBoundsY: 262f,
                mapCenterX: 26.5f, mapCenterZ: 206.4f,
                minimapWidth: 250f, minimapHeight: 180f
            );

            // Min corner is center - bounds/2
            float minX = 26.5f - 250f;
            float minZ = 206.4f - 131f;

            var (normX, normZ) = transformer.WorldToNormalized(minX, minZ);

            Assert.Equal(0.0, (double)normX, precision: 3);
            Assert.Equal(0.0, (double)normZ, precision: 3);
        }

        [Fact]
        public void WorldToNormalized_AtMaxCorner_ReturnsOne()
        {
            var transformer = new MinimapCoordTransformer(
                mapBoundsX: 500f, mapBoundsY: 262f,
                mapCenterX: 26.5f, mapCenterZ: 206.4f,
                minimapWidth: 250f, minimapHeight: 180f
            );

            // Max corner is center + bounds/2
            float maxX = 26.5f + 250f;
            float maxZ = 206.4f + 131f;

            var (normX, normZ) = transformer.WorldToNormalized(maxX, maxZ);

            Assert.Equal(1.0, (double)normX, precision: 3);
            Assert.Equal(1.0, (double)normZ, precision: 3);
        }

        [Fact]
        public void WorldToNormalized_OutOfBounds_ClampedToZeroOne()
        {
            var transformer = new MinimapCoordTransformer(
                mapBoundsX: 500f, mapBoundsY: 262f,
                mapCenterX: 26.5f, mapCenterZ: 206.4f,
                minimapWidth: 250f, minimapHeight: 180f
            );

            // Way outside bounds
            var (normX, normZ) = transformer.WorldToNormalized(-1000f, 1000f);

            Assert.Equal(0f, normX);
            Assert.Equal(1f, normZ);
        }

        [Fact]
        public void WorldToMinimap_AtMapCenter_ReturnsCenterOfMinimap()
        {
            var transformer = new MinimapCoordTransformer(
                mapBoundsX: 500f, mapBoundsY: 262f,
                mapCenterX: 26.5f, mapCenterZ: 206.4f,
                minimapWidth: 250f, minimapHeight: 180f
            );

            var (mapX, mapY) = transformer.WorldToMinimap(26.5f, 206.4f, 0f, 0f);

            // Center in X should be minimapWidth * 0.5
            Assert.Equal(125.0, (double)mapX, precision: 1);
            // Center in Y: (1 - 0.5) * height = 0.5 * 180 = 90
            Assert.Equal(90.0, (double)mapY, precision: 1);
        }

        [Fact]
        public void WorldToMinimap_WithOffset_AddsScreenPosition()
        {
            var transformer = new MinimapCoordTransformer(
                mapBoundsX: 500f, mapBoundsY: 262f,
                mapCenterX: 26.5f, mapCenterZ: 206.4f,
                minimapWidth: 250f, minimapHeight: 180f
            );

            float screenX = 100f;
            float screenY = 50f;

            var (mapX, mapY) = transformer.WorldToMinimap(26.5f, 206.4f, screenX, screenY);

            // Center + offset
            Assert.Equal(225.0, (double)mapX, precision: 1); // 100 + 125
            Assert.Equal(140.0, (double)mapY, precision: 1); // 50 + 90
        }
    }
}
