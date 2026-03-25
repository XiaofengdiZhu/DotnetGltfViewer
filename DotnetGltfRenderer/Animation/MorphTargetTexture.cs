using System;
using System.Collections.Generic;
using System.Numerics;
using Silk.NET.OpenGLES;

namespace DotnetGltfRenderer {
    /// <summary>
    /// Morph Target 纹理，将 morph targets 数据打包为 TEXTURE_2D_ARRAY
    /// 参考 glTF-Sample-Renderer 的 primitive.js 实现
    /// 纹理布局：
    /// - 类型：TEXTURE_2D_ARRAY (RGBA32F)
    /// - 宽度/高度：ceil(sqrt(vertexCount))
    /// - 层数：targetCount * attributeCount
    /// - 层组织：[POSITION targets][NORMAL targets][TANGENT targets]...
    /// </summary>
    public class MorphTargetTexture : IDisposable {
        readonly GL _gl;
        bool _disposed;

        // Attribute offsets (layer index offset for each attribute type)

        // Active attributes (sorted in canonical order)
        readonly List<string> _activeAttributes = [];

        // 定义属性的规范顺序
        static readonly string[] CanonicalAttributeOrder = ["POSITION", "NORMAL", "TANGENT", "TEXCOORD_0", "TEXCOORD_1", "COLOR_0"];

        /// <summary>
        /// 纹理句柄
        /// </summary>
        public uint TextureHandle { get; private set; }

        /// <summary>
        /// 纹理尺寸（宽高相等）
        /// </summary>
        public int TextureSize { get; }

        /// <summary>
        /// 顶点数量
        /// </summary>
        public int VertexCount { get; }

        /// <summary>
        /// Morph target 数量
        /// </summary>
        public int TargetCount { get; }

        /// <summary>
        /// 纹理层数
        /// </summary>
        public int LayerCount { get; }

        /// <summary>
        /// 是否有 POSITION morph targets
        /// </summary>
        public bool HasPosition => PositionOffset >= 0;

        /// <summary>
        /// 是否有 NORMAL morph targets
        /// </summary>
        public bool HasNormal => NormalOffset >= 0;

        /// <summary>
        /// 是否有 TANGENT morph targets
        /// </summary>
        public bool HasTangent => TangentOffset >= 0;

        /// <summary>
        /// 是否有 TEXCOORD_0 morph targets
        /// </summary>
        public bool HasTexCoord0 => TexCoord0Offset >= 0;

        /// <summary>
        /// 是否有 TEXCOORD_1 morph targets
        /// </summary>
        public bool HasTexCoord1 => TexCoord1Offset >= 0;

        /// <summary>
        /// 是否有 COLOR_0 morph targets
        /// </summary>
        public bool HasColor0 => Color0Offset >= 0;

        /// <summary>
        /// 活跃属性列表（按规范顺序排序）
        /// </summary>
        public IReadOnlyList<string> ActiveAttributes => _activeAttributes;

        /// <summary>
        /// POSITION 层偏移
        /// </summary>
        public int PositionOffset { get; } = -1;

        /// <summary>
        /// NORMAL 层偏移
        /// </summary>
        public int NormalOffset { get; } = -1;

        /// <summary>
        /// TANGENT 层偏移
        /// </summary>
        public int TangentOffset { get; } = -1;

        /// <summary>
        /// TEXCOORD_0 层偏移
        /// </summary>
        public int TexCoord0Offset { get; } = -1;

        /// <summary>
        /// TEXCOORD_1 层偏移
        /// </summary>
        public int TexCoord1Offset { get; } = -1;

        /// <summary>
        /// COLOR_0 层偏移
        /// </summary>
        public int Color0Offset { get; } = -1;

