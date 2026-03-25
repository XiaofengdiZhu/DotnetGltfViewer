using System;
using System.Collections.Generic;
using System.Numerics;

namespace DotnetGltfRenderer {
    /// <summary>
    /// 渲染 Pass 管理器
    /// 管理多 Pass 渲染流程：Scatter Pass、Transmission Pass、Main Pass
    /// </summary>
    public class RenderPassManager {
        readonly FramebufferManager _framebufferManager;
        readonly MeshInstanceRenderer _meshInstanceRenderer;

        public RenderPassManager(FramebufferManager framebufferManager, MeshInstanceRenderer meshInstanceRenderer) {
            _framebufferManager = framebufferManager;
            _meshInstanceRenderer = meshInstanceRenderer;
        }

        /// <summary>
        /// 执行 Scatter Pass
        /// 渲染 VolumeScatter 物体到 Scatter 帧缓冲区
        /// </summary>
        public void ExecuteScatterPass(List<MeshInstance> scatterInstances, in RenderContext context) {
            if (scatterInstances.Count == 0) return;

            _framebufferManager.EnsureScatterFramebuffer();
            _framebufferManager.BindScatterFramebuffer();
            _framebufferManager.ClearScatterFramebuffer();

            _meshInstanceRenderer.SetViewProjectionMatrices(context.View, context.Projection);

            RenderContext scatterContext = context.ForScatterPass();
            foreach (MeshInstance instance in scatterInstances) {
                if (instance.IsVisible) {
                    _meshInstanceRenderer.Render(instance, in scatterContext);
                }
            }

            _framebufferManager.UnbindFramebuffer();
        }

        /// <summary>
        /// 执行 Transmission Pass
        /// 渲染场景到 Transmission 帧缓冲区（用于折射效果）
        /// </summary>
        public void ExecuteTransmissionPass(
            Scene scene,
            in RenderContext context,
            Action<Matrix4x4, Matrix4x4> renderSkyLinear = null) {
            if (scene.TransmissionInstances.Count == 0) return;

            _framebufferManager.EnsureTransmissionFramebuffer();
            _framebufferManager.BindTransmissionFramebuffer();
            _framebufferManager.ClearTransmissionFramebuffer();

            _meshInstanceRenderer.SetViewProjectionMatrices(context.View, context.Projection);

            // 渲染天空盒（线性输出）
            renderSkyLinear?.Invoke(context.View, context.Projection);

            // 渲染不透明和透明物体（线性输出）
            RenderContext transmissionContext = context.ForTransmissionPass();
            foreach (MeshInstance instance in scene.OpaqueInstances) {
                if (instance.IsVisible) {
                    _meshInstanceRenderer.Render(instance, in transmissionContext);
                }
            }
            foreach (MeshInstance instance in scene.TransparentInstances) {
                if (instance.IsVisible) {
                    _meshInstanceRenderer.Render(instance, in transmissionContext);
                }
            }

            _framebufferManager.GenerateTransmissionMipmap();
            _framebufferManager.UnbindFramebuffer();
        }

        /// <summary>
        /// 执行 Main Pass
        /// 渲染天空盒和所有物体
        /// </summary>
        public void ExecuteMainPass(
            Scene scene,
            in RenderContext context,
            Action<Matrix4x4, Matrix4x4> renderSky = null,
            Action onBindScatterTextures = null,
            Action onBindTransmissionTexture = null) {

            _meshInstanceRenderer.SetViewProjectionMatrices(context.View, context.Projection);

            // 渲染天空盒
            renderSky?.Invoke(context.View, context.Projection);

            // 绑定 Scatter 纹理
            if (scene.ScatterInstances.Count > 0) {
                onBindScatterTextures?.Invoke();
            }

            RenderContext mainContext = context.ForMainPass();

            // 渲染不透明物体
            foreach (MeshInstance instance in scene.OpaqueInstances) {
                if (instance.IsVisible) {
                    _meshInstanceRenderer.Render(instance, in mainContext);
                }
            }

            // 渲染 Transmission 物体
            if (scene.TransmissionInstances.Count > 0) {
                onBindTransmissionTexture?.Invoke();
                foreach (MeshInstance instance in scene.TransmissionInstances) {
                    if (instance.IsVisible) {
                        _meshInstanceRenderer.Render(instance, in mainContext);
                    }
                }
            }

            // 渲染透明物体
            foreach (MeshInstance instance in scene.TransparentInstances) {
                if (instance.IsVisible) {
                    _meshInstanceRenderer.Render(instance, in mainContext);
                }
            }
        }
    }
}
