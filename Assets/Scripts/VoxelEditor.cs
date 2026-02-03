using UnityEngine;
using UnityEngine.InputSystem;
using VoxelTerrain;

/// <summary>
/// 복셀 지형 편집기 (새 Input System 사용).
/// 마우스 왼쪽 버튼: 레이캐스팅으로 지형 파기 (구멍 뚫기)
/// 마우스 오른쪽 버튼: 레이캐스팅으로 지형 채우기
/// Mirror 네트워크: GamePlayer의 Command/RPC를 통해 편집이 모든 클라이언트에 동기화됨.
/// </summary>
public class VoxelEditor : MonoBehaviour
{
    [Header("편집 설정")]
    [Tooltip("브러시 반경 (복셀 단위)")]
    [Range(1f, 10f)]
    public float brushRadius = 3f;

    [Tooltip("파기 강도")]
    [Range(0.1f, 2f)]
    public float digIntensity = 1.0f;

    [Tooltip("채우기 강도")]
    [Range(0.1f, 2f)]
    public float fillIntensity = 1.0f;

    [Tooltip("레이캐스트 최대 거리")]
    public float maxRayDistance = 200f;

    [Header("크로스헤어 설정")]
    [Tooltip("크로스헤어 크기")]
    public float crosshairSize = 12f;

    [Tooltip("크로스헤어 두께")]
    public float crosshairThickness = 2f;

    [Tooltip("크로스헤어 색상")]
    public Color crosshairColor = Color.white;

    private GamePlayer gamePlayer;
    private VoxelWorld voxelWorld;
    private Camera cam;
    private Texture2D crosshairTexture;

    // 편집 속도 제한 (초당 최대 20회)
    private float editTimer;
    private const float EDIT_INTERVAL = 0.05f;

    void Start()
    {
        cam = GetComponent<Camera>();
        if (cam == null)
            cam = Camera.main;

        // Mirror 네트워크 모드: 부모 GamePlayer를 통해 편집 동기화
        gamePlayer = GetComponentInParent<GamePlayer>();

        // 폴백: 싱글플레이어 모드 (GamePlayer가 없을 때)
        if (gamePlayer == null)
            voxelWorld = FindFirstObjectByType<VoxelWorld>();

        // 크로스헤어 텍스처 생성
        crosshairTexture = new Texture2D(1, 1);
        crosshairTexture.SetPixel(0, 0, Color.white);
        crosshairTexture.Apply();
    }

    void Update()
    {
        if (cam == null) return;

        var mouse = Mouse.current;
        if (mouse == null) return;

        // 커서가 잠금 상태일 때만 편집 가능
        if (Cursor.lockState != CursorLockMode.Locked) return;

        // 편집 속도 제한
        editTimer += Time.deltaTime;

        // 마우스 왼쪽 버튼: 파기
        if (mouse.leftButton.isPressed && editTimer >= EDIT_INTERVAL)
        {
            editTimer = 0f;
            TryModifyTerrain(-digIntensity);
        }

        // 마우스 오른쪽 버튼: 채우기
        if (mouse.rightButton.isPressed && editTimer >= EDIT_INTERVAL)
        {
            editTimer = 0f;
            TryModifyTerrain(fillIntensity);
        }

        // 마우스 휠로 브러시 크기 조절
        float scrollValue = mouse.scroll.y.ReadValue();
        if (scrollValue != 0f)
        {
            // 새 Input System의 scroll 값은 120 단위이므로 정규화
            float scrollDelta = scrollValue / 120f;
            brushRadius = Mathf.Clamp(brushRadius + scrollDelta * 0.5f, 1f, 10f);
        }
    }

    private void TryModifyTerrain(float intensity)
    {
        Ray ray = cam.ScreenPointToRay(new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f));

        if (Physics.Raycast(ray, out RaycastHit hit, maxRayDistance))
        {
            Vector3 editPoint;
            if (intensity < 0f)
            {
                // 파기: 레이 방향으로 약간 진입
                editPoint = hit.point + ray.direction * (brushRadius * 0.3f);
            }
            else
            {
                // 채우기: 법선 반대 방향(표면 안쪽)으로 약간 진입
                editPoint = hit.point - hit.normal * (brushRadius * 0.3f);
            }

            float scaledIntensity = intensity * EDIT_INTERVAL * 3f;

            if (gamePlayer != null)
            {
                // Mirror 네트워크: GamePlayer Command로 전송 → 모든 클라이언트에 동기화
                gamePlayer.RequestModifyTerrain(editPoint, brushRadius, scaledIntensity);
            }
            else if (voxelWorld != null)
            {
                // 싱글플레이어 폴백
                voxelWorld.ModifyTerrain(editPoint, brushRadius, scaledIntensity);
            }
        }
    }

    void OnGUI()
    {
        if (Cursor.lockState != CursorLockMode.Locked) return;

        // 크로스헤어 그리기
        float centerX = Screen.width * 0.5f;
        float centerY = Screen.height * 0.5f;
        float half = crosshairSize * 0.5f;
        float gap = 3f;

        GUI.color = crosshairColor;

        // 가로선 (좌)
        GUI.DrawTexture(new Rect(centerX - half, centerY - crosshairThickness * 0.5f, half - gap, crosshairThickness), crosshairTexture);
        // 가로선 (우)
        GUI.DrawTexture(new Rect(centerX + gap, centerY - crosshairThickness * 0.5f, half - gap, crosshairThickness), crosshairTexture);
        // 세로선 (상)
        GUI.DrawTexture(new Rect(centerX - crosshairThickness * 0.5f, centerY - half, crosshairThickness, half - gap), crosshairTexture);
        // 세로선 (하)
        GUI.DrawTexture(new Rect(centerX - crosshairThickness * 0.5f, centerY + gap, crosshairThickness, half - gap), crosshairTexture);

        // 브러시 크기 표시
        GUI.color = new Color(1f, 1f, 1f, 0.7f);
        GUIStyle style = new GUIStyle(GUI.skin.label);
        style.fontSize = 14;
        style.alignment = TextAnchor.UpperLeft;
        GUI.Label(new Rect(10, 10, 300, 30), $"브러시 크기: {brushRadius:F1} (마우스 휠로 조절)", style);
        GUI.Label(new Rect(10, 30, 300, 30), "좌클릭: 파기 | 우클릭: 채우기 | ESC: 커서 해제", style);
    }

    void OnDestroy()
    {
        if (crosshairTexture != null)
            Destroy(crosshairTexture);
    }
}
