using Microsoft.Extensions.Logging;
using ZLogger;

namespace DotnetGltfRenderer {
    /// <summary>
    /// 全局日志管理器，提供统一的日志记录功能。
    /// </summary>
    public static class LogManager {
        static ILoggerFactory _loggerFactory;

        /// <summary>
        /// 获取全局日志记录器。
        /// </summary>
        public static ILogger Logger { get; private set; }

        /// <summary>
        /// 初始化日志系统，配置控制台输出。
        /// </summary>
        public static void Initialize() {
            _loggerFactory = LoggerFactory.Create(logging => {
                    logging.SetMinimumLevel(LogLevel.Debug);
                    logging.AddZLoggerConsole(options => {
                            options.UsePlainTextFormatter(formatter => {
                                    formatter.SetPrefixFormatter(
                                        $"{0:HH:mm:ss.fff} [{1:short}] ",
                                        (in template, in info) => template.Format(info.Timestamp, info.LogLevel)
                                    );
                                }
                            );
                        }
                    );
                }
            );
            Logger = _loggerFactory.CreateLogger("DotnetGltfRenderer");
        }

        /// <summary>
        /// 获取指定类型的日志记录器。
        /// </summary>
        /// <typeparam name="T">使用日志记录器的类型。</typeparam>
        /// <returns>类型化的日志记录器。</returns>
        public static ILogger<T> GetLogger<T>() where T : class => _loggerFactory.CreateLogger<T>();

        /// <summary>
        /// 获取指定类别名称的日志记录器。
        /// </summary>
        /// <param name="categoryName">类别名称。</param>
        /// <returns>日志记录器。</returns>
        public static ILogger GetLogger(string categoryName) => _loggerFactory.CreateLogger(categoryName);

        /// <summary>
        /// 关闭日志系统并释放资源。
        /// </summary>
        public static void Shutdown() {
            _loggerFactory?.Dispose();
        }
    }
}