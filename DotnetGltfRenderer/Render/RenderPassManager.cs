using System;
using System.Numerics;

namespace DotnetGltfRenderer {
    /// <summary>
    /// 渲染 Pass 管理器
    /// 管理多 Pass 渲染流程：Scatter Pass、Transmission Pass、Main Pass
    /// </summary>
    public class RenderPassManager {
        readonly FramebufferManager _framebufferManager;
        readonly DrawableRenderer _drawableRenderer;

        public RenderPassManager(FramebufferManager framebufferManager, DrawableRenderer drawableRenderer) {
            _framebufferManager = framebufferManager;
            _drawableRenderer = drawableRenderer;
        }

        /// <summary>
        /// 执行 Scatter Pass
        /// 渲染 VolumeScatter 物体到 Scatter 帧缓冲区
        /// </summary>
        public void ExecuteScatterPass(RenderQueue renderQueue, in RenderContext context) {
            if (!renderQueue.HasScatterDrawables) return;

            _framebufferManager.EnsureScatterFramebuffer();
            _framebufferManager.BindScatterFramebuffer();
            _framebufferManager.ClearScatterFramebuffer();

            _drawableRenderer.SetViewProjectionMatrices(context.View, context.Projection);

            RenderContext scatterContext = context.ForScatterPass();
            foreach (Drawable drawable in renderQueue.ScatterDrawables) {
                _drawableRenderer.Render(drawable, in scatterContext);
            }

            _framebufferManager.UnbindFramebuffer();
        }

        /// <summary>
        /// 执行 Transmission Pass
        /// 渲染场景到 Transmission 帧缓冲区（用于折射效果）
        /// </summary>
        public void ExecuteTransmissionPass(
            RenderQueue renderQueue,
            in RenderContext context,
            Action<Matrix4x4, Matrix4x4> renderSkyLinear = null) {
            if (!renderQueue.HasTransmissionDrawables) return;

            _framebufferManager.EnsureTransmissionFramebuffer();
            _framebufferManager.BindTransmissionFramebuffer();
            _framebufferManager.ClearTransmissionFramebuffer();

            _drawableRenderer.SetViewProjectionMatrices(context.View, context.Projection);

            // 渲染天空盒（线性输出）
            renderSkyLinear?.Invoke(context.View, context.Projection);

            // 渲染不透明和透明物体（线性输出）
            RenderContext transmissionContext = context.ForTransmissionPass();
            foreach (Drawable drawable in renderQueue.OpaqueDrawables) {
                _drawableRenderer.Render(drawable, in transmissionContext);
            }
            foreach (Drawable drawable in renderQueue.TransparentDrawables) {
                _drawableRenderer.Render(drawable, in transmissionContext);
            }

            _framebufferManager.GenerateTransmissionMipmap();
            _framebufferManager.UnbindFramebuffer();
        }

        /// <summary>
        /// 执行 Main Pass
        /// 渲染天空盒和所有物体
        /// </summary>
        public void ExecuteMainPass(
            RenderQueue renderQueue,
            in RenderContext context,
            Action<Matrix4x4, Matrix4x4> renderSky = null,
            Action onBindScatterTextures = null,
            Action onBindTransmissionTexture = null) {

            _drawableRenderer.SetViewProjectionMatrices(context.View, context.Projection);

            // 渲染天空盒
            renderSky?.Invoke(context.View, context.Projection);

            // 绑定 Scatter 纹理
            if (renderQueue.HasScatterDrawables) {
                onBindScatterTextures?.Invoke();
            }

            RenderContext mainContext = context.ForMainPass();

            // 渲染不透明物体
            foreach (Drawable drawable in renderQueue.OpaqueDrawables) {
                _drawableRenderer.Render(drawable, in mainContext);
            }

            // 渲染 Transmission 物体
            if (renderQueue.HasTransmissionDrawables) {
                onBindTransmissionTexture?.Invoke();
                foreach (Drawable drawable in renderQueue.TransmissionDrawables) {
                    _drawableRenderer.Render(drawable, in mainContext);
                }
            }

            // 渲染透明物体
            foreach (Drawable drawable in renderQueue.TransparentDrawables) {
                _drawableRenderer.Render(drawable, in mainContext);
            }
        }
    }
}
