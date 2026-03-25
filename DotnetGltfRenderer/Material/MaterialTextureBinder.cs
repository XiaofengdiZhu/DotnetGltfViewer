using System.Numerics;
using Silk.NET.OpenGLES;

namespace DotnetGltfRenderer {
    /// <summary>
    /// 材质纹理绑定器
    /// </summary>
    public static class MaterialTextureBinder {
        /// <summary>
        /// 绑定材质的所有纹理
        /// </summary>
        public static void BindMaterialTextures(GL gl, Material material, Shader shader) {
            if (material.BaseColorTexture != null) {
                BindTexture(gl, material.BaseColorTexture, MaterialTextureSlot.BaseColor);
            }
            if (material.MetallicRoughnessTexture != null) {
                BindTexture(gl, material.MetallicRoughnessTexture, MaterialTextureSlot.MetallicRoughness);
            }
            if (material.NormalTexture != null) {
                BindTexture(gl, material.NormalTexture, MaterialTextureSlot.Normal);
            }
            if (material.OcclusionTexture != null) {
                BindTexture(gl, material.OcclusionTexture, MaterialTextureSlot.Occlusion);
            }
            if (material.EmissiveTexture != null) {
                BindTexture(gl, material.EmissiveTexture, MaterialTextureSlot.Emissive);
            }

            // 扩展纹理
            if (material.ClearCoat?.IsEnabled == true) {
                BindTexture(gl, material.ClearCoat.Texture, MaterialTextureSlot.ClearCoat);
                BindTexture(gl, material.ClearCoat.RoughnessTexture, MaterialTextureSlot.ClearCoatRoughness);
                BindTexture(gl, material.ClearCoat.NormalTexture, MaterialTextureSlot.ClearCoatNormal);
            }
            if (material.Iridescence?.IsEnabled == true) {
                BindTexture(gl, material.Iridescence.Texture, MaterialTextureSlot.Iridescence);
                BindTexture(gl, material.Iridescence.ThicknessTexture, MaterialTextureSlot.IridescenceThickness);
            }
            if (material.Transmission?.IsEnabled == true) {
                BindTexture(gl, material.Transmission.Texture, MaterialTextureSlot.Transmission);
            }
            if (material.Volume?.IsEnabled == true) {
                BindTexture(gl, material.Volume.ThicknessTexture, MaterialTextureSlot.Thickness);
            }
            if (material.Sheen?.IsEnabled == true) {
                BindTexture(gl, material.Sheen.ColorTexture, MaterialTextureSlot.SheenColor);
                BindTexture(gl, material.Sheen.RoughnessTexture, MaterialTextureSlot.SheenRoughness);
            }
            if (material.Specular?.IsEnabled == true) {
                BindTexture(gl, material.Specular.SpecularTexture, MaterialTextureSlot.Specular);
                BindTexture(gl, material.Specular.SpecularColorTexture, MaterialTextureSlot.SpecularColor);
            }
            if (material.Anisotropy?.IsEnabled == true) {
                BindTexture(gl, material.Anisotropy.AnisotropyTexture, MaterialTextureSlot.Anisotropy);
            }
            if (material.DiffuseTransmission?.IsEnabled == true) {
                BindTexture(gl, material.DiffuseTransmission.Texture, MaterialTextureSlot.DiffuseTransmission);
                BindTexture(gl, material.DiffuseTransmission.ColorTexture, MaterialTextureSlot.DiffuseTransmissionColor);
            }

            // SpecularGlossiness workflow textures
            if (material.SpecularGlossiness?.IsEnabled == true) {
                BindTexture(gl, material.SpecularGlossiness.DiffuseTexture, MaterialTextureSlot.Diffuse);
                BindTexture(gl, material.SpecularGlossiness.SpecularGlossinessTexture, MaterialTextureSlot.SpecularGlossiness);
            }
        }

