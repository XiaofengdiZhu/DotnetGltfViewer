// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.OpenGLES;

namespace DotnetGltfRenderer {
    public enum MaterialTextureSlot {
        BaseColor = 0,
        MetallicRoughness = 1,
        Normal = 2,
        Occlusion = 3,
        Emissive = 4,
        Environment = 5,
        ClearCoat = 6,
        ClearCoatRoughness = 7,
        ClearCoatNormal = 8,
        Iridescence = 9,
        IridescenceThickness = 10,

        // IBL Texture Slots
        IBLLambertian = 11,
        IBLGGX = 12,
        IBLCharlie = 13,
        IBLGGXLUT = 14,
        IBLCharlieLUT = 15,

        // Phase 3 extension slots
        Transmission = 16,
        Thickness = 17,
        SheenColor = 18,
        SheenRoughness = 19,
        Specular = 20,
        SpecularColor = 21,
        DiffuseTransmission = 22,
        DiffuseTransmissionColor = 23,
        Anisotropy = 24,

        // Framebuffer texture for transmission refraction
        TransmissionFramebuffer = 25,

        // SpecularGlossiness workflow
        Diffuse = 26,
        SpecularGlossiness = 27,

        // Scatter Framebuffer for VolumeScatter
        ScatterFramebuffer = 28,
        ScatterDepthFramebuffer = 29,

        // Morph Target Texture (TEXTURE_2D_ARRAY)
        MorphTargets = 30
    }

    public enum AlphaMode {
        Opaque,
        Mask,
        Blend
    }

    /// <summary>
    /// 网格类，仅包含几何数据
    /// </summary>
    public class Mesh : IDisposable {
        public const int BaseVertexStride = 5;
        public const int UV1VertexStride = 2;
        public const int SurfaceVertexStride = 11;
        public const int SkinVertexStride = 8;

        // Geometry data
        public float[] BaseVertices;
        public float[] UV1Vertices;
        public float[] SurfaceVertices;
        public float[] SkinVertices;
        public uint[] Indices;

        // Vertex attributes flags
        public bool HasUV1;
        public bool HasSurfaceAttributes;
        public bool HasSkinAttributes;
        public bool HasColor0;
        public bool UseGeneratedTangents;

        // GPU resources
        public VertexArrayObject<float, uint> VAO;
        public BufferObject<float> BaseVBO;
        public BufferObject<float> UV1VBO;
        public BufferObject<float> SurfaceVBO;
        public BufferObject<float> SkinVBO;
        public BufferObject<uint> EBO;

        // Material reference
        public Material Material { get; set; }

        // Morph Target Texture support (GPU-based morphing)
        /// <summary>
        /// Morph target 纹理（用于 GPU 端 morphing）
        /// </summary>
        public MorphTargetTexture MorphTargetTexture { get; set; }

        /// <summary>
        /// Morph target 数量
        /// </summary>
        public int MorphTargetCount { get; set; }

        /// <summary>
        /// 当前 morph weights（由动画更新）
        /// </summary>
        public float[] MorphWeights { get; set; }

        /// <summary>
        /// 是否有 morph targets
        /// </summary>
        public bool HasMorphTargets => MorphTargetTexture != null && MorphTargetCount > 0;

        // GPU Instancing support (EXT_mesh_gpu_instancing)
        /// <summary>
        /// 实例数量（大于 0 表示使用 GPU 实例化）
        /// </summary>
        public int InstanceCount { get; set; } = 0;

        /// <summary>
        /// 实例变换矩阵数组（每个实例一个 mat4）
        /// </summary>
        public Matrix4x4[] InstanceMatrices { get; set; }

        /// <summary>
        /// 实例矩阵 VBO
        /// </summary>
        public BufferObject<float> InstanceVBO { get; set; }

        /// <summary>
        /// 是否使用 GPU 实例化
        /// </summary>
        public bool UseInstancing => InstanceCount > 0 && InstanceMatrices != null;

        /// <summary>
        /// 是否为负缩放实例（需要翻转面绕序）
        /// </summary>
        public bool IsNegativeScaleInstance { get; set; } = false;

        // 缓存的顶点着色器 defines
        ShaderDefines _cachedVertDefines;

