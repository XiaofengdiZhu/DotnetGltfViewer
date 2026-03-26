using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.OpenGLES;

namespace DotnetGltfRenderer {
    /// <summary>
    /// 通用 Uniform Buffer Object (UBO) 实现
    /// 使用 std140 布局
    /// </summary>
    public class UniformBuffer<T> : IDisposable where T : unmanaged {
        readonly uint _handle;
        readonly int _size;

        /// <summary>
        /// UBO 绑定点
        /// </summary>
        public int BindingPoint { get; }

        public unsafe UniformBuffer(int bindingPoint) {
            BindingPoint = bindingPoint;
            _size = Marshal.SizeOf<T>();
            _handle = GlContext.GL.GenBuffer();
            GlContext.GL.BindBuffer(BufferTargetARB.UniformBuffer, _handle);
            GlContext.GL.BufferData(BufferTargetARB.UniformBuffer, (nuint)_size, null, BufferUsageARB.DynamicDraw);
            GlContext.GL.BindBufferBase(BufferTargetARB.UniformBuffer, (uint)BindingPoint, _handle);
        }

        /// <summary>
        /// 更新 UBO 数据
        /// </summary>
        public unsafe void Update(ref T data) {
            GlContext.GL.BindBuffer(BufferTargetARB.UniformBuffer, _handle);
            fixed (T* ptr = &data) {
                GlContext.GL.BufferSubData(BufferTargetARB.UniformBuffer, 0, (nuint)_size, ptr);
            }
        }

        /// <summary>
        /// 绑定到指定着色器的 uniform block
        /// </summary>
        public void BindToShader(uint programHandle, string blockName) {
            uint blockIndex = GlContext.GL.GetUniformBlockIndex(programHandle, blockName);
            if (blockIndex != uint.MaxValue) {
                GlContext.GL.UniformBlockBinding(programHandle, blockIndex, (uint)BindingPoint);
            }
        }

        public void Dispose() {
            GlContext.GL.DeleteBuffer(_handle);
        }
    }

    #region UBO Data Structures

    /// <summary>
    /// 场景数据 UBO（每帧更新一次）
    /// std140 布局：必须按 16 字节对齐
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SceneData {
        // Camera
        public Vector4 CameraPos; // 16 bytes (vec4，但只用 xyz)

        // Environment
        public float Exposure; // 4 bytes
        public float EnvironmentStrength; // 4 bytes - IBL 环境贴图强度
        public int MipCount; // 4 bytes - IBL mipmap count
        public float Padding0;

        // EnvRotation (mat3 stored as 3 vec4s for std140 alignment)
        // Each column of mat3 is stored in a vec4, using only xyz
        public Vector4 EnvRotationCol0; // 16 bytes, offset 32
        public Vector4 EnvRotationCol1; // 16 bytes, offset 48
        public Vector4 EnvRotationCol2; // 16 bytes, offset 64

        // Total: 80 bytes
    }

