using System;
using System.Collections.Generic;
using DotnetGltfRenderer;
using Hexa.NET.ImGui;
using ZLogger;

namespace DotnetGltfViewer.Windows.Sidebar {
    /// <summary>
    /// Advanced Tab 渲染
    /// </summary>
    public static class AdvancedTab {
        static Scene _scene;
        static Renderer _renderer;
        static SidebarState _state;

        public static void Initialize(Scene scene, Renderer renderer, SidebarState state) {
            _scene = scene;
            _renderer = renderer;
            _state = state;
        }

        public static void Render() {
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
            foreach (KeyValuePair<string, bool> kvp in _state.ExtensionEnabled) {
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
                _scene.RemoveModel(_scene.SelectedModel);

                // 重新加载模型
                SceneModel sceneModel = _scene.AddModel(modelPath);
                SelectionManager.Select(sceneModel);
                _renderer.UpdateLightsFromScene();

                // 更新场景列表
                _state.UpdateAvailableScenes(sceneModel.Model.SceneNames);
                AnimationTab.UpdateAnimationList(sceneModel.Model);
                SidebarPanel.UpdateStatistics();
            }
            catch (Exception ex) {
                LogManager.Logger.ZLogError($"Failed to reload model: {ex.Message}");
            }
        }
    }
}