using UnityEngine;

namespace VoxelTerrain
{
    /// <summary>
    /// 개별 청크의 메쉬를 생성하고 관리한다.
    /// density 데이터와 바이옴 맵을 저장하여 바이옴 기반 컬러링을 지원한다.
    /// </summary>
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    [RequireComponent(typeof(MeshCollider))]
    public class VoxelChunk : MonoBehaviour
    {
        private MeshFilter meshFilter;
        private MeshRenderer meshRenderer;
        private MeshCollider meshCollider;

        private float[,,] densities;
        private ColumnBiomeInfo[,] biomeMap;
        private float voxelSize;
        private int chunkHeight;

        // 재사용 오브젝트 (GC 할당 제거)
        private Mesh persistentMesh;
        private MeshData cachedMeshData;
        private Color[] cachedColors;

        // 청크 수정 추적
        private bool isModified;
        public bool IsModified => isModified;

        // 경계 벽 관리
        private GameObject[] wallObjects = new GameObject[4]; // MinX, MaxX, MinZ, MaxZ
        private bool[] hasWall = new bool[4];
        private int chunkSize;

        public void Initialize(Material material)
        {
            meshFilter = GetComponent<MeshFilter>();
            meshRenderer = GetComponent<MeshRenderer>();
            meshCollider = GetComponent<MeshCollider>();

            meshRenderer.material = material;

            persistentMesh = new Mesh();
            persistentMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            meshFilter.sharedMesh = persistentMesh;

            cachedMeshData = new MeshData();
        }

        /// <summary>
        /// density + 바이옴 데이터를 받아 메쉬를 생성한다.
        /// </summary>
        public void BuildMesh(float[,,] densities, ColumnBiomeInfo[,] biomeMap, float voxelSize, int chunkHeight)
        {
            this.densities = densities;
            this.biomeMap = biomeMap;
            this.voxelSize = voxelSize;
            this.chunkHeight = chunkHeight;
            this.chunkSize = densities.GetLength(0) - 1;

            RebuildMeshVisual();
            UpdateCollider();
        }

        /// <summary>
        /// density + 바이옴 데이터를 받아 시각적 메쉬만 생성한다 (콜라이더 지연).
        /// </summary>
        public void BuildMeshDeferred(float[,,] densities, ColumnBiomeInfo[,] biomeMap, float voxelSize, int chunkHeight)
        {
            this.densities = densities;
            this.biomeMap = biomeMap;
            this.voxelSize = voxelSize;
            this.chunkHeight = chunkHeight;
            this.chunkSize = densities.GetLength(0) - 1;

            RebuildMeshVisual();
        }

        /// <summary>
        /// 기존 호환용 (바이옴 없이).
        /// </summary>
        public void BuildMesh(float[,,] densities, float voxelSize, int chunkHeight)
        {
            BuildMesh(densities, null, voxelSize, chunkHeight);
        }

        public void BuildMeshDeferred(float[,,] densities, float voxelSize, int chunkHeight)
        {
            BuildMeshDeferred(densities, null, voxelSize, chunkHeight);
        }

        public void RebuildMeshVisual()
        {
            if (densities == null || persistentMesh == null) return;

            MarchingCubes.GenerateMesh(densities, voxelSize, cachedMeshData);

            if (cachedMeshData.vertices.Count == 0)
            {
                persistentMesh.Clear();
                meshCollider.sharedMesh = null;
                return;
            }

            cachedMeshData.ApplyToMesh(persistentMesh);
            ApplyVertexColors(persistentMesh);
        }

        public void UpdateCollider()
        {
            if (persistentMesh == null || persistentMesh.vertexCount == 0)
            {
                meshCollider.sharedMesh = null;
                return;
            }

            meshCollider.sharedMesh = null;
            meshCollider.sharedMesh = persistentMesh;
        }

