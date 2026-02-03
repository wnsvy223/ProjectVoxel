using UnityEngine;
using System.Collections.Generic;
using System.Diagnostics;

namespace VoxelTerrain
{
    /// <summary>
    /// 무한 복셀 월드 매니저.
    /// 플레이어 위치 기반으로 청크를 동적으로 생성/언로드한다.
    /// 성능 최적화: 시간 기반 예산 시스템.
    /// 틈 방지: 밀도+메시 통합 처리 + 인접 청크 즉시 업데이트.
    /// </summary>
    public class VoxelWorld : MonoBehaviour
    {
        [Header("월드 설정")]
        [Tooltip("월드 시드값 (동일 시드 = 동일 지형)")]
        public int seed = 42;

        [Header("청크 설정")]
        [Tooltip("청크 한 변의 복셀 개수")]
        [Range(8, 64)]
        public int chunkSize = 32;

        [Tooltip("청크 높이 (복셀 개수)")]
        [Range(16, 256)]
        public int chunkHeight = 96;

        [Tooltip("복셀 하나의 크기 (월드 유닛)")]
        [Range(0.25f, 4f)]
        public float voxelSize = 1f;

        [Header("지형 생성")]
        [Tooltip("지형 노이즈 주파수 (낮을수록 완만한 지형)")]
        [Range(0.001f, 0.05f)]
        public float terrainFrequency = 0.008f;

        [Tooltip("지형 높이 진폭 (복셀 단위)")]
        [Range(5f, 80f)]
        public float terrainAmplitude = 35f;

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

        [Header("동적 청크 로딩")]
        [Tooltip("플레이어 주변 활성화할 청크 거리 (청크 단위)")]
        [Range(1, 12)]
        public int viewDistance = 8;

        [Tooltip("청크 상태 확인 주기 (초)")]
        [Range(0.1f, 2f)]
        public float chunkCheckInterval = 0.3f;

        [Tooltip("viewDistance 밖의 청크 삭제까지 추가 거리")]
        [Range(0, 8)]
        public int unloadBuffer = 4;

        [Header("성능 최적화")]
        [Tooltip("프레임당 청크 처리에 할당할 최대 시간 (밀리초)")]
        [Range(2f, 25f)]
        public float frameBudgetMs = 16f;

        [Tooltip("프레임당 최대 콜라이더 업데이트 수")]
        [Range(1, 8)]
        public int maxColliderUpdatesPerFrame = 3;

        // 동적 청크 저장소
        private Dictionary<Vector2Int, VoxelChunk> chunks = new Dictionary<Vector2Int, VoxelChunk>();

        // 플레이어 추적
        private Transform playerTransform;
        private Vector2Int lastPlayerChunk = new Vector2Int(int.MinValue, int.MinValue);
        private float chunkCheckTimer;

        // 수정된 청크 데이터 저장소
        private Dictionary<Vector2Int, float[,,]> savedChunkData = new Dictionary<Vector2Int, float[,,]>();
        private HashSet<Vector2Int> modifiedChunks = new HashSet<Vector2Int>();

        // 청크 생성 큐 (밀도+메시 통합)
        private Queue<Vector2Int> chunkQueue = new Queue<Vector2Int>();
        private HashSet<Vector2Int> pendingChunks = new HashSet<Vector2Int>();

        // 콜라이더 업데이트 큐
        private List<VoxelChunk> colliderQueue = new List<VoxelChunk>();
        private HashSet<VoxelChunk> colliderPending = new HashSet<VoxelChunk>();

        // 언로드 큐
        private Queue<Vector2Int> unloadQueue = new Queue<Vector2Int>();

        // dirty flag (지형 편집용)
        private HashSet<VoxelChunk> dirtyMeshChunks = new HashSet<VoxelChunk>();

        // 캐시
        private float chunkWorldSize;
        private Stopwatch frameStopwatch = new Stopwatch();

        void Start()
        {
            chunkWorldSize = chunkSize * voxelSize;
        }

        void Update()
        {
            if (playerTransform != null)
            {
                chunkCheckTimer += Time.deltaTime;
                if (chunkCheckTimer >= chunkCheckInterval)
                {
                    chunkCheckTimer = 0f;
                    QueueChunksAroundPlayer();
                }
            }

            ProcessChunkQueuesWithBudget();
        }

        void LateUpdate()
        {
            // 지형 편집으로 인한 메시 업데이트 (즉시)
            if (dirtyMeshChunks.Count > 0)
            {
                foreach (var chunk in dirtyMeshChunks)
                {
                    if (chunk != null && chunk.gameObject.activeSelf)
                        chunk.RebuildMeshVisual();
                }
                dirtyMeshChunks.Clear();
            }

            ProcessColliderUpdates();
        }

