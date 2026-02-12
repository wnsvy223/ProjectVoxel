namespace VoxelTerrain
{
    /// <summary>
    /// 블로그 Part 1: 높이 기반 지형 타입 (임계값으로 분류)
    /// </summary>
    public enum HeightType
    {
        DeepWater,
        ShallowWater,
        Sand,
        Grass,
        Forest,
        Rock,
        Snow
    }

    /// <summary>
    /// 블로그 Part 3: 위도 기반 온도 타입
    /// </summary>
    public enum HeatType
    {
        Coldest,
        Colder,
        Cold,
        Warm,
        Warmer,
        Warmest
    }

    /// <summary>
    /// 블로그 Part 3: 습도 타입
    /// </summary>
    public enum MoistureType
    {
        Dryest,
        Dryer,
        Dry,
        Wet,
        Wetter,
        Wettest
    }

    /// <summary>
    /// 블로그 Part 4: Whittaker 바이옴 타입
    /// </summary>
    public enum BiomeType
    {
        Ice,
        BorealForest,
        Desert,
        Grassland,
        SeasonalForest,
        Savanna,
        TemperateRainforest,
        TropicalRainforest,
        Tundra,
        Woodland
    }

    /// <summary>
    /// 각 (x,z) 컬럼에 대한 바이옴 정보를 저장한다.
    /// 3D 복셀 시스템에서 2D 바이옴 맵으로 사용.
    /// </summary>
    public struct ColumnBiomeInfo
    {
        public float HeightValue;       // 0~1 정규화된 높이
        public float HeatValue;         // 0~1 정규화된 온도
        public float MoistureValue;     // 0~1 정규화된 습도
        public HeightType HeightType;
        public HeatType HeatType;
        public MoistureType MoistureType;
        public BiomeType BiomeType;
    }

    /// <summary>
    /// 블로그 Part 4: Whittaker 바이옴 분류 테이블 및 임계값.
    /// 6x6 룩업 테이블로 Heat × Moisture → Biome 매핑.
    /// </summary>
    public static class BiomeTable
    {
        // === 높이 임계값 (블로그 Part 1, 3D 월드에 맞게 조정) ===
        // 해수면(0.5) 기준: 아래=수역, 위=육지
        // Grass+Forest 대역을 넓혀서 바이옴 색상이 잘 보이게
        public const float DeepWaterThreshold = 0.25f;
        public const float ShallowWaterThreshold = 0.4f;
        public const float SandThreshold = 0.45f;
        public const float GrassThreshold = 0.7f;
        public const float ForestThreshold = 0.82f;
        public const float RockThreshold = 0.92f;

        // === 온도 임계값 (블로그 Part 3) ===
        public const float ColdestValue = 0.05f;
        public const float ColderValue = 0.18f;
        public const float ColdValue = 0.4f;
        public const float WarmValue = 0.6f;
        public const float WarmerValue = 0.8f;

        // === 습도 임계값 (블로그 Part 3) ===
        public const float DryestValue = 0.12f;
        public const float DryerValue = 0.27f;
        public const float DryValue = 0.4f;
        public const float WetValue = 0.6f;
        public const float WetterValue = 0.8f;

        // === Whittaker 바이옴 테이블 (블로그 Part 4) ===
        // [MoistureType, HeatType] → BiomeType
        //   열: Coldest  Colder         Cold              Warm              Warmer            Warmest
        // 행: Dryest ~ Wettest
        private static readonly BiomeType[,] Table = new BiomeType[6, 6]
        {
            // Dryest
            { BiomeType.Ice, BiomeType.Tundra, BiomeType.Grassland, BiomeType.Desert, BiomeType.Desert, BiomeType.Desert },
            // Dryer
            { BiomeType.Ice, BiomeType.Tundra, BiomeType.Grassland, BiomeType.Grassland, BiomeType.Desert, BiomeType.Desert },
            // Dry
            { BiomeType.Ice, BiomeType.Tundra, BiomeType.Woodland, BiomeType.Woodland, BiomeType.Savanna, BiomeType.Savanna },
            // Wet
            { BiomeType.Ice, BiomeType.Tundra, BiomeType.BorealForest, BiomeType.SeasonalForest, BiomeType.SeasonalForest, BiomeType.Woodland },
            // Wetter
            { BiomeType.Ice, BiomeType.Tundra, BiomeType.BorealForest, BiomeType.SeasonalForest, BiomeType.TemperateRainforest, BiomeType.TropicalRainforest },
            // Wettest
            { BiomeType.Ice, BiomeType.Tundra, BiomeType.BorealForest, BiomeType.TemperateRainforest, BiomeType.TropicalRainforest, BiomeType.TropicalRainforest },
        };

        public static HeightType GetHeightType(float heightValue)
        {
            if (heightValue < DeepWaterThreshold) return HeightType.DeepWater;
            if (heightValue < ShallowWaterThreshold) return HeightType.ShallowWater;
            if (heightValue < SandThreshold) return HeightType.Sand;
            if (heightValue < GrassThreshold) return HeightType.Grass;
            if (heightValue < ForestThreshold) return HeightType.Forest;
            if (heightValue < RockThreshold) return HeightType.Rock;
            return HeightType.Snow;
        }

        public static HeatType GetHeatType(float heatValue)
        {
            if (heatValue < ColdestValue) return HeatType.Coldest;
            if (heatValue < ColderValue) return HeatType.Colder;
            if (heatValue < ColdValue) return HeatType.Cold;
            if (heatValue < WarmValue) return HeatType.Warm;
            if (heatValue < WarmerValue) return HeatType.Warmer;
            return HeatType.Warmest;
        }

        public static MoistureType GetMoistureType(float moistureValue)
        {
            if (moistureValue < DryestValue) return MoistureType.Dryest;
            if (moistureValue < DryerValue) return MoistureType.Dryer;
            if (moistureValue < DryValue) return MoistureType.Dry;
            if (moistureValue < WetValue) return MoistureType.Wet;
            if (moistureValue < WetterValue) return MoistureType.Wetter;
            return MoistureType.Wettest;
        }

        /// <summary>
        /// 블로그 Part 4: Heat × Moisture로 바이옴을 결정한다.
        /// 물 타일은 바이옴 분류에서 제외 (높이 타입 기반).
        /// </summary>
        public static BiomeType GetBiomeType(HeatType heat, MoistureType moisture)
        {
            return Table[(int)moisture, (int)heat];
        }

        /// <summary>
        /// 블로그 Part 3: 높이에 따른 온도 보정.
        /// 높은 지형일수록 온도가 낮아진다 (고도 효과).
        /// </summary>
        public static float AdjustHeatForHeight(float heatValue, HeightType heightType)
        {
            switch (heightType)
            {
                case HeightType.Forest: return heatValue - 0.1f;
                case HeightType.Rock: return heatValue - 0.25f;
                case HeightType.Snow: return heatValue - 0.4f;
                default: return heatValue;
            }
        }

        /// <summary>
        /// 블로그 Part 3: 높이에 따른 습도 보정.
        /// 수역 근처일수록 습도가 높아진다.
        /// </summary>
        public static float AdjustMoistureForHeight(float moistureValue, HeightType heightType)
        {
            switch (heightType)
            {
                case HeightType.DeepWater: return moistureValue + 0.8f;
                case HeightType.ShallowWater: return moistureValue + 0.4f;
                case HeightType.Sand: return moistureValue + 0.1f;
                default: return moistureValue;
            }
        }
    }
}