        public bool ModifyDensity(Vector3 localPos, float radius, float intensity)
        {
            if (densities == null) return false;

            int sizeX = densities.GetLength(0);
            int sizeY = densities.GetLength(1);
            int sizeZ = densities.GetLength(2);

            float vx = localPos.x / voxelSize;
            float vy = localPos.y / voxelSize;
            float vz = localPos.z / voxelSize;

            int minX = Mathf.Max(0, Mathf.FloorToInt(vx - radius));
            int maxX = Mathf.Min(sizeX - 1, Mathf.CeilToInt(vx + radius));
            int minY = Mathf.Max(0, Mathf.FloorToInt(vy - radius));
            int maxY = Mathf.Min(sizeY - 1, Mathf.CeilToInt(vy + radius));
            int minZ = Mathf.Max(0, Mathf.FloorToInt(vz - radius));
            int maxZ = Mathf.Min(sizeZ - 1, Mathf.CeilToInt(vz + radius));

            bool modified = false;

            for (int x = minX; x <= maxX; x++)
            {
                for (int y = minY; y <= maxY; y++)
                {
                    for (int z = minZ; z <= maxZ; z++)
                    {
                        float dx = x - vx;
                        float dy = y - vy;
                        float dz = z - vz;
                        float dist = Mathf.Sqrt(dx * dx + dy * dy + dz * dz);

                        if (dist <= radius)
                        {
                            float falloff = 1.0f - (dist / radius);
                            falloff = falloff * falloff;

                            densities[x, y, z] += intensity * falloff;
                            densities[x, y, z] = Mathf.Clamp(densities[x, y, z], -1f, 1f);
                            modified = true;
                            isModified = true;
                        }
                    }
                }
            }

            return modified;
        }

        public float[,,] GetDensities() => densities;
        public ColumnBiomeInfo[,] GetBiomeMap() => biomeMap;
        public float GetVoxelSize() => voxelSize;
        public int GetChunkHeight() => chunkHeight;

        public void SetDensities(float[,,] newDensities, float voxelSize, int chunkHeight, bool wasModified)
        {
            this.densities = newDensities;
            this.voxelSize = voxelSize;
            this.chunkHeight = chunkHeight;
            this.isModified = wasModified;
            RebuildMeshVisual();
            UpdateCollider();
        }

        public void SetDensitiesDeferred(float[,,] newDensities, float voxelSize, int chunkHeight, bool wasModified)
        {
            this.densities = newDensities;
            this.voxelSize = voxelSize;
            this.chunkHeight = chunkHeight;
            this.isModified = wasModified;
            RebuildMeshVisual();
        }

        public void SetBiomeMap(ColumnBiomeInfo[,] biomeMap)
        {
            this.biomeMap = biomeMap;
        }

        public float[,,] CopyDensities()
        {
            if (densities == null) return null;

            int sizeX = densities.GetLength(0);
            int sizeY = densities.GetLength(1);
            int sizeZ = densities.GetLength(2);

            float[,,] copy = new float[sizeX, sizeY, sizeZ];
            System.Buffer.BlockCopy(densities, 0, copy, 0, sizeX * sizeY * sizeZ * sizeof(float));
            return copy;
        }

        public void SetModifiedFlag() { isModified = true; }
        public void ClearDensities() { densities = null; biomeMap = null; }
        public void ResetModifiedFlag() { isModified = false; }

        // ==================== 경계 벽 관리 ====================

        public void UpdateWalls(bool needsMinX, bool needsMaxX, bool needsMinZ, bool needsMaxZ)
        {
            bool[] needs = { needsMinX, needsMaxX, needsMinZ, needsMaxZ };

            for (int i = 0; i < 4; i++)
            {
                if (needs[i] && !hasWall[i])
                {
                    CreateWall(i);
                    hasWall[i] = true;
                }
                else if (!needs[i] && hasWall[i])
                {
                    DestroyWall(i);
                    hasWall[i] = false;
                }
            }
        }

        private void CreateWall(int direction)
        {
            if (densities == null) return;

            float chunkWorldSize = chunkSize * voxelSize;
            float maxHeight = chunkHeight * voxelSize;

            GameObject wallObj = new GameObject($"Wall_{direction}");
            wallObj.transform.parent = transform;
            wallObj.transform.localPosition = Vector3.zero;
            wallObj.transform.localRotation = Quaternion.identity;

            MeshFilter mf = wallObj.AddComponent<MeshFilter>();
            MeshRenderer mr = wallObj.AddComponent<MeshRenderer>();
            mr.material = meshRenderer.material;

            Mesh wallMesh = CreateWallMesh(direction, chunkWorldSize, maxHeight);
            mf.sharedMesh = wallMesh;

            wallObjects[direction] = wallObj;
        }

