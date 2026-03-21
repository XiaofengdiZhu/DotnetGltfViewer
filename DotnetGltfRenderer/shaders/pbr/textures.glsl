// shaders/pbr/textures.glsl
// UBO 版本 - 纹理采样器和 UV 函数
// 注意：factor、UVSet 等参数已移至 ubos.glsl 的 MaterialData/SceneData UBO 中

#ifndef TEXTURES_GLSL
#define TEXTURES_GLSL

// ============================================================================
// IBL 纹理采样器
// 注意：u_MipCount 已移至 ubos.glsl 的 SceneData 中
// ============================================================================
uniform samplerCube u_LambertianEnvSampler;
uniform samplerCube u_GGXEnvSampler;
uniform sampler2D u_GGXLUT;
uniform samplerCube u_CharlieEnvSampler;
uniform sampler2D u_CharlieLUT;
uniform sampler2D u_SheenELUT;
uniform mat3 u_EnvRotation;


// General Material


uniform sampler2D u_NormalSampler;
// u_NormalScale - 在 ubos.glsl MaterialData 中
// u_NormalUVSet - 在 ubos.glsl MaterialData 中（通过宏访问）
uniform mat3 u_NormalUVTransform;

// u_EmissiveFactor - 在 ubos.glsl MaterialData 中
uniform sampler2D u_EmissiveSampler;
// u_EmissiveUVSet - 在 ubos.glsl MaterialData 中（通过宏访问）
uniform mat3 u_EmissiveUVTransform;

uniform sampler2D u_OcclusionSampler;
// u_OcclusionUVSet - 在 ubos.glsl MaterialData 中（通过宏访问）
// u_OcclusionStrength - 在 ubos.glsl MaterialData 中
uniform mat3 u_OcclusionUVTransform;


in vec2 v_texcoord_0;
in vec2 v_texcoord_1;


vec2 getNormalUV()
{
    vec3 uv = vec3(u_NormalUVSet < 1 ? v_texcoord_0 : v_texcoord_1, 1.0);

    #ifdef HAS_NORMAL_UV_TRANSFORM
    uv = u_NormalUVTransform * uv;
    #endif

    return uv.xy;
}


vec2 getEmissiveUV()
{
    vec3 uv = vec3(u_EmissiveUVSet < 1 ? v_texcoord_0 : v_texcoord_1, 1.0);

    #ifdef HAS_EMISSIVE_UV_TRANSFORM
    uv = u_EmissiveUVTransform * uv;
    #endif

    return uv.xy;
}


vec2 getOcclusionUV()
{
    vec3 uv = vec3(u_OcclusionUVSet < 1 ? v_texcoord_0 : v_texcoord_1, 1.0);

    #ifdef HAS_OCCLUSION_UV_TRANSFORM
    uv = u_OcclusionUVTransform * uv;
    #endif

    return uv.xy;
}


// Metallic Roughness Material


#ifdef MATERIAL_METALLICROUGHNESS

uniform sampler2D u_BaseColorSampler;
// u_BaseColorUVSet - 在 ubos.glsl MaterialData 中（通过宏访问）
uniform mat3 u_BaseColorUVTransform;

uniform sampler2D u_MetallicRoughnessSampler;
// u_MetallicRoughnessUVSet - 在 ubos.glsl MaterialData 中（通过宏访问）
uniform mat3 u_MetallicRoughnessUVTransform;

vec2 getBaseColorUV()
{
    vec3 uv = vec3(u_BaseColorUVSet < 1 ? v_texcoord_0 : v_texcoord_1, 1.0);

    #ifdef HAS_BASECOLOR_UV_TRANSFORM
    uv = u_BaseColorUVTransform * uv;
    #endif

    return uv.xy;
}

vec2 getMetallicRoughnessUV()
{
    vec3 uv = vec3(u_MetallicRoughnessUVSet < 1 ? v_texcoord_0 : v_texcoord_1, 1.0);

    #ifdef HAS_METALLICROUGHNESS_UV_TRANSFORM
    uv = u_MetallicRoughnessUVTransform * uv;
    #endif

    return uv.xy;
}

#endif


// Specular Glossiness Material


#ifdef MATERIAL_SPECULARGLOSSINESS

uniform sampler2D u_DiffuseSampler;
// u_DiffuseUVSet - 在 ubos.glsl MaterialData 中（通过宏访问）
uniform mat3 u_DiffuseUVTransform;

uniform sampler2D u_SpecularGlossinessSampler;
// u_SpecularGlossinessUVSet - 在 ubos.glsl MaterialData 中（通过宏访问）
uniform mat3 u_SpecularGlossinessUVTransform;


vec2 getSpecularGlossinessUV()
{
    vec3 uv = vec3(u_SpecularGlossinessUVSet < 1 ? v_texcoord_0 : v_texcoord_1, 1.0);

    #ifdef HAS_SPECULARGLOSSINESS_UV_TRANSFORM
    uv = u_SpecularGlossinessUVTransform * uv;
    #endif

    return uv.xy;
}

