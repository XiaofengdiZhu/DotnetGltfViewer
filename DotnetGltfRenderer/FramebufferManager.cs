using System;
using Silk.NET.OpenGLES;

namespace DotnetGltfRenderer {
    /// <summary>
    /// 帧缓冲区管理器
    /// </summary>
    public class FramebufferManager : IDisposable {
        readonly GL _gl;
        OffscreenFramebuffer _transmissionFramebuffer;
        ScatterFramebuffer _scatterFramebuffer;

        /// <summary>
        /// 当前帧缓冲区宽度
        /// </summary>
        public int Width { get; private set; }

        /// <summary>
        /// 当前帧缓冲区高度
        /// </summary>
        public int Height { get; private set; }

        /// <summary>
        /// Transmission 帧缓冲区是否可用
        /// </summary>
        public bool HasTransmissionFramebuffer => _transmissionFramebuffer != null;

        /// <summary>
        /// Scatter 帧缓冲区是否可用
        /// </summary>
        public bool HasScatterFramebuffer => _scatterFramebuffer != null;

        public FramebufferManager(GL gl) {
            _gl = gl;
        }

        /// <summary>
        /// 设置帧缓冲区尺寸
        /// </summary>
        public void SetSize(int width, int height) {
            Width = width;
            Height = height;
        }

        /// <summary>
        /// 确保 Transmission 帧缓冲区已创建（使用当前尺寸）
        /// </summary>
        public void EnsureTransmissionFramebuffer() {
            // 如果尺寸变化，需要重新创建
            if (_transmissionFramebuffer == null ||
                Width != _transmissionFramebuffer.Width ||
                Height != _transmissionFramebuffer.Height) {
                _transmissionFramebuffer?.Dispose();
                _transmissionFramebuffer = new OffscreenFramebuffer(_gl, Width, Height);
            }
        }

        /// <summary>
        /// 确保 Scatter 帧缓冲区已创建
        /// </summary>
        public void EnsureScatterFramebuffer() {
            if (_scatterFramebuffer == null ||
                Width != _scatterFramebuffer.Width ||
                Height != _scatterFramebuffer.Height) {
                _scatterFramebuffer?.Dispose();
                _scatterFramebuffer = new ScatterFramebuffer(_gl, Width, Height);
            }
        }

        /// <summary>
        /// 绑定 Transmission 帧缓冲区
        /// </summary>
        public void BindTransmissionFramebuffer() {
            _transmissionFramebuffer?.Bind();
        }

        /// <summary>
        /// 绑定 Scatter 帧缓冲区
        /// </summary>
        public void BindScatterFramebuffer() {
            _scatterFramebuffer?.Bind();
        }

        /// <summary>
        /// 清除 Transmission 帧缓冲区
        /// </summary>
        public void ClearTransmissionFramebuffer() {
            _transmissionFramebuffer?.Clear();
        }

        /// <summary>
        /// 清除 Scatter 帧缓冲区
        /// </summary>
        public void ClearScatterFramebuffer() {
            _scatterFramebuffer?.Clear();
        }

        /// <summary>
        /// 解绑帧缓冲区并恢复默认
        /// </summary>
        public void UnbindFramebuffer() {
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            _gl.Viewport(0, 0, (uint)Width, (uint)Height);
        }

        /// <summary>
        /// 生成 Transmission 帧缓冲区 Mipmap
        /// </summary>
        public void GenerateTransmissionMipmap() {
            _transmissionFramebuffer?.GenerateMipmap();
        }

        /// <summary>
        /// 绑定 Transmission 纹理
        /// </summary>
        public void BindTransmissionTexture() {
            if (_transmissionFramebuffer == null) {
                return;
            }

            _transmissionFramebuffer.BindColorTexture(
                (TextureUnit)((int)TextureUnit.Texture0 + (int)MaterialTextureSlot.TransmissionFramebuffer));
        }

        /// <summary>
        /// 绑定 Scatter 纹理
        /// </summary>
        public void BindScatterTextures() {
            if (_scatterFramebuffer == null) {
                return;
            }

            _scatterFramebuffer.BindColorTexture(
                (TextureUnit)((int)TextureUnit.Texture0 + (int)MaterialTextureSlot.ScatterFramebuffer));
            _scatterFramebuffer.BindDepthTexture(
                (TextureUnit)((int)TextureUnit.Texture0 + (int)MaterialTextureSlot.ScatterDepthFramebuffer));
        }

        public void Dispose() {
            _transmissionFramebuffer?.Dispose();
            _scatterFramebuffer?.Dispose();
        }
    }
}
