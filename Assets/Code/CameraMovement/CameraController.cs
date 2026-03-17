using AlligUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

public class CameraController : MonoBehaviour
{
    public static CameraController Instance;
    private ModelViewerInput _input;
    private bool _orbiting, _panning;

    private float _yaw, _pitch, _verticalMove;

    private Vector2 _moveInput;

    [Header("References")]
    [SerializeField] private Transform _cameraRig;
    [SerializeField] private Transform _pivot;
    [SerializeField] private Camera _cam;

    [Header("Orbit")]
    [SerializeField] private float _orbitSpeed = 0.2f;
    [SerializeField] private float _minPitch = -80f;
    [SerializeField] private float _maxPitch = 80f;

    [Header("Pan")]
    [SerializeField] private float _panSpeed = 0.005f;

    [Header("Zoom")]
    [SerializeField] private float _zoomSpeed = 0.5f;
    [SerializeField] private float _minZoom = -0.5f;
    [SerializeField] private float _maxZoom = -50f;

    [Header("Fly")]
    [SerializeField] private float _flySpeed = 5f;
    [SerializeField] private float _fastMultiplier = 3f;

    [SerializeField] private float _virtualDistance = 10f;

    [Header("Mobile Touch")]
    [SerializeField] private float _touchOrbitSpeed = 0.3f;
    [SerializeField] private float _touchPanSpeed = 0.01f;
    [SerializeField] private float _touchZoomSpeed = 0.02f;
    [SerializeField] private float _pinchZoomSensitivity = 0.01f;

    // Touch gesture tracking
    private int _activeTouchCount = 0;
    private Vector2 _touch0Pos, _touch1Pos;
    private Vector2 _prevTouch0Pos, _prevTouch1Pos;
    private float _prevPinchDistance = 0f;
    private bool _isPinching = false;
    private bool _isTwoFingerPanning = false;

    public Camera MainCamera => _cam;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }


// #if UNITY_ANDROID
        Application.targetFrameRate = -1;

        // Disable VSync
        QualitySettings.vSyncCount = 0;
