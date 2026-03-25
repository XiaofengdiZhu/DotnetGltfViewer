using System.Numerics;

namespace DotnetGltfRenderer {
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
        /// 创建默认渲染上下文
        /// </summary>
        public static RenderContext Default => new() {
            UseIBL = true,
            ToneMapMode = ToneMapMode.KhrPbrNeutral
        };

        /// <summary>
        /// 创建用于 Scatter Pass 的上下文
        /// </summary>
        public RenderContext ForScatterPass() => new() {
            View = View,
            Projection = Projection,
            UseIBL = UseIBL,
            UseLinearOutput = true,
            IsScatterPass = true,
            ToneMapMode = ToneMapMode,
            LightCount = LightCount,
            FramebufferWidth = FramebufferWidth,
            FramebufferHeight = FramebufferHeight,
            HasTransmissionFramebuffer = HasTransmissionFramebuffer,
            HasScatterFramebuffer = HasScatterFramebuffer
        };

        /// <summary>
        /// 创建用于 Transmission Pass 的上下文
        /// </summary>
        public RenderContext ForTransmissionPass() => new() {
            View = View,
            Projection = Projection,
            UseIBL = UseIBL,
            UseLinearOutput = true,
            IsScatterPass = false,
            ToneMapMode = ToneMapMode,
            LightCount = LightCount,
            FramebufferWidth = FramebufferWidth,
            FramebufferHeight = FramebufferHeight,
            HasTransmissionFramebuffer = HasTransmissionFramebuffer,
            HasScatterFramebuffer = HasScatterFramebuffer
        };

        /// <summary>
        /// 创建用于 Main Pass 的上下文
        /// </summary>
        public RenderContext ForMainPass() => new() {
            View = View,
            Projection = Projection,
            UseIBL = UseIBL,
            UseLinearOutput = false,
            IsScatterPass = false,
            ToneMapMode = ToneMapMode,
            LightCount = LightCount,
            FramebufferWidth = FramebufferWidth,
            FramebufferHeight = FramebufferHeight,
            HasTransmissionFramebuffer = HasTransmissionFramebuffer,
            HasScatterFramebuffer = HasScatterFramebuffer
        };
    }
}
