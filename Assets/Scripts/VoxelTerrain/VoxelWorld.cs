using UnityEngine;
using System.Collections.Generic;

namespace VoxelTerrain
{
    /// <summary>
    /// 복셀 월드 매니저.
    /// 여러 청크를 그리드 형태로 생성하고 관리한다.
    /// 런타임 지형 편집(파기/채우기)을 지원한다.
    /// dirty flag 시스템으로 메쉬/콜라이더 업데이트를 분리하여 성능 최적화.
    /// </summary>
    public class VoxelWorld : MonoBehaviour
    {
        [Header("월드 설정")]
        [Tooltip("월드 시드값")]
        public int seed = 42;

        [Tooltip("X/Z 방향 청크 개수")]
        [Range(1, 16)]
        public int worldSizeInChunks = 4;

        [Header("청크 설정")]
        [Tooltip("청크 한 변의 복셀 개수")]
        [Range(8, 64)]
        public int chunkSize = 32;

        [Tooltip("청크 높이 (복셀 개수)")]
        [Range(16, 128)]
        public int chunkHeight = 64;

        [Tooltip("복셀 하나의 크기 (월드 유닛)")]
        [Range(0.25f, 4f)]
        public float voxelSize = 1f;

        [Header("지형 생성")]
        [Tooltip("지형 노이즈 주파수 (낮을수록 완만한 지형)")]
        [Range(0.001f, 0.05f)]
        public float terrainFrequency = 0.008f;

        [Tooltip("지형 높이 진폭 (복셀 단위)")]
        [Range(5f, 60f)]
        public float terrainAmplitude = 20f;

        [Header("동굴 설정 (현재 비활성)")]
        [Tooltip("동굴 노이즈 주파수")]
        [Range(0.01f, 0.1f)]
        public float caveFrequency = 0.03f;

        [Tooltip("동굴 임계값")]
        [Range(0.1f, 0.8f)]
        public float caveThreshold = 0.35f;

        [Header("머티리얼")]
        [Tooltip("버텍스 컬러 머티리얼 (VertexColorTerrain 셰이더)")]
        public Material terrainMaterial;

        private VoxelChunk[,] chunks;
        private float halfWorld;

        // dirty flag 시스템: 시각적 메쉬와 콜라이더 업데이트를 분리
        private HashSet<VoxelChunk> dirtyMeshChunks = new HashSet<VoxelChunk>();
        private HashSet<VoxelChunk> dirtyColliderChunks = new HashSet<VoxelChunk>();
        private float colliderTimer;
        private const float COLLIDER_UPDATE_INTERVAL = 0.15f;

        void Start()
        {
            GenerateWorld();
        }

        void LateUpdate()
        {
            // 시각적 메쉬: 매 프레임 즉시 업데이트
            if (dirtyMeshChunks.Count > 0)
            {
                foreach (var chunk in dirtyMeshChunks)
                {
                    if (chunk != null)
                        chunk.RebuildMeshVisual();
                }
                dirtyMeshChunks.Clear();
            }

            // MeshCollider: 일정 간격으로 지연 업데이트 (가장 비용이 큰 연산)
            colliderTimer += Time.deltaTime;
            if (colliderTimer >= COLLIDER_UPDATE_INTERVAL && dirtyColliderChunks.Count > 0)
            {
                colliderTimer = 0f;
                foreach (var chunk in dirtyColliderChunks)
                {
                    if (chunk != null)
                        chunk.UpdateCollider();
                }
                dirtyColliderChunks.Clear();
            }
        }

        [ContextMenu("월드 재생성")]
        public void GenerateWorld()
        {
            ClearWorld();
            chunks = new VoxelChunk[worldSizeInChunks, worldSizeInChunks];

            float totalWorldSize = worldSizeInChunks * chunkSize * voxelSize;
            halfWorld = totalWorldSize * 0.5f;

            for (int cx = 0; cx < worldSizeInChunks; cx++)
            {
                for (int cz = 0; cz < worldSizeInChunks; cz++)
                {
                    CreateChunk(cx, cz);
                }
            }
        }

