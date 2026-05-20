using System;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using DotnetGltfRenderer;
using DotnetGltfViewer.Windows.Sidebar;
using DotnetGltfViewer.Windows.VR;
using NativeFileDialogCore;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGLES;
using Silk.NET.Windowing;
using ZLogger;

namespace DotnetGltfViewer.Windows {
    /// <summary>
    /// 主窗口类，负责窗口创建、事件处理和渲染协调。
    /// </summary>
    public static class MainWindow {
        static IWindow _window;
        static GL _gl;
        static bool _isClosing;

        static Scene _scene;
        static Renderer _renderer;
        static Camera _camera;
        static AppContext _context;
        static XrManager _xrManager;

        // VR 模式
        public static bool VREnabled { get; private set; }
        static Vector3 _vrCameraOffset;

        // 默认路径
        const string DefaultModelPath = "Models/DamagedHelmet/glTF/DamagedHelmet.gltf";
        const string DefaultEnvironmentMapPath = "Environments/Cannon_Exterior.hdr";
        const string ShadersDirectory = "shaders";

        public static Vector2D<int> Size { get; private set; }

        public static float MonitorScale => GetDpiForWindow(_window.Native?.Win32?.Hwnd ?? IntPtr.Zero) / 96f;

        /// <summary>
        /// 初始化主窗口实例。
        /// </summary>
        public static void Initialize() {
            // 检测 VR 模式
            VREnabled = Array.Exists(Environment.GetCommandLineArgs(), arg => arg.Equals("-vr", StringComparison.OrdinalIgnoreCase));

            Window.ShouldLoadFirstPartyPlatforms(false);
            Window.TryAdd("Silk.NET.Windowing.Glfw");
            InputWindowExtensions.ShouldLoadFirstPartyPlatforms(false);
            InputWindowExtensions.TryAdd("Silk.NET.Input.Glfw");
            WindowOptions options = WindowOptions.Default;
            Rectangle<int> bounds = Monitor.GetMainMonitor(null).Bounds;
            // VR 模式也使用 OpenGL ES 3.0
            options.API = new GraphicsAPI(ContextAPI.OpenGLES, new APIVersion(3, 0));
            Vector2D<int> size = new((int)(bounds.Size.X * 0.8f), (int)(bounds.Size.Y * 0.8f));
            options.Size = size;
            options.Position = bounds.Origin + (bounds.Size - size) / 2;
            options.Title = VREnabled ? "DotnetGltfViewer [VR]" : "DotnetGltfViewer";
            options.VSync = true;
            options.UpdatesPerSecond = 60;
            _window = Window.Create(options);
        }

        /// <summary>
        /// 运行主窗口消息循环。
        /// </summary>
        public static void Run() {
            _window.ShouldSwapAutomatically = false;
            _window.Load += OnLoad;
            _window.Render += OnRender;
            _window.FramebufferResize += OnFramebufferResize;
            _window.Closing += OnClosing;
            _window.Run();
        }

        /// <summary>
        /// 窗口加载完成事件处理。
        /// 初始化 OpenGL 上下文、输入系统和渲染循环。
        /// </summary>
        static void OnLoad() {
            LogManager.Logger.ZLogInformation($"初始化窗口...");
            _gl = GL.GetApi(_window);
            GlContext.GL = _gl;

            IInputContext input = _window.CreateInput();
            LogManager.Logger.ZLogInformation($"窗口初始化完成, Size: {_window.Size.X}x{_window.Size.Y}");
            LogManager.Logger.ZLogInformation($"OpenGL 版本: {_gl.GetStringS(StringName.Version)}");
            LogManager.Logger.ZLogInformation($"渲染器: {_gl.GetStringS(StringName.Renderer)}");

            // 初始化场景
            _scene = new Scene();

            // 加载默认模型
            if (File.Exists(DefaultModelPath)) {
                _scene.AddModel(DefaultModelPath);
            }

            // 初始化渲染器
            _renderer = new Renderer(_scene, DefaultEnvironmentMapPath, ShadersDirectory);
            _camera = _renderer.Camera;

            // 创建 AppContext
            _context = new AppContext(_scene, _renderer, _camera);
            _context.FocusRequested += () => FocusOnSelection();
            _context.CloseRequested += Close;

            // 初始化各管理器
            OnFramebufferResize(_window.FramebufferSize);
            SelectionManager.Initialize(_context);
            ImGuiManager.Initialize(_window, input, _context);
            InputManager.Initialize(_context, input);
            CameraController.ResetCameraToScene(_scene, _camera, _window.Size);
            PerformanceManager.Initialize();

            // VR 模式：初始化 OpenXR
            if (VREnabled) {
                InitializeVR();
            }
        }