        /// <summary>
        /// 创建 Morph Target 纹理
        /// </summary>
        /// <param name="gl">OpenGL 上下文</param>
        /// <param name="vertexCount">顶点数量</param>
        /// <param name="targetCount">Morph target 数量</param>
        /// <param name="attributes">活跃属性集合</param>
        public MorphTargetTexture(GL gl, int vertexCount, int targetCount, IReadOnlySet<string> attributes) {
            _gl = gl;
            VertexCount = vertexCount;
            TargetCount = targetCount;

            // 按规范顺序排序属性
            foreach (string canonicalAttr in CanonicalAttributeOrder) {
                if (attributes.Contains(canonicalAttr)) {
                    _activeAttributes.Add(canonicalAttr);
                }
            }

            // 计算纹理尺寸：ceil(sqrt(vertexCount))
            TextureSize = (int)Math.Ceiling(Math.Sqrt(vertexCount));

            // 计算层数和属性偏移
            // 布局：[POSITION targets][NORMAL targets][TANGENT targets]...
            // 每个属性占 targetCount 层
            int layerIndex = 0;
            foreach (string attr in _activeAttributes) {
                switch (attr) {
                    case "POSITION": PositionOffset = layerIndex; break;
                    case "NORMAL": NormalOffset = layerIndex; break;
                    case "TANGENT": TangentOffset = layerIndex; break;
                    case "TEXCOORD_0": TexCoord0Offset = layerIndex; break;
                    case "TEXCOORD_1": TexCoord1Offset = layerIndex; break;
                    case "COLOR_0": Color0Offset = layerIndex; break;
                }
                // 每个属性的偏移增加 targetCount（不是 1）
                layerIndex += targetCount;
            }

            // 总层数 = targetCount * attributeCount
            LayerCount = targetCount * _activeAttributes.Count;
            CreateTexture();
        }

        unsafe void CreateTexture() {
            TextureHandle = _gl.GenTexture();
            _gl.BindTexture(TextureTarget.Texture2DArray, TextureHandle);

            // 设置纹理参数（与官方一致）
            _gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            _gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            _gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            _gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);

            // 分配纹理存储（RGBA32F）
            // 使用 glTexImage3D 创建 2D Array 纹理
            _gl.TexImage3D(
                TextureTarget.Texture2DArray,
                0,
                InternalFormat.Rgba32f,
                (uint)TextureSize,
                (uint)TextureSize,
                (uint)LayerCount,
                0,
                PixelFormat.Rgba,
                PixelType.Float,
                null
            );
            _gl.BindTexture(TextureTarget.Texture2DArray, 0);
        }

        /// <summary>
        /// 上传 morph target 数据到纹理
        /// </summary>
        /// <param name="positions">POSITION deltas 数组（每个 target 一个数组）</param>
        /// <param name="normals">NORMAL deltas 数组（每个 target 一个数组）</param>
        /// <param name="tangents">TANGENT deltas 数组（每个 target 一个数组）</param>
        /// <param name="texCoords0">TEXCOORD_0 deltas 数组（每个 target 一个数组）</param>
        /// <param name="texCoords1">TEXCOORD_1 deltas 数组（每个 target 一个数组）</param>
        /// <param name="colors0">COLOR_0 deltas 数组（每个 target 一个数组）</param>
        public void UploadData(IReadOnlyList<Vector3>[] positions,
            IReadOnlyList<Vector3>[] normals,
            IReadOnlyList<Vector4>[] tangents,
            IReadOnlyList<Vector2>[] texCoords0,
            IReadOnlyList<Vector2>[] texCoords1,
            IReadOnlyList<Vector4>[] colors0) {
            _gl.BindTexture(TextureTarget.Texture2DArray, TextureHandle);

            // 每层纹理的大小（像素数 * 4 floats）
            int layerPixelCount = TextureSize * TextureSize;
            float[] layerData = new float[layerPixelCount * 4];

            // 上传每个 target 的每个属性
            for (int t = 0; t < TargetCount; t++) {
                // POSITION
                if (PositionOffset >= 0
                    && positions != null
                    && t < positions.Length
                    && positions[t] != null) {
                    UploadAttributeLayer(layerData, positions[t], PositionOffset + t);
                }

                // NORMAL
                if (NormalOffset >= 0
                    && normals != null
                    && t < normals.Length
                    && normals[t] != null) {
                    UploadAttributeLayer(layerData, normals[t], NormalOffset + t);
                }

                // TANGENT
                if (TangentOffset >= 0
                    && tangents != null
                    && t < tangents.Length
                    && tangents[t] != null) {
                    UploadAttributeLayer(layerData, tangents[t], TangentOffset + t);
                }

                // TEXCOORD_0
                if (TexCoord0Offset >= 0
                    && texCoords0 != null
                    && t < texCoords0.Length
                    && texCoords0[t] != null) {
                    UploadAttributeLayer(layerData, texCoords0[t], TexCoord0Offset + t);
                }

                // TEXCOORD_1
                if (TexCoord1Offset >= 0
                    && texCoords1 != null
                    && t < texCoords1.Length
                    && texCoords1[t] != null) {
                    UploadAttributeLayer(layerData, texCoords1[t], TexCoord1Offset + t);
                }

                // COLOR_0
                if (Color0Offset >= 0
                    && colors0 != null
                    && t < colors0.Length
                    && colors0[t] != null) {
                    UploadAttributeLayer(layerData, colors0[t], Color0Offset + t);
                }
            }
            _gl.BindTexture(TextureTarget.Texture2DArray, 0);
        }

