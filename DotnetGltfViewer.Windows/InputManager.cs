using System;
using System.Linq;
using System.Numerics;
using DotnetGltfRenderer;
using Hexa.NET.ImGui;
using Silk.NET.Input;

namespace DotnetGltfViewer.Windows {
    /// <summary>
    /// 输入管理器，处理键盘和鼠标输入，控制轨道相机
    /// </summary>
    public static class InputManager {
        static IInputContext _input;
        static IKeyboard _primaryKeyboard;
        static Camera _camera;
        static Scene _scene;

        static Vector2 _lastMousePosition;
        static bool _hasMousePosition;
        static bool _leftMouseDown;
        static bool _rightMouseDown;

        /// <summary>
        /// 鼠标左键旋转灵敏度
        /// </summary>
        public static float MouseOrbitSensitivity { get; set; } = 0.3f;

        /// <summary>
        /// 鼠标右键平移灵敏度
        /// </summary>
        public static float MousePanSensitivity { get; set; } = 0.6f;

        /// <summary>
        /// 滚轮缩放速度
        /// </summary>
        public static float ScrollSpeed { get; set; } = 0.5f;

        /// <summary>
        /// 键盘移动速度倍率
        /// </summary>
        public static float KeyboardMoveMultiplier { get; set; } = 4f;

        public static void Initialize(Camera camera, IInputContext input) {
            _camera = camera;
            _input = input;
            _primaryKeyboard = _input.Keyboards.FirstOrDefault();
            if (_primaryKeyboard != null) {
                _primaryKeyboard.KeyDown += OnKeyDown;
            }
            foreach (IMouse mouse in _input.Mice) {
                mouse.MouseDown += OnMouseDown;
                mouse.MouseUp += OnMouseUp;
                mouse.MouseMove += OnMouseMove;
                mouse.Scroll += OnMouseScroll;
            }
        }

        /// <summary>
        /// 设置场景引用（用于射线拾取）
        /// </summary>
        public static void SetScene(Scene scene) {
            _scene = scene;
        }

        public static void OnKeyDown(IKeyboard keyboard, Key key, int arg3) {
            if (ImGuiManager.IO.WantCaptureKeyboard) {
                return;
            }
            if (key == Key.Escape) {
                MainWindow.Close();
            }
        }

        /// <summary>
        /// 处理鼠标按下事件
        /// </summary>
        public static void OnMouseDown(IMouse mouse, MouseButton button) {
            if (ImGuiManager.IO.WantCaptureMouse || GizmoManager.IsOver()) {
                return;
            }

            // 选择模式：左键点击进行射线拾取，但仍然允许相机操作
            if (button == MouseButton.Left && GizmoManager.CurrentMode == GizmoMode.Select) {
                if (_scene != null && _camera != null) {
                    Vector2 screenPos = mouse.Position;
                    Vector2 screenSize = new Vector2(MainWindow.Size.X, MainWindow.Size.Y);
                    Ray ray = RayPicker.CreateRayFromScreen(screenPos, screenSize, _camera);
                    PickingResult result = RayPicker.Pick(_scene, ray);

                    if (result.Hit) {
                        SelectionManager.Select(result.Model, result.MeshInstance);
                    }
                    // 点击空白处不清除选择，保持当前选择状态
                }
                // 不再 return，继续处理相机操作
            }

            if (button == MouseButton.Left) {
                _leftMouseDown = true;
                _hasMousePosition = false;
            }
            else if (button == MouseButton.Right) {
                _rightMouseDown = true;
                _hasMousePosition = false;
            }
        }

        /// <summary>
        /// 处理鼠标释放事件
        /// </summary>
        public static void OnMouseUp(IMouse mouse, MouseButton button) {
            if (button == MouseButton.Left) {
                _leftMouseDown = false;
            }
            else if (button == MouseButton.Right) {
                _rightMouseDown = false;
            }
        }

        /// <summary>
        /// 处理鼠标移动事件
        /// </summary>
        public static void OnMouseMove(IMouse mouse, Vector2 position) {
            if (ImGuiManager.IO.WantCaptureMouse || GizmoManager.IsUsing()) {
                return;
            }
            if (!_hasMousePosition) {
                _lastMousePosition = position;
                _hasMousePosition = true;
                return;
            }

            Vector2 delta = position - _lastMousePosition;
            _lastMousePosition = position;

            if (_leftMouseDown) {
                // 左键拖拽：以固定点为中心旋转相机
                _camera.Orbit(delta.X * MouseOrbitSensitivity, -delta.Y * MouseOrbitSensitivity);
            }
            else if (_rightMouseDown) {
                // 右键拖拽：平移相机和固定点
                _camera.Pan(-delta.X * MousePanSensitivity, delta.Y * MousePanSensitivity);
            }
        }

        /// <summary>
        /// 处理鼠标滚轮事件
        /// </summary>
        public static void OnMouseScroll(IMouse mouse, ScrollWheel scrollWheel) {
            if (ImGuiManager.IO.WantCaptureMouse || GizmoManager.IsUsing()) {
                return;
            }
            // 滚轮：缩放（靠近/远离固定点）
            float delta = scrollWheel.Y;
            if (MathF.Abs(delta) > 0.0001f) {
                _camera.ZoomDistance(delta * ScrollSpeed);
            }
        }

        public static void Update(float deltaTime) {
            ProcessKeyboard(deltaTime);
        }

        /// <summary>
        /// 处理键盘输入（每帧调用）
        /// </summary>
        public static void ProcessKeyboard(float deltaTime) {
            if (ImGuiManager.IO.WantCaptureKeyboard) {
                return;
            }
            if (_primaryKeyboard == null) return;

            float speed = _camera.MoveSpeed * KeyboardMoveMultiplier * deltaTime;

            // 计算相机的前方向（朝向固定点的方向）和右方向
            Vector3 forward = _camera.GetForwardDirection();
            Vector3 right = _camera.GetRightDirection();
            Vector3 up = Vector3.UnitY;

            Vector3 movement = Vector3.Zero;

            if (_primaryKeyboard.IsKeyPressed(Key.W)) {
                movement += forward;
            }
            if (_primaryKeyboard.IsKeyPressed(Key.S)) {
                movement -= forward;
            }
            if (_primaryKeyboard.IsKeyPressed(Key.A)) {
                movement -= right;
            }
            if (_primaryKeyboard.IsKeyPressed(Key.D)) {
                movement += right;
            }
            if (_primaryKeyboard.IsKeyPressed(Key.Space)) {
                movement += up;
            }
            if (_primaryKeyboard.IsKeyPressed(Key.ShiftLeft) || _primaryKeyboard.IsKeyPressed(Key.ShiftRight)) {
                movement -= up;
            }

            if (movement.LengthSquared() > 0f) {
                movement = Vector3.Normalize(movement) * speed;
                // 键盘移动：同时平移相机和固定点
                _camera.Pan(movement);
            }
        }

        /// <summary>
        /// 重置鼠标状态
        /// </summary>
        public static void ResetMouseState() {
            _hasMousePosition = false;
            _leftMouseDown = false;
            _rightMouseDown = false;
        }

        public static void Dispose() {
            _primaryKeyboard.KeyDown -= OnKeyDown;
            foreach (IMouse mouse in _input?.Mice ?? []) {
                mouse.MouseDown -= OnMouseDown;
                mouse.MouseUp -= OnMouseUp;
                mouse.MouseMove -= OnMouseMove;
                mouse.Scroll -= OnMouseScroll;
            }
            _input?.Dispose();
        }
    }
}
