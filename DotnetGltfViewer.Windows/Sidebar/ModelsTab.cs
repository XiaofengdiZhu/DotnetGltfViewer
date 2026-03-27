using System;
using DotnetGltfRenderer;
using Hexa.NET.ImGui;
using ZLogger;

namespace DotnetGltfViewer.Windows.Sidebar {
    /// <summary>
    /// Models Tab 渲染
    /// </summary>
    public static class ModelsTab {
        static Scene _scene;
        static Renderer _renderer;
        static SidebarState _state;

        public static void Initialize(Scene scene, Renderer renderer, SidebarState state) {
            _scene = scene;
            _renderer = renderer;
            _state = state;
        }

        public static void Render() {
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
                    }
                }
            }
            if (ImGui.Button("Replace")) {
                TryLoadSelectedModel();
            }
            ImGui.SameLine();
            if (ImGui.Button("Add")) {
                TryLoadSelectedModel(false);
            }
            ImGui.Separator();

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

            // Variants 下拉菜单
            if (_state.AvailableVariants.Count > 0) {
                ImGui.Text("Variant");
                ImGui.SetNextItemWidth(-1);
                int newVariantIndex = _state.SelectedVariantIndex;
                if (ImGui.Combo("##Variant", ref newVariantIndex, _state.AvailableVariants.ToArray(), _state.AvailableVariants.Count)) {
                    if (newVariantIndex != _state.SelectedVariantIndex) {
                        _state.SelectedVariantIndex = newVariantIndex;
                        ApplyVariantSelection();
                    }
                }
            }
            ImGui.EndTabItem();
        }

        static void TryLoadSelectedModel(bool replace = true) {
            string modelPath = _state.GetSelectedModelPath();
            if (string.IsNullOrEmpty(modelPath)) {
                return;
            }
            try {
                if (replace) {
                    if (_scene.ModelCount == 1) {
                        _scene.RemoveModel(_scene.Models[0]);
                    }
                    else {
                        _scene.RemoveModel(SelectionManager.SelectedModel);
                    }
                }

                // 加载模型
                SceneModel sceneModel = _scene.AddModel(modelPath);
                SelectionManager.Select(sceneModel);
                MainWindow.MoveModelToScreenCenter(sceneModel);
                _renderer.UpdateLightsFromScene();

                // 更新场景列表
                _state.UpdateAvailableScenes(sceneModel.Model.SceneNames);

                // 更新动画列表
                SidebarPanel.UpdateAnimationList(sceneModel.Model);

                // 更新变体列表
                _state.UpdateAvailableVariants(sceneModel.Model.Variants, sceneModel.Model.ActiveVariantIndex);

                // 更新统计信息
                SidebarPanel.UpdateStatistics();
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
            SidebarPanel.UpdateStatistics();
        }

        static void ApplyVariantSelection() {
            if (_scene.SelectedModel == null) {
                return;
            }
            int variantIndex = _state.SelectedVariantIndex - 1;
            _scene.SetModelVariant(_scene.SelectedModel, variantIndex);
        }
    }
}