using System;
using Silk.NET.OpenGLES;

namespace DotnetGltfRenderer {
    /// <summary>
    /// Scatter 帧缓冲区，用于 VolumeScatter 次表面散射
    /// 与 Transmission Framebuffer 不同，Scatter Framebuffer 需要深度纹理供着色器采样
    /// </summary>
    public class ScatterFramebuffer : IDisposable {
        // Framebuffer
        uint _framebuffer;

        // 纹理

        // 尺寸

        /// <summary>
        /// 颜色纹理（散射光照）
        /// </summary>
        public uint ColorTexture { get; private set; }

        /// <summary>
        /// 深度纹理
        /// </summary>
        public uint DepthTexture { get; private set; }

        /// <summary>
        /// 帧缓冲区宽度
        /// </summary>
        public int Width { get; }

        /// <summary>
        /// 帧缓冲区高度
        /// </summary>
        public int Height { get; }

        /// <summary>
        /// 创建 Scatter 帧缓冲区
        /// </summary>
        /// <param name="width">宽度</param>
        /// <param name="height">高度</param>
        public ScatterFramebuffer(int width, int height) {
            Width = width;
            Height = height;
            CreateFramebuffer();
        }

        unsafe void CreateFramebuffer() {
            // 创建颜色纹理
            ColorTexture = GlContext.GL.GenTexture();
            GlContext.GL.BindTexture(TextureTarget.Texture2D, ColorTexture);
            GlContext.GL.TexImage2D(
                TextureTarget.Texture2D,
                0,
                InternalFormat.Rgba16f,
                (uint)Width,
                (uint)Height,
                0,
                PixelFormat.Rgba,
                PixelType.HalfFloat,
                null
            );

            // 设置纹理参数（不支持 Mipmap，使用线性过滤）
            GlContext.GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GlContext.GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GlContext.GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GlContext.GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

            // 创建深度纹理（不是 Renderbuffer，因为着色器需要采样）
            DepthTexture = GlContext.GL.GenTexture();
            GlContext.GL.BindTexture(TextureTarget.Texture2D, DepthTexture);
            GlContext.GL.TexImage2D(
                TextureTarget.Texture2D,
                0,
                InternalFormat.DepthComponent24,
                (uint)Width,
                (uint)Height,
                0,
                PixelFormat.DepthComponent,
                PixelType.UnsignedInt,
                null
            );

            // 设置深度纹理参数
            GlContext.GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GlContext.GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GlContext.GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GlContext.GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

            // 创建帧缓冲区
            _framebuffer = GlContext.GL.GenFramebuffer();
            GlContext.GL.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);

            // 附加颜色纹理
            GlContext.GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, ColorTexture, 0);

            // 附加深度纹理
            GlContext.GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, TextureTarget.Texture2D, DepthTexture, 0);

            // 检查帧缓冲区状态
            FramebufferStatus status = (FramebufferStatus)GlContext.GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
            if (status != FramebufferStatus.Complete) {
                throw new InvalidOperationException($"Scatter Framebuffer incomplete: {status}");
            }

            // 解绑
            GlContext.GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        }

        /// <summary>
        /// 绑定帧缓冲区进行渲染
        /// </summary>
        public void Bind() {
            GlContext.GL.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
            GlContext.GL.Viewport(0, 0, (uint)Width, (uint)Height);
        }

        /// <summary>
        /// 解绑帧缓冲区（恢复默认帧缓冲区）
        /// </summary>
        /// <param name="defaultWidth">默认帧缓冲区宽度</param>
        /// <param name="defaultHeight">默认帧缓冲区高度</param>
        public void Unbind(int defaultWidth, int defaultHeight) {
            GlContext.GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            GlContext.GL.Viewport(0, 0, (uint)defaultWidth, (uint)defaultHeight);
        }

        /// <summary>
        /// 绑定颜色纹理到指定纹理单元
        /// </summary>
        public void BindColorTexture(TextureUnit unit) {
            GlContext.GL.ActiveTexture(unit);
            GlContext.GL.BindTexture(TextureTarget.Texture2D, ColorTexture);
        }

        /// <summary>
        /// 绑定深度纹理到指定纹理单元
        /// </summary>
        public void BindDepthTexture(TextureUnit unit) {
            GlContext.GL.ActiveTexture(unit);
            GlContext.GL.BindTexture(TextureTarget.Texture2D, DepthTexture);
        }

        /// <summary>
        /// 清除帧缓冲区
        /// </summary>
        public void Clear() {
            // 清除为透明黑色（散射贡献为 0）
            GlContext.GL.ClearColor(0f, 0f, 0f, 0f);
            GlContext.GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        }

        public void Dispose() {
            if (ColorTexture != 0) {
                GlContext.GL.DeleteTexture(ColorTexture);
                ColorTexture = 0;
            }
            if (DepthTexture != 0) {
                GlContext.GL.DeleteTexture(DepthTexture);
                DepthTexture = 0;
            }
            if (_framebuffer != 0) {
                GlContext.GL.DeleteFramebuffer(_framebuffer);
                _framebuffer = 0;
            }
        }
    }
}