        private void CreateChunk(int chunkX, int chunkZ)
        {
            int worldOffsetX = chunkX * chunkSize;
            int worldOffsetZ = chunkZ * chunkSize;

            Vector3 chunkPosition = new Vector3(
                chunkX * chunkSize * voxelSize - halfWorld,
                0,
                chunkZ * chunkSize * voxelSize - halfWorld
            );

            GameObject chunkObj = new GameObject($"Chunk_{chunkX}_{chunkZ}");
            chunkObj.transform.parent = transform;
            chunkObj.transform.position = chunkPosition;

            chunkObj.AddComponent<MeshFilter>();
            chunkObj.AddComponent<MeshRenderer>();
            chunkObj.AddComponent<MeshCollider>();

            VoxelChunk chunk = chunkObj.AddComponent<VoxelChunk>();
            chunk.Initialize(terrainMaterial);

            // 월드 경계 청크인지 판단 (측면 벽 생성용)
            bool isEdgeMinX = (chunkX == 0);
            bool isEdgeMaxX = (chunkX == worldSizeInChunks - 1);
            bool isEdgeMinZ = (chunkZ == 0);
            bool isEdgeMaxZ = (chunkZ == worldSizeInChunks - 1);

            float[,,] densities = VoxelData.GenerateDensityField(
                chunkSize, chunkHeight,
                worldOffsetX, worldOffsetZ,
                seed,
                terrainFrequency,
                caveFrequency,
                caveThreshold,
                terrainAmplitude,
                isEdgeMinX, isEdgeMaxX,
                isEdgeMinZ, isEdgeMaxZ
            );

            chunk.BuildMesh(densities, voxelSize, chunkHeight);

            chunks[chunkX, chunkZ] = chunk;
        }

        /// <summary>
        /// 월드 좌표에서 지형을 수정한다.
        /// 브러시 반경 내의 모든 청크에 영향을 준다.
        /// density만 수정하고, 실제 메쉬/콜라이더 업데이트는 LateUpdate에서 일괄 처리.
        /// </summary>
        public void ModifyTerrain(Vector3 worldPos, float radius, float intensity)
        {
            if (chunks == null) return;

            float chunkWorldSize = chunkSize * voxelSize;
            float radiusWorld = radius * voxelSize;

            for (int cx = 0; cx < worldSizeInChunks; cx++)
            {
                for (int cz = 0; cz < worldSizeInChunks; cz++)
                {
                    VoxelChunk chunk = chunks[cx, cz];
                    if (chunk == null) continue;

                    Vector3 chunkPos = chunk.transform.position;

                    // 청크 AABB와 브러시 구의 교차 검사
                    float chunkMinX = chunkPos.x;
                    float chunkMaxX = chunkPos.x + chunkWorldSize;
                    float chunkMinZ = chunkPos.z;
                    float chunkMaxZ = chunkPos.z + chunkWorldSize;
                    float chunkMinY = chunkPos.y;
                    float chunkMaxY = chunkPos.y + chunkHeight * voxelSize;

                    if (worldPos.x + radiusWorld < chunkMinX || worldPos.x - radiusWorld > chunkMaxX) continue;
                    if (worldPos.z + radiusWorld < chunkMinZ || worldPos.z - radiusWorld > chunkMaxZ) continue;
                    if (worldPos.y + radiusWorld < chunkMinY || worldPos.y - radiusWorld > chunkMaxY) continue;

                    // 월드 좌표를 청크 로컬 좌표로 변환
                    Vector3 localPos = worldPos - chunkPos;

                    if (chunk.ModifyDensity(localPos, radius, intensity))
                    {
                        // dirty 플래그만 설정 (실제 업데이트는 LateUpdate에서)
                        dirtyMeshChunks.Add(chunk);
                        dirtyColliderChunks.Add(chunk);
                    }
                }
            }
        }

        [ContextMenu("월드 삭제")]
        public void ClearWorld()
        {
            dirtyMeshChunks.Clear();
            dirtyColliderChunks.Clear();

            if (chunks != null)
            {
                foreach (var chunk in chunks)
                {
                    if (chunk != null)
                        DestroyImmediate(chunk.gameObject);
                }
                chunks = null;
            }

            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                DestroyImmediate(transform.GetChild(i).gameObject);
            }
        }
    }
}
