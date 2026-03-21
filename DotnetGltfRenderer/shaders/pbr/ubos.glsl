// shaders/pbr/ubos.glsl
// UBO 定义文件，供 pbr.frag 包含
// 必须与 UniformBuffer.cs 中的结构体完全匹配

// ============================================================================
// SceneData UBO - 场景级数据 (Binding Point 0)
// Total: 32 bytes
// ============================================================================
layout(std140) uniform SceneData {
    vec4 CameraPos;// offset 0, xyz: camera position
    float Exposure;// offset 16
    float EnvironmentStrength;// offset 20, IBL 环境贴图强度
    int MipCount;// offset 24, IBL mipmap count
    float _Padding0;
} scene;

// ============================================================================
// MaterialData UBO - 材质数据 (Binding Point 1)
// Total: 448 bytes
// ============================================================================
layout(std140) uniform MaterialData {
// ============ PBR Core ============
    vec4 BaseColorFactor;// offset 0, u_BaseColorFactor
    vec4 EmissiveFactor;// offset 16, u_EmissiveFactor

    float MetallicFactor;// offset 32, u_MetallicFactor
    float RoughnessFactor;// offset 36, u_RoughnessFactor
    float NormalScale;// offset 40, u_NormalScale
    float OcclusionStrength;// offset 44, u_OcclusionStrength

// ============ Alpha ============
    int AlphaMode;// offset 48, 0=OPAQUE, 1=MASK, 2=BLEND
    float AlphaCutoff;// offset 52, u_AlphaCutoff
    int UseGeneratedTangents;// offset 56
    float _Padding0;// offset 60

// ============ IOR (KHR_materials_ior) ============
    float Ior;// offset 64, u_Ior (default: 1.5)
    float _IorPad0;// offset 68
    float _IorPad1;// offset 72
    float _IorPad2;// offset 76

// ============ Emissive Strength (KHR_materials_emissive_strength) ============
    float EmissiveStrength;// offset 80, u_EmissiveStrength (default: 1.0)
    float _EmissiveStrPad0;// offset 84
    float _EmissiveStrPad1;// offset 88
    float _EmissiveStrPad2;// offset 92

// ============ Specular (KHR_materials_specular) ============
    float SpecularFactor;// offset 96, u_KHR_materials_specular_specularFactor
    float _SpecularPad0;// offset 100
    float _SpecularPad1;// offset 104
    float _SpecularPad2;// offset 108
    vec4 SpecularColorFactor;// offset 112, u_KHR_materials_specular_specularColorFactor

// ============ Sheen (KHR_materials_sheen) ============
    vec4 SheenColorFactor;// offset 128, u_SheenColorFactor
    float SheenRoughnessFactor;// offset 144, u_SheenRoughnessFactor
    float _SheenPad0;// offset 148
    float _SheenPad1;// offset 152
    float _SheenPad2;// offset 156

// ============ ClearCoat (KHR_materials_clearcoat) ============
    float ClearCoatFactor;// offset 160, u_ClearcoatFactor
    float ClearCoatRoughness;// offset 164, u_ClearcoatRoughnessFactor
    float ClearCoatNormalScale;// offset 168, u_ClearcoatNormalScale
    float _ClearCoatPad0;// offset 172

// ============ Transmission (KHR_materials_transmission) ============
    float TransmissionFactor;// offset 176, u_TransmissionFactor
    float _TransmissionPad0;// offset 180
    float _TransmissionPad1;// offset 184
    float _TransmissionPad2;// offset 188

// ============ Volume (KHR_materials_volume) ============
    float ThicknessFactor;// offset 192, u_ThicknessFactor
    float AttenuationDistance;// offset 196, u_AttenuationDistance
    float _VolumePad0;// offset 200
    float _VolumePad1;// offset 204
    vec4 AttenuationColor;// offset 208, u_AttenuationColor

// ============ Iridescence (KHR_materials_iridescence) ============
    float IridescenceFactor;// offset 224, u_IridescenceFactor
    float IridescenceIor;// offset 228, u_IridescenceIor
    float IridescenceThicknessMin;// offset 232, u_IridescenceThicknessMinimum
    float IridescenceThicknessMax;// offset 236, u_IridescenceThicknessMaximum

// ============ Dispersion (KHR_materials_dispersion) ============
    float Dispersion;// offset 240, u_Dispersion
    float _DispersionPad0;// offset 244
    float _DispersionPad1;// offset 248
    float _DispersionPad2;// offset 252

// ============ Diffuse Transmission (KHR_materials_diffuse_transmission) ============
    float DiffuseTransmissionFactor;// offset 256, u_DiffuseTransmissionFactor
    float _DiffTransPad0;// offset 260
    float _DiffTransPad1;// offset 264
    float _DiffTransPad2;// offset 268
    vec4 DiffuseTransmissionColorFactor;// offset 272, u_DiffuseTransmissionColorFactor

// ============ Anisotropy (KHR_materials_anisotropy) ============
    vec4 Anisotropy;// offset 288, u_Anisotropy (xyz=direction, w=strength)

// ============ UV Indices ============
    int BaseColorUVSet;// offset 304
    int MetallicRoughnessUVSet;// offset 308
    int NormalUVSet;// offset 312
    int OcclusionUVSet;// offset 316
    int EmissiveUVSet;// offset 320
    int DiffuseUVSet;// offset 324 (SpecularGlossiness)
    int SpecularGlossinessUVSet;// offset 328
    int _UVPad0;// offset 332

// Extension UV sets
    int ClearCoatUVSet;// offset 336
    int ClearCoatRoughnessUVSet;// offset 340
    int ClearCoatNormalUVSet;// offset 344
    int IridescenceUVSet;// offset 348
    int IridescenceThicknessUVSet;// offset 352
    int SheenColorUVSet;// offset 356
    int SheenRoughnessUVSet;// offset 360
    int SpecularUVSet;// offset 364
    int SpecularColorUVSet;// offset 368
    int TransmissionUVSet;// offset 372
    int ThicknessUVSet;// offset 376
    int DiffuseTransmissionUVSet;// offset 380
    int DiffuseTransmissionColorUVSet;// offset 384
    int AnisotropyUVSet;// offset 388
    int _UVSetPad0;// offset 392
    int _UVSetPad1;// offset 396

// ============ Flags ============
    int ExtensionFlags;// offset 400
    int TextureFlags;// offset 404
    int _FlagsPad0;// offset 408
    int _FlagsPad1;// offset 412

// ============ SpecularGlossiness (KHR_materials_pbrSpecularGlossiness) ============
// std140 布局中 vec3 需要 16 字节对齐，使用 vec4 保证正确对齐
    vec4 SpecularFactorSG;// offset 416, u_SpecularFactor (vec4, only xyz used)
    float GlossinessFactor;// offset 432, u_GlossinessFactor
    float _SGPad0;// offset 436
    float _SGPad1;// offset 440
    float _SGPad2;// offset 444
} Material;

