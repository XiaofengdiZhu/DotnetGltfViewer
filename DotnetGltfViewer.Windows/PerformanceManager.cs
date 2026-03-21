using System.Diagnostics;

namespace DotnetGltfViewer.Windows {
    /// <summary>
    /// 性能管理器类，负责计算 FPS、每帧时间。
    /// </summary>
    public static class PerformanceManager {
        static Stopwatch _stopwatch;
        static double _frameCount;
        static double _fpsAccumulator;

        /// <summary>
        /// 获取当前帧率（FPS）。
        /// </summary>
        public static float FPS { get; private set; }

        /// <summary>
        /// 获取从启动到现在的总时间（秒）。
        /// </summary>
        public static double TotalTime { get; private set; }

        /// <summary>
        /// 获取上一帧的间隔时间（秒）。
        /// </summary>
        public static double DeltaTime { get; private set; }

        /// <summary>
        /// 初始化性能管理器实例。
        /// </summary>
        public static void Initialize() {
            _stopwatch = Stopwatch.StartNew();
        }

        /// <summary>
        /// 更新帧状态。
        /// 计算帧时间、FPS 等统计信息。
        /// </summary>
        /// <param name="deltaTime">帧间隔时间（秒）。</param>
        public static void Update(double deltaTime) {
            DeltaTime = deltaTime;
            TotalTime = _stopwatch.Elapsed.TotalSeconds;
            _frameCount++;
            _fpsAccumulator += deltaTime;
            if (_fpsAccumulator >= 1.0) {
                FPS = (float)(_frameCount / _fpsAccumulator);
                _frameCount = 0;
                _fpsAccumulator = 0;
            }
        }

        /// <summary>
        /// 释放性能管理器资源。
        /// </summary>
        public static void Dispose() {
            _stopwatch?.Stop();
        }
    }
}