        private Mesh CreateWallMesh(int direction, float chunkWorldSize, float maxHeight)
        {
            Mesh mesh = new Mesh();
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

            int resolution = chunkSize + 1;
            int vertexCount = resolution * 2;

            Vector3[] vertices = new Vector3[vertexCount];
            Vector3[] normals = new Vector3[vertexCount];
            Color[] colors = new Color[vertexCount];
            int[] triangles = new int[(resolution - 1) * 6];

            Vector3 normal;
            float xPos, zPos;

            for (int i = 0; i < resolution; i++)
            {
                float t = (float)i / chunkSize;
                float surfaceHeight = GetSurfaceHeightAtEdge(direction, i);

                // 벽 정점의 바이옴 정보 가져오기
                int bx, bz;
                switch (direction)
                {
                    case 0: bx = 0; bz = i; break;
                    case 1: bx = chunkSize; bz = i; break;
                    case 2: bx = i; bz = 0; break;
                    default: bx = i; bz = chunkSize; break;
                }

                switch (direction)
                {
                    case 0:
                        xPos = 0;
                        zPos = t * chunkWorldSize;
                        normal = Vector3.left;
                        break;
                    case 1:
                        xPos = chunkWorldSize;
                        zPos = t * chunkWorldSize;
                        normal = Vector3.right;
                        break;
                    case 2:
                        xPos = t * chunkWorldSize;
                        zPos = 0;
                        normal = Vector3.back;
                        break;
                    default:
                        xPos = t * chunkWorldSize;
                        zPos = chunkWorldSize;
                        normal = Vector3.forward;
                        break;
                }

                vertices[i * 2] = new Vector3(xPos, surfaceHeight, zPos);
                normals[i * 2] = normal;

                vertices[i * 2 + 1] = new Vector3(xPos, 0, zPos);
                normals[i * 2 + 1] = normal;

                Color topColor = GetBiomeColor(bx, bz, surfaceHeight / maxHeight);
                colors[i * 2] = topColor;
                colors[i * 2 + 1] = GetBiomeColor(bx, bz, 0);
            }

            int triIdx = 0;
            for (int i = 0; i < resolution - 1; i++)
            {
                int v0 = i * 2;
                int v1 = i * 2 + 1;
                int v2 = (i + 1) * 2;
                int v3 = (i + 1) * 2 + 1;

                if (direction == 0 || direction == 2)
                {
                    triangles[triIdx++] = v0;
                    triangles[triIdx++] = v2;
                    triangles[triIdx++] = v1;
                    triangles[triIdx++] = v1;
                    triangles[triIdx++] = v2;
                    triangles[triIdx++] = v3;
                }
                else
                {
                    triangles[triIdx++] = v0;
                    triangles[triIdx++] = v1;
                    triangles[triIdx++] = v2;
                    triangles[triIdx++] = v1;
                    triangles[triIdx++] = v3;
                    triangles[triIdx++] = v2;
                }
            }

            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.colors = colors;
            mesh.triangles = triangles;

            return mesh;
        }

        private float GetSurfaceHeightAtEdge(int direction, int index)
        {
            if (densities == null) return 0;

            int sizeY = densities.GetLength(1);
            int x, z;

            switch (direction)
            {
                case 0: x = 0; z = index; break;
                case 1: x = chunkSize; z = index; break;
                case 2: x = index; z = 0; break;
                default: x = index; z = chunkSize; break;
            }

            for (int y = sizeY - 1; y > 0; y--)
            {
                if (densities[x, y, z] > 0)
                {
                    float d0 = densities[x, y, z];
                    float d1 = densities[x, y + 1 < sizeY ? y + 1 : y, z];

                    if (d1 < 0)
                    {
                        float t = d0 / (d0 - d1);
                        return (y + t) * voxelSize;
                    }
                    return y * voxelSize;
                }
            }

            return 0;
        }

