using System;
using DotnetGltfRenderer;
using ZLogger;

namespace DotnetGltfViewer.Windows;

/// <summary>
/// 场景加载器，负责加载模型和环境贴图
/// </summary>
public static class SceneLoader {
    /// <summary>
    /// 加载模型到场景
    /// </summary>
    /// <param name="scene">场景实例</param>
    /// <param name="path">模型路径</param>
    /// <param name="renderer">渲染器实例（用于更新光照）</param>
    /// <returns>加载的场景模型，失败返回 null</returns>
    public static SceneModel LoadModel(Scene scene, string path, Renderer renderer) {
        if (scene == null || string.IsNullOrEmpty(path)) {
            return null;
        }

        try {
            SceneModel sceneModel = scene.AddModel(path);
            renderer?.UpdateLightsFromScene();
            LogManager.Logger.ZLogInformation($"Loaded model: {path}");
            return sceneModel;
        }
        catch (Exception ex) {
            LogManager.Logger.ZLogError($"Failed to load model: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 加载环境贴图
    /// </summary>
    /// <param name="renderer">渲染器实例</param>
    /// <param name="path">环境贴图路径</param>
    /// <returns>是否成功</returns>
    public static bool LoadEnvironmentMap(Renderer renderer, string path) {
        if (renderer == null || string.IsNullOrEmpty(path)) {
            return false;
        }

        try {
            renderer.SetEnvironmentMap(path);
            LogManager.Logger.ZLogInformation($"Loaded environment map: {path}");
            return true;
        }
        catch (Exception ex) {
            LogManager.Logger.ZLogError($"Failed to load environment map: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 从场景移除模型
    /// </summary>
    /// <param name="scene">场景实例</param>
    /// <param name="model">要移除的模型</param>
    /// <param name="renderer">渲染器实例（用于更新光照）</param>
    public static void RemoveModel(Scene scene, SceneModel model, Renderer renderer) {
        if (scene == null || model == null) {
            return;
        }
        scene.RemoveModel(model);
        renderer?.UpdateLightsFromScene();
    }

    /// <summary>
    /// 清除场景中的所有模型
    /// </summary>
    /// <param name="scene">场景实例</param>
    /// <param name="renderer">渲染器实例（用于更新光照）</param>
    public static void ClearScene(Scene scene, Renderer renderer) {
        if (scene == null) {
            return;
        }
        SelectionManager.ClearSelection();
        scene.Clear();
        renderer?.UpdateLightsFromScene();
    }
}
