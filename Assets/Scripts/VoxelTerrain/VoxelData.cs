using FastNoise;

namespace VoxelTerrain
{
    /// <summary>
    /// 3D density 필드를 생성한다.
    /// FastNoiseLite의 노이즈를 활용하여 입체적 지형을 만든다.
    /// 모든 면(상단 표면, 바닥면, 측면 벽)이 존재하는 볼류메트릭 형태.
    /// </summary>
    public static class VoxelData
    {
        /// <summary>
        /// 청크 하나에 대한 density 배열을 생성한다.
        /// 반환 크기: (chunkSize+1, chunkHeight+1, chunkSize+1)
        /// 양수 = solid, 음수 = air
        /// </summary>
        /// <param name="isEdgeMinX">이 청크가 월드의 -X 경계인지</param>
        /// <param name="isEdgeMaxX">이 청크가 월드의 +X 경계인지</param>
        /// <param name="isEdgeMinZ">이 청크가 월드의 -Z 경계인지</param>
        /// <param name="isEdgeMaxZ">이 청크가 월드의 +Z 경계인지</param>
        public static float[,,] GenerateDensityField(
            int chunkSize, int chunkHeight,
            int worldOffsetX, int worldOffsetZ,
            int seed,
            float terrainFrequency,
            float caveFrequency,
            float caveThreshold,
            float terrainAmplitude,
            bool isEdgeMinX, bool isEdgeMaxX,
            bool isEdgeMinZ, bool isEdgeMaxZ)
        {
            int sizeX = chunkSize + 1;
            int sizeY = chunkHeight + 1;
            int sizeZ = chunkSize + 1;

            float[,,] densities = new float[sizeX, sizeY, sizeZ];

            // 지형 형태 노이즈 (대규모 지형 윤곽)
            var terrainNoise = new FastNoiseLite(seed);
            terrainNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
            terrainNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
            terrainNoise.SetFractalOctaves(5);
            terrainNoise.SetFractalLacunarity(2.0f);
            terrainNoise.SetFractalGain(0.5f);
            terrainNoise.SetFrequency(terrainFrequency);

            // 산악 지형용 ridged 노이즈
            var ridgedNoise = new FastNoiseLite(seed + 100);
            ridgedNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
            ridgedNoise.SetFractalType(FastNoiseLite.FractalType.Ridged);
            ridgedNoise.SetFractalOctaves(4);
            ridgedNoise.SetFractalLacunarity(2.0f);
            ridgedNoise.SetFractalGain(0.5f);
            ridgedNoise.SetFrequency(terrainFrequency * 0.5f);

            float halfHeight = chunkHeight * 0.5f;

            for (int x = 0; x < sizeX; x++)
            {
                for (int z = 0; z < sizeZ; z++)
                {
                    float worldX = x + worldOffsetX;
                    float worldZ = z + worldOffsetZ;

                    // 2D 높이맵 기반 지형 (기본 형태)
                    float terrainValue = terrainNoise.GetNoise(worldX, worldZ);
                    float ridgedValue = ridgedNoise.GetNoise(worldX, worldZ);

                    // 두 노이즈를 혼합하여 풍부한 지형 생성
                    float combinedTerrain = terrainValue * 0.6f + ridgedValue * 0.4f;
                    float surfaceHeight = halfHeight + combinedTerrain * terrainAmplitude;

                    for (int y = 0; y < sizeY; y++)
                    {
                        // === 기본 density ===
                        // 표면 아래 = 양수(solid), 표면 위 = 음수(air)
                        float rawDist = surfaceHeight - y;
                        float density = Clamp(rawDist, -terrainAmplitude, terrainAmplitude);
                        density /= terrainAmplitude;

                        // === 바닥 보장 (y=1~3은 항상 solid) ===
                        if (y >= 1 && y <= 3)
                            density = Max(density, 1.0f - (y - 1) * 0.33f);

                        // === 볼류메트릭 경계면 생성 ===
                        // Marching Cubes는 density가 양→음 전환되는 곳에만 메쉬를 생성.
                        // 경계를 음수(air)로 만들어 바닥면과 측면 벽 메쉬가 생기게 한다.

                        // 바닥면: y=0을 air로 만들어 y≈0.5 위치에 바닥 메쉬 생성
                        if (y == 0)
                            density = -1f;

                        // 상단 캡: 최상단도 확실히 air (표면 위에 노이즈가 닿더라도)
                        if (y >= sizeY - 1)
                            density = Min(density, -0.5f);

                        // 측면 벽: 월드 경계 청크의 가장자리를 air로 만들어 수직 벽 생성
                        if (isEdgeMinX && x == 0) density = -1f;
                        if (isEdgeMaxX && x == sizeX - 1) density = -1f;
                        if (isEdgeMinZ && z == 0) density = -1f;
                        if (isEdgeMaxZ && z == sizeZ - 1) density = -1f;

                        densities[x, y, z] = density;
                    }
                }
            }

            return densities;
        }

        private static float Abs(float v) => v < 0 ? -v : v;
        private static float Min(float a, float b) => a < b ? a : b;
        private static float Max(float a, float b) => a > b ? a : b;
        private static float Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);
        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }
}