        /// <summary>
        /// 바이옴 맵 기반으로 컬럼 (x,z)에서의 색상을 반환한다.
        /// </summary>
        private Color GetBiomeColor(int x, int z, float heightRatio)
        {
            if (biomeMap != null && x >= 0 && x < biomeMap.GetLength(0) && z >= 0 && z < biomeMap.GetLength(1))
            {
                var info = biomeMap[x, z];
                return GetColorForBiome(info, heightRatio);
            }
            return GetFallbackColor(heightRatio);
        }

        private void DestroyWall(int direction)
        {
            if (wallObjects[direction] != null)
            {
                MeshFilter mf = wallObjects[direction].GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null)
                {
                    Destroy(mf.sharedMesh);
                }
                Destroy(wallObjects[direction]);
                wallObjects[direction] = null;
            }
        }

        public void ClearAllWalls()
        {
            for (int i = 0; i < 4; i++)
            {
                if (hasWall[i])
                {
                    DestroyWall(i);
                    hasWall[i] = false;
                }
            }
        }

        /// <summary>
        /// 바이옴 기반 버텍스 컬러링 (블로그 Part 4).
        /// 각 버텍스의 (x,z) 위치에서 바이옴 맵을 참조하여 색상 결정.
        /// </summary>
        private void ApplyVertexColors(Mesh mesh)
        {
            Vector3[] vertices = mesh.vertices;
            Vector3[] normals = mesh.normals;

            if (cachedColors == null || cachedColors.Length != vertices.Length)
                cachedColors = new Color[vertices.Length];

            float maxHeight = chunkHeight * voxelSize;
            int biomeWidth = biomeMap != null ? biomeMap.GetLength(0) : 0;
            int biomeDepth = biomeMap != null ? biomeMap.GetLength(1) : 0;

            for (int i = 0; i < vertices.Length; i++)
            {
                float heightRatio = vertices[i].y / maxHeight;
                float steepness = 1.0f - Mathf.Abs(Vector3.Dot(normals[i], Vector3.up));

                Color color;

                if (biomeMap != null)
                {
                    // 버텍스 위치에서 가장 가까운 바이옴 컬럼 찾기
                    int bx = Mathf.Clamp(Mathf.RoundToInt(vertices[i].x / voxelSize), 0, biomeWidth - 1);
                    int bz = Mathf.Clamp(Mathf.RoundToInt(vertices[i].z / voxelSize), 0, biomeDepth - 1);

                    var info = biomeMap[bx, bz];
                    color = GetColorForBiome(info, heightRatio);
                }
                else
                {
                    color = GetFallbackColor(heightRatio);
                }

                // 급경사면은 암석 색상으로 블렌드
                if (steepness > 0.5f)
                {
                    Color rock = new Color(0.50f, 0.48f, 0.45f);
                    float rockBlend = (steepness - 0.5f) * 2.0f;
                    color = Color.Lerp(color, rock, rockBlend);
                }

                cachedColors[i] = color;
            }

            mesh.colors = cachedColors;
        }