        static void InitializeVR() {
            try {
                nint hwnd = _window.Native?.Win32?.Hwnd ?? IntPtr.Zero;
                nint hdc = GetDC(hwnd);
                nint hglrc = wglGetCurrentContext();

                _xrManager = new XrManager();
                _xrManager.Initialize(hdc, hglrc, _gl);
                ReleaseDC(hwnd, hdc);

                if (_xrManager.IsRunning) {
                    _renderer.SetFramebufferSize((int)_xrManager.SwapchainWidth, (int)_xrManager.SwapchainHeight);
                    ComputeVRCameraOffset();
                    LogManager.Logger.ZLogInformation($"VR 模式已启用，swapchain: {_xrManager.SwapchainWidth}x{_xrManager.SwapchainHeight}");
                }
                else {
                    LogManager.Logger.ZLogWarning($"OpenXR 初始化失败，回退到桌面模式");
                    _xrManager?.Dispose();
                    _xrManager = null;
                    VREnabled = false;
                }
            }
            catch (Exception ex) {
                LogManager.Logger.ZLogError($"OpenXR 初始化异常: {ex.Message}");
                _xrManager = null;
                VREnabled = false;
            }
        }

        /// <summary>
        /// 计算 VR 相机偏移，使用户站在模型前方合适距离处
        /// </summary>
        static void ComputeVRCameraOffset() {
            if (!_scene.TryGetSceneBounds(out Vector3 min, out Vector3 max)) {
                _vrCameraOffset = Vector3.Zero;
                return;
            }
            Vector3 center = (min + max) * 0.5f;
            Vector3 size = max - min;
            float maxExtent = Math.Max(Math.Max(size.X, size.Y), size.Z);

            // 用户站在模型中心前方（+Z 方向），看向 -Z 方向面对模型
            // OpenXR Local space: +X 右, +Y 上, -Z 前
            _vrCameraOffset = new Vector3(center.X, center.Y, center.Z + maxExtent * 1.5f);
        }

        /// <summary>
        /// 窗口渲染事件处理。
        /// </summary>
        /// <param name="deltaTime">帧间隔时间（秒）。</param>
        static void OnRender(double deltaTime) {
            if (_isClosing) {
                _window.Close();
                return;
            }

            if (VREnabled && _xrManager?.IsRunning == true) {
                RenderVR(deltaTime);
            }
            else {
                RenderDesktop(deltaTime);
            }
        }

        static void RenderDesktop(double deltaTime) {
            InputManager.Update((float)deltaTime);
            _scene.Update((float)deltaTime);
            _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
            _renderer.Render();
            RenderUI(deltaTime);
            PerformanceManager.Update(deltaTime);
            _window.SwapBuffers();
        }

        static void RenderVR(double deltaTime) {
            _scene.Update((float)deltaTime);

            _xrManager.PollEvents();
            if (!_xrManager.IsRunning) return;

            if (!_xrManager.BeginFrame()) return;

            if (_xrManager.ShouldRender) {
                for (int eye = 0; eye < 2; eye++) {
                    var (fbo, view) = _xrManager.AcquireEye(eye);

                    _gl.BindFramebuffer(FramebufferTarget.Framebuffer, fbo);
                    _gl.Viewport(0, 0, _xrManager.SwapchainWidth, _xrManager.SwapchainHeight);
                    _gl.Scissor(0, 0, _xrManager.SwapchainWidth, _xrManager.SwapchainHeight);
                    _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

                    // 将 HMD 位姿偏移到场景空间的合适位置
                    Silk.NET.OpenXR.Posef offsetPose = view.Pose;
                    offsetPose.Position = new() {
                        X = view.Pose.Position.X + _vrCameraOffset.X,
                        Y = view.Pose.Position.Y + _vrCameraOffset.Y,
                        Z = view.Pose.Position.Z + _vrCameraOffset.Z
                    };

                    System.Numerics.Matrix4x4 eyeView = VRCamera.CreateViewMatrix(offsetPose);
                    System.Numerics.Matrix4x4 eyeProj = VRCamera.CreateProjectionMatrix(view.Fov, 0.1f, 100f);
                    System.Numerics.Vector3 cameraPos = new(offsetPose.Position.X, offsetPose.Position.Y, offsetPose.Position.Z);

                    _renderer.Render(eyeView, eyeProj, cameraPos);

                    _xrManager.ReleaseEye(eye);
                }

                _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            }

            _xrManager.EndFrame();
        }

