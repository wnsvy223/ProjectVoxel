using UnityEngine;

namespace VoxelTerrain
{
    /// <summary>
    /// 개별 청크의 메쉬를 생성하고 관리한다.
    /// density 데이터를 저장하여 런타임 지형 편집을 지원한다.
    /// Mesh와 MeshData를 재사용하여 GC 부담을 줄인다.
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
        private int chunkSize; // 청크 한 변 복셀 수

        public void Initialize(Material material)
        {
            meshFilter = GetComponent<MeshFilter>();
            meshRenderer = GetComponent<MeshRenderer>();
            meshCollider = GetComponent<MeshCollider>();

            meshRenderer.material = material;

            // 영구 Mesh 생성 (재사용)
            persistentMesh = new Mesh();
            persistentMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            meshFilter.sharedMesh = persistentMesh;

            // MeshData 캐시 생성
            cachedMeshData = new MeshData();
        }

        /// <summary>
        /// density 데이터와 설정값을 받아 메쉬를 생성한다.
        /// density 데이터를 내부에 저장하여 이후 편집에 사용한다.
        /// </summary>
        public void BuildMesh(float[,,] densities, float voxelSize, int chunkHeight)
        {
            this.densities = densities;
            this.voxelSize = voxelSize;
            this.chunkHeight = chunkHeight;
            this.chunkSize = densities.GetLength(0) - 1;

            RebuildMeshVisual();
            UpdateCollider();
        }

        /// <summary>
        /// density 데이터와 설정값을 받아 시각적 메쉬만 생성한다 (콜라이더 지연).
        /// 성능 최적화를 위해 콜라이더는 나중에 별도로 업데이트한다.
        /// </summary>
        public void BuildMeshDeferred(float[,,] densities, float voxelSize, int chunkHeight)
        {
            this.densities = densities;
            this.voxelSize = voxelSize;
            this.chunkHeight = chunkHeight;
            this.chunkSize = densities.GetLength(0) - 1;

            RebuildMeshVisual();
            // 콜라이더는 나중에 VoxelWorld에서 큐를 통해 업데이트
        }

        /// <summary>
        /// 저장된 density 데이터로 시각적 메쉬만 재생성한다 (MeshCollider 제외).
        /// 지형 편집 중 매 프레임 호출해도 부담이 적다.
        /// </summary>
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

        /// <summary>
        /// MeshCollider를 현재 메쉬로 갱신한다.
        /// 비용이 크므로 일정 간격으로만 호출하는 것이 좋다.
        /// </summary>
        public void UpdateCollider()
        {
            if (persistentMesh == null || persistentMesh.vertexCount == 0)
            {
                meshCollider.sharedMesh = null;
                return;
            }

            // sharedMesh를 null로 설정 후 다시 할당해야 Unity가 재빌드함
            meshCollider.sharedMesh = null;
            meshCollider.sharedMesh = persistentMesh;
        }