        /// <summary>
        /// 블로그 Part 4: 바이옴 타입 + 높이 타입에 따른 색상 결정.
        /// 모든 육지 타입에 바이옴 색상이 반영된다.
        /// </summary>
        private static Color GetColorForBiome(ColumnBiomeInfo info, float heightRatio)
        {
            // --- 수역은 온도에 따라 색상 변화 ---
            if (info.HeightType == HeightType.DeepWater)
            {
                if (info.HeatValue < 0.2f)
                    return new Color(0.15f, 0.20f, 0.35f); // 차가운 심해 (어두운 남색)
                return new Color(0.08f, 0.18f, 0.45f);     // 따뜻한 심해 (진한 파랑)
            }
            if (info.HeightType == HeightType.ShallowWater)
            {
                if (info.HeatValue < 0.2f)
                    return new Color(0.25f, 0.35f, 0.45f); // 차가운 얕은물 (회청색)
                if (info.HeatValue > 0.7f)
                    return new Color(0.10f, 0.45f, 0.55f); // 열대 얕은물 (청록)
                return new Color(0.12f, 0.32f, 0.55f);     // 온대 얕은물
            }

            // --- 바이옴 기반 메인 색상 (확연히 구분) ---
            Color biomeColor;
            switch (info.BiomeType)
            {
                case BiomeType.Ice:
                    biomeColor = new Color(0.82f, 0.88f, 0.95f); // 밝은 하늘색-흰색
                    break;
                case BiomeType.Tundra:
                    biomeColor = new Color(0.65f, 0.68f, 0.50f); // 칙칙한 올리브-갈색
                    break;
                case BiomeType.BorealForest:
                    biomeColor = new Color(0.12f, 0.30f, 0.18f); // 매우 어두운 침엽수 녹색
                    break;
                case BiomeType.Grassland:
                    biomeColor = new Color(0.55f, 0.72f, 0.25f); // 밝은 연두 (확연히 밝음)
                    break;
                case BiomeType.Woodland:
                    biomeColor = new Color(0.40f, 0.55f, 0.20f); // 중간 녹색-갈색
                    break;
                case BiomeType.SeasonalForest:
                    biomeColor = new Color(0.30f, 0.52f, 0.15f); // 선명한 녹색
                    break;
                case BiomeType.TemperateRainforest:
                    biomeColor = new Color(0.08f, 0.40f, 0.12f); // 짙은 에메랄드
                    break;
                case BiomeType.TropicalRainforest:
                    biomeColor = new Color(0.05f, 0.35f, 0.08f); // 매우 짙은 정글 녹색
                    break;
                case BiomeType.Desert:
                    biomeColor = new Color(0.85f, 0.75f, 0.45f); // 밝은 모래색 (황토)
                    break;
                case BiomeType.Savanna:
                    biomeColor = new Color(0.72f, 0.62f, 0.28f); // 마른 풀색 (갈색-노랑)
                    break;
                default:
                    biomeColor = new Color(0.40f, 0.55f, 0.22f);
                    break;
            }

            // --- 높이 타입에 따라 바이옴 색상을 변조 ---
            switch (info.HeightType)
            {
                case HeightType.Sand:
                    // 모래: 바이옴 색상과 모래색을 블렌드 (사막 지역 모래는 더 노랗게)
                    Color sandBase = new Color(0.76f, 0.70f, 0.50f);
                    return Color.Lerp(sandBase, biomeColor, 0.3f);

                case HeightType.Rock:
                    // 암석: 바이옴 색상과 회색을 블렌드 (극지 암석은 더 밝게)
                    Color rockBase = new Color(0.50f, 0.48f, 0.45f);
                    return Color.Lerp(rockBase, biomeColor, 0.2f);

                case HeightType.Snow:
                    // 눈: 바이옴 색상 살짝 반영 (극지 눈은 청백, 열대 고산은 순백)
                    Color snowBase = new Color(0.92f, 0.93f, 0.96f);
                    return Color.Lerp(snowBase, biomeColor, 0.08f);

                default:
                    // Grass, Forest: 바이옴 색상 그대로
                    return biomeColor;
            }
        }

        /// <summary>
        /// 바이옴 맵이 없을 때 사용하는 기존 높이 기반 색상.
        /// </summary>
        private static Color GetFallbackColor(float heightRatio)
        {
            Color dirt = new Color(0.55f, 0.38f, 0.20f);
            Color grass = new Color(0.30f, 0.55f, 0.18f);
            Color rock = new Color(0.50f, 0.48f, 0.45f);
            Color snow = new Color(0.90f, 0.92f, 0.95f);
            Color sand = new Color(0.76f, 0.70f, 0.50f);

            if (heightRatio < 0.05f) return sand;
            if (heightRatio < 0.3f) return Color.Lerp(dirt, grass, (heightRatio - 0.05f) / 0.25f);
            if (heightRatio < 0.6f) return grass;
            if (heightRatio < 0.8f) return Color.Lerp(grass, rock, (heightRatio - 0.6f) / 0.2f);
            return Color.Lerp(rock, snow, (heightRatio - 0.8f) / 0.2f);
        }

        void OnDestroy()
        {
            if (persistentMesh != null)
                Destroy(persistentMesh);

            ClearAllWalls();
        }
    }
}
