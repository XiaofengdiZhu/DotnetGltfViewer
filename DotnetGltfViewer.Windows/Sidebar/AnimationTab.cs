using System.Collections.Generic;
using System.Numerics;
using DotnetGltfRenderer;
using Hexa.NET.ImGui;

namespace DotnetGltfViewer.Windows.Sidebar {
    /// <summary>
    /// Animation Tab 渲染
    /// </summary>
    public static class AnimationTab {
        static Scene _scene;
        static SidebarState _state;

        public static void Initialize(Scene scene, SidebarState state) {
            _scene = scene;
            _state = state;
        }

        public static void Render() {
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

        public static void UpdateAnimationList(Model model) {
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
    }
}