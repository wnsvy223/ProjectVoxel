using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// FPS 스타일 카메라 컨트롤러 (Mirror 멀티플레이어 대응, 새 Input System 사용).
/// 플레이어 본체: 수평 회전(yaw)만 적용 → 다른 플레이어에게 자연스럽게 보임.
/// 카메라(자식): 수직 회전(pitch) 적용 → 위/아래 시점 독립 제어.
/// CharacterController 기반 지면 이동 + 중력 + 점프.
/// </summary>
public class FPSCameraController : MonoBehaviour
{
    [Header("이동 설정")]
    [Tooltip("이동 속도")]
    public float moveSpeed = 8f;

    [Tooltip("빠른 이동 속도 (Shift 키)")]
    public float sprintSpeed = 14f;

    [Tooltip("점프 힘")]
    public float jumpForce = 14f;

    [Tooltip("중력 가속도")]
    public float gravity = 20f;

    [Header("마우스 설정")]
    [Tooltip("마우스 감도")]
    public float mouseSensitivity = 0.15f;

    [Tooltip("수직 회전 제한 (도)")]
    public float pitchLimit = 89f;

    private float yaw;
    private float pitch;
    private float verticalVelocity;
    private bool cursorLocked = true;
    private Transform cameraTransform;
    private CharacterController characterController;

    void Start()
    {
        // 물리 컴포넌트 제거
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null) Destroy(rb);

        // CharacterController 참조
        characterController = GetComponent<CharacterController>();

        // 자식 카메라 찾기
        cameraTransform = GetComponentInChildren<Camera>()?.transform;

        // 초기 회전값
        yaw = transform.eulerAngles.y;
        pitch = 0f;

        LockCursor();
    }

    void Update()
    {
        var keyboard = Keyboard.current;
        var mouse = Mouse.current;
        if (keyboard == null || mouse == null) return;

        HandleCursorLock(keyboard);

        if (cursorLocked)
        {
            HandleMouseLook(mouse);
            HandleMovement(keyboard);
        }
    }

    private void HandleCursorLock(Keyboard keyboard)
    {
        if (keyboard.escapeKey.wasPressedThisFrame)
        {
            if (cursorLocked)
                UnlockCursor();
            else
                LockCursor();
        }
    }

    private void HandleMouseLook(Mouse mouse)
    {
        Vector2 delta = mouse.delta.ReadValue();
        float mouseX = delta.x * mouseSensitivity;
        float mouseY = delta.y * mouseSensitivity;

        yaw += mouseX;
        pitch -= mouseY;
        pitch = Mathf.Clamp(pitch, -pitchLimit, pitchLimit);

        // 플레이어 본체: 수평 회전만 (yaw)
        transform.rotation = Quaternion.Euler(0f, yaw, 0f);

        // 카메라: 수직 회전 (pitch)
        if (cameraTransform != null)
            cameraTransform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
    }

    private void HandleMovement(Keyboard keyboard)
    {
        if (characterController == null) return;

        float speed = keyboard.leftShiftKey.isPressed ? sprintSpeed : moveSpeed;

        // 플레이어 본체 기준 수평 방향 (yaw만 적용된 방향)
        Vector3 forward = transform.forward;
        Vector3 right = transform.right;

        Vector3 horizontalMove = Vector3.zero;
        if (keyboard.wKey.isPressed) horizontalMove += forward;
        if (keyboard.sKey.isPressed) horizontalMove -= forward;
        if (keyboard.dKey.isPressed) horizontalMove += right;
        if (keyboard.aKey.isPressed) horizontalMove -= right;

        if (horizontalMove.sqrMagnitude > 0f)
            horizontalMove = horizontalMove.normalized;

        // 중력 + 점프
        if (characterController.isGrounded)
        {
            // 지면에 있을 때: 약간의 하방 힘으로 접지 유지
            verticalVelocity = -2f;

            if (keyboard.spaceKey.wasPressedThisFrame)
                verticalVelocity = jumpForce;
        }
        else
        {
            // 공중: 중력 적용
            verticalVelocity -= gravity * Time.deltaTime;
        }

        // 최종 이동 벡터 = 수평 이동 + 수직 속도
        Vector3 finalMove = horizontalMove * speed + Vector3.up * verticalVelocity;
        characterController.Move(finalMove * Time.deltaTime);
    }

    private void LockCursor()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        cursorLocked = true;
    }

    private void UnlockCursor()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        cursorLocked = false;
    }
}
