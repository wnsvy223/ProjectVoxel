using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 아이소메트릭 카메라 컨트롤러 (Mirror 멀티플레이어 대응, 새 Input System 사용).
/// 고정된 아이소메트릭 시점에서 마우스 클릭으로 이동하는 방식.
/// 초기화 시 카메라를 플레이어에서 분리하여 독립적으로 추적.
/// CharacterController 기반 지면 이동 + 중력.
/// </summary>
public class IsometricCameraController : MonoBehaviour
{
    [Header("카메라 설정")]
    [Tooltip("플레이어로부터 카메라까지의 거리")]
    public float cameraDistance = 28f;

    [Header("이동 설정")]
    [Tooltip("이동 속도")]
    public float moveSpeed = 8f;

    [Tooltip("목표 지점 도달 판정 거리")]
    public float stoppingDistance = 0.3f;

    [Tooltip("중력 가속도")]
    public float gravity = 20f;

    [Tooltip("클릭 가능한 레이어")]
    public LayerMask groundLayer = ~0;

    private Vector3 targetPosition;
    private bool hasTarget;
    private float verticalVelocity;
    private Camera cam;
    private Transform cameraTransform;
    private CharacterController characterController;
    private Quaternion initialCameraRotation;
    private bool initialized;

    // 이동 목표 시각화용
    private GameObject targetIndicator;

    void Start()
    {
        TryInitialize();
    }

    private bool TryInitialize()
    {
        if (initialized) return true;

        // CharacterController 참조
        characterController = GetComponent<CharacterController>();
        if (characterController == null) return false;

        // 자식 카메라 찾기
        cam = GetComponentInChildren<Camera>();
        if (cam == null) return false;
        cameraTransform = cam.transform;

        // 프리팹에 설정된 카메라 회전값을 저장 (플레이어가 아직 identity 상태이므로 local = world)
        initialCameraRotation = cameraTransform.localRotation;

        // 카메라를 플레이어에서 분리 → 독립 오브젝트로 만듦
        // 플레이어 회전이 카메라에 영향을 주지 않음
        cameraTransform.SetParent(null);

        // 물리 컴포넌트 제거
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null) Destroy(rb);

        // 초기 목표 위치를 현재 위치로 설정
        targetPosition = transform.position;
        hasTarget = false;

        // 커서 표시 (아이소메트릭 뷰에서는 커서가 보여야 함)
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // 카메라 초기 위치 설정
        ForceUpdateCamera();

        // 클릭 지점 표시용 인디케이터 생성
        CreateTargetIndicator();

        initialized = true;
        return true;
    }

    void Update()
    {
        if (!initialized && !TryInitialize()) return;

        var mouse = Mouse.current;
        if (mouse == null) return;

        HandleClickToMove(mouse);
        HandleMovement();
        UpdateTargetIndicator();
    }

    void LateUpdate()
    {
        if (!initialized) return;
        UpdateCamera();
    }

    private void HandleClickToMove(Mouse mouse)
    {
        if (mouse.leftButton.wasPressedThisFrame)
        {
            if (cam == null) return;

            Vector2 mousePos = mouse.position.ReadValue();
            Ray ray = cam.ScreenPointToRay(mousePos);

            if (Physics.Raycast(ray, out RaycastHit hit, 1000f, groundLayer))
            {
                targetPosition = hit.point;
                hasTarget = true;
            }
            else if (RaycastGroundPlane(ray, transform.position.y, out Vector3 planeHit))
            {
                targetPosition = planeHit;
                hasTarget = true;
            }
        }
    }

    private bool RaycastGroundPlane(Ray ray, float groundY, out Vector3 hitPoint)
    {
        hitPoint = Vector3.zero;

        if (Mathf.Approximately(ray.direction.y, 0f))
            return false;

        float t = (groundY - ray.origin.y) / ray.direction.y;
        if (t < 0f)
            return false;

        hitPoint = ray.origin + ray.direction * t;
        return true;
    }

    private void HandleMovement()
    {
        if (characterController == null) return;

        Vector3 moveDirection = Vector3.zero;

        if (hasTarget)
        {
            Vector3 toTarget = targetPosition - transform.position;
            toTarget.y = 0f;
            float distance = toTarget.magnitude;

            if (distance > stoppingDistance)
            {
                Vector3 direction = toTarget.normalized;
                moveDirection = direction * moveSpeed;

                Quaternion targetRotation = Quaternion.LookRotation(direction);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, 10f * Time.deltaTime);
            }
            else
            {
                hasTarget = false;
            }
        }

        if (characterController.isGrounded)
        {
            verticalVelocity = -2f;
        }
        else
        {
            verticalVelocity -= gravity * Time.deltaTime;
        }

        Vector3 finalMove = moveDirection + Vector3.up * verticalVelocity;
        characterController.Move(finalMove * Time.deltaTime);
    }

    private void UpdateCamera()
    {
        if (cameraTransform == null) return;

        // 카메라는 독립 오브젝트이므로 단순히 위치/회전을 설정하면 됨
        cameraTransform.rotation = initialCameraRotation;
        cameraTransform.position = transform.position + initialCameraRotation * new Vector3(0f, 0f, -cameraDistance);
    }

    private void ForceUpdateCamera()
    {
        if (cameraTransform == null) return;
        cameraTransform.rotation = initialCameraRotation;
        cameraTransform.position = transform.position + initialCameraRotation * new Vector3(0f, 0f, -cameraDistance);
    }

    private void CreateTargetIndicator()
    {
        targetIndicator = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        targetIndicator.transform.localScale = new Vector3(0.5f, 0.05f, 0.5f);
        targetIndicator.name = "MoveTargetIndicator";

        Collider col = targetIndicator.GetComponent<Collider>();
        if (col != null) Destroy(col);

        Renderer renderer = targetIndicator.GetComponent<Renderer>();
        if (renderer != null)
        {
            Material mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            mat.SetFloat("_Surface", 1);
            mat.SetFloat("_Blend", 0);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
            mat.renderQueue = 4000;
            mat.color = new Color(1f, 0.2f, 0.1f, 0.8f);
            renderer.material = mat;
        }

        targetIndicator.SetActive(false);
    }

    private void UpdateTargetIndicator()
    {
        if (targetIndicator == null) return;

        if (hasTarget)
        {
            targetIndicator.SetActive(true);
            targetIndicator.transform.position = targetPosition + Vector3.up * 0.15f;

            // 지형 법선을 구해서 경사면에 맞춰 회전
            Ray downRay = new Ray(targetPosition + Vector3.up * 2f, Vector3.down);
            if (Physics.Raycast(downRay, out RaycastHit hit, 5f, groundLayer))
            {
                targetIndicator.transform.rotation = Quaternion.FromToRotation(Vector3.up, hit.normal);
            }
        }
        else
        {
            targetIndicator.SetActive(false);
        }
    }

    void OnDestroy()
    {
        if (targetIndicator != null)
            Destroy(targetIndicator);
    }
}