        /// <summary>
        /// 渲染 ImGui 用户界面。
        /// </summary>
        /// <param name="deltaTime">帧间隔时间（秒）。</param>
        static void RenderUI(double deltaTime) {
            ImGuiManager.Render((float)deltaTime);
        }

        /// <summary>
        /// 帧缓冲区大小变化事件处理。
        /// </summary>
        /// <param name="newSize">新的帧缓冲区大小。</param>
        static void OnFramebufferResize(Vector2D<int> newSize) {
            Size = newSize;
            if (_context != null) {
                _context.Size = newSize;
                _context.MonitorScale = MonitorScale;
            }
            _gl.Viewport(0, 0, (uint)newSize.X, (uint)newSize.Y);
            _gl.Scissor(0, 0, (uint)newSize.X, (uint)newSize.Y);
            _renderer?.SetFramebufferSize(newSize.X, newSize.Y);
        }

        /// <summary>
        /// 窗口关闭事件处理。
        /// 释放所有资源。
        /// </summary>
        static void OnClosing() {
            LogManager.Logger.ZLogInformation($"正在关闭窗口...");
            _xrManager?.Dispose();
            SidebarPanel.Dispose();
            ImGuiManager.Dispose();
            InputManager.Dispose();
            PerformanceManager.Dispose();
            _gl?.Dispose();
        }

        public static void Close() {
            _isClosing = true;
        }

        /// <summary>
        /// 重置相机以正视整个场景
        /// </summary>
        public static void ResetCameraToScene() {
            CameraController.ResetCameraToScene(_scene, _camera, _window.Size);
        }

        /// <summary>
        /// 聚焦到选中的物品，保持当前视角方向不变
        /// </summary>
        public static void FocusOnSelection(SceneModel selectedModel = null) {
            CameraController.FocusOnSelection(_scene, _camera, _window.Size, selectedModel);
        }

        /// <summary>
        /// 移动模型到画面中心，保持相机不动
        /// </summary>
        public static void MoveModelToScreenCenter(SceneModel sceneModel) {
            CameraController.MoveModelToScreenCenter(sceneModel, _camera);
        }

        /// <summary>
        /// 打开模型文件对话框
        /// </summary>
        public static void OpenModelFileDialog() {
            DialogResult result = Dialog.FileOpenEx("[glTF Files (*.gltf;*.glb)|*.gltf;*.glb]", null, "Open glTF Model");
            if (result.IsOk) {
                SceneModel sceneModel = SceneLoader.LoadModel(_scene, result.Path, _renderer);
                if (sceneModel != null) {
                    SelectionManager.Select(sceneModel);
                    MoveModelToScreenCenter(sceneModel);
                }
            }
        }

        /// <summary>
        /// 打开环境贴图文件对话框
        /// </summary>
        public static void OpenEnvironmentMapDialog() {
            DialogResult result = Dialog.FileOpenEx("[HDR Files (*.hdr)|*.hdr]", null, "Open Environment Map");
            if (result.IsOk) {
                SceneLoader.LoadEnvironmentMap(_renderer, result.Path);
            }
        }

        /// <summary>
        /// 清除场景中的所有模型
        /// </summary>
        public static void ClearScene() {
            SceneLoader.ClearScene(_scene, _renderer);
        }

        [DllImport("user32.dll", SetLastError = true)]
        static extern uint GetDpiForWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        static extern nint GetDC(nint hWnd);

        [DllImport("user32.dll")]
        static extern int ReleaseDC(nint hWnd, nint hDC);

        [DllImport("opengl32.dll")]
        static extern nint wglGetCurrentContext();
    }
}