        // ==================== 시간 기반 예산 시스템 ====================

        private void ProcessChunkQueuesWithBudget()
        {
            frameStopwatch.Restart();

            // 청크 생성 (밀도+메시 통합 - 한 프레임에 하나씩)
            if (chunkQueue.Count > 0 && frameStopwatch.ElapsedMilliseconds < frameBudgetMs)
            {
                ProcessOneChunkCreation();
            }

            // 언로드 (가벼움)
            while (unloadQueue.Count > 0 && frameStopwatch.ElapsedMilliseconds < frameBudgetMs)
            {
                ProcessOneUnload();
            }

            frameStopwatch.Stop();
        }

        private void ProcessOneChunkCreation()
        {
            Vector2Int coord = chunkQueue.Dequeue();
            pendingChunks.Remove(coord);

            if (chunks.ContainsKey(coord)) return;

            // === 1. GameObject 생성 ===
            Vector3 chunkPosition = ChunkCoordToWorldPos(coord);
            GameObject chunkObj = new GameObject($"Chunk_{coord.x}_{coord.y}");
            chunkObj.transform.parent = transform;
            chunkObj.transform.position = chunkPosition;

            chunkObj.AddComponent<MeshFilter>();
            chunkObj.AddComponent<MeshRenderer>();
            chunkObj.AddComponent<MeshCollider>();

            VoxelChunk chunk = chunkObj.AddComponent<VoxelChunk>();
            chunk.Initialize(terrainMaterial);

            // 먼저 딕셔너리에 추가 (인접 청크 판단을 위해)
            chunks[coord] = chunk;

            // === 2. 밀도 계산 + 메시 빌드 (통합) ===
            if (savedChunkData.TryGetValue(coord, out float[,,] savedData))
            {
                // 저장된 데이터 복원
                chunk.BuildMeshDeferred(savedData, voxelSize, chunkHeight);
                chunk.SetModifiedFlag();
                savedChunkData.Remove(coord);
            }
            else
            {
                // 새 지형 생성 - isEdge 플래그는 사용하지 않음 (노이즈 기반 연속 밀도)
                // 노이즈가 같은 월드 좌표에서 같은 값을 반환하므로 경계가 자연스럽게 일치
                float[,,] densities = VoxelData.GenerateDensityField(
                    chunkSize, chunkHeight,
                    coord.x * chunkSize, coord.y * chunkSize,
                    seed, terrainFrequency, caveFrequency, caveThreshold, terrainAmplitude,
                    false, false, false, false  // isEdge 플래그 모두 false
                );

                chunk.BuildMeshDeferred(densities, voxelSize, chunkHeight);
            }

            // 콜라이더 큐에 추가
            AddToColliderQueue(chunk);

            // === 3. 벽 상태 업데이트 ===
            UpdateChunkWalls(coord);
            UpdateAdjacentChunkWalls(coord);
        }

        /// <summary>
        /// 청크의 벽 상태를 업데이트한다 (인접 청크 존재 여부에 따라).
        /// </summary>
        private void UpdateChunkWalls(Vector2Int coord)
        {
            if (!chunks.TryGetValue(coord, out VoxelChunk chunk)) return;

            bool needsMinX = !chunks.ContainsKey(new Vector2Int(coord.x - 1, coord.y));
            bool needsMaxX = !chunks.ContainsKey(new Vector2Int(coord.x + 1, coord.y));
            bool needsMinZ = !chunks.ContainsKey(new Vector2Int(coord.x, coord.y - 1));
            bool needsMaxZ = !chunks.ContainsKey(new Vector2Int(coord.x, coord.y + 1));

            chunk.UpdateWalls(needsMinX, needsMaxX, needsMinZ, needsMaxZ);
        }

        /// <summary>
        /// 인접 청크들의 벽 상태를 업데이트한다.
        /// </summary>
        private void UpdateAdjacentChunkWalls(Vector2Int coord)
        {
            Vector2Int[] adjacentCoords = new Vector2Int[]
            {
                new Vector2Int(coord.x - 1, coord.y),
                new Vector2Int(coord.x + 1, coord.y),
                new Vector2Int(coord.x, coord.y - 1),
                new Vector2Int(coord.x, coord.y + 1)
            };

            foreach (var adjCoord in adjacentCoords)
            {
                UpdateChunkWalls(adjCoord);
            }
        }