    /// <summary>
    /// 材质数据 UBO（每个 mesh 更新）
    /// std140 布局，对齐官方 material_info.glsl 的 uniform 定义
    /// 注意：std140 布局中 vec3 需要 16 字节对齐
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct MaterialData {
        // ============ PBR Core (对齐官方) ============
        public Vector4 BaseColorFactor; // u_BaseColorFactor (vec4) - 16 bytes, offset 0
        public Vector4 EmissiveFactor; // u_EmissiveFactor (vec4) - 16 bytes, offset 16

        public float MetallicFactor; // u_MetallicFactor - 4 bytes, offset 32
        public float RoughnessFactor; // u_RoughnessFactor - 4 bytes, offset 36
        public float NormalScale; // u_NormalScale - 4 bytes, offset 40
        public float OcclusionStrength; // u_OcclusionStrength - 4 bytes, offset 44

        // ============ Alpha ============
        public int AlphaMode; // 0=OPAQUE, 1=MASK, 2=BLEND - 4 bytes, offset 48
        public float AlphaCutoff; // u_AlphaCutoff - 4 bytes, offset 52
        public int UseGeneratedTangents; // 4 bytes, offset 56
        public float UnlitPadding0; // 4 bytes, offset 60

        // ============ IOR (KHR_materials_ior) ============
        public float Ior; // u_Ior (default: 1.5) - 4 bytes, offset 64
        public float IorPadding0; // padding for 16-byte alignment - 4 bytes, offset 68
        public float IorPadding1; // padding - 4 bytes, offset 72
        public float IorPadding2; // padding - 4 bytes, offset 76

        // ============ Emissive Strength (KHR_materials_emissive_strength) ============
        public float EmissiveStrength; // u_EmissiveStrength (default: 1.0) - 4 bytes, offset 80
        public float EmissiveStrengthPadding0; // padding - 4 bytes, offset 84
        public float EmissiveStrengthPadding1; // padding - 4 bytes, offset 88
        public float EmissiveStrengthPadding2; // padding - 4 bytes, offset 92

        // ============ Specular (KHR_materials_specular) ============
        public float SpecularFactor; // u_KHR_materials_specular_specularFactor - 4 bytes, offset 96
        public float SpecularPadding0; // padding - 4 bytes, offset 100
        public float SpecularPadding1; // padding - 4 bytes, offset 104
        public float SpecularPadding2; // padding - 4 bytes, offset 108
        public Vector4 SpecularColorFactor; // u_KHR_materials_specular_specularColorFactor (vec4 for alignment) - 16 bytes, offset 112

        // ============ Sheen (KHR_materials_sheen) ============
        public Vector4 SheenColorFactor; // u_SheenColorFactor (vec4 for alignment) - 16 bytes, offset 128
        public float SheenRoughnessFactor; // u_SheenRoughnessFactor - 4 bytes, offset 144
        public float SheenPadding0; // padding - 4 bytes, offset 148
        public float SheenPadding1; // padding - 4 bytes, offset 152
        public float SheenPadding2; // padding - 4 bytes, offset 156

        // ============ ClearCoat (KHR_materials_clearcoat) ============
        public float ClearCoatFactor; // u_ClearcoatFactor - 4 bytes, offset 160
        public float ClearCoatRoughness; // u_ClearcoatRoughnessFactor - 4 bytes, offset 164
        public float ClearCoatNormalScale; // u_ClearcoatNormalScale - 4 bytes, offset 168
        public float ClearCoatPadding0; // padding - 4 bytes, offset 172

        // ============ Transmission (KHR_materials_transmission) ============
        public float TransmissionFactor; // u_TransmissionFactor - 4 bytes, offset 176
        public float TransmissionPadding0; // padding - 4 bytes, offset 180
        public float TransmissionPadding1; // padding - 4 bytes, offset 184
        public float TransmissionPadding2; // padding - 4 bytes, offset 188

        // ============ Volume (KHR_materials_volume) ============
        public float ThicknessFactor; // u_ThicknessFactor - 4 bytes, offset 192
        public float AttenuationDistance; // u_AttenuationDistance - 4 bytes, offset 196
        public float VolumePadding0; // padding - 4 bytes, offset 200
        public float VolumePadding1; // padding - 4 bytes, offset 204
        public Vector4 AttenuationColor; // u_AttenuationColor (vec4 for alignment) - 16 bytes, offset 208

        // ============ Iridescence (KHR_materials_iridescence) ============
        public float IridescenceFactor; // u_IridescenceFactor - 4 bytes, offset 224
        public float IridescenceIor; // u_IridescenceIor - 4 bytes, offset 228
        public float IridescenceThicknessMin; // u_IridescenceThicknessMinimum - 4 bytes, offset 232
        public float IridescenceThicknessMax; // u_IridescenceThicknessMaximum - 4 bytes, offset 236

        // ============ Dispersion (KHR_materials_dispersion) ============
        public float Dispersion; // u_Dispersion - 4 bytes, offset 240
        public float DispersionPadding0; // padding - 4 bytes, offset 244
        public float DispersionPadding1; // padding - 4 bytes, offset 248
        public float DispersionPadding2; // padding - 4 bytes, offset 252

        // ============ Diffuse Transmission (KHR_materials_diffuse_transmission) ============
        public float DiffuseTransmissionFactor; // u_DiffuseTransmissionFactor - 4 bytes, offset 256
        public float DiffuseTransmissionPadding0; // padding - 4 bytes, offset 260
        public float DiffuseTransmissionPadding1; // padding - 4 bytes, offset 264
        public float DiffuseTransmissionPadding2; // padding - 4 bytes, offset 268
        public Vector4 DiffuseTransmissionColorFactor; // u_DiffuseTransmissionColorFactor (vec4 for alignment) - 16 bytes, offset 272

        // ============ Anisotropy (KHR_materials_anisotropy) ============
        public Vector4 Anisotropy; // u_Anisotropy (xyz=direction, w=strength) - 16 bytes, offset 288

        // ============ UV Indices (用于纹理采样) ============
        public int BaseColorUVSet; // u_BaseColorUVSet - 4 bytes, offset 304
        public int MetallicRoughnessUVSet; // u_MetallicRoughnessUVSet - 4 bytes, offset 308
        public int NormalUVSet; // u_NormalUVSet - 4 bytes, offset 312
        public int OcclusionUVSet; // u_OcclusionUVSet - 4 bytes, offset 316
        public int EmissiveUVSet; // u_EmissiveUVSet - 4 bytes, offset 320
        public int DiffuseUVSet; // u_DiffuseUVSet (SpecularGlossiness) - 4 bytes, offset 324
        public int SpecularGlossinessUVSet; // u_SpecularGlossinessUVSet - 4 bytes, offset 328
        public int UVPadding0; // padding - 4 bytes, offset 332

        // Extension UV sets (继续)
        public int ClearCoatUVSet; // u_ClearcoatUVSet - 4 bytes, offset 336
        public int ClearCoatRoughnessUVSet; // u_ClearcoatRoughnessUVSet - 4 bytes, offset 340
        public int ClearCoatNormalUVSet; // u_ClearcoatNormalUVSet - 4 bytes, offset 344
        public int IridescenceUVSet; // u_IridescenceUVSet - 4 bytes, offset 348
        public int IridescenceThicknessUVSet; // u_IridescenceThicknessUVSet - 4 bytes, offset 352
        public int SheenColorUVSet; // u_SheenColorUVSet - 4 bytes, offset 356
        public int SheenRoughnessUVSet; // u_SheenRoughnessUVSet - 4 bytes, offset 360
        public int SpecularUVSet; // u_SpecularUVSet - 4 bytes, offset 364
        public int SpecularColorUVSet; // u_SpecularColorUVSet - 4 bytes, offset 368
        public int TransmissionUVSet; // u_TransmissionUVSet - 4 bytes, offset 372
        public int ThicknessUVSet; // u_ThicknessUVSet - 4 bytes, offset 376
        public int DiffuseTransmissionUVSet; // u_DiffuseTransmissionUVSet - 4 bytes, offset 380
        public int DiffuseTransmissionColorUVSet; // u_DiffuseTransmissionColorUVSet - 4 bytes, offset 384
        public int AnisotropyUVSet; // u_AnisotropyUVSet - 4 bytes, offset 388
        public int UVSetPadding0; // padding - 4 bytes, offset 392
        public int UVSetPadding1; // padding - 4 bytes, offset 396

        // ============ Flags ============
        public int ExtensionFlags; // 启用的扩展标志 - 4 bytes, offset 400
        public int TextureFlags; // 纹理存在标志 - 4 bytes, offset 404
        public int FlagsPadding0; // padding - 4 bytes, offset 408
        public int FlagsPadding1; // padding - 4 bytes, offset 412

        // ============ SpecularGlossiness (KHR_materials_pbrSpecularGlossiness) ============
        // std140 布局中 vec3 需要 16 字节对齐，必须使用 Vector4 来保证正确的内存布局
        public Vector4 SpecularFactorSG; // u_SpecularFactor (vec4 for std140 alignment, only xyz used) - 16 bytes, offset 416
        public float GlossinessFactor; // uGlContext.GLossinessFactor - 4 bytes, offset 432
        public float _SGPad0; // padding - 4 bytes, offset 436
        public float _SGPad1; // padding - 4 bytes, offset 440
        public float _SGPad2; // padding - 4 bytes, offset 444

        // Total: 448 bytes
    }

