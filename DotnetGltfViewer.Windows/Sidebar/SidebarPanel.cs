using System;
using System.Numerics;
using DotnetGltfRenderer;
using Hexa.NET.ImGui;

namespace DotnetGltfViewer.Windows.Sidebar;

/// <summary>
/// 侧边栏面板渲染类
/// </summary>
public static class SidebarPanel {
    static SidebarState _state;
    static Scene _scene;
    static Renderer _renderer;

    /// <summary>
    /// 侧边栏宽度
    /// </summary>
    public static float SidebarWidth { get; set; } = 600f;

    /// <summary>
    /// 最小侧边栏宽度
    /// </summary>
    const float MinSidebarWidth = 200f;

    /// <summary>
    /// 最大侧边栏宽度
    /// </summary>
    const float MaxSidebarWidth = 800f;

    /// <summary>
    /// 状态
    /// </summary>
    public static SidebarState State => _state;

    /// <summary>
    /// 初始化侧边栏面板
    /// </summary>
    public static void Initialize(Scene scene, Renderer renderer) {
        _scene = scene;
        _renderer = renderer;
        _state = new SidebarState();
        _state.Initialize();

        // 订阅选择变化事件
        SelectionManager.OnSelectionChanged += OnSelectionChanged;

        // 初始化各 Tab
        ModelsTab.Initialize(scene, renderer, _state);
        DisplayTab.Initialize(renderer, _state);
        AnimationTab.Initialize(scene, _state);
        AdvancedTab.Initialize(scene, renderer, _state);

        // 同步已经加载的模型状态
        if (_scene.Models.Count > 0) {
            SceneModel firstModel = _scene.Models[0];
            _state.UpdateAvailableScenes(firstModel.Model.SceneNames);
            UpdateAnimationList(firstModel.Model);
            UpdateStatistics();
        }
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public static void Dispose() {
        SelectionManager.OnSelectionChanged -= OnSelectionChanged;
    }

    /// <summary>
    /// 选择变化事件处理
    /// </summary>
    static void OnSelectionChanged(SceneModel model, MeshInstance instance) {
        if (model?.Model == null) {
            _state.UpdateAvailableScenes(null);
            _state.UpdateAvailableAnimations(null);
            _state.UpdateAvailableVariants(null, -1);
            _state.SelectedSceneIndex = -1;
            _state.SelectedAnimationIndex = -1;
            return;
        }

        // 同步场景列表
        _state.UpdateAvailableScenes(model.Model.SceneNames);
        _state.SelectedSceneIndex = model.Model.ActiveSceneIndex;

        // 同步动画列表
        UpdateAnimationList(model.Model);
        _state.SelectedAnimationIndex = model.Model.ActiveAnimationIndex;
        _state.IsAnimationPaused = model.Model.IsAnimationPaused;

        // 同步变体列表
        _state.UpdateAvailableVariants(model.Model.Variants, model.Model.ActiveVariantIndex);

        // 更新统计信息
        UpdateStatistics();
    }

    /// <summary>
    /// 渲染侧边栏
    /// </summary>
    public static void Render() {
        if (_state == null || !_state.IsVisible) {
            return;
        }

        // 计算侧边栏位置和大小
        Vector2 displaySize = ImGui.GetIO().DisplaySize;
        float menuBarHeight = ImGui.GetFrameHeight();

        // 设置窗口位置和大小
        ImGui.SetNextWindowPos(new Vector2(displaySize.X - SidebarWidth, menuBarHeight), ImGuiCond.Once);
        ImGui.SetNextWindowSize(new Vector2(SidebarWidth, displaySize.Y - menuBarHeight), ImGuiCond.Once);
        ImGui.SetNextWindowSizeConstraints(new Vector2(MinSidebarWidth, 100), new Vector2(MaxSidebarWidth, displaySize.Y));

        // 窗口标志（移除 NoResize 允许调整宽度）
        ImGuiWindowFlags flags = ImGuiWindowFlags.NoCollapse |
                                 ImGuiWindowFlags.NoBringToFrontOnFocus;

        // 开始渲染窗口
        bool isVisible = _state.IsVisible;
        if (ImGui.Begin("Sidebar", ref isVisible, flags)) {
            // 同步当前窗口宽度
            Vector2 windowSize = ImGui.GetWindowSize();
            SidebarWidth = windowSize.X;

            // 确保窗口保持在右侧
            Vector2 windowPos = ImGui.GetWindowPos();
            float expectedX = displaySize.X - SidebarWidth;
            if (Math.Abs(windowPos.X - expectedX) > 1f) {
                ImGui.SetWindowPos(new Vector2(expectedX, menuBarHeight));
            }

            RenderTabs();
        }
        _state.IsVisible = isVisible;
        ImGui.End();
    }

    /// <summary>
    /// 渲染 Tab 栏
    /// </summary>
    static void RenderTabs() {
        if (ImGui.BeginTabBar("SidebarTabs")) {
            ModelsTab.Render();
            DisplayTab.Render();
            AnimationTab.Render();
            AdvancedTab.Render();
            ImGui.EndTabBar();
        }
    }

    /// <summary>
    /// 更新动画列表
    /// </summary>
    public static void UpdateAnimationList(Model model) {
        AnimationTab.UpdateAnimationList(model);
    }

    /// <summary>
    /// 更新统计信息
    /// </summary>
    public static void UpdateStatistics() {
        if (_scene == null) {
            _state.ModelCount = 0;
            _state.MeshCount = 0;
            _state.TriangleCount = 0;
            _state.OpaqueMaterialCount = 0;
            _state.TransparentMaterialCount = 0;
            return;
        }

        _state.ModelCount = _scene.Models.Count;

        int meshCount = 0;
        int triangleCount = 0;
        int opaqueCount = 0;
        int transparentCount = 0;

        foreach (SceneModel sceneModel in _scene.Models) {
            if (sceneModel.Model == null) {
                continue;
            }

            meshCount += sceneModel.Model.MeshInstances.Count;

            foreach (MeshInstance instance in sceneModel.Model.MeshInstances) {
                if (instance.Mesh?.Indices != null) {
                    triangleCount += instance.Mesh.Indices.Length / 3;
                }

                if (instance.Mesh?.Material != null) {
                    if (instance.Mesh.Material.AlphaMode == AlphaMode.Opaque) {
                        opaqueCount++;
                    }
                    else {
                        transparentCount++;
                    }
                }
            }
        }

        _state.MeshCount = meshCount;
        _state.TriangleCount = triangleCount;
        _state.OpaqueMaterialCount = opaqueCount;
        _state.TransparentMaterialCount = transparentCount;
    }

    /// <summary>
    /// 更新状态（每帧调用）
    /// </summary>
    public static void Update() {
        if (_state == null) {
            return;
        }

        // 更新统计信息
        UpdateStatistics();
    }

    /// <summary>
    /// 切换侧边栏可见性
    /// </summary>
    public static void ToggleVisibility() {
        if (_state != null) {
            _state.IsVisible = !_state.IsVisible;
        }
    }
}
