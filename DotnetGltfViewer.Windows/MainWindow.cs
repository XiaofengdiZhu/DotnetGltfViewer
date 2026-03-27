using System;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using DotnetGltfRenderer;
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

        // 默认路径
        const string DefaultModelPath = "Assets/DamagedHelmet/glTF/DamagedHelmet.gltf";
        const string DefaultEnvironmentMapPath = "Assets/Environments/Cannon_Exterior.hdr";
        const string ShadersDirectory = "shaders";

        public static Vector2D<int> Size { get; private set; }

        public static float MonitorScale => GetDpiForWindow(_window.Native?.Win32?.Hwnd ?? IntPtr.Zero) / 96f;

        /// <summary>
        /// 初始化主窗口实例。
        /// </summary>
        public static void Initialize() {
            Window.ShouldLoadFirstPartyPlatforms(false);
            Window.TryAdd("Silk.NET.Windowing.Glfw");
            InputWindowExtensions.ShouldLoadFirstPartyPlatforms(false);
            InputWindowExtensions.TryAdd("Silk.NET.Input.Glfw");
            WindowOptions options = WindowOptions.Default;
            Rectangle<int> bounds = Monitor.GetMainMonitor(null).Bounds;
            options.API = new GraphicsAPI(ContextAPI.OpenGLES, new APIVersion(3, 0));
            Vector2D<int> size = new((int)(bounds.Size.X * 0.8f), (int)(bounds.Size.Y * 0.8f));
            options.Size = size;
            options.Position = bounds.Origin + (bounds.Size - size) / 2;
            options.Title = "DotnetGltfViewer";
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
            GlContext.GL = _gl; // 设置全局 GL 上下文
            IInputContext input = _window.CreateInput();
            LogManager.Logger.ZLogInformation($"窗口初始化完成, Size: {_window.Size.X}x{_window.Size.Y}");
            LogManager.Logger.ZLogInformation($"OpenGL ES 版本: {_gl.GetStringS(StringName.Version)}");
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
            ImGuiManager.Initialize(_window, input, _camera, _scene);
            OnFramebufferResize(_window.FramebufferSize);
            ResetCameraToModel();
            InputManager.Initialize(_camera, input);
            InputManager.SetScene(_scene);
            PerformanceManager.Initialize();
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
            InputManager.Update((float)deltaTime);
            _scene.Update((float)deltaTime);
            _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
            _renderer.Render();
            RenderUI(deltaTime);
            PerformanceManager.Update(deltaTime);
            _window.SwapBuffers();
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
            ImGuiManager.Dispose();
            InputManager.Dispose();
            PerformanceManager.Dispose();
            _gl?.Dispose();
        }

        public static void Close() {
            _isClosing = true;
        }

        /// <summary>
        /// 重置相机以正视整个场景（启动时调用）
        /// </summary>
        public static void ResetCameraToModel() {
            if (_scene.TryGetSceneBounds(out Vector3 min, out Vector3 max)) {
                Vector2D<int> size = _window.Size;
                float aspect = (float)Math.Max(size.X, 1) / Math.Max(size.Y, 1);
                _renderer.Camera.LookAtBoundingBox(min, max, aspect);
            }
        }

        /// <summary>
        /// 聚焦到选中的物品，保持当前视角方向不变
        /// </summary>
        public static void FocusOnSelection(SceneModel selectedModel = null) {
            selectedModel ??= _scene?.SelectedModel;
            if (selectedModel == null) {
                return;
            }
            BoundingBox bounds = selectedModel.WorldBounds;
            if (bounds.IsValid) {
                Vector2D<int> size = _window.Size;
                float aspect = (float)Math.Max(size.X, 1) / Math.Max(size.Y, 1);
                _renderer.Camera.FocusOnBoundingBox(bounds.Min, bounds.Max, aspect);
            }
        }

        /// <summary>
        /// 移动模型到画面中心，保持相机不动
        /// </summary>
        public static void MoveModelToScreenCenter(SceneModel sceneModel) {
            if (sceneModel?.Model == null) {
                return;
            }
            BoundingBox bounds = sceneModel.WorldBounds;
            if (!bounds.IsValid) {
                return;
            }
            // 模型中心
            Vector3 modelCenter = (bounds.Min + bounds.Max) * 0.5f;
            // 画面中心（相机焦点）
            Vector3 screenCenter = _camera.FocalPoint;
            // 计算需要的平移量
            Vector3 translation = screenCenter - modelCenter;
            // 应用平移变换到模型的所有 MeshInstance
            Matrix4x4 translateMatrix = Matrix4x4.CreateTranslation(translation);
            foreach (MeshInstance instance in sceneModel.Model.MeshInstances) {
                instance.SetGizmoTransform(translateMatrix);
            }
            // 更新包围盒
            sceneModel.UpdateWorldBounds();
        }

        /// <summary>
        /// 打开模型文件对话框
        /// </summary>
        public static void OpenModelFileDialog() {
            DialogResult result = Dialog.FileOpenEx("[glTF Files (*.gltf;*.glb)|*.gltf;*.glb]", null, "Open glTF Model");
            if (result.IsOk) {
                try {
                    SceneModel sceneModel = _scene.AddModel(result.Path);
                    SelectionManager.Select(sceneModel);
                    MoveModelToScreenCenter(sceneModel);
                    _renderer.UpdateLightsFromScene();
                    LogManager.Logger.ZLogInformation($"Loaded model: {result.Path}");
                }
                catch (Exception ex) {
                    LogManager.Logger.ZLogError($"Failed to load model: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 打开环境贴图文件对话框
        /// </summary>
        public static void OpenEnvironmentMapDialog() {
            DialogResult result = Dialog.FileOpenEx("[HDR Files (*.hdr)|*.hdr]", null, "Open Environment Map");
            if (result.IsOk) {
                try {
                    _renderer.SetEnvironmentMap(result.Path);
                    LogManager.Logger.ZLogInformation($"Loaded environment map: {result.Path}");
                }
                catch (Exception ex) {
                    LogManager.Logger.ZLogError($"Failed to load environment map: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 清除场景中的所有模型
        /// </summary>
        public static void ClearScene() {
            SelectionManager.ClearSelection();
            _scene.Clear();
            _renderer.UpdateLightsFromScene();
        }

        /// <summary>
        /// 获取当前场景
        /// </summary>
        public static Scene GetScene() => _scene;

        /// <summary>
        /// 获取当前渲染器
        /// </summary>
        public static Renderer GetRenderer() => _renderer;

        [DllImport("user32.dll", SetLastError = true)]
        static extern uint GetDpiForWindow(IntPtr hwnd);
    }
}