    /// <summary>
    /// 纹理标志位（对应官方 HAS_XXX_MAP defines）
    /// </summary>
    [Flags]
    public enum TextureFlags {
        None = 0,

        // Core textures
        BaseColor = 1 << 0,
        MetallicRoughness = 1 << 1,
        Normal = 1 << 2,
        Occlusion = 1 << 3,
        Emissive = 1 << 4,

        // Clearcoat textures
        ClearCoat = 1 << 5,
        ClearCoatRoughness = 1 << 6,
        ClearCoatNormal = 1 << 7,

        // Iridescence textures
        Iridescence = 1 << 8,
        IridescenceThickness = 1 << 9,

        // Sheen textures
        SheenColor = 1 << 10,
        SheenRoughness = 1 << 11,

        // Specular textures
        Specular = 1 << 12,
        SpecularColor = 1 << 13,

        // Transmission textures
        Transmission = 1 << 14,

        // Volume textures
        Thickness = 1 << 15,

        // Diffuse Transmission textures
        DiffuseTransmission = 1 << 16,
        DiffuseTransmissionColor = 1 << 17,

        // Anisotropy texture
        Anisotropy = 1 << 18
    }

    /// <summary>
    /// 材质扩展标志位（对应官方 MATERIAL_XXX defines）
    /// </summary>
    [Flags]
    public enum ExtensionFlags {
        None = 0,
        MetallicRoughness = 1 << 0, // MATERIAL_METALLICROUGHNESS
        SpecularGlossiness = 1 << 1, // MATERIAL_SPECULARGLOSSINESS
        ClearCoat = 1 << 2, // MATERIAL_CLEARCOAT
        Sheen = 1 << 3, // MATERIAL_SHEEN
        Specular = 1 << 4, // MATERIAL_SPECULAR
        Transmission = 1 << 5, // MATERIAL_TRANSMISSION
        Volume = 1 << 6, // MATERIAL_VOLUME
        Iridescence = 1 << 7, // MATERIAL_IRIDESCENCE
        Ior = 1 << 8, // MATERIAL_IOR
        Anisotropy = 1 << 9, // MATERIAL_ANISOTROPY
        EmissiveStrength = 1 << 10, // MATERIAL_EMISSIVE_STRENGTH
        Dispersion = 1 << 11, // MATERIAL_DISPERSION
        DiffuseTransmission = 1 << 12, // MATERIAL_DIFFUSE_TRANSMISSION
        VolumeScatter = 1 << 13, // MATERIAL_VOLUME_SCATTER
        Unlit = 1 << 14 // MATERIAL_UNLIT
    }

