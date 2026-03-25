using System;
using Silk.NET.OpenGLES;

namespace DotnetGltfRenderer {
    /// <summary>
    /// 离屏帧缓冲区，用于 Transmission 渲染
    /// </summary>
    public class OffscreenFramebuffer : IDisposable {
        readonly GL _gl;

        // Framebuffer
        uint _framebuffer;

        // 纹理
        uint _depthRenderbuffer; // 深度附件

        // 尺寸

        /// <summary>
        /// 颜色纹理（用于 Transmission 采样）
        /// </summary>
        public uint ColorTexture { get; private set; }

        /// <summary>
        /// 帧缓冲区宽度
        /// </summary>
        public int Width { get; }

        /// <summary>
        /// 帧缓冲区高度
        /// </summary>
        public int Height { get; }

        /// <summary>
        /// 创建离屏帧缓冲区
        /// </summary>
        /// <param name="gl">GL 实例</param>
        /// <param name="width">宽度</param>
        /// <param name="height">高度</param>
        public OffscreenFramebuffer(GL gl, int width, int height) {
            _gl = gl;
            Width = width;
            Height = height;
            CreateFramebuffer();
        }

        unsafe void CreateFramebuffer() {
            // 创建颜色纹理
            ColorTexture = _gl.GenTexture();
            _gl.BindTexture(TextureTarget.Texture2D, ColorTexture);
            _gl.TexImage2D(
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

            // 设置纹理参数（支持 Mipmap）
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

            // 创建深度 Renderbuffer
            _depthRenderbuffer = _gl.GenRenderbuffer();
            _gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _depthRenderbuffer);
            _gl.RenderbufferStorage(RenderbufferTarget.Renderbuffer, InternalFormat.DepthComponent24, (uint)Width, (uint)Height);

            // 创建帧缓冲区
            _framebuffer = _gl.GenFramebuffer();
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);

            // 附加颜色纹理
            _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, ColorTexture, 0);

            // 附加深度 Renderbuffer
            _gl.FramebufferRenderbuffer(
                FramebufferTarget.Framebuffer,
                FramebufferAttachment.DepthAttachment,
                RenderbufferTarget.Renderbuffer,
                _depthRenderbuffer
            );

            // 检查帧缓冲区状态
            GLEnum status = _gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
            if (status != GLEnum.FramebufferComplete) {
                throw new InvalidOperationException($"Framebuffer incomplete: {status}");
            }

            // 解绑
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        }

        /// <summary>
        /// 绑定帧缓冲区进行渲染
        /// </summary>
        public void Bind() {
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
            _gl.Viewport(0, 0, (uint)Width, (uint)Height);
        }

        /// <summary>
        /// 解绑帧缓冲区（恢复默认帧缓冲区）
        /// </summary>
        /// <param name="defaultWidth">默认帧缓冲区宽度</param>
        /// <param name="defaultHeight">默认帧缓冲区高度</param>
        public void Unbind(int defaultWidth, int defaultHeight) {
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            _gl.Viewport(0, 0, (uint)defaultWidth, (uint)defaultHeight);
        }

        /// <summary>
        /// 生成颜色纹理的 Mipmap（用于粗糙度模糊）
        /// </summary>
        public void GenerateMipmap() {
            _gl.BindTexture(TextureTarget.Texture2D, ColorTexture);
            _gl.GenerateMipmap(TextureTarget.Texture2D);
        }

        /// <summary>
        /// 绑定颜色纹理到指定纹理单元
        /// </summary>
        public void BindColorTexture(TextureUnit unit) {
            _gl.ActiveTexture(unit);
            _gl.BindTexture(TextureTarget.Texture2D, ColorTexture);
        }

        /// <summary>
        /// 清除帧缓冲区
        /// </summary>
        /// <param name="r">红色分量</param>
        /// <param name="g">绿色分量</param>
        /// <param name="b">蓝色分量</param>
        /// <param name="a">透明度</param>
        public void Clear(float r = 0f, float g = 0f, float b = 0f, float a = 1f) {
            _gl.ClearColor(r, g, b, a);
            _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        }

        public void Dispose() {
            if (ColorTexture != 0) {
                _gl.DeleteTexture(ColorTexture);
                ColorTexture = 0;
            }
            if (_depthRenderbuffer != 0) {
                _gl.DeleteRenderbuffer(_depthRenderbuffer);
                _depthRenderbuffer = 0;
            }
            if (_framebuffer != 0) {
                _gl.DeleteFramebuffer(_framebuffer);
                _framebuffer = 0;
            }
        }
    }
}