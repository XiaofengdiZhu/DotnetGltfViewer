using System.Numerics;

namespace DotnetGltfRenderer {
    /// <summary>
    /// Debug 渲染通道（值必须匹配 functions.glsl 中的定义）
    /// </summary>
    public enum DebugChannel {
        None = 0,
        UV0 = 1,
        UV1 = 2,
        NormalMap = 3,
        Normal = 4, // DEBUG_NORMAL_SHADING
        GeometricNormal = 5,
        Tangent = 6,
        TangentW = 7,
        Bitangent = 8,
        BaseColorAlpha = 9,
        Occlusion = 10,
        Emissive = 11,
        Metallic = 12,
        Roughness = 13,
        BaseColor = 14,
        Clearcoat = 15,
        ClearcoatRoughness = 16,
        ClearcoatNormal = 17,
        Sheen = 18,
        SheenRoughness = 19,
        SpecularFactor = 20,
        SpecularColor = 21,
        Transmission = 22,
        VolumeThickness = 23
    }

    /// <summary>
    /// 渲染上下文参数
    /// 封装单次渲染调用所需的所有上下文信息
    /// </summary>
    public readonly struct RenderContext {
        /// <summary>
        /// 视图矩阵
        /// </summary>
        public Matrix4x4 View { get; init; }

        /// <summary>
        /// 投影矩阵
        /// </summary>
        public Matrix4x4 Projection { get; init; }

        /// <summary>
        /// 是否启用 IBL
        /// </summary>
        public bool UseIBL { get; init; }

        /// <summary>
        /// 是否使用线性输出（用于离屏渲染）
        /// </summary>
        public bool UseLinearOutput { get; init; }

        /// <summary>
        /// 是否为 Scatter Pass
        /// </summary>
        public bool IsScatterPass { get; init; }

        /// <summary>
        /// 色调映射模式
        /// </summary>
        public ToneMapMode ToneMapMode { get; init; }

        /// <summary>
        /// 光源数量
        /// </summary>
        public int LightCount { get; init; }

        /// <summary>
        /// 帧缓冲区宽度
        /// </summary>
        public int FramebufferWidth { get; init; }

        /// <summary>
        /// 帧缓冲区高度
        /// </summary>
        public int FramebufferHeight { get; init; }

        /// <summary>
        /// 是否有 Transmission 帧缓冲区
        /// </summary>
        public bool HasTransmissionFramebuffer { get; init; }

        /// <summary>
        /// 是否有 Scatter 帧缓冲区
        /// </summary>
        public bool HasScatterFramebuffer { get; init; }

        /// <summary>
        /// Debug 渲染通道
        /// </summary>
        public DebugChannel DebugChannel { get; init; }

        /// <summary>
        /// 是否启用蒙皮动画
        /// </summary>
        public bool EnableSkinning { get; init; }

        /// <summary>
        /// 是否启用 Morph Target 动画
        /// </summary>
        public bool EnableMorphing { get; init; }

        /// <summary>
        /// 创建默认渲染上下文
        /// </summary>
        public static RenderContext Default =>
            new() { UseIBL = true, ToneMapMode = ToneMapMode.KhrPbrNeutral, EnableSkinning = true, EnableMorphing = true };

        /// <summary>
        /// 创建用于 Scatter Pass 的上下文
        /// 使用 with 表达式，编译器可优化复制
        /// </summary>
        public RenderContext ForScatterPass() => this with { UseLinearOutput = true, IsScatterPass = true };

        /// <summary>
        /// 创建用于 Transmission Pass 的上下文
        /// 使用 with 表达式，编译器可优化复制
        /// </summary>
        public RenderContext ForTransmissionPass() => this with { UseLinearOutput = true, IsScatterPass = false };

        /// <summary>
        /// 创建用于 Main Pass 的上下文
        /// 使用 with 表达式，编译器可优化复制
        /// </summary>
        public RenderContext ForMainPass() => this with { UseLinearOutput = false, IsScatterPass = false };
    }
}