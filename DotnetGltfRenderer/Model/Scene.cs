using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Silk.NET.OpenGLES;

namespace DotnetGltfRenderer {
    /// <summary>
    /// 场景中的模型项，包含模型及其元数据
    /// </summary>
    public class SceneModel : IDisposable {
        /// <summary>
        /// 模型实例
        /// </summary>
        public Model Model { get; }

        /// <summary>
        /// 模型文件路径
        /// </summary>
        public string FilePath { get; }

        /// <summary>
        /// 模型名称（用于显示）
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 是否可见
        /// </summary>
        public bool IsVisible { get; set; } = true;

        /// <summary>
        /// 是否被选中
        /// </summary>
        public bool IsSelected { get; internal set; }

        /// <summary>
        /// 唯一标识符
        /// </summary>
        public int Id { get; internal set; }

        /// <summary>
        /// 世界空间边界（考虑变换）
        /// </summary>
        public BoundingBox WorldBounds { get; internal set; }

        internal SceneModel(GL gl, string filePath, int id) {
            FilePath = filePath;
            Id = id;
            Name = Path.GetFileNameWithoutExtension(filePath);
            Model = new Model(gl, filePath);
            UpdateWorldBounds();
        }

        /// <summary>
        /// 更新世界空间边界
        /// </summary>
        public void UpdateWorldBounds() {
            if (Model == null || Model.MeshInstances.Count == 0) {
                WorldBounds = BoundingBox.Empty;
                return;
            }

            Vector3 min = new(float.MaxValue);
            Vector3 max = new(float.MinValue);

            foreach (MeshInstance instance in Model.MeshInstances) {
                if (!instance.IsVisible) continue;

                // 获取网格的局部包围盒
                BoundingBox localBounds = instance.Mesh.LocalBounds;
                if (!localBounds.IsValid) continue;

                // 变换到世界空间
                BoundingBox worldBounds = BoundingBox.Transform(localBounds, instance.WorldMatrix);
                min = Vector3.Min(min, worldBounds.Min);
                max = Vector3.Max(max, worldBounds.Max);
            }

            WorldBounds = min.X != float.MaxValue ? new BoundingBox(min, max) : BoundingBox.Empty;
        }

        public void Dispose() {
            Model?.Dispose();
        }
    }

    /// <summary>
    /// 场景类，管理多个模型的加载、渲染和交互
    /// </summary>
    public class Scene : IDisposable {
        readonly GL _gl;
        readonly List<SceneModel> _models = new();
        int _nextModelId = 1;

        /// <summary>
        /// 场景中的所有模型
        /// </summary>
        public IReadOnlyList<SceneModel> Models => _models;

        /// <summary>
        /// 当前选中的模型
        /// </summary>
        public SceneModel SelectedModel { get; private set; }

        /// <summary>
        /// 模型添加事件
        /// </summary>
        public event Action<SceneModel> OnModelAdded;

        /// <summary>
        /// 模型移除事件
        /// </summary>
        public event Action<SceneModel> OnModelRemoved;

        /// <summary>
        /// 选择变化事件
        /// </summary>
        public event Action<SceneModel> OnSelectionChanged;

        /// <summary>
        /// 创建场景
        /// </summary>
        /// <param name="gl">OpenGL ES 接口</param>
        public Scene(GL gl) {
            _gl = gl;
        }

        /// <summary>
        /// 添加模型到场景
        /// </summary>
        /// <param name="filePath">模型文件路径</param>
        /// <returns>添加的模型项</returns>
        public SceneModel AddModel(string filePath) {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) {
                throw new FileNotFoundException($"Model file not found: {filePath}");
            }

            SceneModel sceneModel = new(_gl, filePath, _nextModelId++);
            _models.Add(sceneModel);
            OnModelAdded?.Invoke(sceneModel);
            return sceneModel;
        }

        /// <summary>
        /// 从场景移除模型
        /// </summary>
        /// <param name="model">要移除的模型</param>
        public void RemoveModel(SceneModel model) {
            if (model == null) return;

            if (SelectedModel == model) {
                ClearSelection();
            }

            _models.Remove(model);
            OnModelRemoved?.Invoke(model);
            model.Dispose();
        }

        /// <summary>
        /// 选中模型
        /// </summary>
        /// <param name="model">要选中的模型</param>
        public void SelectModel(SceneModel model) {
            if (SelectedModel == model) return;

            // 清除之前的选中状态
            if (SelectedModel != null) {
                SelectedModel.IsSelected = false;
            }

            // 设置新的选中状态
            SelectedModel = model;
            if (model != null) {
                model.IsSelected = true;
            }

            OnSelectionChanged?.Invoke(model);
        }

        /// <summary>
        /// 清除选择
        /// </summary>
        public void ClearSelection() {
            if (SelectedModel != null) {
                SelectedModel.IsSelected = false;
                SelectedModel = null;
                OnSelectionChanged?.Invoke(null);
            }
        }

        /// <summary>
        /// 清除场景中的所有模型
        /// </summary>
        public void Clear() {
            ClearSelection();
            foreach (SceneModel model in _models.ToList()) {
                RemoveModel(model);
            }
        }

        /// <summary>
        /// 获取所有可见模型的 MeshInstance
        /// </summary>
        public IEnumerable<MeshInstance> GetAllMeshInstances() {
            foreach (SceneModel model in _models) {
                if (!model.IsVisible) continue;
                foreach (MeshInstance instance in model.Model.MeshInstances) {
                    if (instance.IsVisible) {
                        yield return instance;
                    }
                }
            }
        }

        /// <summary>
        /// 获取场景边界
        /// </summary>
        public BoundingBox GetSceneBounds() {
            if (_models.Count == 0) {
                return BoundingBox.Empty;
            }

            Vector3 min = new(float.MaxValue);
            Vector3 max = new(float.MinValue);
            bool hasAny = false;

            foreach (SceneModel model in _models) {
                if (!model.IsVisible) continue;

                model.UpdateWorldBounds();
                BoundingBox bounds = model.WorldBounds;
                if (bounds.IsValid) {
                    min = Vector3.Min(min, bounds.Min);
                    max = Vector3.Max(max, bounds.Max);
                    hasAny = true;
                }
            }

            return hasAny ? new BoundingBox(min, max) : BoundingBox.Empty;
        }

        /// <summary>
        /// 更新所有模型的动画
        /// </summary>
        public void Update(float deltaTimeSeconds) {
            foreach (SceneModel model in _models) {
                model.Model?.Update(deltaTimeSeconds);
                model.UpdateWorldBounds();
            }
        }

        /// <summary>
        /// 尝试获取场景边界（用于相机适配）
        /// </summary>
        public bool TryGetSceneBounds(out Vector3 min, out Vector3 max) {
            BoundingBox bounds = GetSceneBounds();
            if (bounds.IsValid) {
                min = bounds.Min;
                max = bounds.Max;
                return true;
            }

            min = Vector3.Zero;
            max = Vector3.One;
            return false;
        }

        /// <summary>
        /// 获取场景中的模型数量
        /// </summary>
        public int ModelCount => _models.Count;

        /// <summary>
        /// 检查场景是否为空
        /// </summary>
        public bool IsEmpty => _models.Count == 0;

        public void Dispose() {
            Clear();
        }
    }
}
