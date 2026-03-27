using System;
using System.Collections.Generic;
using System.Numerics;
using DotnetGltfRenderer;
using Hexa.NET.ImGui;
using ZLogger;

namespace DotnetGltfViewer.Windows {
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

            // 同步已经加载的模型状态
            if (_scene.Models.Count > 0) {
                SceneModel firstModel = _scene.Models[0];
                _state.UpdateAvailableScenes(firstModel.Model.SceneNames);
                UpdateAnimationList(firstModel.Model);
                UpdateStatistics();
            }
        }

        /// <summary>
        /// 选择变化事件处理
        /// </summary>
        static void OnSelectionChanged(SceneModel model, MeshInstance instance) {
            if (model?.Model == null) {
                _state.UpdateAvailableScenes(null);
                _state.UpdateAvailableAnimations(null);
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
            ImGuiWindowFlags flags = ImGuiWindowFlags.NoMove |
                                     ImGuiWindowFlags.NoCollapse |
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
                RenderModelsTab();
                RenderDisplayTab();
                RenderAnimationTab();
                RenderAdvancedTab();
                ImGui.EndTabBar();
            }
        }

        #region Models Tab

        static void RenderModelsTab() {
            if (!ImGui.BeginTabItem("Models")) {
                return;
            }

            // Models 下拉菜单
            ImGui.Text("Model");
            ImGui.SetNextItemWidth(-1);
            int newModelIndex = _state.SelectedModelIndex;
            if (ImGui.Combo("##Model", ref newModelIndex, _state.AvailableModels.ToArray(), _state.AvailableModels.Count)) {
                if (newModelIndex != _state.SelectedModelIndex) {
                    _state.SelectedModelIndex = newModelIndex;
                    _state.ScanAvailableFlavors();
                    TryLoadSelectedModel();
                }
            }

            // Flavor 下拉菜单
            if (_state.AvailableFlavors.Count > 0) {
                ImGui.Text("Flavor");
                ImGui.SetNextItemWidth(-1);
                int newFlavorIndex = _state.SelectedFlavorIndex;
                if (ImGui.Combo("##Flavor", ref newFlavorIndex, _state.AvailableFlavors.ToArray(), _state.AvailableFlavors.Count)) {
                    if (newFlavorIndex != _state.SelectedFlavorIndex) {
                        _state.SelectedFlavorIndex = newFlavorIndex;
                        TryLoadSelectedModel();
                    }
                }
            }

            // Scenes 下拉菜单
            if (_state.AvailableScenes.Count > 0) {
                ImGui.Text("Scene");
                ImGui.SetNextItemWidth(-1);
                int newSceneIndex = _state.SelectedSceneIndex;
                if (ImGui.Combo("##Scene", ref newSceneIndex, _state.AvailableScenes.ToArray(), _state.AvailableScenes.Count)) {
                    if (newSceneIndex != _state.SelectedSceneIndex) {
                        _state.SelectedSceneIndex = newSceneIndex;
                        ApplySceneSelection();
                    }
                }
            }

            ImGui.EndTabItem();
        }

        static void TryLoadSelectedModel() {
            string modelPath = _state.GetSelectedModelPath();
            if (string.IsNullOrEmpty(modelPath)) {
                return;
            }

            try {
                // 只有一个模型时替换，多个模型时添加
                if (_scene.Models.Count <= 1) {
                    MainWindow.ClearScene();
                }

                // 加载模型
                SceneModel sceneModel = _scene.AddModel(modelPath);
                SelectionManager.Select(sceneModel);
                MainWindow.MoveModelToScreenCenter(sceneModel);
                _renderer.UpdateLightsFromScene();

                // 更新场景列表
                _state.UpdateAvailableScenes(sceneModel.Model.SceneNames);

                // 更新动画列表
                UpdateAnimationList(sceneModel.Model);

                // 更新统计信息
                UpdateStatistics();
            }
            catch (Exception ex) {
                LogManager.Logger.ZLogError($"Failed to load model: {ex.Message}");
            }
        }

        static void ApplySceneSelection() {
            if (_scene.SelectedModel?.Model == null) {
                return;
            }
            _scene.SelectedModel.Model.ActiveSceneIndex = _state.SelectedSceneIndex;
            _renderer.UpdateLightsFromScene();
            UpdateStatistics();
        }

        #endregion

        #region Display Tab

        static void RenderDisplayTab() {
            if (!ImGui.BeginTabItem("Display")) {
                return;
            }

            RenderLightingSection();
            ImGui.Separator();
            RenderBackgroundSection();

            ImGui.EndTabItem();
        }

        static void RenderLightingSection() {
            ImGui.Text("Lighting");

            // Image Based Lighting 开关
            bool useIBL = _state.UseIBL;
            if (ImGui.Checkbox("Image Based Lighting", ref useIBL)) {
                _state.UseIBL = useIBL;
                ApplyIBLSetting();
            }

            // Punctual Lighting 开关
            bool usePunctual = _state.UsePunctualLighting;
            if (ImGui.Checkbox("Punctual Lighting", ref usePunctual)) {
                _state.UsePunctualLighting = usePunctual;
                // TODO: Apply punctual lighting setting
            }

            // IBL Intensity 滑动条（只影响模型反射）
            float iblIntensity = _state.IBLIntensity;
            if (ImGui.SliderFloat("IBL Intensity", ref iblIntensity, 0.0f, 10.0f)) {
                _state.IBLIntensity = iblIntensity;
                ApplyIBLSetting();
            }

            // Exposure 滑动条
            float exposure = _state.Exposure;
            if (ImGui.SliderFloat("Exposure", ref exposure, 0.0f, 5.0f)) {
                _state.Exposure = exposure;
                ApplyExposureSetting();
            }

            // Tone Map 下拉菜单
            ImGui.Text("Tone Map");
            ImGui.SetNextItemWidth(-1);
            int toneMapIndex = _state.ToneMapIndex;
            if (ImGui.Combo("##ToneMap", ref toneMapIndex, SidebarState.ToneMapModes, SidebarState.ToneMapModes.Length)) {
                _state.ToneMapIndex = toneMapIndex;
                ApplyToneMapSetting();
            }
        }

        static void RenderBackgroundSection() {
            ImGui.Text("Background");

            // Skybox 开关
            bool showSkybox = _state.ShowSkybox;
            if (ImGui.Checkbox("Skybox", ref showSkybox)) {
                _state.ShowSkybox = showSkybox;
                ApplyEnvironmentMapVisibility();
            }

            // Skybox Intensity 滑动条（只影响天空盒亮度）
            if (showSkybox) {
                float skyboxIntensity = _state.SkyboxIntensity;
                if (ImGui.SliderFloat("Skybox Intensity", ref skyboxIntensity, 0.0f, 10.0f)) {
                    _state.SkyboxIntensity = skyboxIntensity;
                    ApplySkyboxIntensity();
                }
            }

            // Background Color 颜色选择器
            Vector3 bgColor = _state.BackgroundColor;
            if (ImGui.ColorEdit3("Background Color", ref bgColor)) {
                _state.BackgroundColor = bgColor;
                ApplyBackgroundColor();
            }

            // Environment Rotation 下拉菜单
            ImGui.Text("Environment Rotation");
            ImGui.SetNextItemWidth(-1);
            int envRotIndex = _state.EnvironmentRotationIndex;
            if (ImGui.Combo("##EnvRotation", ref envRotIndex, SidebarState.EnvironmentRotations, SidebarState.EnvironmentRotations.Length)) {
                _state.EnvironmentRotationIndex = envRotIndex;
                ApplyEnvironmentRotation();
            }

            // Active Environment 下拉菜单
            ImGui.Text("Active Environment");
            ImGui.SetNextItemWidth(-1);
            int envIndex = _state.SelectedEnvironmentIndex;
            if (ImGui.Combo("##Environment", ref envIndex, _state.AvailableEnvironments.ToArray(), _state.AvailableEnvironments.Count)) {
                _state.SelectedEnvironmentIndex = envIndex;
                ApplyEnvironmentMap();
            }
        }

        static void ApplyIBLSetting() {
            if (_renderer?.IBLManager != null) {
                _renderer.IBLManager.EnvironmentStrength = _state.UseIBL ? _state.IBLIntensity : 0.0f;
            }
        }

        static void ApplyExposureSetting() {
            if (_renderer?.LightingSystem != null) {
                _renderer.LightingSystem.Exposure = _state.Exposure;
            }
        }

        static void ApplySkyboxIntensity() {
            if (_renderer != null) {
                _renderer.SkyboxIntensity = _state.SkyboxIntensity;
            }
        }

        static void ApplyToneMapSetting() {
            if (_renderer != null) {
                _renderer.ToneMapMode = (ToneMapMode)_state.ToneMapIndex;
            }
        }

        static void ApplyEnvironmentRotation() {
            if (_renderer == null) {
                return;
            }
            // 根据索引转换为角度：0: +Z (0°), 1: -X (90°), 2: -Z (180°), 3: +X (270°)
            _renderer.EnvironmentRotation = _state.EnvironmentRotationIndex * 90f;
        }

        static void ApplyEnvironmentMapVisibility() {
            if (_renderer != null) {
                _renderer.ShowEnvironmentMap = _state.ShowSkybox;
            }
        }

        static void ApplyBackgroundColor() {
            if (_renderer != null) {
                _renderer.BackgroundColor = _state.BackgroundColor;
            }
        }

        static void ApplyEnvironmentMap() {
            string envPath = _state.GetSelectedEnvironmentPath();
            if (string.IsNullOrEmpty(envPath)) {
                return;
            }
            try {
                _renderer.SetEnvironmentMap(envPath);
            }
            catch (Exception ex) {
                LogManager.Logger.ZLogError($"Failed to load environment map: {ex.Message}");
            }
        }

        #endregion

        #region Animation Tab

        static void RenderAnimationTab() {
            if (!ImGui.BeginTabItem("Animation")) {
                return;
            }

            // Play/Pause 按钮
            string buttonText = _state.IsAnimationPaused ? "Play" : "Pause";
            if (ImGui.Button(buttonText, new Vector2(-1, 0))) {
                _state.IsAnimationPaused = !_state.IsAnimationPaused;
                ApplyAnimationPause();
            }

            // Animations 下拉菜单
            if (_state.AvailableAnimations.Count > 0) {
                ImGui.Text("Animation");
                ImGui.SetNextItemWidth(-1);
                int animIndex = _state.SelectedAnimationIndex;
                if (ImGui.Combo("##Animation", ref animIndex, _state.AvailableAnimations.ToArray(), _state.AvailableAnimations.Count)) {
                    _state.SelectedAnimationIndex = animIndex;
                    ApplyAnimationSelection();
                }
            }
            else {
                ImGui.Text("No animations available");
            }

            ImGui.EndTabItem();
        }

        static void UpdateAnimationList(Model model) {
            if (model == null) {
                _state.UpdateAvailableAnimations(null);
                _state.SelectedAnimationIndex = -1;
                return;
            }

            // 使用 Model 类暴露的动画名称列表
            List<string> animNames = new();
            for (int i = 0; i < model.AnimationNames.Count; i++) {
                string name = model.AnimationNames[i];
                animNames.Add(name);
            }
            _state.UpdateAvailableAnimations(animNames);
            _state.SelectedAnimationIndex = model.ActiveAnimationIndex;
            _state.IsAnimationPaused = model.IsAnimationPaused;
        }

        static void ApplyAnimationPause() {
            if (_scene?.SelectedModel?.Model != null) {
                _scene.SelectedModel.Model.IsAnimationPaused = _state.IsAnimationPaused;
            }
        }

        static void ApplyAnimationSelection() {
            if (_scene?.SelectedModel?.Model != null) {
                _scene.SelectedModel.Model.SetActiveAnimation(_state.SelectedAnimationIndex);
            }
        }

        #endregion

        #region Advanced Tab

        static void RenderAdvancedTab() {
            if (!ImGui.BeginTabItem("Advanced")) {
                return;
            }

            // Debug Channels 下拉菜单
            ImGui.Text("Debug Channels");
            ImGui.SetNextItemWidth(-1);
            int debugIndex = _state.DebugChannelIndex;
            if (ImGui.Combo("##DebugChannels", ref debugIndex, SidebarState.DebugChannels, SidebarState.DebugChannels.Length)) {
                _state.DebugChannelIndex = debugIndex;
                ApplyDebugChannel();
            }

            // Skinning 开关
            bool skinning = _state.EnableSkinning;
            if (ImGui.Checkbox("Skinning", ref skinning)) {
                _state.EnableSkinning = skinning;
                ApplySkinningSetting();
            }

            // Morphing 开关
            bool morphing = _state.EnableMorphing;
            if (ImGui.Checkbox("Morphing", ref morphing)) {
                _state.EnableMorphing = morphing;
                ApplyMorphingSetting();
            }

            ImGui.Separator();

            // KHR Material Extensions
            ImGui.Text("KHR Material Extensions");
            foreach (var kvp in _state.ExtensionEnabled) {
                string displayName = kvp.Key.Replace("KHR_materials_", "").Replace("_", " ");
                bool enabled = kvp.Value;
                if (ImGui.Checkbox(displayName, ref enabled)) {
                    _state.ExtensionEnabled[kvp.Key] = enabled;
                    ApplyExtensionSetting(kvp.Key, enabled);
                }
            }

            ImGui.Separator();

            // Statistics
            ImGui.Text("Statistics");
            ImGui.Text($"Model Count: {_state.ModelCount}");
            ImGui.Text($"Mesh Count: {_state.MeshCount}");
            ImGui.Text($"Triangle Count: {_state.TriangleCount}");
            ImGui.Text($"Opaque Material Count: {_state.OpaqueMaterialCount}");
            ImGui.Text($"Transparent Material Count: {_state.TransparentMaterialCount}");

            ImGui.EndTabItem();
        }

        static void ApplyDebugChannel() {
            if (_renderer != null) {
                _renderer.DebugChannel = SidebarState.DebugChannelMapping[_state.DebugChannelIndex];
            }
        }

        static void ApplySkinningSetting() {
            if (_renderer != null) {
                _renderer.EnableSkinning = _state.EnableSkinning;
            }
        }

        static void ApplyMorphingSetting() {
            if (_renderer != null) {
                _renderer.EnableMorphing = _state.EnableMorphing;
            }
        }

        static void ApplyExtensionSetting(string extensionName, bool enabled) {
            if (enabled) {
                ExtensionManager.EnableExtension(extensionName);
            }
            else {
                ExtensionManager.DisableExtension(extensionName);
            }
            // 重新加载当前模型以使扩展设置生效
            ReloadCurrentModel();
        }

        static void ReloadCurrentModel() {
            if (_scene?.SelectedModel == null) {
                return;
            }
            string modelPath = _scene.SelectedModel.FilePath;
            try {
                // 移除当前模型
                string modelName = _scene.SelectedModel.Name;
                _scene.RemoveModel(_scene.SelectedModel);

                // 重新加载模型
                SceneModel sceneModel = _scene.AddModel(modelPath);
                SelectionManager.Select(sceneModel);
                _renderer.UpdateLightsFromScene();

                // 更新场景列表
                _state.UpdateAvailableScenes(sceneModel.Model.SceneNames);
                UpdateAnimationList(sceneModel.Model);
                UpdateStatistics();
            }
            catch (Exception ex) {
                LogManager.Logger.ZLogError($"Failed to reload model: {ex.Message}");
            }
        }

        static void UpdateStatistics() {
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

        #endregion

        /// <summary>
        /// 更新状态（每帧调用）
        /// </summary>
        public static void Update() {
            if (_state == null) {
                return;
            }

            // 更新动画暂停状态
            // 注意：需要 Model 类支持暂停动画

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
}
