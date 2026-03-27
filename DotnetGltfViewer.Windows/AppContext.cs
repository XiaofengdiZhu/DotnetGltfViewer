using System;
using DotnetGltfRenderer;
using Silk.NET.Maths;

namespace DotnetGltfViewer.Windows {
    /// <summary>
    /// 应用程序共享上下文，用于传递核心引用
    /// </summary>
    public class AppContext {
        /// <summary>
        /// 场景实例
        /// </summary>
        public Scene Scene { get; }

        /// <summary>
        /// 渲染器实例
        /// </summary>
        public Renderer Renderer { get; }

        /// <summary>
        /// 相机实例
        /// </summary>
        public Camera Camera { get; }

        /// <summary>
        /// 窗口大小
        /// </summary>
        public Vector2D<int> Size { get; set; }

        /// <summary>
        /// 显示器缩放比例
        /// </summary>
        public float MonitorScale { get; set; }

        /// <summary>
        /// 请求聚焦到选中物体
        /// </summary>
        public event Action FocusRequested;

        /// <summary>
        /// 请求关闭窗口
        /// </summary>
        public event Action CloseRequested;

        /// <summary>
        /// 初始化 AppContext
        /// </summary>
        public AppContext(Scene scene, Renderer renderer, Camera camera) {
            Scene = scene;
            Renderer = renderer;
            Camera = camera;
        }

        /// <summary>
        /// 触发聚焦请求
        /// </summary>
        public void RequestFocus() => FocusRequested?.Invoke();

        /// <summary>
        /// 触发关闭请求
        /// </summary>
        public void RequestClose() => CloseRequested?.Invoke();
    }
}