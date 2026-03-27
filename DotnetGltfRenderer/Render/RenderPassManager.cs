using System;
using System.Collections.Generic;
using System.Numerics;

namespace DotnetGltfRenderer {
    public class RenderPassManager : IDisposable {
        readonly FramebufferManager _framebufferManager;
        readonly MeshInstanceRenderer _meshInstanceRenderer;
        readonly InstancingBatchManager _batchManager = new();
        readonly List<MeshInstance> _nonBatchedInstances = new(64);
        bool _disposed;

        public RenderPassManager(FramebufferManager framebufferManager, MeshInstanceRenderer meshInstanceRenderer) {
            _framebufferManager = framebufferManager;
            _meshInstanceRenderer = meshInstanceRenderer;
        }

        public void ExecuteScatterPass(List<MeshInstance> scatterInstances, in RenderContext context) {
            if (scatterInstances.Count == 0) {
                return;
            }
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

        public void ExecuteTransmissionPass(Scene scene, in RenderContext context, Action<Matrix4x4, Matrix4x4> renderSkyLinear = null) {
            if (scene.TransmissionInstances.Count == 0) {
                return;
            }
            _framebufferManager.EnsureTransmissionFramebuffer();
            _framebufferManager.BindTransmissionFramebuffer();
            _framebufferManager.ClearTransmissionFramebuffer();
            _meshInstanceRenderer.SetViewProjectionMatrices(context.View, context.Projection);

            renderSkyLinear?.Invoke(context.View, context.Projection);

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

        public void ExecuteMainPass(Scene scene,
            in RenderContext context,
            Action<Matrix4x4, Matrix4x4> renderSky = null,
            Action onBindScatterTextures = null,
            Action onBindTransmissionTexture = null) {
            _meshInstanceRenderer.SetViewProjectionMatrices(context.View, context.Projection);

            renderSky?.Invoke(context.View, context.Projection);

            if (scene.ScatterInstances.Count > 0) {
                onBindScatterTextures?.Invoke();
            }
            RenderContext mainContext = context.ForMainPass();

            BuildBatches(scene.OpaqueInstances);
            foreach (DynamicInstancingBatch batch in _batchManager.AllBatches) {
                _meshInstanceRenderer.RenderBatch(batch, in mainContext);
            }
            foreach (MeshInstance instance in _nonBatchedInstances) {
                if (instance.IsVisible) {
                    _meshInstanceRenderer.Render(instance, in mainContext);
                }
            }

            foreach (MeshInstance instance in scene.ScatterInstances) {
                if (instance.IsVisible) {
                    _meshInstanceRenderer.Render(instance, in mainContext);
                }
            }

            if (scene.TransmissionInstances.Count > 0) {
                onBindTransmissionTexture?.Invoke();
                foreach (MeshInstance instance in scene.TransmissionInstances) {
                    if (instance.IsVisible) {
                        _meshInstanceRenderer.Render(instance, in mainContext);
                    }
                }
            }

            foreach (MeshInstance instance in scene.TransparentInstances) {
                if (instance.IsVisible) {
                    _meshInstanceRenderer.Render(instance, in mainContext);
                }
            }
        }

        void BuildBatches(List<MeshInstance> instances) {
            _batchManager.Clear();
            _nonBatchedInstances.Clear();

            Dictionary<(Mesh, Material), DynamicInstancingBatch> batchMap = new();

            foreach (MeshInstance instance in instances) {
                if (!instance.IsVisible) {
                    continue;
                }

                if (!InstancingBatchManager.CanBatch(instance)) {
                    _nonBatchedInstances.Add(instance);
                    continue;
                }

                Mesh mesh = instance.Mesh;
                Material material = instance.CurrentMaterial;
                (Mesh, Material) key = (mesh, material);

                if (!batchMap.TryGetValue(key, out DynamicInstancingBatch batch)) {
                    batch = _batchManager.FindOrCreateBatch(mesh, material);
                    batchMap[key] = batch;
                }

                if (batch.CanAddInstance) {
                    batch.AddInstance(instance);
                }
                else {
                    batch = _batchManager.FindOrCreateBatch(mesh, material);
                    batchMap[key] = batch;
                    batch.AddInstance(instance);
                }
            }
        }

        public void Dispose() {
            if (_disposed) {
                return;
            }
            _batchManager?.Dispose();
            _disposed = true;
        }
    }
}