// ============================================================================
// LightsData UBO - 多光源支持 (Binding Point 2)
// Total: 528 bytes (16 + 8 * 64)
// ============================================================================
const int LIGHT_COUNT = 8;
const int LightType_Directional = 0;
const int LightType_Point = 1;
const int LightType_Spot = 2;
#define LIGHT_TYPE_CONSTANTS_DEFINED

struct Light {
    vec3 direction;// offset 0
    float range;// offset 12
    vec3 color;// offset 16
    float intensity;// offset 28
    vec3 position;// offset 32
    float innerConeCos;// offset 44
    float outerConeCos;// offset 48
    int type;// offset 52
    float pad0;// offset 56
    float pad1;// offset 60
// Total: 64 bytes
};
#define LIGHT_STRUCT_DEFINED

layout(std140) uniform LightsData {
    int LightCount;// offset 0
    int pad0;// offset 4
    int pad1;// offset 8
    int pad2;// offset 12
    Light lights[LIGHT_COUNT];// offset 16
} lightsData;

// ============================================================================
// 便捷访问宏（兼容官方 uniform 名称）
// ============================================================================
// 官方 uniform 名称映射
#define u_Camera scene.CameraPos.xyz
#define u_Exposure scene.Exposure
#define u_EnvIntensity scene.EnvironmentStrength
#define u_MipCount scene.MipCount
#define u_MetallicFactor Material.MetallicFactor
#define u_RoughnessFactor Material.RoughnessFactor
#define u_BaseColorFactor Material.BaseColorFactor
#define u_EmissiveFactor Material.EmissiveFactor.xyz
#define u_NormalScale Material.NormalScale
#define u_OcclusionStrength Material.OcclusionStrength
#define u_AlphaCutoff Material.AlphaCutoff
#define u_Ior Material.Ior
#define u_EmissiveStrength Material.EmissiveStrength
#define u_ClearcoatFactor Material.ClearCoatFactor
#define u_ClearcoatRoughnessFactor Material.ClearCoatRoughness
#define u_ClearcoatNormalScale Material.ClearCoatNormalScale
#define u_SheenColorFactor Material.SheenColorFactor.xyz
#define u_SheenRoughnessFactor Material.SheenRoughnessFactor
#define u_KHR_materials_specular_specularFactor Material.SpecularFactor
#define u_KHR_materials_specular_specularColorFactor Material.SpecularColorFactor.xyz
#define u_TransmissionFactor Material.TransmissionFactor
#define u_ThicknessFactor Material.ThicknessFactor
#define u_AttenuationColor Material.AttenuationColor.xyz
#define u_AttenuationDistance Material.AttenuationDistance
#define u_IridescenceFactor Material.IridescenceFactor
#define u_IridescenceIor Material.IridescenceIor
#define u_IridescenceThicknessMinimum Material.IridescenceThicknessMin
#define u_IridescenceThicknessMaximum Material.IridescenceThicknessMax
#define u_Dispersion Material.Dispersion
#define u_DiffuseTransmissionFactor Material.DiffuseTransmissionFactor
#define u_DiffuseTransmissionColorFactor Material.DiffuseTransmissionColorFactor.xyz
#define u_Anisotropy Material.Anisotropy

