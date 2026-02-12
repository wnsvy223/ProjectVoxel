using FastNoise;

namespace VoxelTerrain
{
    /// <summary>
    /// 3D density 필드와 2D 바이옴 맵을 생성한다.
    /// 블로그(jgallant) Part 1~4의 노이즈 시스템.
    ///
    /// 연속성 보장 원칙:
    /// - 같은 월드 좌표 → 항상 같은 노이즈 값 (FastNoiseLite 특성)
    /// - surfaceHeight를 노이즈에서 직접 계산 (청크별 정규화 없음)
    /// - heightValue는 surfaceHeight에서 고정 수식으로 도출
    /// </summary>
    public static class VoxelData
    {
        public struct GenerationResult
        {
            public float[,,] Densities;
            public ColumnBiomeInfo[,] BiomeMap;
        }

        public static GenerationResult GenerateChunkData(
            int chunkSize, int chunkHeight,
            int worldOffsetX, int worldOffsetZ,
            int seed,
            float terrainFrequency,
            float terrainAmplitude,
            float heatFrequency,
            float moistureFrequency,
            float seaLevel)
        {
            int sizeX = chunkSize + 1;
            int sizeY = chunkHeight + 1;
            int sizeZ = chunkSize + 1;

            float[,,] densities = new float[sizeX, sizeY, sizeZ];
            ColumnBiomeInfo[,] biomeMap = new ColumnBiomeInfo[sizeX, sizeZ];

            // === 높이맵 노이즈 (블로그 Part 1) ===
            var terrainNoise = new FastNoiseLite(seed);
            terrainNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
            terrainNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
            terrainNoise.SetFractalOctaves(6);
            terrainNoise.SetFractalLacunarity(2.0f);
            terrainNoise.SetFractalGain(0.5f);
            terrainNoise.SetFrequency(terrainFrequency);

            var ridgedNoise = new FastNoiseLite(seed + 100);
            ridgedNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
            ridgedNoise.SetFractalType(FastNoiseLite.FractalType.Ridged);
            ridgedNoise.SetFractalOctaves(4);
            ridgedNoise.SetFractalLacunarity(2.0f);
            ridgedNoise.SetFractalGain(0.5f);
            ridgedNoise.SetFrequency(terrainFrequency * 0.8f);

            // 대륙 형태 (저주파) - 대규모 해양/대륙 구분
            var continentNoise = new FastNoiseLite(seed + 200);
            continentNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
            continentNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
            continentNoise.SetFractalOctaves(3);
            continentNoise.SetFractalLacunarity(2.0f);
            continentNoise.SetFractalGain(0.5f);
            continentNoise.SetFrequency(terrainFrequency * 0.25f);

            // === 온도 노이즈 (블로그 Part 3) ===
            var heatNoise = new FastNoiseLite(seed + 300);
            heatNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
            heatNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
            heatNoise.SetFractalOctaves(4);
            heatNoise.SetFractalLacunarity(2.0f);
            heatNoise.SetFractalGain(0.5f);
            heatNoise.SetFrequency(heatFrequency);

            // === 습도 노이즈 (블로그 Part 3) ===
            var moistureNoise = new FastNoiseLite(seed + 400);
            moistureNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
            moistureNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
            moistureNoise.SetFractalOctaves(4);
            moistureNoise.SetFractalLacunarity(2.0f);
            moistureNoise.SetFractalGain(0.5f);
            moistureNoise.SetFrequency(moistureFrequency);

            // 높이값의 고정 범위 (청크별 정규화 대신)
            // surfaceHeight 범위: [seaLevel - amp, seaLevel + amp]
            float heightMin = seaLevel - terrainAmplitude;
            float heightRange = 2f * terrainAmplitude;

            for (int x = 0; x < sizeX; x++)
            {
                for (int z = 0; z < sizeZ; z++)
                {
                    float worldX = x + worldOffsetX;
                    float worldZ = z + worldOffsetZ;

                    // === 1. 표면 높이 직접 계산 (원본 방식 + 대륙 노이즈) ===
                    // 노이즈 출력: 대략 [-1, 1]
                    float terrainValue = terrainNoise.GetNoise(worldX, worldZ);
                    float ridgedValue = ridgedNoise.GetNoise(worldX, worldZ);
                    float continentValue = continentNoise.GetNoise(worldX, worldZ);

                    // 대륙 노이즈가 양수인 지역에서만 산악 강조
                    float continentFactor = Clamp01((continentValue + 1f) * 0.5f);
                    float ridgedContrib = Max(0f, ridgedValue) * continentFactor;

                    // 블로그식 혼합: 기본 60% + 산악 40% (원본과 유사)
                    float combinedTerrain = terrainValue * 0.6f + ridgedContrib * 0.4f;

                    // 표면 높이 직접 계산 (연속적, 청크별 정규화 없음)
                    float surfaceHeight = seaLevel + combinedTerrain * terrainAmplitude;

                    // === 2. 바이옴 분류용 heightValue [0, 1] ===
                    // 고정 수식: (surfaceHeight - min) / range
                    // 모든 청크에서 동일한 세계좌표 → 동일한 surfaceHeight → 동일한 heightValue
                    float heightValue = Clamp01((surfaceHeight - heightMin) / heightRange);

                    // === 3. 온도: 위도 그래디언트 × 노이즈 (블로그 Part 3) ===
                    // 주기를 2048로 확대 → chunkSize=32 기준 64청크마다 1주기
                    // 수천 복셀을 이동해야 극지↔적도를 체험
                    float latitudePeriod = 2048f;
                    float latitudeT = (worldZ % latitudePeriod) / latitudePeriod;
                    if (latitudeT < 0) latitudeT += 1f;
                    // 적도(0.5)에서 가장 따뜻하고, 극지(0, 1)에서 가장 추움
                    float latitudeGradient = 1f - Abs(latitudeT - 0.5f) * 2f;

                    float heatNoiseVal = (heatNoise.GetNoise(worldX, worldZ) + 1f) * 0.5f;
                    float heatValue = Clamp01(latitudeGradient * 0.7f + heatNoiseVal * 0.3f);

                    // === 4. 습도: 프랙탈 노이즈 (블로그 Part 3) ===
                    float moistureValue = Clamp01((moistureNoise.GetNoise(worldX, worldZ) + 1f) * 0.5f);

                    // === 5. 바이옴 분류 (블로그 Part 3~4) ===
                    HeightType heightType = BiomeTable.GetHeightType(heightValue);

                    float adjustedHeat = Clamp01(BiomeTable.AdjustHeatForHeight(heatValue, heightType));
                    float adjustedMoist = Clamp01(BiomeTable.AdjustMoistureForHeight(moistureValue, heightType));

                    HeatType heatType = BiomeTable.GetHeatType(adjustedHeat);
                    MoistureType moistType = BiomeTable.GetMoistureType(adjustedMoist);

                    BiomeType biomeType;
                    if (heightType == HeightType.DeepWater || heightType == HeightType.ShallowWater)
                        biomeType = BiomeType.Ice; // 물은 별도 처리
                    else
                        biomeType = BiomeTable.GetBiomeType(heatType, moistType);

                    biomeMap[x, z] = new ColumnBiomeInfo
                    {
                        HeightValue = heightValue,
                        HeatValue = adjustedHeat,
                        MoistureValue = adjustedMoist,
                        HeightType = heightType,
                        HeatType = heatType,
                        MoistureType = moistType,
                        BiomeType = biomeType
                    };

                    // === 6. 3D 밀도 필드 ===
                    for (int y = 0; y < sizeY; y++)
                    {
                        float rawDist = surfaceHeight - y;
                        float density = Clamp(rawDist, -terrainAmplitude, terrainAmplitude);
                        density /= terrainAmplitude;

                        if (y >= 1 && y <= 8)
                            density = Max(density, 1.0f - (y - 1) * 0.125f);

                        if (y == 0)
                            density = -1f;

                        if (y >= sizeY - 1)
                            density = Min(density, -0.5f);

                        densities[x, y, z] = density;
                    }
                }
            }

            return new GenerationResult
            {
                Densities = densities,
                BiomeMap = biomeMap
            };
        }

        /// <summary>
        /// 기존 호환용: density만 반환.
        /// </summary>
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
            var result = GenerateChunkData(
                chunkSize, chunkHeight,
                worldOffsetX, worldOffsetZ,
                seed, terrainFrequency, terrainAmplitude,
                0.005f, 0.005f,
                chunkHeight * 0.35f);
            return result.Densities;
        }

        private static float Abs(float v) => v < 0 ? -v : v;
        private static float Min(float a, float b) => a < b ? a : b;
        private static float Max(float a, float b) => a > b ? a : b;
        private static float Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);
        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }
}