        // KHR_materials_variants: mappings from variant index to material
        // Key: variant index, Value: material for that variant
        readonly Dictionary<int, Material> _variantMaterials = new();

        /// <summary>
        /// 获取指定变体的材质，如果没有映射则返回默认材质
        /// </summary>
        public Material GetMaterialForVariant(int variantIndex) {
            if (variantIndex >= 0
                && _variantMaterials.TryGetValue(variantIndex, out Material material)) {
                return material;
            }
            return Material;
        }

        /// <summary>
        /// 添加变体材质映射
        /// </summary>
        public void SetMaterialForVariant(int variantIndex, Material material) {
            _variantMaterials[variantIndex] = material;
        }

        /// <summary>
        /// 获取所有变体材质映射
        /// </summary>
        public IReadOnlyDictionary<int, Material> VariantMaterials => _variantMaterials;

        // Centroid for depth sorting (computed from position data)

        /// <summary>
        /// 网格质心（三角形中心平均值，用于深度排序）
        /// 在 SetupMesh() 中预计算
        /// </summary>
        public Vector3 Centroid { get; private set; }

        /// <summary>
        /// 局部空间包围盒（用于射线拾取）
        /// </summary>
        public BoundingBox LocalBounds { get; private set; }

        void ComputeCentroid() {
            if (BaseVertices == null
                || BaseVertices.Length < 3) {
                Centroid = Vector3.Zero;
                return;
            }

            // 使用索引遍历三角形，计算三角形中心平均值（更准确）
            // BaseVertices 格式: [Pos.x, Pos.y, Pos.z, UV0.x, UV0.y]
            if (Indices != null
                && Indices.Length >= 3) {
                Vector3 sum = Vector3.Zero;
                int triangleCount = Indices.Length / 3;
                for (int t = 0; t < triangleCount; t++) {
                    int i0 = (int)Indices[t * 3];
                    int i1 = (int)Indices[t * 3 + 1];
                    int i2 = (int)Indices[t * 3 + 2];
                    int offset0 = i0 * BaseVertexStride;
                    int offset1 = i1 * BaseVertexStride;
                    int offset2 = i2 * BaseVertexStride;

                    // 三角形中心 = 三个顶点的平均值
                    float cx = (BaseVertices[offset0] + BaseVertices[offset1] + BaseVertices[offset2]) / 3f;
                    float cy = (BaseVertices[offset0 + 1] + BaseVertices[offset1 + 1] + BaseVertices[offset2 + 1]) / 3f;
                    float cz = (BaseVertices[offset0 + 2] + BaseVertices[offset1 + 2] + BaseVertices[offset2 + 2]) / 3f;
                    sum.X += cx;
                    sum.Y += cy;
                    sum.Z += cz;
                }
                Centroid = sum / triangleCount;
            }
            else {
                // 没有索引时，使用顶点平均值
                int vertexCount = BaseVertices.Length / BaseVertexStride;
                Vector3 sum = Vector3.Zero;
                for (int i = 0; i < vertexCount; i++) {
                    int offset = i * BaseVertexStride;
                    sum.X += BaseVertices[offset];
                    sum.Y += BaseVertices[offset + 1];
                    sum.Z += BaseVertices[offset + 2];
                }
                Centroid = sum / vertexCount;
            }
        }

        /// <summary>
        /// 计算局部空间包围盒
        /// </summary>
        void ComputeLocalBounds() {
            if (BaseVertices == null || BaseVertices.Length < BaseVertexStride) {
                LocalBounds = BoundingBox.Empty;
                return;
            }

            Vector3 min = new(float.MaxValue);
            Vector3 max = new(float.MinValue);

            int vertexCount = BaseVertices.Length / BaseVertexStride;
            for (int i = 0; i < vertexCount; i++) {
                int offset = i * BaseVertexStride;
                float x = BaseVertices[offset];
                float y = BaseVertices[offset + 1];
                float z = BaseVertices[offset + 2];

                min = Vector3.Min(min, new Vector3(x, y, z));
                max = Vector3.Max(max, new Vector3(x, y, z));
            }

            LocalBounds = min.X != float.MaxValue ? new BoundingBox(min, max) : BoundingBox.Empty;
        }

