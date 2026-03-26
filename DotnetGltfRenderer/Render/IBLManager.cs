using System;
using Silk.NET.OpenGLES;

namespace DotnetGltfRenderer {
    /// <summary>
    /// IBL（Image-Based Lighting）管理器
    /// 统一管理环境贴图、IBL Sampler 和相关 uniform 设置
    /// </summary>
    public class IBLManager : IDisposable {
        /// <summary>
        /// IBL Sampler（预过滤环境贴图）
        /// </summary>
        public IblSampler IblSampler { get; private set; }

        /// <summary>
        /// 环境贴图纹理
        /// </summary>
        public Texture EnvironmentTexture { get; private set; }

        /// <summary>
        /// 环境光强度
        /// </summary>
        public float EnvironmentStrength { get; set; } = 1.0f;

        /// <summary>
        /// IBL 是否可用
        /// </summary>
        public bool IsEnabled => IblSampler != null;

        /// <summary>
        /// Mipmap 层级数
        /// </summary>
        public int MipCount => IblSampler?.MipCount ?? 0;

        /// <summary>
        /// 环境贴图路径
        /// </summary>
        public string EnvironmentMapPath { get; private set; }

        /// <summary>
        /// 加载 HDR 环境贴图
        /// </summary>
        public void Load(string hdrPath) {
            // 清理旧资源
            EnvironmentTexture?.Dispose();
            IblSampler?.Dispose();

            // 加载新环境贴图
            EnvironmentMap environmentMap = EnvironmentMap.LoadHDR(hdrPath);
            EnvironmentTexture = Texture.FromEnvironmentMap(environmentMap);
            IblSampler = new IblSampler();
            IblSampler.Process(environmentMap);
            EnvironmentMapPath = hdrPath;
            environmentMap.Dispose();
        }

        /// <summary>
        /// 绑定 IBL 纹理到当前着色器
        /// </summary>
        public void Bind(Shader shader) {
            if (!IsEnabled
                || shader == null) {
                return;
            }

            // 设置环境旋转矩阵（默认单位矩阵）
            shader.SetUniformMatrix3(
                "u_EnvRotation",
                new System.Numerics.Vector3(1f, 0f, 0f),
                new System.Numerics.Vector3(0f, 1f, 0f),
                new System.Numerics.Vector3(0f, 0f, 1f)
            );

            // 绑定 IBL 纹理
            MaterialTextureBinder.BindIBLTextures(IblSampler);
        }

        /// <summary>
        /// 绑定 GGX 环境贴图（用于天空盒渲染）
        /// </summary>
        public void BindGGXTexture() {
            if (!IsEnabled) {
                return;
            }
            GlContext.GL.ActiveTexture(TextureUnit.Texture0);
            GlContext.GL.BindTexture(TextureTarget.TextureCubeMap, IblSampler.GGXTexture);
        }

        public void Dispose() {
            EnvironmentTexture?.Dispose();
            IblSampler?.Dispose();
        }
    }
}