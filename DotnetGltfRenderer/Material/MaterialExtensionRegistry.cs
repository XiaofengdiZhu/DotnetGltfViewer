using System;
using System.Collections.Generic;

namespace DotnetGltfRenderer {
    /// <summary>
    /// 材质扩展注册表，支持自动发现和加载扩展
    /// </summary>
    public static class MaterialExtensionRegistry {
        static readonly Dictionary<string, Type> _extensions = new();
        static readonly Dictionary<string, Func<MaterialExtension>> _factories = new();

        /// <summary>
        /// 已注册的扩展名称列表
        /// </summary>
        public static IReadOnlyCollection<string> RegisteredExtensions => _extensions.Keys;

        /// <summary>
        /// 注册扩展类型
        /// </summary>
        public static void Register<T>() where T : MaterialExtension, new() {
            // 创建临时实例获取扩展名
            T instance = new();
            string name = instance.ExtensionName;
            _extensions[name] = typeof(T);
            _factories[name] = () => new T();
        }

        /// <summary>
        /// 使用工厂函数注册扩展
        /// </summary>
        public static void Register(string name, Func<MaterialExtension> factory) {
            _extensions[name] = factory().GetType();
            _factories[name] = factory;
        }

        /// <summary>
        /// 创建扩展实例
        /// </summary>
        public static MaterialExtension Create(string extensionName) {
            if (_factories.TryGetValue(extensionName, out Func<MaterialExtension> factory)) {
                return factory();
            }
            return null;
        }

        /// <summary>
        /// 检查扩展是否已注册
        /// </summary>
        public static bool IsRegistered(string extensionName) => _extensions.ContainsKey(extensionName);

        /// <summary>
        /// 获取扩展类型
        /// </summary>
        public static Type GetExtensionType(string extensionName) => _extensions.TryGetValue(extensionName, out Type type) ? type : null;

        /// <summary>
        /// 清除所有已注册的扩展
        /// </summary>
        public static void Clear() {
            _extensions.Clear();
            _factories.Clear();
        }

        /// <summary>
        /// 初始化材质扩展注册表
        /// </summary>
        public static void Initialize() {
            // Phase 1/2 扩展
            Register<ClearCoatExtension>();
            Register<IridescenceExtension>();
            // Phase 3 扩展
            Register<TransmissionExtension>();
            Register<VolumeExtension>();
            Register<SheenExtension>();
            Register<SpecularExtension>();
            Register<IorExtension>();
            Register<EmissiveStrengthExtension>();
            Register<DispersionExtension>();
            Register<AnisotropyExtension>();
            Register<DiffuseTransmissionExtension>();
            Register<VolumeScatterExtension>();
            Register<UnlitExtension>();
            Register<SpecularGlossinessExtension>();
        }
    }
}