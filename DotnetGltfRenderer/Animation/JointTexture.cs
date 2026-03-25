using System;
using System.Numerics;
using Silk.NET.OpenGLES;

namespace DotnetGltfRenderer {
    /// <summary>
    /// 骨骼动画纹理，用于存储关节矩阵
    /// 参考 glTF-Sample-Renderer 的 skin.js 实现
    /// </summary>
    public class JointTexture : IDisposable {
        bool _disposed;

        // 预分配的纹理数据数组，避免每帧 GC
        readonly float[] _textureData;

        // 预计算的法线矩阵缓存
        readonly Matrix4x4[] _normalMatrices;

        /// <summary>
        /// 纹理句柄
        /// </summary>
        public uint TextureHandle { get; private set; }

        /// <summary>
        /// 纹理尺寸（宽高相等）
        /// </summary>
        public int TextureSize { get; }

        /// <summary>
        /// 关节数量
        /// </summary>
        public int JointCount { get; }

        /// <summary>
        /// 创建骨骼纹理
        /// </summary>
        /// <param name="maxJoints">最大关节数</param>
        public JointTexture(int maxJoints) {
            JointCount = maxJoints;

            // 每个关节需要 2 个 mat4（jointMatrix + normalMatrix）
            // 每个 mat4 需要 4 个像素（每个像素 RGBA = vec4）
            // 所以每个关节需要 8 个像素
            // 纹理大小是 ceil(sqrt(joints * 8)) 的平方
            TextureSize = (int)Math.Ceiling(Math.Sqrt(maxJoints * 8));

            // 预分配数组
            _textureData = new float[TextureSize * TextureSize * 4];
            _normalMatrices = new Matrix4x4[maxJoints];
            CreateTexture();
        }

        unsafe void CreateTexture() {
            TextureHandle = GlContext.GL.GenTexture();
            GlContext.GL.BindTexture(TextureTarget.Texture2D, TextureHandle);

            // 设置纹理参数（与官方一致）
            GlContext.GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GlContext.GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            GlContext.GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            GlContext.GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);

            // 分配纹理存储（RGBA32F）
            // 纹理大小为 textureSize x textureSize
            GlContext.GL.TexImage2D(
                TextureTarget.Texture2D,
                0,
                InternalFormat.Rgba32f,
                (uint)TextureSize,
                (uint)TextureSize,
                0,
                PixelFormat.Rgba,
                PixelType.Float,
                null
            );
            GlContext.GL.BindTexture(TextureTarget.Texture2D, 0);
        }

        /// <summary>
        /// 更新骨骼纹理数据
        /// </summary>
        /// <param name="jointMatrices">关节矩阵数组（已与逆绑定矩阵相乘）</param>
        public unsafe void Update(Matrix4x4[] jointMatrices) {
            if (jointMatrices == null
                || jointMatrices.Length == 0) {
                return;
            }
            GlContext.GL.BindTexture(TextureTarget.Texture2D, TextureHandle);
            int count = Math.Min(jointMatrices.Length, JointCount);
            for (int i = 0; i < count; i++) {
                Matrix4x4 jointMatrix = jointMatrices[i];

                // 计算法线矩阵（逆转置）
                Matrix4x4.Invert(jointMatrix, out _normalMatrices[i]);
                _normalMatrices[i] = Matrix4x4.Transpose(_normalMatrices[i]);

                // 写入 jointMatrix（offset = i * 32）
                int offset = i * 32;
                WriteMatrixToTextureData(_textureData, offset, jointMatrix);

                // 写入 normalMatrix（offset = i * 32 + 16）
                WriteMatrixToTextureData(_textureData, offset + 16, _normalMatrices[i]);
            }

            // 上传纹理数据
            fixed (float* ptr = _textureData) {
                GlContext.GL.TexSubImage2D(
                    TextureTarget.Texture2D,
                    0,
                    0,
                    0,
                    (uint)TextureSize,
                    (uint)TextureSize,
                    PixelFormat.Rgba,
                    PixelType.Float,
                    ptr
                );
            }
            GlContext.GL.BindTexture(TextureTarget.Texture2D, 0);
        }

        /// <summary>
        /// 将矩阵写入纹理数据数组
        /// </summary>
        static void WriteMatrixToTextureData(float[] data, int offset, Matrix4x4 matrix) {
            // OpenGL 使用列主序存储矩阵
            // Matrix4x4 是行主序，需要转置
            data[offset + 0] = matrix.M11;
            data[offset + 1] = matrix.M12;
            data[offset + 2] = matrix.M13;
            data[offset + 3] = matrix.M14;
            data[offset + 4] = matrix.M21;
            data[offset + 5] = matrix.M22;
            data[offset + 6] = matrix.M23;
            data[offset + 7] = matrix.M24;
            data[offset + 8] = matrix.M31;
            data[offset + 9] = matrix.M32;
            data[offset + 10] = matrix.M33;
            data[offset + 11] = matrix.M34;
            data[offset + 12] = matrix.M41;
            data[offset + 13] = matrix.M42;
            data[offset + 14] = matrix.M43;
            data[offset + 15] = matrix.M44;
        }

        /// <summary>
        /// 绑定骨骼纹理到指定纹理单元
        /// </summary>
        public void Bind(TextureUnit unit) {
            GlContext.GL.ActiveTexture(unit);
            GlContext.GL.BindTexture(TextureTarget.Texture2D, TextureHandle);
        }

        public void Dispose() {
            if (_disposed) {
                return;
            }
            if (TextureHandle != 0) {
                GlContext.GL.DeleteTexture(TextureHandle);
                TextureHandle = 0;
            }
            _disposed = true;
        }
    }
}