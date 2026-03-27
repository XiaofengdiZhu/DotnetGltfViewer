using System;
using System.Numerics;
using DotnetGltfRenderer;
using Hexa.NET.ImGui;
using Hexa.NET.ImGuizmo;
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
        static bool _showGizmoOptions;
        static bool _showScenePanel;
        static Camera _camera;
        static Scene _scene;

        public static ImGuiIOPtr IO => _io;

        /// <summary>
        /// 初始化 ImGui 管理器。
        /// </summary>
        /// <param name="window">窗口实例。</param>
        /// <param name="input">输入上下文。</param>
        /// <param name="camera">相机实例。</param>
        /// <param name="scene">场景实例。</param>
        /// <param name="imGuiFontConfig">ImGui 字体配置。</param>
        /// <param name="onConfigureIO">配置 ImGui IO 回调。</param>
        public static void Initialize(IWindow window,
            Silk.NET.Input.IInputContext input,
            Camera camera,
            Scene scene,
            ImGuiFontConfig? imGuiFontConfig = null,
            Action onConfigureIO = null) {
            _controller = new ImGuiController(GlContext.GL, window, input, imGuiFontConfig, onConfigureIO ?? DefaultOnConfigureIO);
            ImGuizmo.SetImGuiContext(_controller.Context);
            _camera = camera;
            _scene = scene;
            GizmoManager.Initialize(scene);
            float scale = MainWindow.MonitorScale;
            if (scale > 1.0f) {
                ImGuiStylePtr style = ImGui.GetStyle();
                style.ScaleAllSizes(scale);
                style.FontScaleMain = scale;
            }
            // 初始化侧边栏
            SidebarPanel.Initialize(scene, MainWindow.GetRenderer());
            LogManager.Logger.ZLogDebug($"ImGui 管理器初始化完成");
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
            GizmoManager.Update();
            _controller.Update(deltaTime);
            ImGui.DockSpaceOverViewport(0, ImGui.GetMainViewport(), ImGuiDockNodeFlags.PassthruCentralNode);
            RenderMainMenuBar();
            RenderToolbar();
            RenderOptionalWindows();
            RenderGizmo();
            SidebarPanel.Render();
            SidebarPanel.Update();
            _controller.Render();
        }

        /// <summary>
        /// 渲染 Gizmo
        /// </summary>
        static void RenderGizmo() {
            if (_camera == null) {
                return;
            }
            Matrix4x4 viewMatrix = _camera.ViewMatrix;
            Matrix4x4 projectionMatrix = _camera.GetProjectionMatrix((float)MainWindow.Size.X / MainWindow.Size.Y);
            GizmoManager.Render(viewMatrix, projectionMatrix);
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
                    if (ImGui.MenuItem("Open Model...", "Ctrl+O")) {
                        MainWindow.OpenModelFileDialog();
                    }
                    if (ImGui.MenuItem("Open Environment Map...", "Ctrl+E")) {
                        MainWindow.OpenEnvironmentMapDialog();
                    }
                    ImGui.Separator();
                    if (ImGui.MenuItem("Clear Scene")) {
                        MainWindow.ClearScene();
                    }
                    ImGui.Separator();
                    if (ImGui.MenuItem("Quit", "Alt+F4")) {
                        MainWindow.Close();
                    }
                    ImGui.EndMenu();
                }
                if (ImGui.BeginMenu("View")) {
                    bool showSidebar = SidebarPanel.State?.IsVisible ?? false;
                    if (ImGui.MenuItem("Sidebar", string.Empty, showSidebar)) {
                        SidebarPanel.ToggleVisibility();
                    }
                    ImGui.MenuItem("Demo", string.Empty, ref _showDemoWindow);
                    ImGui.MenuItem("Metrics", string.Empty, ref _showMetricsWindow);
                    ImGui.MenuItem("Gizmo Options", string.Empty, ref _showGizmoOptions);
                    ImGui.MenuItem("Scene Panel", string.Empty, ref _showScenePanel);
                    ImGui.EndMenu();
                }
                ImGui.EndMainMenuBar();
            }
        }

        public static void RenderToolbar() {
            const float buttonWidth = 100f;
            const float buttonHeight = 36f;
            const float padding = 8f;
            float y = 48f;
            float h = buttonHeight * 5.6f + padding * 6f;
            // 左侧工具栏
            ImGui.SetNextWindowPos(new Vector2(padding, y));
            ImGui.SetNextWindowSize(new Vector2(buttonWidth + padding * 2f, h));
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(padding, padding));
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0f, padding));
            ImGuiWindowFlags flags = ImGuiWindowFlags.NoTitleBar
                | ImGuiWindowFlags.NoResize
                | ImGuiWindowFlags.NoMove
                | ImGuiWindowFlags.NoScrollbar
                | ImGuiWindowFlags.NoScrollWithMouse
                | ImGuiWindowFlags.NoCollapse
                | ImGuiWindowFlags.NoBringToFrontOnFocus;
            if (ImGui.Begin("GizmoToolbar1", flags)) {
                RenderToolbar1Button("Select", GizmoMode.None, "Num 1", buttonWidth, buttonHeight);
                RenderToolbar1Button("Move", GizmoMode.Translate, "Num 2", buttonWidth, buttonHeight);
                RenderToolbar1Button("Rotate", GizmoMode.Rotate, "Num 3", buttonWidth, buttonHeight);
                RenderToolbar1Button("Scale", GizmoMode.Scale, "Num 4", buttonWidth, buttonHeight);
                RenderToolbar2Button("Reset\nTrans.", "Reset transform to default", () => GizmoManager.ResetModelMatrix(), buttonWidth, buttonHeight * 1.6f);
            }
            ImGui.End();
            ImGui.PopStyleVar(2);
            y += h + padding;
            h = buttonHeight * 2.6f + padding * 3f;

            // 第二组工具栏
            ImGui.SetNextWindowPos(new Vector2(padding, y));
            ImGui.SetNextWindowSize(new Vector2(buttonWidth + padding * 2f, h));
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(padding, padding));
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0f, padding));
            if (ImGui.Begin("GizmoToolbar2", flags)) {
                RenderToolbar2Button("Focus", "Focus camera on selection (F)", () => MainWindow.FocusOnSelection(), buttonWidth, buttonHeight);
                RenderToolbar2Button("Reset\nCamera", "Reset camera to default", MainWindow.ResetCameraToScene, buttonWidth, buttonHeight * 1.6f);
                //RenderToolbar2Button("Delete" , "Delete selected object (Del)", () => _scene.RemoveModel(_scene.SelectedModel), buttonWidth, buttonHeight);
            }
            ImGui.End();
            ImGui.PopStyleVar(2);
        }

        static void RenderToolbar1Button(string label, GizmoMode mode, string shortcut, float buttonWidth, float buttonHeight) {
            bool isActive = GizmoManager.CurrentMode == mode;
            if (isActive) {
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.28f, 0.56f, 0.88f, 1.0f));
            }
            if (ImGui.Button(label, new Vector2(buttonWidth, buttonHeight))) {
                GizmoManager.CurrentMode = mode;
            }
            if (ImGui.IsItemHovered()) {
                ImGui.SetTooltip($"{label} ({shortcut})");
            }
            if (isActive) {
                ImGui.PopStyleColor();
            }
        }

        static void RenderToolbar2Button(string label, string toolTip, Action action, float buttonWidth, float buttonHeight) {
            if (ImGui.Button(label, new Vector2(buttonWidth, buttonHeight))) {
                action();
            }
            if (ImGui.IsItemHovered()) {
                ImGui.SetTooltip(toolTip);
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
            if (_showGizmoOptions) {
                ImGui.Begin("Gizmo Options", ref _showGizmoOptions);
                GizmoManager.RenderOptionsPanel();
                ImGui.End();
            }
            if (_showScenePanel) {
                RenderScenePanel();
            }
        }

        /// <summary>
        /// 渲染场景面板
        /// </summary>
        static void RenderScenePanel() {
            ImGui.Begin("Scene", ref _showScenePanel);
            if (_scene == null) {
                ImGui.Text("No scene loaded");
                ImGui.End();
                return;
            }

            // 模型列表
            ImGui.Text($"Models: {_scene.Models.Count}");
            ImGui.Separator();
            for (int i = 0; i < _scene.Models.Count; i++) {
                SceneModel model = _scene.Models[i];
                bool isSelected = _scene.SelectedModel == model;

                // 可见性复选框
                bool isVisible = model.IsVisible;
                ImGui.PushID($"visible_{i}");
                if (ImGui.Checkbox("##visible", ref isVisible)) {
                    model.IsVisible = isVisible;
                }
                ImGui.PopID();
                ImGui.SameLine();

                // 选中状态
                if (ImGui.Selectable($"{model.Name}", isSelected)) {
                    _scene.SelectModel(model);
                }

                // 右键菜单
                if (ImGui.BeginPopupContextItem()) {
                    if (ImGui.MenuItem("Remove")) {
                        _scene.RemoveModel(model);
                        break; // 避免在枚举时修改集合
                    }
                    ImGui.EndPopup();
                }
            }
            ImGui.Separator();

            // 添加模型按钮
            if (ImGui.Button("Add Model...")) {
                MainWindow.OpenModelFileDialog();
            }
            ImGui.End();
        }

        /// <summary>
        /// 释放 ImGui 管理器资源。
        /// </summary>
        public static void Dispose() {
            GizmoManager.Dispose();
            _controller?.Dispose();
            LogManager.Logger.ZLogDebug($"ImGui 管理器已释放");
        }
    }
}