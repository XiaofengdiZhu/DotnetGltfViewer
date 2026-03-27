using DotnetGltfRenderer;
using ZLogger;

namespace DotnetGltfViewer.Windows {
    /// <summary>
    /// 应用程序入口点。
    /// </summary>
    static class Program {
        /// <summary>
        /// 应用程序主入口。
        /// </summary>
        /// <param name="args">命令行参数。</param>
        static void Main(string[] args) {
            LogManager.Initialize();
            LogManager.Logger.ZLogInformation($"DotnetGltfViewer 启动中...");
            MainWindow.Initialize();
            MainWindow.Run();
            LogManager.Logger.ZLogInformation($"DotnetGltfViewer 已退出");
            LogManager.Shutdown();
        }
    }
}