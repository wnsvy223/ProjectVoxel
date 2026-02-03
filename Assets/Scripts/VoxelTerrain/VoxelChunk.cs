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

            RebuildMeshVisual();
            UpdateCollider();
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
                        }
                    }
                }
            }

            return modified;
        }

        public float[,,] GetDensities() => densities;
        public float GetVoxelSize() => voxelSize;

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
        }
    }
}
