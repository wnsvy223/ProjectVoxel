using UnityEngine;
using Mirror;
using VoxelTerrain;

/// <summary>
/// Mirror 네트워크 플레이어.
/// 로컬 플레이어만 카메라/입력을 활성화하고,
/// 지형 편집을 서버를 통해 모든 클라이언트에 동기화한다.
/// </summary>
public class GamePlayer : NetworkBehaviour
{
    [Header("초기 위치")]
    [Tooltip("플레이어 시작 높이 (지형 위)")]
    public float startHeight = 100f;

    private Camera playerCamera;

    void Awake()
    {
        // 모든 인스턴스에서 Rigidbody 제거 (캡슐이 굴러다니는 문제 방지)
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null) Destroy(rb);
    }

    public override void OnStartLocalPlayer()
    {
        // === 카메라 설정 ===
        playerCamera = GetComponentInChildren<Camera>(true);
        if (playerCamera != null)
        {
            playerCamera.enabled = true;
            playerCamera.tag = "MainCamera";
            playerCamera.farClipPlane = 1000f;

            AudioListener listener = playerCamera.GetComponent<AudioListener>();
            if (listener != null) listener.enabled = true;

            // VoxelEditor를 카메라에 추가
            if (playerCamera.GetComponent<VoxelEditor>() == null)
                playerCamera.gameObject.AddComponent<VoxelEditor>();
        }

        // === 로컬 플레이어 외형 (아이소메트릭 뷰에서는 보여야 함) ===
        // MeshRenderer는 활성 상태 유지

        // === CapsuleCollider → CharacterController 교체 (지면 이동용) ===
        Collider col = GetComponent<Collider>();
        if (col != null) Destroy(col);

        CharacterController cc = gameObject.AddComponent<CharacterController>();
        cc.height = 2f;
        cc.radius = 0.4f;
        cc.center = Vector3.zero;
        cc.stepOffset = 0.5f;
        cc.slopeLimit = 50f;

        // === 아이소메트릭 카메라 컨트롤러 추가 ===
        if (GetComponent<IsometricCameraController>() == null)
            gameObject.AddComponent<IsometricCameraController>();

        // === 시작 위치 ===
        transform.position = new Vector3(0, startHeight, 0);
        transform.rotation = Quaternion.identity;

        // === VoxelWorld에 플레이어 등록 (동적 청크 로딩용) ===
        VoxelWorld vw = FindFirstObjectByType<VoxelWorld>();
        if (vw != null)
            vw.SetPlayer(transform);
    }

    public override void OnStartClient()
    {
        if (!isLocalPlayer)
        {
            // 원격 플레이어: 카메라/오디오 비활성화
            Camera cam = GetComponentInChildren<Camera>(true);
            if (cam != null) cam.enabled = false;

            AudioListener listener = GetComponentInChildren<AudioListener>(true);
            if (listener != null) listener.enabled = false;
        }
    }

    // ========== 지형 편집 네트워크 동기화 ==========

    /// <summary>
    /// 지형 수정 요청 (VoxelEditor에서 호출).
    /// Command를 통해 서버에 전달하고, ClientRpc로 모든 클라이언트에 브로드캐스트한다.
    /// </summary>
    public void RequestModifyTerrain(Vector3 worldPos, float radius, float intensity)
    {
        CmdModifyTerrain(worldPos, radius, intensity);
    }

    [Command]
    private void CmdModifyTerrain(Vector3 worldPos, float radius, float intensity)
    {
        // 서버에서 모든 클라이언트로 브로드캐스트
        RpcModifyTerrain(worldPos, radius, intensity);
    }

    [ClientRpc]
    private void RpcModifyTerrain(Vector3 worldPos, float radius, float intensity)
    {
        VoxelWorld vw = FindFirstObjectByType<VoxelWorld>();
        if (vw != null)
            vw.ModifyTerrain(worldPos, radius, intensity);
    }
}