        /// <summary>
        /// 设置纹理槽 uniform
        /// </summary>
        public static void SetTextureSlotUniforms(Shader shader) {
            // 官方着色器使用 *Sampler 命名
            shader.SetUniform("u_BaseColorSampler", (int)MaterialTextureSlot.BaseColor);
            shader.SetUniform("u_MetallicRoughnessSampler", (int)MaterialTextureSlot.MetallicRoughness);
            shader.SetUniform("u_NormalSampler", (int)MaterialTextureSlot.Normal);
            shader.SetUniform("u_OcclusionSampler", (int)MaterialTextureSlot.Occlusion);
            shader.SetUniform("u_EmissiveSampler", (int)MaterialTextureSlot.Emissive);

            // Clearcoat samplers
            shader.SetUniform("u_ClearcoatSampler", (int)MaterialTextureSlot.ClearCoat);
            shader.SetUniform("u_ClearcoatRoughnessSampler", (int)MaterialTextureSlot.ClearCoatRoughness);
            shader.SetUniform("u_ClearcoatNormalSampler", (int)MaterialTextureSlot.ClearCoatNormal);

            // Iridescence samplers
            shader.SetUniform("u_IridescenceSampler", (int)MaterialTextureSlot.Iridescence);
            shader.SetUniform("u_IridescenceThicknessSampler", (int)MaterialTextureSlot.IridescenceThickness);

            // Transmission sampler
            shader.SetUniform("u_TransmissionSampler", (int)MaterialTextureSlot.Transmission);

            // Volume/Thickness sampler
            shader.SetUniform("u_ThicknessSampler", (int)MaterialTextureSlot.Thickness);

            // Sheen samplers
            shader.SetUniform("u_SheenColorSampler", (int)MaterialTextureSlot.SheenColor);
            shader.SetUniform("u_SheenRoughnessSampler", (int)MaterialTextureSlot.SheenRoughness);

            // Specular samplers
            shader.SetUniform("u_SpecularSampler", (int)MaterialTextureSlot.Specular);
            shader.SetUniform("u_SpecularColorSampler", (int)MaterialTextureSlot.SpecularColor);

            // Anisotropy sampler
            shader.SetUniform("u_AnisotropySampler", (int)MaterialTextureSlot.Anisotropy);

            // SpecularGlossiness samplers (KHR_materials_pbrSpecularGlossiness)
            shader.SetUniform("u_DiffuseSampler", (int)MaterialTextureSlot.Diffuse);
            shader.SetUniform("u_SpecularGlossinessSampler", (int)MaterialTextureSlot.SpecularGlossiness);

            // Diffuse Transmission samplers
            shader.SetUniform("u_DiffuseTransmissionSampler", (int)MaterialTextureSlot.DiffuseTransmission);
            shader.SetUniform("u_DiffuseTransmissionColorSampler", (int)MaterialTextureSlot.DiffuseTransmissionColor);

            // IBL Samplers
            shader.SetUniform("u_LambertianEnvSampler", (int)MaterialTextureSlot.IBLLambertian);
            shader.SetUniform("u_GGXEnvSampler", (int)MaterialTextureSlot.IBLGGX);
            shader.SetUniform("u_CharlieEnvSampler", (int)MaterialTextureSlot.IBLCharlie);
            shader.SetUniform("u_GGXLUT", (int)MaterialTextureSlot.IBLGGXLUT);
            shader.SetUniform("u_CharlieLUT", (int)MaterialTextureSlot.IBLCharlieLUT);

            // Scatter Samplers (VolumeScatter)
            shader.SetUniform("u_ScatterFramebufferSampler", (int)MaterialTextureSlot.ScatterFramebuffer);
            shader.SetUniform("u_ScatterDepthFramebufferSampler", (int)MaterialTextureSlot.ScatterDepthFramebuffer);

            // Morph Target Sampler
            shader.SetUniform("u_MorphTargetsSampler", (int)MaterialTextureSlot.MorphTargets);
        }

        /// <summary>
        /// 设置 UV 变换
        /// </summary>
        public static void SetUVTransforms(Material material, Shader shader) {
            // Core textures UV transforms
            SetUVTransform(material.BaseColorTexture, shader, "u_BaseColorUVTransform");
            SetUVTransform(material.NormalTexture, shader, "u_NormalUVTransform");
            SetUVTransform(material.MetallicRoughnessTexture, shader, "u_MetallicRoughnessUVTransform");
            SetUVTransform(material.OcclusionTexture, shader, "u_OcclusionUVTransform");
            SetUVTransform(material.EmissiveTexture, shader, "u_EmissiveUVTransform");

            // Extension textures UV transforms
            if (material.ClearCoat?.IsEnabled == true) {
                SetUVTransform(material.ClearCoat.Texture, shader, "u_ClearcoatUVTransform");
                SetUVTransform(material.ClearCoat.RoughnessTexture, shader, "u_ClearcoatRoughnessUVTransform");
                SetUVTransform(material.ClearCoat.NormalTexture, shader, "u_ClearcoatNormalUVTransform");
            }
            if (material.Iridescence?.IsEnabled == true) {
                SetUVTransform(material.Iridescence.Texture, shader, "u_IridescenceUVTransform");
                SetUVTransform(material.Iridescence.ThicknessTexture, shader, "u_IridescenceThicknessUVTransform");
            }
            if (material.Transmission?.IsEnabled == true) {
                SetUVTransform(material.Transmission.Texture, shader, "u_TransmissionUVTransform");
            }
            if (material.Volume?.IsEnabled == true) {
                SetUVTransform(material.Volume.ThicknessTexture, shader, "u_ThicknessUVTransform");
            }
            if (material.Sheen?.IsEnabled == true) {
                SetUVTransform(material.Sheen.ColorTexture, shader, "u_SheenColorUVTransform");
                SetUVTransform(material.Sheen.RoughnessTexture, shader, "u_SheenRoughnessUVTransform");
            }
            if (material.Specular?.IsEnabled == true) {
                SetUVTransform(material.Specular.SpecularTexture, shader, "u_SpecularUVTransform");
                SetUVTransform(material.Specular.SpecularColorTexture, shader, "u_SpecularColorUVTransform");
            }
            if (material.Anisotropy?.IsEnabled == true) {
                SetUVTransform(material.Anisotropy.AnisotropyTexture, shader, "u_AnisotropyUVTransform");
            }
            if (material.DiffuseTransmission?.IsEnabled == true) {
                SetUVTransform(material.DiffuseTransmission.Texture, shader, "u_DiffuseTransmissionUVTransform");
                SetUVTransform(material.DiffuseTransmission.ColorTexture, shader, "u_DiffuseTransmissionColorUVTransform");
            }

            // SpecularGlossiness UV transforms
            if (material.SpecularGlossiness?.IsEnabled == true) {
                SetUVTransform(material.SpecularGlossiness.DiffuseTexture, shader, "u_DiffuseUVTransform");
                SetUVTransform(material.SpecularGlossiness.SpecularGlossinessTexture, shader, "u_SpecularGlossinessUVTransform");
            }
        }