        private void ProcessOneUnload()
        {
            Vector2Int coord = unloadQueue.Dequeue();
            if (!chunks.TryGetValue(coord, out VoxelChunk chunk)) return;

            // 수정된 청크 데이터 저장
            if (modifiedChunks.Contains(coord) || chunk.IsModified)
            {
                float[,,] dataCopy = chunk.CopyDensities();
                if (dataCopy != null)
                {
                    savedChunkData[coord] = dataCopy;
                    modifiedChunks.Add(coord);
                }
            }

            dirtyMeshChunks.Remove(chunk);
            colliderQueue.Remove(chunk);
            colliderPending.Remove(chunk);

            Destroy(chunk.gameObject);
            chunks.Remove(coord);

            // 인접 청크의 벽 상태 업데이트 (언로드된 방향에 벽 필요)
            UpdateAdjacentChunkWalls(coord);
        }

        private void ProcessColliderUpdates()
        {
            int processed = 0;
            while (colliderQueue.Count > 0 && processed < maxColliderUpdatesPerFrame)
            {
                VoxelChunk chunk = colliderQueue[0];
                colliderQueue.RemoveAt(0);
                colliderPending.Remove(chunk);

                if (chunk != null && chunk.gameObject.activeSelf)
                {
                    chunk.UpdateCollider();
                    processed++;
                }
            }
        }

        // ==================== 청크 큐잉 ====================

        private void QueueChunksAroundPlayer()
        {
            if (playerTransform == null) return;

            Vector2Int currentChunk = WorldToChunkCoord(playerTransform.position);
            if (currentChunk == lastPlayerChunk) return;
            lastPlayerChunk = currentChunk;

            // 필요한 청크 수집 및 거리순 정렬
            List<Vector2Int> neededChunks = new List<Vector2Int>();
            for (int dx = -viewDistance; dx <= viewDistance; dx++)
            {
                for (int dz = -viewDistance; dz <= viewDistance; dz++)
                {
                    Vector2Int coord = new Vector2Int(currentChunk.x + dx, currentChunk.y + dz);
                    if (!chunks.ContainsKey(coord) && !pendingChunks.Contains(coord))
                    {
                        neededChunks.Add(coord);
                    }
                }
            }

            // 거리순 정렬 (가까운 것 먼저)
            neededChunks.Sort((a, b) =>
            {
                int distA = Mathf.Abs(a.x - currentChunk.x) + Mathf.Abs(a.y - currentChunk.y);
                int distB = Mathf.Abs(b.x - currentChunk.x) + Mathf.Abs(b.y - currentChunk.y);
                return distA.CompareTo(distB);
            });

            foreach (var coord in neededChunks)
            {
                chunkQueue.Enqueue(coord);
                pendingChunks.Add(coord);
            }

            // 언로드 대상
            int unloadDistance = viewDistance + unloadBuffer;
            foreach (var kvp in chunks)
            {
                Vector2Int coord = kvp.Key;
                int dist = Mathf.Max(Mathf.Abs(coord.x - currentChunk.x), Mathf.Abs(coord.y - currentChunk.y));
                if (dist > unloadDistance && !unloadQueue.Contains(coord))
                {
                    unloadQueue.Enqueue(coord);
                }
            }
        }

        private void AddToColliderQueue(VoxelChunk chunk)
        {
            if (!colliderPending.Contains(chunk))
            {
                colliderQueue.Add(chunk);
                colliderPending.Add(chunk);
            }
        }

        // ==================== 플레이어 등록 ====================

        public void SetPlayer(Transform player)
        {
            playerTransform = player;
            lastPlayerChunk = new Vector2Int(int.MinValue, int.MinValue);

            // 플레이어 바로 아래 청크들 즉시 동기 생성 (단순화: 노이즈 기반 연속 밀도)
            // 5x5 = 25개 청크만 동기 생성 (성능 최적화), 나머지는 비동기 큐
            Vector2Int currentChunk = WorldToChunkCoord(player.position);
            const int initialRadius = 2; // 5x5 청크 (-2 ~ +2)

            for (int dx = -initialRadius; dx <= initialRadius; dx++)
            {
                for (int dz = -initialRadius; dz <= initialRadius; dz++)
                {
                    Vector2Int coord = new Vector2Int(currentChunk.x + dx, currentChunk.y + dz);
                    if (!chunks.ContainsKey(coord))
                    {
                        CreateChunkImmediate(coord);
                    }
                }
            }

            // 모든 초기 청크의 벽 상태 업데이트
            UpdateAllInitialWalls(currentChunk, initialRadius);

            // 나머지는 큐에 추가
            lastPlayerChunk = currentChunk;
            QueueChunksAroundPlayer();
        }