// UV Set 访问
#define u_BaseColorUVSet Material.BaseColorUVSet
#define u_MetallicRoughnessUVSet Material.MetallicRoughnessUVSet
#define u_NormalUVSet Material.NormalUVSet
#define u_OcclusionUVSet Material.OcclusionUVSet
#define u_EmissiveUVSet Material.EmissiveUVSet
#define u_DiffuseUVSet Material.DiffuseUVSet
#define u_SpecularGlossinessUVSet Material.SpecularGlossinessUVSet
#define u_ClearcoatUVSet Material.ClearCoatUVSet
#define u_ClearcoatRoughnessUVSet Material.ClearCoatRoughnessUVSet
#define u_ClearcoatNormalUVSet Material.ClearCoatNormalUVSet
#define u_IridescenceUVSet Material.IridescenceUVSet
#define u_IridescenceThicknessUVSet Material.IridescenceThicknessUVSet
#define u_SheenColorUVSet Material.SheenColorUVSet
#define u_SheenRoughnessUVSet Material.SheenRoughnessUVSet
#define u_SpecularUVSet Material.SpecularUVSet
#define u_SpecularColorUVSet Material.SpecularColorUVSet
#define u_TransmissionUVSet Material.TransmissionUVSet
#define u_ThicknessUVSet Material.ThicknessUVSet
#define u_DiffuseTransmissionUVSet Material.DiffuseTransmissionUVSet
#define u_DiffuseTransmissionColorUVSet Material.DiffuseTransmissionColorUVSet
#define u_AnisotropyUVSet Material.AnisotropyUVSet

// Light access
#define u_Lights lightsData.lights
#define u_LightCount lightsData.LightCount