        /// <summary>
        /// 绑定 IBL 纹理
        /// </summary>
        public static void BindIBLTextures(GL gl, IblSampler iblSampler) {
            BindCubemapTexture(gl, iblSampler.LambertianTexture, MaterialTextureSlot.IBLLambertian);
            BindCubemapTexture(gl, iblSampler.GGXTexture, MaterialTextureSlot.IBLGGX);
            BindCubemapTexture(gl, iblSampler.SheenTexture, MaterialTextureSlot.IBLCharlie);
            BindTexture2D(gl, iblSampler.GGXLut, MaterialTextureSlot.IBLGGXLUT);
            BindTexture2D(gl, iblSampler.CharlieLut, MaterialTextureSlot.IBLCharlieLUT);
        }

        /// <summary>
        /// 绑定单个纹理
        /// </summary>
        public static void BindTexture(GL gl, MaterialTexture matTex, MaterialTextureSlot slot) {
            if (matTex?.Texture != null) {
                matTex.Texture.Bind((TextureUnit)((int)TextureUnit.Texture0 + (int)slot));
            }
        }

        /// <summary>
        /// 绑定 Cubemap 纹理
        /// </summary>
        public static void BindCubemapTexture(GL gl, uint texture, MaterialTextureSlot slot) {
            gl.ActiveTexture((TextureUnit)((int)TextureUnit.Texture0 + (int)slot));
            gl.BindTexture(TextureTarget.TextureCubeMap, texture);
        }

        /// <summary>
        /// 绑定 2D 纹理
        /// </summary>
        public static void BindTexture2D(GL gl, uint texture, MaterialTextureSlot slot) {
            gl.ActiveTexture((TextureUnit)((int)TextureUnit.Texture0 + (int)slot));
            gl.BindTexture(TextureTarget.Texture2D, texture);
        }

        /// <summary>
        /// 设置单个 UV 变换
        /// </summary>
        static void SetUVTransform(MaterialTexture matTex, Shader shader, string uniformName) {
            if (matTex?.HasUVTransform != true) {
                return;
            }

            // Convert Matrix3x2 to mat3 (3x3 matrix for UV transform)
            //
            // Matrix3x2 stores the 2D affine transform as:
            // | M11 M12 |
            // | M21 M22 |
            // | M31 M32 |  (M31, M32 = translation)
            //
            // This corresponds to a 3x3 matrix (row-major):
            // | M11 M12 0  |
            // | M21 M22 0  |
            // | M31 M32 1  |
            //
            // For UV transform, we need the translation in the 3rd column (column-major):
            // | M11 M21 0  |     | sx*cos  -sx*sin  0 |
            // | M12 M22 0  | --> | sy*sin   sy*cos  0 |
            // | M31 M32 1  |     | tx       ty      1 |
            //
            // But GLSL mat3 * vec3 expects translation in 3rd column:
            // | sx*cos  -sx*sin  tx |
            // | sy*sin   sy*cos  ty |
            // | 0        0       1  |
            //
            // So we need to construct the matrix differently:
            // Column 0: [M11, M21, 0]    (scale*rotation part 1)
            // Column 1: [M12, M22, 0]    (scale*rotation part 2)
            // Column 2: [M31, M32, 1]   (translation)
            shader.SetUniformMatrix3(
                uniformName,
                new Vector3(matTex.UVTransform.M11, matTex.UVTransform.M21, 0f),
                new Vector3(matTex.UVTransform.M12, matTex.UVTransform.M22, 0f),
                new Vector3(matTex.UVTransform.M31, matTex.UVTransform.M32, 1f)
            );
        }
    }
}