vec2 getDiffuseUV()
{
    vec3 uv = vec3(u_DiffuseUVSet < 1 ? v_texcoord_0 : v_texcoord_1, 1.0);

    #ifdef HAS_DIFFUSE_UV_TRANSFORM
    uv = u_DiffuseUVTransform * uv;
    #endif

    return uv.xy;
}

#endif


// Clearcoat Material


#ifdef MATERIAL_CLEARCOAT

uniform sampler2D u_ClearcoatSampler;
// u_ClearcoatUVSet - 在 ubos.glsl MaterialData 中（通过宏访问）
uniform mat3 u_ClearcoatUVTransform;

uniform sampler2D u_ClearcoatRoughnessSampler;
// u_ClearcoatRoughnessUVSet - 在 ubos.glsl MaterialData 中（通过宏访问）
uniform mat3 u_ClearcoatRoughnessUVTransform;

uniform sampler2D u_ClearcoatNormalSampler;
// u_ClearcoatNormalUVSet - 在 ubos.glsl MaterialData 中（通过宏访问）
uniform mat3 u_ClearcoatNormalUVTransform;
// u_ClearcoatNormalScale - 在 ubos.glsl MaterialData 中


vec2 getClearcoatUV()
{
    vec3 uv = vec3(u_ClearcoatUVSet < 1 ? v_texcoord_0 : v_texcoord_1, 1.0);
    #ifdef HAS_CLEARCOAT_UV_TRANSFORM
    uv = u_ClearcoatUVTransform * uv;
    #endif
    return uv.xy;
}

vec2 getClearcoatRoughnessUV()
{
    vec3 uv = vec3(u_ClearcoatRoughnessUVSet < 1 ? v_texcoord_0 : v_texcoord_1, 1.0);
    #ifdef HAS_CLEARCOATROUGHNESS_UV_TRANSFORM
    uv = u_ClearcoatRoughnessUVTransform * uv;
    #endif
    return uv.xy;
}

vec2 getClearcoatNormalUV()
{
    vec3 uv = vec3(u_ClearcoatNormalUVSet < 1 ? v_texcoord_0 : v_texcoord_1, 1.0);
    #ifdef HAS_CLEARCOATNORMAL_UV_TRANSFORM
    uv = u_ClearcoatNormalUVTransform * uv;
    #endif
    return uv.xy;
}

#endif


// Sheen Material


#ifdef MATERIAL_SHEEN

uniform sampler2D u_SheenColorSampler;
// u_SheenColorUVSet - 在 ubos.glsl MaterialData 中（通过宏访问）
uniform mat3 u_SheenColorUVTransform;
uniform sampler2D u_SheenRoughnessSampler;
// u_SheenRoughnessUVSet - 在 ubos.glsl MaterialData 中（通过宏访问）
uniform mat3 u_SheenRoughnessUVTransform;


vec2 getSheenColorUV()
{
    vec3 uv = vec3(u_SheenColorUVSet < 1 ? v_texcoord_0 : v_texcoord_1, 1.0);
    #ifdef HAS_SHEENCOLOR_UV_TRANSFORM
    uv = u_SheenColorUVTransform * uv;
    #endif
    return uv.xy;
}

vec2 getSheenRoughnessUV()
{
    vec3 uv = vec3(u_SheenRoughnessUVSet < 1 ? v_texcoord_0 : v_texcoord_1, 1.0);
    #ifdef HAS_SHEENROUGHNESS_UV_TRANSFORM
    uv = u_SheenRoughnessUVTransform * uv;
    #endif
    return uv.xy;
}

#endif


// Specular Material


#ifdef MATERIAL_SPECULAR

uniform sampler2D u_SpecularSampler;
// u_SpecularUVSet - 在 ubos.glsl MaterialData 中（通过宏访问）
uniform mat3 u_SpecularUVTransform;
uniform sampler2D u_SpecularColorSampler;
// u_SpecularColorUVSet - 在 ubos.glsl MaterialData 中（通过宏访问）
uniform mat3 u_SpecularColorUVTransform;


vec2 getSpecularUV()
{
    vec3 uv = vec3(u_SpecularUVSet < 1 ? v_texcoord_0 : v_texcoord_1, 1.0);
    #ifdef HAS_SPECULAR_UV_TRANSFORM
    uv = u_SpecularUVTransform * uv;
    #endif
    return uv.xy;
}

vec2 getSpecularColorUV()
{
    vec3 uv = vec3(u_SpecularColorUVSet < 1 ? v_texcoord_0 : v_texcoord_1, 1.0);
    #ifdef HAS_SPECULARCOLOR_UV_TRANSFORM
    uv = u_SpecularColorUVTransform * uv;
    #endif
    return uv.xy;
}

#endif


// Transmission Material


#ifdef MATERIAL_TRANSMISSION

