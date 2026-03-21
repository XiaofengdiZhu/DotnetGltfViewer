using System;
using System.Runtime.InteropServices;
using Hexa.NET.ImGui;
using Silk.NET.OpenGLES;
using Silk.NET.OpenGLES.Extensions.Hexa.ImGui;
using Silk.NET.Windowing;
using ZLogger;

namespace DotnetGltfViewer.Windows {
    /// <summary>
    /// ImGui 管理器类，负责 ImGui 的初始化、更新和渲染。
    /// </summary>
    public static class ImGuiManager {
        static ImGuiController _controller;
        static ImGuiIOPtr _io;
        static bool _showDemoWindow;
        static bool _showMetricsWindow;

        public static ImGuiIOPtr IO => _io;

        /// <summary>
        /// 初始化 ImGui 管理器。
        /// </summary>
        /// <param name="gl">OpenGL ES 接口实例。</param>
        /// <param name="window">窗口实例。</param>
        /// <param name="input">输入上下文。</param>
        /// <param name="imGuiFontConfig">ImGui 字体配置。</param>
        /// <param name="onConfigureIO">配置 ImGui IO 回调。</param>
        public static void Initialize(GL gl,
            IWindow window,
            Silk.NET.Input.IInputContext input,
            ImGuiFontConfig? imGuiFontConfig = null,
            Action onConfigureIO = null) {
            _controller = new ImGuiController(gl, window, input, imGuiFontConfig, onConfigureIO ?? DefaultOnConfigureIO );
            float scale = MainWindow.MonitorScale;
            if (scale > 1.0f) {
                ImGuiStylePtr style = ImGui.GetStyle();
                style.ScaleAllSizes(scale);
                style.FontScaleMain = scale;
            }
            DotnetGltfRenderer.LogManager.Logger.ZLogDebug($"ImGui 管理器初始化完成");
        }

        static void DefaultOnConfigureIO() {
            _io = ImGui.GetIO();
            _io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;
        }

        /// <summary>
        /// 渲染 ImGui 内容。
        /// </summary>
        /// <param name="deltaTime">帧间隔时间（秒）。</param>
        public static void Render(float deltaTime) {
            _controller.Update(deltaTime);
            RenderMainMenuBar();
            RenderOptionalWindows();
            _controller.Render();
        }

        /// <summary>
        /// 显示 ImGui 演示窗口。
        /// </summary>
        /// <param name="open">是否显示窗口。</param>
        public static void ShowDemoWindow(ref bool open) {
            _showDemoWindow = open;
        }

        /// <summary>
        /// 显示 ImGui 性能指标窗口。
        /// </summary>
        /// <param name="open">是否显示窗口。</param>
        public static void ShowMetricsWindow(ref bool open) {
            _showMetricsWindow = open;
        }

        /// <summary>
        /// 渲染主菜单栏。
        /// </summary>
        public static void RenderMainMenuBar() {
            if (ImGui.BeginMainMenuBar()) {
                if (ImGui.BeginMenu("File")) {
                    if (ImGui.MenuItem("Open", "Ctrl+O")) { }
                    if (ImGui.MenuItem("Quit", "Alt+F4")) {
                        MainWindow.Close();
                    }
                    ImGui.EndMenu();
                }
                if (ImGui.BeginMenu("View")) {
                    ImGui.MenuItem("Demo", string.Empty, ref _showDemoWindow);
                    ImGui.MenuItem("Metrics", string.Empty, ref _showMetricsWindow);
                    ImGui.EndMenu();
                }
                ImGui.EndMainMenuBar();
            }
        }

        /// <summary>
        /// 渲染可选窗口（演示窗口、性能指标窗口等）。
        /// </summary>
        public static void RenderOptionalWindows() {
            if (_showDemoWindow) {
                ImGui.ShowDemoWindow(ref _showDemoWindow);
            }
            if (_showMetricsWindow) {
                ImGui.ShowMetricsWindow(ref _showMetricsWindow);
            }
        }

        /// <summary>
        /// 释放 ImGui 管理器资源。
        /// </summary>
        public static void Dispose() {
            _controller?.Dispose();
            DotnetGltfRenderer.LogManager.Logger.ZLogDebug($"ImGui 管理器已释放");
        }

        [DllImport("user32.dll", SetLastError = true)]
        static extern uint GetDpiForWindow(IntPtr hwnd);
    }
}