        unsafe void UploadAttributeLayer<T>(float[] layerData, IReadOnlyList<T> attributeData, int layerIndex)
            where T : unmanaged {
            // 清零层数据
            Array.Clear(layerData, 0, layerData.Length);
            int vertexCount = Math.Min(attributeData.Count, VertexCount);

            // 填充数据（VEC2/VEC3 填充为 VEC4/RGBA）
            if (attributeData is IReadOnlyList<Vector3> vec3Data) {
                for (int i = 0; i < vertexCount; i++) {
                    int offset = i * 4;
                    Vector3 v = vec3Data[i];
                    layerData[offset + 0] = v.X;
                    layerData[offset + 1] = v.Y;
                    layerData[offset + 2] = v.Z;
                    layerData[offset + 3] = 0f; // padding
                }
            }
            else if (attributeData is IReadOnlyList<Vector4> vec4Data) {
                for (int i = 0; i < vertexCount; i++) {
                    int offset = i * 4;
                    Vector4 v = vec4Data[i];
                    layerData[offset + 0] = v.X;
                    layerData[offset + 1] = v.Y;
                    layerData[offset + 2] = v.Z;
                    layerData[offset + 3] = v.W;
                }
            }
            else if (attributeData is IReadOnlyList<Vector2> vec2Data) {
                for (int i = 0; i < vertexCount; i++) {
                    int offset = i * 4;
                    Vector2 v = vec2Data[i];
                    layerData[offset + 0] = v.X;
                    layerData[offset + 1] = v.Y;
                    layerData[offset + 2] = 0f; // padding
                    layerData[offset + 3] = 0f; // padding
                }
            }

            // 上传到纹理
            fixed (float* ptr = layerData) {
                _gl.TexSubImage3D(
                    TextureTarget.Texture2DArray,
                    0,
                    0,
                    0,
                    layerIndex,
                    (uint)TextureSize,
                    (uint)TextureSize,
                    1,
                    PixelFormat.Rgba,
                    PixelType.Float,
                    ptr
                );
            }
        }

        /// <summary>
        /// 绑定 morph target 纹理到指定纹理单元
        /// </summary>
        public void Bind(TextureUnit unit) {
            _gl.ActiveTexture(unit);
            _gl.BindTexture(TextureTarget.Texture2DArray, TextureHandle);
        }

        public void Dispose() {
            if (_disposed) {
                return;
            }
            if (TextureHandle != 0) {
                _gl.DeleteTexture(TextureHandle);
                TextureHandle = 0;
            }
            _disposed = true;
        }
    }
}