    /// <summary>
    /// 材质默认值（来自 glTF 规范）
    /// </summary>
    public static class MaterialDefaults {
        public const float MetallicFactor = 1.0f;
        public const float RoughnessFactor = 1.0f;
        public const float NormalScale = 1.0f;
        public const float OcclusionStrength = 1.0f;
        public const float AlphaCutoff = 0.5f;
        public const float Ior = 1.5f;
        public const float EmissiveStrength = 1.0f;
        public const float SpecularFactor = 1.0f;
        public const float ClearCoatFactor = 0.0f;
        public const float ClearCoatRoughnessFactor = 0.0f;
        public const float TransmissionFactor = 0.0f;
        public const float ThicknessFactor = 0.0f;
        public const float IridescenceFactor = 0.0f;
        public const float IridescenceIor = 1.3f;
        public const float Dispersion = 0.0f;
        public const float DiffuseTransmissionFactor = 0.0f;
        public const float SheenRoughnessFactor = 0.0f;
    }

    #endregion

    #region Render State Data Structure

    /// <summary>
    /// 渲染状态数据 UBO（每帧更新一次）
    /// Binding Point: 3
    /// std140 布局
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct RenderStateData {
        public Matrix4x4 ViewProjectionMatrix;  // 64 bytes, offset 0
        public Matrix4x4 ViewMatrix;             // 64 bytes, offset 64
        public Matrix4x4 ProjectionMatrix;       // 64 bytes, offset 128
        public Matrix4x4 ModelMatrix;            // 64 bytes, offset 192
        public Matrix4x4 NormalMatrix;           // 64 bytes, offset 256
        // Total: 320 bytes
    }

    #endregion

    #region Light Data Structures

    /// <summary>
    /// 光源数据结构（std140 布局）
    /// 必须与着色器中的 Light 结构体匹配
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct LightData {
        public Vector3 Direction; // 12 bytes
        public float Range; // 4 bytes

        public Vector3 Color; // 12 bytes
        public float Intensity; // 4 bytes

        public Vector3 Position; // 12 bytes
        public float InnerConeCos; // 4 bytes

        public float OuterConeCos; // 4 bytes
        public int Type; // 4 bytes (0=Directional, 1=Point, 2=Spot)
        public float Pad0; // 4 bytes
        public float Pad1; // 4 bytes

        // Total: 64 bytes (4 * vec4)
    }

    /// <summary>
    /// 光源数组 UBO（最多支持 8 个光源）
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct LightsData {
        public int LightCount; // 4 bytes
        public int Pad0; // 4 bytes
        public int Pad1; // 4 bytes
        public int Pad2; // 4 bytes

        // 固定数组，最多 8 个光源
        public LightData Light0;
        public LightData Light1;
        public LightData Light2;
        public LightData Light3;
        public LightData Light4;
        public LightData Light5;
        public LightData Light6;
        public LightData Light7;

        // Total: 16 + 8 * 64 = 528 bytes
    }

