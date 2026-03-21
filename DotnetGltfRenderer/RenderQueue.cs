using System;
using System.Collections.Generic;
using System.Numerics;

namespace DotnetGltfRenderer {
    /// <summary>
    /// 可绘制对象，包含渲染所需的所有信息
    /// </summary>
    public struct Drawable : IComparable<Drawable> {
        public Mesh Mesh;
        public Matrix4x4 WorldMatrix;
        public Material Material;
        public float Depth; // 视图空间 Z 值（用于排序）
        public bool HasSkinning;
        public Matrix4x4[] JointMatrices;
        public JointTexture JointTexture;
        public bool IsNegativeScale;

        public int CompareTo(Drawable other) =>
            // 从远到近排序（Z 值大的在前）
            other.Depth.CompareTo(Depth);
    }

    /// <summary>
    /// 渲染队列管理器
    /// 将场景中的物体按渲染顺序分类
    /// </summary>
    public class RenderQueue {
        readonly List<Drawable> _opaqueDrawables = new();
        readonly List<Drawable> _transparentDrawables = new();
        readonly List<Drawable> _transmissionDrawables = new();
        readonly List<Drawable> _scatterDrawables = new();

        /// <summary>
        /// 不透明物体队列（alphaMode != "BLEND" 且无 transmission）
        /// </summary>
        public IReadOnlyList<Drawable> OpaqueDrawables => _opaqueDrawables;

        /// <summary>
        /// 透明物体队列（alphaMode === "BLEND"）
        /// </summary>
        public IReadOnlyList<Drawable> TransparentDrawables => _transparentDrawables;

        /// <summary>
        /// Transmission 物体队列（有 KHR_materials_transmission 扩展）
        /// </summary>
        public IReadOnlyList<Drawable> TransmissionDrawables => _transmissionDrawables;

        /// <summary>
        /// Scatter 物体队列（有 KHR_materials_volume 扩展）
        /// </summary>
        public IReadOnlyList<Drawable> ScatterDrawables => _scatterDrawables;

        /// <summary>
        /// 是否存在 Transmission 物体
        /// </summary>
        public bool HasTransmissionDrawables => _transmissionDrawables.Count > 0;

        /// <summary>
        /// 是否存在 Scatter 物体
        /// </summary>
        public bool HasScatterDrawables => _scatterDrawables.Count > 0;

        /// <summary>
        /// 清空所有队列
        /// </summary>
        public void Clear() {
            _opaqueDrawables.Clear();
            _transparentDrawables.Clear();
            _transmissionDrawables.Clear();
            _scatterDrawables.Clear();
        }

        /// <summary>
        /// 准备渲染队列（从 Model.MeshInstances 分类）
        /// </summary>
        /// <param name="instances">网格实例列表</param>
        /// <param name="variantIndex">当前激活的变体索引（-1 表示使用默认材质）</param>
        public void Prepare(IEnumerable<Model.MeshInstance> instances, int variantIndex = -1) {
            Clear();
            foreach (Model.MeshInstance instance in instances) {
                // 跳过不可见的 MeshInstance（KHR_node_visibility 动画控制）
                if (!instance.IsVisible) {
                    continue;
                }
                Mesh mesh = instance.Mesh;
                // 根据变体索引获取正确的材质
                Material material = mesh.GetMaterialForVariant(variantIndex);
                if (material == null) {
                    _opaqueDrawables.Add(CreateDrawable(instance, variantIndex));
                    continue;
                }

                // 检查材质类型，分配到不同队列
                bool isTransparent = material.AlphaMode == AlphaMode.Blend;
                // KHR_materials_transmission - sparse volumes（需要 transmission framebuffer）
                bool hasTransmission = material.Transmission?.IsEnabled == true;
                // KHR_materials_diffuse_transmission - dense volumes（不需要 framebuffer）
                bool hasDiffuseTransmission = material.DiffuseTransmission?.IsEnabled == true;
                bool hasVolume = material.Volume?.IsEnabled == true;
                bool hasVolumeScatter = material.VolumeScatter?.IsEnabled == true;
                Drawable drawable = CreateDrawable(instance, variantIndex);

                // 官方渲染器说明：
                // - dense volumes using KHR_materials_diffuse_transmission -> 作为不透明物体渲染
                // - sparse volumes using KHR_materials_transmission -> 需要 transmission framebuffer
                //
                // VolumeScatter 是 DiffuseTransmission 的增强
                // VolumeScatter 物体需要：
                // 1. 先渲染到 scatter framebuffer（使用 scatter.frag）
                // 2. 在主 pass 中从 scatter framebuffer 采样

                // 将 VolumeScatter 物体添加到 scatter 队列（用于 scatter pass）
                if (hasVolumeScatter && hasDiffuseTransmission) {
                    _scatterDrawables.Add(drawable);
                }
                if (hasTransmission) {
                    // Sparse volumes (KHR_materials_transmission) 需要 transmission framebuffer
                    _transmissionDrawables.Add(drawable);
                }
                else if (isTransparent) {
                    _transparentDrawables.Add(drawable);
                }
                else {
                    // Opaque objects (包括 DiffuseTransmission/VolumeScatter 的 dense volumes)
                    _opaqueDrawables.Add(drawable);
                }
            }
        }

        /// <summary>
        /// 按视图空间深度排序透明物体和 Transmission 物体（从远到近）
        /// </summary>
        public void SortByDepth(Matrix4x4 viewMatrix) {
            SortDrawablesByDepth(_transparentDrawables, viewMatrix);
            SortDrawablesByDepth(_transmissionDrawables, viewMatrix);
            SortDrawablesByDepth(_scatterDrawables, viewMatrix);
        }

        void SortDrawablesByDepth(List<Drawable> drawables, Matrix4x4 viewMatrix) {
            if (drawables.Count <= 1) {
                return;
            }

            // 计算每个 Drawable 的深度
            for (int i = 0; i < drawables.Count; i++) {
                Drawable d = drawables[i];
                Vector3 centroid = d.Mesh.Centroid;

                // 将质心变换到视图空间
                Matrix4x4 modelView = Matrix4x4.Multiply(d.WorldMatrix, viewMatrix);
                Vector3 viewPos = Vector3.Transform(centroid, modelView);
                d.Depth = viewPos.Z;
                drawables[i] = d;
            }

            // 从远到近排序（Z 值大的在前）
            drawables.Sort();
        }

        static Drawable CreateDrawable(Model.MeshInstance instance, int variantIndex = -1) => new() {
            Mesh = instance.Mesh,
            WorldMatrix = instance.WorldMatrix,
            Material = instance.Mesh.GetMaterialForVariant(variantIndex),
            HasSkinning = instance.HasSkinning,
            JointMatrices = instance.JointMatrices,
            JointTexture = instance.JointTexture,
            IsNegativeScale = instance.IsNegativeScale,
            Depth = 0f
        };
    }
}