        /// <summary>
        /// 청크를 즉시 생성한다 (플레이어 초기 위치용).
        /// </summary>
        private void CreateChunkImmediate(Vector2Int coord)
        {
            Vector3 chunkPosition = ChunkCoordToWorldPos(coord);
            GameObject chunkObj = new GameObject($"Chunk_{coord.x}_{coord.y}");
            chunkObj.transform.parent = transform;
            chunkObj.transform.position = chunkPosition;

            chunkObj.AddComponent<MeshFilter>();
            chunkObj.AddComponent<MeshRenderer>();
            chunkObj.AddComponent<MeshCollider>();

            VoxelChunk chunk = chunkObj.AddComponent<VoxelChunk>();
            chunk.Initialize(terrainMaterial);

            chunks[coord] = chunk;

            // 노이즈 기반 밀도 생성 (isEdge 사용 안 함)
            float[,,] densities = VoxelData.GenerateDensityField(
                chunkSize, chunkHeight,
                coord.x * chunkSize, coord.y * chunkSize,
                seed, terrainFrequency, caveFrequency, caveThreshold, terrainAmplitude,
                false, false, false, false
            );

            chunk.BuildMesh(densities, voxelSize, chunkHeight);
        }

        /// <summary>
        /// 초기 청크들의 벽 상태를 일괄 업데이트한다.
        /// SetPlayer()에서 모든 초기 청크 생성 후 호출.
        /// </summary>
        private void UpdateAllInitialWalls(Vector2Int centerChunk, int radius)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                for (int dz = -radius; dz <= radius; dz++)
                {
                    Vector2Int coord = new Vector2Int(centerChunk.x + dx, centerChunk.y + dz);
                    UpdateChunkWalls(coord);
                }
            }
        }

        // ==================== 좌표 변환 ====================

        public Vector2Int WorldToChunkCoord(Vector3 worldPos)
        {
            return new Vector2Int(
                Mathf.FloorToInt(worldPos.x / chunkWorldSize),
                Mathf.FloorToInt(worldPos.z / chunkWorldSize)
            );
        }

        public Vector3 ChunkCoordToWorldPos(Vector2Int coord)
        {
            return new Vector3(coord.x * chunkWorldSize, 0f, coord.y * chunkWorldSize);
        }

        // ==================== 지형 수정 ====================

        public void ModifyTerrain(Vector3 worldPos, float radius, float intensity)
        {
            float radiusWorld = radius * voxelSize;
            Vector2Int minChunk = WorldToChunkCoord(worldPos - new Vector3(radiusWorld, 0, radiusWorld));
            Vector2Int maxChunk = WorldToChunkCoord(worldPos + new Vector3(radiusWorld, 0, radiusWorld));

            for (int cx = minChunk.x; cx <= maxChunk.x; cx++)
            {
                for (int cz = minChunk.y; cz <= maxChunk.y; cz++)
                {
                    Vector2Int coord = new Vector2Int(cx, cz);
                    if (!chunks.TryGetValue(coord, out VoxelChunk chunk)) continue;
                    if (chunk == null || !chunk.gameObject.activeSelf) continue;

                    Vector3 chunkPos = chunk.transform.position;
                    float chunkMaxY = chunkPos.y + chunkHeight * voxelSize;

                    if (worldPos.x + radiusWorld < chunkPos.x || worldPos.x - radiusWorld > chunkPos.x + chunkWorldSize) continue;
                    if (worldPos.z + radiusWorld < chunkPos.z || worldPos.z - radiusWorld > chunkPos.z + chunkWorldSize) continue;
                    if (worldPos.y + radiusWorld < chunkPos.y || worldPos.y - radiusWorld > chunkMaxY) continue;

                    if (chunk.ModifyDensity(worldPos - chunkPos, radius, intensity))
                    {
                        dirtyMeshChunks.Add(chunk);
                        AddToColliderQueue(chunk);
                        modifiedChunks.Add(coord);
                    }
                }
            }
        }

        // ==================== 유틸리티 ====================

        public VoxelChunk GetChunk(Vector2Int coord)
        {
            chunks.TryGetValue(coord, out VoxelChunk chunk);
            return chunk;
        }

        public int GetLoadedChunkCount() => chunks.Count;
        public int GetPendingCount() => chunkQueue.Count;

        public void ClearSavedData()
        {
            savedChunkData.Clear();
            modifiedChunks.Clear();
        }

        [ContextMenu("모든 청크 언로드")]
        public void UnloadAllChunks()
        {
            chunkQueue.Clear();
            pendingChunks.Clear();
            unloadQueue.Clear();
            colliderQueue.Clear();
            colliderPending.Clear();
            dirtyMeshChunks.Clear();

            foreach (var kvp in chunks)
            {
                if (kvp.Value != null) Destroy(kvp.Value.gameObject);
            }
            chunks.Clear();
            lastPlayerChunk = new Vector2Int(int.MinValue, int.MinValue);
        }

        [ContextMenu("월드 초기화")]
        public void ResetWorld()
        {
            UnloadAllChunks();
            ClearSavedData();
            if (playerTransform != null) SetPlayer(playerTransform);
        }
    }
}
