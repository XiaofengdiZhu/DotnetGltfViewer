using System.Numerics;
using DotnetGltfRenderer;
using Silk.NET.Maths;

namespace DotnetGltfViewer.Windows;

/// <summary>
/// 相机控制器，负责相机操作
/// </summary>
public static class CameraController {
    /// <summary>
    /// 重置相机以正视整个场景
    /// </summary>
    /// <param name="scene">场景实例</param>
    /// <param name="camera">相机实例</param>
    /// <param name="windowSize">窗口大小</param>
    public static void ResetCameraToScene(Scene scene, Camera camera, Vector2D<int> windowSize) {
        if (scene == null || camera == null) {
            return;
        }
        if (scene.TryGetSceneBounds(out Vector3 min, out Vector3 max)) {
            float aspect = (float)System.Math.Max(windowSize.X, 1) / System.Math.Max(windowSize.Y, 1);
            camera.LookAtBoundingBox(min, max, aspect);
        }
    }

    /// <summary>
    /// 聚焦到选中的物品，保持当前视角方向不变
    /// </summary>
    /// <param name="scene">场景实例</param>
    /// <param name="camera">相机实例</param>
    /// <param name="windowSize">窗口大小</param>
    /// <param name="selectedModel">选中的模型（可选，默认使用场景的选中模型）</param>
    public static void FocusOnSelection(Scene scene, Camera camera, Vector2D<int> windowSize, SceneModel selectedModel = null) {
        selectedModel ??= scene?.SelectedModel;
        if (selectedModel == null) {
            return;
        }
        BoundingBox bounds = selectedModel.WorldBounds;
        if (bounds.IsValid) {
            float aspect = (float)System.Math.Max(windowSize.X, 1) / System.Math.Max(windowSize.Y, 1);
            camera.FocusOnBoundingBox(bounds.Min, bounds.Max, aspect);
        }
    }

    /// <summary>
    /// 移动模型到画面中心，保持相机不动
    /// </summary>
    /// <param name="sceneModel">场景模型</param>
    /// <param name="camera">相机实例</param>
    public static void MoveModelToScreenCenter(SceneModel sceneModel, Camera camera) {
        if (sceneModel?.Model == null || camera == null) {
            return;
        }
        BoundingBox bounds = sceneModel.WorldBounds;
        if (!bounds.IsValid) {
            return;
        }
        // 模型中心
        Vector3 modelCenter = (bounds.Min + bounds.Max) * 0.5f;
        // 画面中心（相机焦点）
        Vector3 screenCenter = camera.FocalPoint;
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
}