    #endregion

    #region UV Transform Data Structure

    /// <summary>
    /// UV 变换矩阵数据 UBO（每个材质更新）
    /// Binding Point: 4
    /// std140 布局：mat3 需要 48 字节（每列 16 字节对齐，使用 vec4 存储 xyz）
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct UVTransformData {
        // Core textures
        public UVMatrix3 NormalUVTransform;         // 48 bytes
        public UVMatrix3 EmissiveUVTransform;       // 48 bytes
        public UVMatrix3 OcclusionUVTransform;      // 48 bytes

        // MetallicRoughness
        public UVMatrix3 BaseColorUVTransform;      // 48 bytes
        public UVMatrix3 MetallicRoughnessUVTransform; // 48 bytes

        // SpecularGlossiness
        public UVMatrix3 DiffuseUVTransform;        // 48 bytes
        public UVMatrix3 SpecularGlossinessUVTransform; // 48 bytes

        // ClearCoat
        public UVMatrix3 ClearcoatUVTransform;      // 48 bytes
        public UVMatrix3 ClearcoatRoughnessUVTransform; // 48 bytes
        public UVMatrix3 ClearcoatNormalUVTransform; // 48 bytes

        // Sheen
        public UVMatrix3 SheenColorUVTransform;     // 48 bytes
        public UVMatrix3 SheenRoughnessUVTransform; // 48 bytes

        // Specular
        public UVMatrix3 SpecularUVTransform;       // 48 bytes
        public UVMatrix3 SpecularColorUVTransform;  // 48 bytes

        // Transmission
        public UVMatrix3 TransmissionUVTransform;   // 48 bytes

        // Volume
        public UVMatrix3 ThicknessUVTransform;      // 48 bytes

        // Iridescence
        public UVMatrix3 IridescenceUVTransform;    // 48 bytes
        public UVMatrix3 IridescenceThicknessUVTransform; // 48 bytes

        // Diffuse Transmission
        public UVMatrix3 DiffuseTransmissionUVTransform; // 48 bytes
        public UVMatrix3 DiffuseTransmissionColorUVTransform; // 48 bytes

        // Anisotropy
        public UVMatrix3 AnisotropyUVTransform;     // 48 bytes

        // Total: 21 * 48 = 1008 bytes
    }

    /// <summary>
    /// UV 变换矩阵（mat3 存储为 3 个 vec4，用于 std140 布局）
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct UVMatrix3 {
        public Vector4 Col0; // xyz = first column of mat3
        public Vector4 Col1; // xyz = second column of mat3
        public Vector4 Col2; // xyz = third column of mat3

        public UVMatrix3(Matrix4x4 matrix) {
            Col0 = new Vector4(matrix.M11, matrix.M21, matrix.M31, 0f);
            Col1 = new Vector4(matrix.M12, matrix.M22, matrix.M32, 0f);
            Col2 = new Vector4(matrix.M13, matrix.M23, matrix.M33, 0f);
        }

        public static UVMatrix3 Identity => new UVMatrix3(
            new Vector4(1f, 0f, 0f, 0f),
            new Vector4(0f, 1f, 0f, 0f),
            new Vector4(0f, 0f, 1f, 0f)
        );

        public UVMatrix3(Vector4 col0, Vector4 col1, Vector4 col2) {
            Col0 = col0;
            Col1 = col1;
            Col2 = col2;
        }
    }

    #endregion

    #region Volume Scatter Data Structure

    /// <summary>
    /// Volume Scatter 数据 UBO（材质启用 Volume Scatter 时更新）
    /// Binding Point: 5
    /// std140 布局
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct VolumeScatterData {
        public Vector4 MultiScatterColor;  // 16 bytes, offset 0 (xyz used)
        public float MinRadius;            // 4 bytes, offset 16
        public int MaterialID;             // 4 bytes, offset 20
        public int FramebufferWidth;       // 4 bytes, offset 24
        public int FramebufferHeight;      // 4 bytes, offset 28
        // Total: 32 bytes

        // ScatterSamples 数组太大，保持使用独立 uniform
        // 因为 std140 布局下 55 个 vec3 需要 55 * 16 = 880 bytes
    }

    #endregion
}