        /// <summary>
        /// 월드 좌표를 로컬 density 인덱스로 변환하고 density를 수정한다.
        /// </summary>
        public bool ModifyDensity(Vector3 localPos, float radius, float intensity)
        {
            if (densities == null) return false;

            int sizeX = densities.GetLength(0);
            int sizeY = densities.GetLength(1);
            int sizeZ = densities.GetLength(2);

            // 로컬 좌표를 복셀 인덱스로 변환
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
                            // 거리에 따른 감쇠 (중심에서 강하고 가장자리에서 약하게)
                            float falloff = 1.0f - (dist / radius);
                            falloff = falloff * falloff; // 부드러운 감쇠

                            densities[x, y, z] += intensity * falloff;
                            densities[x, y, z] = Mathf.Clamp(densities[x, y, z], -1f, 1f);
                            modified = true;
                            isModified = true; // 청크가 수정됨을 표시
                        }
                    }
                }
            }

            return modified;
        }

        public float[,,] GetDensities() => densities;
        public float GetVoxelSize() => voxelSize;
        public int GetChunkHeight() => chunkHeight;

        /// <summary>
        /// 외부 밀도 데이터를 설정하고 메시를 재빌드한다 (청크 복원용).
        /// </summary>
        public void SetDensities(float[,,] newDensities, float voxelSize, int chunkHeight, bool wasModified)
        {
            this.densities = newDensities;
            this.voxelSize = voxelSize;
            this.chunkHeight = chunkHeight;
            this.isModified = wasModified;
            RebuildMeshVisual();
            UpdateCollider();
        }

        /// <summary>
        /// 외부 밀도 데이터를 설정하고 시각적 메시만 재빌드한다 (콜라이더 지연).
        /// 성능 최적화를 위해 콜라이더는 나중에 별도로 업데이트한다.
        /// </summary>
        public void SetDensitiesDeferred(float[,,] newDensities, float voxelSize, int chunkHeight, bool wasModified)
        {
            this.densities = newDensities;
            this.voxelSize = voxelSize;
            this.chunkHeight = chunkHeight;
            this.isModified = wasModified;
            RebuildMeshVisual();
            // 콜라이더는 나중에 VoxelWorld에서 큐를 통해 업데이트
        }

        /// <summary>
        /// 밀도 데이터를 복사하여 반환한다 (저장용).
        /// </summary>
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

        /// <summary>
        /// 수정 플래그를 설정한다.
        /// </summary>
        public void SetModifiedFlag()
        {
            isModified = true;
        }

        /// <summary>
        /// 밀도 데이터를 해제하여 메모리를 절약한다 (비활성화용).
        /// </summary>
        public void ClearDensities()
        {
            densities = null;
        }

        /// <summary>
        /// 수정 플래그를 초기화한다.
        /// </summary>
        public void ResetModifiedFlag()
        {
            isModified = false;
        }

        // ==================== 경계 벽 관리 ====================

        /// <summary>
        /// 경계 벽 상태를 업데이트한다.
        /// </summary>
        /// <param name="needsMinX">-X 방향에 벽이 필요한지</param>
        /// <param name="needsMaxX">+X 방향에 벽이 필요한지</param>
        /// <param name="needsMinZ">-Z 방향에 벽이 필요한지</param>
        /// <param name="needsMaxZ">+Z 방향에 벽이 필요한지</param>
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

        /// <summary>
        /// 특정 방향의 벽을 생성한다.
        /// </summary>
        private void CreateWall(int direction)
        {
            if (densities == null) return;

            float chunkWorldSize = chunkSize * voxelSize;
            float maxHeight = chunkHeight * voxelSize;

            // 벽 GameObject 생성
            GameObject wallObj = new GameObject($"Wall_{direction}");
            wallObj.transform.parent = transform;
            wallObj.transform.localPosition = Vector3.zero;
            wallObj.transform.localRotation = Quaternion.identity;

            MeshFilter mf = wallObj.AddComponent<MeshFilter>();
            MeshRenderer mr = wallObj.AddComponent<MeshRenderer>();
            mr.material = meshRenderer.material;

            // 벽 메시 생성
            Mesh wallMesh = CreateWallMesh(direction, chunkWorldSize, maxHeight);
            mf.sharedMesh = wallMesh;

            wallObjects[direction] = wallObj;
        }

        /// <summary>
        /// 벽 메시를 생성한다.
        /// </summary>
        private Mesh CreateWallMesh(int direction, float chunkWorldSize, float maxHeight)
        {
            Mesh mesh = new Mesh();
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

            // 밀도 데이터에서 표면 높이 샘플링하여 벽 생성
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

                switch (direction)
                {
                    case 0: // MinX (-X 방향)
                        xPos = 0;
                        zPos = t * chunkWorldSize;
                        normal = Vector3.left;
                        break;
                    case 1: // MaxX (+X 방향)
                        xPos = chunkWorldSize;
                        zPos = t * chunkWorldSize;
                        normal = Vector3.right;
                        break;
                    case 2: // MinZ (-Z 방향)
                        xPos = t * chunkWorldSize;
                        zPos = 0;
                        normal = Vector3.back;
                        break;
                    default: // MaxZ (+Z 방향)
                        xPos = t * chunkWorldSize;
                        zPos = chunkWorldSize;
                        normal = Vector3.forward;
                        break;
                }

                // 상단 정점 (표면)
                vertices[i * 2] = new Vector3(xPos, surfaceHeight, zPos);
                normals[i * 2] = normal;

                // 하단 정점 (바닥)
                vertices[i * 2 + 1] = new Vector3(xPos, 0, zPos);
                normals[i * 2 + 1] = normal;

                // 색상 (높이 기반)
                colors[i * 2] = GetColorForHeight(surfaceHeight / maxHeight);
                colors[i * 2 + 1] = GetColorForHeight(0);
            }

            // 삼각형 생성
            int triIdx = 0;
            for (int i = 0; i < resolution - 1; i++)
            {
                int v0 = i * 2;
                int v1 = i * 2 + 1;
                int v2 = (i + 1) * 2;
                int v3 = (i + 1) * 2 + 1;

                // 법선 방향에 따라 와인딩 조정
                if (direction == 0 || direction == 2) // MinX, MinZ
                {
                    triangles[triIdx++] = v0;
                    triangles[triIdx++] = v2;
                    triangles[triIdx++] = v1;
                    triangles[triIdx++] = v1;
                    triangles[triIdx++] = v2;
                    triangles[triIdx++] = v3;
                }
                else // MaxX, MaxZ
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

        /// <summary>
        /// 경계에서 표면 높이를 가져온다.
        /// </summary>
        private float GetSurfaceHeightAtEdge(int direction, int index)
        {
            if (densities == null) return 0;

            int sizeY = densities.GetLength(1);
            int x, z;

            switch (direction)
            {
                case 0: x = 0; z = index; break;  // MinX
                case 1: x = chunkSize; z = index; break;  // MaxX
                case 2: x = index; z = 0; break;  // MinZ
                default: x = index; z = chunkSize; break;  // MaxZ
            }

            // Y축으로 표면 찾기 (밀도가 양수→음수로 전환되는 지점)
            for (int y = sizeY - 1; y > 0; y--)
            {
                if (densities[x, y, z] > 0)
                {
                    // 보간하여 정확한 표면 높이 계산
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
        /// 높이 비율에 따른 색상을 반환한다.
        /// </summary>
        private Color GetColorForHeight(float heightRatio)
        {
            Color dirt = new Color(0.55f, 0.38f, 0.20f);
            Color grass = new Color(0.30f, 0.55f, 0.18f);
            Color rock = new Color(0.50f, 0.48f, 0.45f);
            Color sand = new Color(0.76f, 0.70f, 0.50f);

            if (heightRatio < 0.05f) return sand;
            if (heightRatio < 0.3f) return Color.Lerp(dirt, grass, (heightRatio - 0.05f) / 0.25f);
            if (heightRatio < 0.6f) return grass;
            if (heightRatio < 0.8f) return Color.Lerp(grass, rock, (heightRatio - 0.6f) / 0.2f);
            return rock;
        }

        /// <summary>
        /// 특정 방향의 벽을 제거한다.
        /// </summary>
        private void DestroyWall(int direction)
        {
            if (wallObjects[direction] != null)
            {
                // 메시 해제
                MeshFilter mf = wallObjects[direction].GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null)
                {
                    Destroy(mf.sharedMesh);
                }
                Destroy(wallObjects[direction]);
                wallObjects[direction] = null;
            }
        }

        /// <summary>
        /// 모든 벽을 제거한다.
        /// </summary>
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

        private void ApplyVertexColors(Mesh mesh)
        {
            Vector3[] vertices = mesh.vertices;
            Vector3[] normals = mesh.normals;

            // 색상 배열 재사용 (크기가 다를 때만 재할당)
            if (cachedColors == null || cachedColors.Length != vertices.Length)
                cachedColors = new Color[vertices.Length];

            float maxHeight = chunkHeight * voxelSize;

            Color deepGround = new Color(0.35f, 0.25f, 0.15f);
            Color dirt = new Color(0.55f, 0.38f, 0.20f);
            Color grass = new Color(0.30f, 0.55f, 0.18f);
            Color rock = new Color(0.50f, 0.48f, 0.45f);
            Color snow = new Color(0.90f, 0.92f, 0.95f);
            Color sand = new Color(0.76f, 0.70f, 0.50f);

            for (int i = 0; i < vertices.Length; i++)
            {
                float heightRatio = vertices[i].y / maxHeight;
                float steepness = 1.0f - Mathf.Abs(Vector3.Dot(normals[i], Vector3.up));

                Color color;

                if (heightRatio < 0.05f)
                {
                    color = sand;
                }
                else if (heightRatio < 0.3f)
                {
                    float t = (heightRatio - 0.05f) / 0.25f;
                    color = Color.Lerp(dirt, grass, t);
                }
                else if (heightRatio < 0.6f)
                {
                    color = grass;
                }
                else if (heightRatio < 0.8f)
                {
                    float t = (heightRatio - 0.6f) / 0.2f;
                    color = Color.Lerp(grass, rock, t);
                }
                else
                {
                    float t = (heightRatio - 0.8f) / 0.2f;
                    color = Color.Lerp(rock, snow, t);
                }

                if (steepness > 0.5f)
                {
                    float rockBlend = (steepness - 0.5f) * 2.0f;
                    color = Color.Lerp(color, rock, rockBlend);
                }

                if (heightRatio < 0.15f && steepness > 0.3f)
                {
                    color = Color.Lerp(color, deepGround, 0.5f);
                }

                cachedColors[i] = color;
            }

            mesh.colors = cachedColors;
        }

        void OnDestroy()
        {
            if (persistentMesh != null)
                Destroy(persistentMesh);

            ClearAllWalls();
        }
    }
}
