using System.Collections.Generic;
using System.Numerics;
using SharpGLTF.Schema2;
using GltfMaterial = SharpGLTF.Schema2.Material;

namespace DotnetGltfRenderer {
    /// <summary>
    /// KHR_materials_pbrSpecularGlossiness 扩展
    /// 旧版 PBR 工作流，使用 Specular-Glossiness 替代 Metallic-Roughness
    /// </summary>
    public class SpecularGlossinessExtension : MaterialExtension {
        public override string ExtensionName => "KHR_materials_pbrSpecularGlossiness";

        /// <summary>
        /// 漫反射颜色因子 (RGBA)
        /// </summary>
        public Vector4 DiffuseFactor { get; set; } = Vector4.One;

        /// <summary>
        /// 高光颜色因子 (RGB)
        /// </summary>
        public Vector3 SpecularFactor { get; set; } = Vector3.One;

        /// <summary>
        /// 光泽度因子 (0-1)
        /// </summary>
        public float GlossinessFactor { get; set; } = 1f;

        /// <summary>
        /// 漫反射纹理
        /// </summary>
        public MaterialTexture DiffuseTexture { get; set; }

        /// <summary>
        /// 高光光泽度纹理 (RGB=Specular, A=Glossiness)
        /// </summary>
        public MaterialTexture SpecularGlossinessTexture { get; set; }

        /// <summary>
        /// 只有在材质实际使用了 SpecularGlossiness 扩展时才启用
        /// </summary>
        public override bool IsEnabled => _isEnabled;

        bool _isEnabled;

        public override IEnumerable<MaterialTextureSlot> GetTextureSlots() {
            if (DiffuseTexture != null) {
                yield return MaterialTextureSlot.Diffuse;
            }
            if (SpecularGlossinessTexture != null) {
                yield return MaterialTextureSlot.SpecularGlossiness;
            }
        }

        public override void LoadFromGltf(GltfMaterial material, Model model) {
            // 通过 SharpGLTF 的 channel API 获取数据
            MaterialChannel? diffuseChannel = material.FindChannel("Diffuse");
            if (diffuseChannel != null) {
                DiffuseFactor = diffuseChannel.Value.Color;
                DiffuseTexture = LoadTextureFromChannel(model, diffuseChannel);
                _isEnabled = true;
            }
            MaterialChannel? sgChannel = material.FindChannel("SpecularGlossiness");
            if (sgChannel != null) {
                // 遍历参数获取 SpecularFactor (Vector3) 和 GlossinessFactor (float)
                foreach (IMaterialParameter param in sgChannel.Value.Parameters) {
                    if (param.Name == "SpecularFactor"
                        && param.ValueType == typeof(Vector3)) {
                        SpecularFactor = (Vector3)param.Value;
                    }
                    else if (param.Name == "GlossinessFactor"
                        && param.ValueType == typeof(float)) {
                        GlossinessFactor = (float)param.Value;
                    }
                }
                SpecularGlossinessTexture = LoadTextureFromChannel(model, sgChannel);
                _isEnabled = true;
            }
        }

        public override void AppendDefines(ShaderDefines defines) {
            defines.Add("MATERIAL_SPECULARGLOSSINESS");
            if (DiffuseTexture != null) {
                defines.AddTextureMap("DIFFUSE");
                if (DiffuseTexture.HasUVTransform) {
                    defines.AddUVTransform("DIFFUSE");
                }
            }
            if (SpecularGlossinessTexture != null) {
                defines.AddTextureMap("SPECULAR_GLOSSINESS");
                if (SpecularGlossinessTexture.HasUVTransform) {
                    defines.AddUVTransform("SPECULARGLOSSINESS");
                }
            }
        }
    }
}