// #endif
    }

    private void OnEnable()
    {
        _input = InputHandler.Instance.Input;
        _input.Camera.Enable();

        _input.Camera.OrbitBtn.started += _ => _orbiting = true;
        _input.Camera.OrbitBtn.canceled += _ => _orbiting = false;

        _input.Camera.PanBtn.started += _ => _panning = true;
        _input.Camera.PanBtn.canceled += _ => _panning = false;

        _input.Camera.Look.performed += OnLook;
        _input.Camera.Pan.performed += OnPan;

        _input.Camera.Zoom.performed += OnZoom;

        _input.Camera.Move.performed += ctx => _moveInput = ctx.ReadValue<Vector2>();
        _input.Camera.Move.canceled += _ => _moveInput = Vector2.zero;

        _input.Camera.Vertical.performed += ctx => _verticalMove = ctx.ReadValue<float>();
        _input.Camera.Vertical.canceled += _ => _verticalMove = 0f;

        _input.Camera.Focus.performed += FocusOnSelectedObject;

        // Mobile touch inputs
        _input.Camera.Touch0Position.performed += ctx => _touch0Pos = ctx.ReadValue<Vector2>();
        _input.Camera.Touch1Position.performed += ctx => _touch1Pos = ctx.ReadValue<Vector2>();

        InputSystem.onAfterUpdate += ApplyMovement;
        InputSystem.onAfterUpdate += HandleTouchGestures;
    }

    private void OnDisable()
    {
        _input.Camera.Disable();

        _input.Camera.OrbitBtn.started -= _ => _orbiting = true;
        _input.Camera.OrbitBtn.canceled -= _ => _orbiting = false;
        _input.Camera.PanBtn.started -= _ => _panning = true;
        _input.Camera.PanBtn.canceled -= _ => _panning = false;

        _input.Camera.Look.performed -= OnLook;
        _input.Camera.Pan.performed -= OnPan;
        _input.Camera.Zoom.performed -= OnZoom;

        InputSystem.onAfterUpdate -= ApplyMovement;
        InputSystem.onAfterUpdate -= HandleTouchGestures;
    }

    private void HandleTouchGestures()
    {
        // Count active touches
        int touchCount = 0;
        var touchscreen = Touchscreen.current;

        if (touchscreen == null)
            return;

        for (int i = 0; i < touchscreen.touches.Count; i++)
        {
            if (touchscreen.touches[i].press.isPressed)
                touchCount++;
        }

        _activeTouchCount = touchCount;

        // Handle different touch counts
        if (_activeTouchCount == 0)
        {
            // Reset touch state
            _isPinching = false;
            _isTwoFingerPanning = false;
            _prevPinchDistance = 0f;
        }
        else if (_activeTouchCount == 1)
        {
            // One finger = orbit (handled by OnLook with OrbitBtn)
            _isPinching = false;
            _isTwoFingerPanning = false;
            _prevPinchDistance = 0f;
        }
        else if (_activeTouchCount >= 2)
        {
            // Two fingers = pinch zoom + pan
            HandleTwoFingerGestures();
        }
    }

    private void HandleTwoFingerGestures()
    {
        // Get current touch positions
        Vector2 touch0 = _touch0Pos;
        Vector2 touch1 = _touch1Pos;

        // Calculate current distance between touches
        float currentDistance = Vector2.Distance(touch0, touch1);

        if (!_isPinching && !_isTwoFingerPanning)
        {
            // Initialize gesture tracking
            _prevTouch0Pos = touch0;
            _prevTouch1Pos = touch1;
            _prevPinchDistance = currentDistance;
            _isPinching = true;
            _isTwoFingerPanning = true;
            return;
        }

        // Pinch to Zoom
        if (_isPinching && _prevPinchDistance > 0f)
        {
            float distanceDelta = currentDistance - _prevPinchDistance;

            // Only zoom if the distance change is significant
            if (Mathf.Abs(distanceDelta) > 1f)
            {
                float zoomAmount = -distanceDelta * _pinchZoomSensitivity;

                if (MainCamera.orthographic)
                {
                    _virtualDistance += zoomAmount;
                    _virtualDistance = Mathf.Clamp(_virtualDistance, 0.1f, 100f);
                    ZoomOrthographic();
                }
                else
                {
                    ZoomPerspectiveDirect(zoomAmount);
                }
            }

            _prevPinchDistance = currentDistance;
        }

        // Two-finger Pan
        if (_isTwoFingerPanning)
        {
            // Calculate the center point movement
            Vector2 prevCenter = (_prevTouch0Pos + _prevTouch1Pos) / 2f;
            Vector2 currentCenter = (touch0 + touch1) / 2f;
            Vector2 centerDelta = currentCenter - prevCenter;

            // Only pan if movement is significant
            if (centerDelta.sqrMagnitude > 0.1f)
            {
                Vector3 right = MainCamera.transform.right;
                Vector3 up = MainCamera.transform.up;

                _pivot.position -= (right * centerDelta.x + up * centerDelta.y) * _touchPanSpeed;
            }
        }

        // Update previous positions
        _prevTouch0Pos = touch0;
        _prevTouch1Pos = touch1;
    }

    private void OnLook(InputAction.CallbackContext ctx)
    {
        // For mobile: only orbit with one finger
        if (_activeTouchCount > 1)
            return;

        if (!_orbiting || _panning)
            return;

        Vector2 delta = ctx.ReadValue<Vector2>();

        // Use different sensitivity for touch vs mouse
        float sensitivity = (_activeTouchCount == 1) ? _touchOrbitSpeed : _orbitSpeed;

        _yaw += delta.x * sensitivity;
        _pitch -= delta.y * sensitivity;
        _pitch = Mathf.Clamp(_pitch, _minPitch, _maxPitch);

        _pivot.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
    }

    private void OnPan(InputAction.CallbackContext ctx)
    {
        // Disable mouse pan when using two-finger touch gestures
        if (_activeTouchCount >= 2)
            return;

        if (!_panning)
            return;

        Vector2 delta = ctx.ReadValue<Vector2>();

        Vector3 right = MainCamera.transform.right;
        Vector3 up = MainCamera.transform.up;

        _pivot.position -= (right * delta.x + up * delta.y) * _panSpeed;
    }

    private void OnZoom(InputAction.CallbackContext ctx)
    {
        // Disable mouse scroll zoom when pinching
        if (_isPinching)
            return;

        float scroll = ctx.ReadValue<float>();
        if (Mathf.Abs(scroll) < 0.01f)
            return;

        if (MainCamera.orthographic)
            ZoomOrthographic();
        else
            ZoomPerspective(scroll);

        _virtualDistance -= scroll * _zoomSpeed;
    }

    private void ZoomPerspective(float scroll)
    {
        Vector3 forward = MainCamera.transform.forward;
        Vector3 newPos = _pivot.position + forward * scroll * _zoomSpeed;
        _pivot.position = newPos;
    }

    private void ZoomPerspectiveDirect(float amount)
    {
        Vector3 forward = MainCamera.transform.forward;
        Vector3 newPos = _pivot.position + forward * amount;
        _pivot.position = newPos;
        _virtualDistance += amount;
    }

    private void ZoomOrthographic()
    {
        float fovRad = MainCamera.fieldOfView * Mathf.Deg2Rad;
        MainCamera.orthographicSize = _virtualDistance * Mathf.Tan(fovRad / 2f);
    }

    public void SetProjection(bool isOrthographic)
    {
        float fovRad = MainCamera.fieldOfView * Mathf.Deg2Rad;

        if (isOrthographic)
        {
            MainCamera.orthographicSize = _virtualDistance * Mathf.Tan(fovRad / 2f);
            MainCamera.orthographic = true;
        }
        else
        {
            _virtualDistance = MainCamera.orthographicSize / Mathf.Tan(fovRad / 2f);
            MainCamera.orthographic = false;
        }
    }

    void OnMove(InputAction.CallbackContext ctx)
    {
        Vector2 move = ctx.ReadValue<Vector2>();
        if (move.sqrMagnitude < 0.001f)
            return;

        float speed = _flySpeed;
        if (_input.Camera.FastMove.IsPressed())
            speed *= _fastMultiplier;

        Vector3 dir =
            MainCamera.transform.forward * move.y +
            MainCamera.transform.right * move.x;

        _pivot.position += dir * speed * Time.deltaTime;
    }

    private void ApplyMovement()
    {
        if (!_orbiting)
            return;
        if (_moveInput.sqrMagnitude < 0.001f && Mathf.Abs(_verticalMove) < 0.001f)
            return;

        float speed = _flySpeed;
        if (_input.Camera.FastMove.IsPressed())
            speed *= _fastMultiplier;

        Vector3 dir =
            MainCamera.transform.forward * _moveInput.y +
            MainCamera.transform.right * _moveInput.x +
            MainCamera.transform.up * _verticalMove;

        _pivot.position += dir * speed * Time.deltaTime;
    }

    public void SnapCameraView(Quaternion rotation)
    {
        SetRotation(rotation);
    }

    private void SetRotation(Quaternion rotation)
    {
        _pivot.localRotation = Quaternion.identity;
        _cameraRig.rotation = rotation;

        Vector3 euler = rotation.eulerAngles;
        _yaw = euler.y;
        _pitch = euler.x;

        if (_pitch > 180f)
            _pitch -= 360f;
    }

    private void FocusOnSelectedObject(InputAction.CallbackContext obj)
    {
        "Focusing".Print();
        // TODO: Implement focus on selected object
    }

    public Quaternion GetRotation()
    {
        return _cameraRig.rotation;
    }

#if UNITY_EDITOR
    private void OnGUI()
    {
        // Debug display for mobile testing
        if (_activeTouchCount > 0)
        {
            GUI.Label(new Rect(10, 10, 300, 20), $"Touch Count: {_activeTouchCount}");
            GUI.Label(new Rect(10, 30, 300, 20), $"Pinching: {_isPinching}");
            GUI.Label(new Rect(10, 50, 300, 20), $"Two Finger Pan: {_isTwoFingerPanning}");
        }
    }
#endif
}