uniform sampler2D u_TransmissionSampler;
// u_TransmissionUVSet - 在 ubos.glsl MaterialData 中（通过宏访问）
uniform mat3 u_TransmissionUVTransform;
uniform sampler2D u_TransmissionFramebufferSampler;
uniform ivec2 u_TransmissionFramebufferSize;


vec2 getTransmissionUV()
{
    vec3 uv = vec3(u_TransmissionUVSet < 1 ? v_texcoord_0 : v_texcoord_1, 1.0);
    #ifdef HAS_TRANSMISSION_UV_TRANSFORM
    uv = u_TransmissionUVTransform * uv;
    #endif
    return uv.xy;
}

#endif


// Volume Material


#ifdef MATERIAL_VOLUME

uniform sampler2D u_ThicknessSampler;
// u_ThicknessUVSet - 在 ubos.glsl MaterialData 中（通过宏访问）
uniform mat3 u_ThicknessUVTransform;


vec2 getThicknessUV()
{
    vec3 uv = vec3(u_ThicknessUVSet < 1 ? v_texcoord_0 : v_texcoord_1, 1.0);
    #ifdef HAS_THICKNESS_UV_TRANSFORM
    uv = u_ThicknessUVTransform * uv;
    #endif
    return uv.xy;
}

#endif


// Volume Scatter

#ifdef MATERIAL_VOLUME_SCATTER
uniform sampler2D u_ScatterFramebufferSampler;
uniform sampler2D u_ScatterDepthFramebufferSampler;
#endif


// Iridescence


#ifdef MATERIAL_IRIDESCENCE

uniform sampler2D u_IridescenceSampler;
// u_IridescenceUVSet - 在 ubos.glsl MaterialData 中（通过宏访问）
uniform mat3 u_IridescenceUVTransform;

uniform sampler2D u_IridescenceThicknessSampler;
// u_IridescenceThicknessUVSet - 在 ubos.glsl MaterialData 中（通过宏访问）
uniform mat3 u_IridescenceThicknessUVTransform;


vec2 getIridescenceUV()
{
    vec3 uv = vec3(u_IridescenceUVSet < 1 ? v_texcoord_0 : v_texcoord_1, 1.0);
    #ifdef HAS_IRIDESCENCE_UV_TRANSFORM
    uv = u_IridescenceUVTransform * uv;
    #endif
    return uv.xy;
}

vec2 getIridescenceThicknessUV()
{
    vec3 uv = vec3(u_IridescenceThicknessUVSet < 1 ? v_texcoord_0 : v_texcoord_1, 1.0);
    #ifdef HAS_IRIDESCENCETHICKNESS_UV_TRANSFORM
    uv = u_IridescenceThicknessUVTransform * uv;
    #endif
    return uv.xy;
}

#endif


// Diffuse Transmission

#ifdef MATERIAL_DIFFUSE_TRANSMISSION

uniform sampler2D u_DiffuseTransmissionSampler;
// u_DiffuseTransmissionUVSet - 在 ubos.glsl MaterialData 中（通过宏访问）
uniform mat3 u_DiffuseTransmissionUVTransform;

uniform sampler2D u_DiffuseTransmissionColorSampler;
// u_DiffuseTransmissionColorUVSet - 在 ubos.glsl MaterialData 中（通过宏访问）
uniform mat3 u_DiffuseTransmissionColorUVTransform;


vec2 getDiffuseTransmissionUV()
{
    vec3 uv = vec3(u_DiffuseTransmissionUVSet < 1 ? v_texcoord_0 : v_texcoord_1, 1.0);
    #ifdef HAS_DIFFUSETRANSMISSION_UV_TRANSFORM
    uv = u_DiffuseTransmissionUVTransform * uv;
    #endif
    return uv.xy;
}

vec2 getDiffuseTransmissionColorUV()
{
    vec3 uv = vec3(u_DiffuseTransmissionColorUVSet < 1 ? v_texcoord_0 : v_texcoord_1, 1.0);
    #ifdef HAS_DIFFUSETRANSMISSIONCOLOR_UV_TRANSFORM
    uv = u_DiffuseTransmissionColorUVTransform * uv;
    #endif
    return uv.xy;
}

#endif

// Anisotropy

#ifdef MATERIAL_ANISOTROPY

uniform sampler2D u_AnisotropySampler;
// u_AnisotropyUVSet - 在 ubos.glsl MaterialData 中（通过宏访问）
uniform mat3 u_AnisotropyUVTransform;

vec2 getAnisotropyUV()
{
    vec3 uv = vec3(u_AnisotropyUVSet < 1 ? v_texcoord_0 : v_texcoord_1, 1.0);
    #ifdef HAS_ANISOTROPY_UV_TRANSFORM
    uv = u_AnisotropyUVTransform * uv;
    #endif
    return uv.xy;
}

#endif

#endif// TEXTURES_GLSL