        public void SetupMesh() {
            HasSurfaceAttributes = SurfaceVertices != null && SurfaceVertices.Length > 0;
            HasSkinAttributes = SkinVertices != null && SkinVertices.Length > 0;

            // 预计算质心（在加载时完成，避免渲染时计算）
            ComputeCentroid();
            // 计算局部空间包围盒（用于射线拾取）
            ComputeLocalBounds();
            VAO = new VertexArrayObject<float, uint>();
            VAO.Bind();
            BaseVBO = new BufferObject<float>(BaseVertices, BufferTargetARB.ArrayBuffer);
            VAO.VertexAttributePointer(0, 3, VertexAttribPointerType.Float, BaseVertexStride, 0);
            VAO.VertexAttributePointer(2, 2, VertexAttribPointerType.Float, BaseVertexStride, 3);
            if (HasUV1) {
                UV1VBO = new BufferObject<float>(UV1Vertices, BufferTargetARB.ArrayBuffer);
                VAO.VertexAttributePointer(7, 2, VertexAttribPointerType.Float, UV1VertexStride, 0);
            }
            if (HasSurfaceAttributes) {
                SurfaceVBO = new BufferObject<float>(SurfaceVertices, BufferTargetARB.ArrayBuffer);
                // SurfaceVertices layout: [Normal(3), Color(4), Tangent(4)]
                VAO.VertexAttributePointer(1, 3, VertexAttribPointerType.Float, SurfaceVertexStride, 0); // Normal at offset 0
                VAO.VertexAttributePointer(4, 4, VertexAttribPointerType.Float, SurfaceVertexStride, 3); // Color at offset 3
                VAO.VertexAttributePointer(3, 4, VertexAttribPointerType.Float, SurfaceVertexStride, 7); // Tangent at offset 7
            }
            EBO = new BufferObject<uint>(Indices, BufferTargetARB.ElementArrayBuffer);
            if (HasSkinAttributes) {
                SkinVBO = new BufferObject<float>(SkinVertices, BufferTargetARB.ArrayBuffer);
                VAO.VertexAttributePointer(5, 4, VertexAttribPointerType.Float, SkinVertexStride, 0);
                VAO.VertexAttributePointer(6, 4, VertexAttribPointerType.Float, SkinVertexStride, 4);
            }

            // Setup instancing buffer if needed
            SetupInstancingBuffer();

            // Prevent subsequent mesh setup from accidentally replacing this VAO's EBO binding.
            GlContext.GL.BindVertexArray(0);
        }

        /// <summary>
        /// 设置 GPU 实例化缓冲区
        /// </summary>
        public unsafe void SetupInstancingBuffer() {
            if (!UseInstancing
                || InstanceMatrices == null
                || InstanceMatrices.Length == 0) {
                return;
            }

            // 确保 VAO 已绑定
            VAO.Bind();
            InstanceVBO = new BufferObject<float>(
                MemoryMarshal.CreateReadOnlySpan(ref InstanceMatrices[0].M11, InstanceMatrices.Length * 16),
                BufferTargetARB.ArrayBuffer
            );
            InstanceVBO.Bind();

            // 实例矩阵属性使用位置 8, 9, 10, 11（mat4 需要 4 个 vec4）
            const uint instanceMatrixLocation = 8;
            VAO.SetInstancedMatrixAttribute(instanceMatrixLocation);

            // 解绑 VAO 防止后续操作干扰
            GlContext.GL.BindVertexArray(0);
        }

        public void Bind() {
            VAO.Bind();
        }

        /// <summary>
        /// 获取缓存的顶点着色器 defines
        /// </summary>
        public ShaderDefines GetVertDefines() {
            if (_cachedVertDefines != null) {
                return _cachedVertDefines;
            }

            _cachedVertDefines = ShaderDefines.CreateFromMesh(this);
            return _cachedVertDefines;
        }

        public void Dispose() {
            VAO.Dispose();
            BaseVBO.Dispose();
            UV1VBO?.Dispose();
            SurfaceVBO?.Dispose();
            SkinVBO?.Dispose();
            InstanceVBO?.Dispose();
            EBO.Dispose();
            MorphTargetTexture?.Dispose();
        }
    }
}