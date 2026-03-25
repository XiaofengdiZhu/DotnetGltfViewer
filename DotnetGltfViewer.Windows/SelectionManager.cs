using System;
using DotnetGltfRenderer;

namespace DotnetGltfViewer.Windows {
    /// <summary>
    /// 选择管理器，处理模型选择和高亮
    /// </summary>
    public static class SelectionManager {
        static SceneModel _selectedModel;
        static MeshInstance _selectedInstance;

        /// <summary>
        /// 当前选中的场景模型
        /// </summary>
        public static SceneModel SelectedModel => _selectedModel;

        /// <summary>
        /// 当前选中的 MeshInstance（可能为 null）
        /// </summary>
        public static MeshInstance SelectedInstance => _selectedInstance;

        /// <summary>
        /// 是否有选中对象
        /// </summary>
        public static bool HasSelection => _selectedModel != null;

        /// <summary>
        /// 选择变化事件
        /// </summary>
        public static event Action<SceneModel, MeshInstance> OnSelectionChanged;

        /// <summary>
        /// 选中模型
        /// </summary>
        /// <param name="model">场景模型</param>
        /// <param name="instance">MeshInstance（可选）</param>
        public static void Select(SceneModel model, MeshInstance instance = null) {
            if (_selectedModel == model && _selectedInstance == instance) {
                return;
            }

            _selectedModel = model;
            _selectedInstance = instance;

            // 同步更新场景的选择状态
            if (model != null) {
                Scene scene = MainWindow.GetScene();
                scene?.SelectModel(model);
                // 更新选中模型的包围盒，确保 Gizmo 位置正确
                model.UpdateWorldBounds();
            }

            OnSelectionChanged?.Invoke(model, instance);
        }

        /// <summary>
        /// 清除选择
        /// </summary>
        public static void ClearSelection() {
            if (_selectedModel == null) {
                return;
            }

            _selectedModel = null;
            _selectedInstance = null;

            // 同步清除场景的选择状态
            Scene scene = MainWindow.GetScene();
            scene?.ClearSelection();

            OnSelectionChanged?.Invoke(null, null);
        }

        /// <summary>
        /// 选择场景中唯一的模型（如果只有一个模型的话）
        /// </summary>
        /// <returns>是否成功选择</returns>
        public static bool SelectSingleModel() {
            Scene scene = MainWindow.GetScene();
            if (scene == null || scene.Models.Count != 1) {
                return false;
            }

            Select(scene.Models[0]);
            return true;
        }
    }
}
