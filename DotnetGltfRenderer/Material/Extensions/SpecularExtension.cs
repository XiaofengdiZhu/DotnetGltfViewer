using System.Collections.Generic;
using System.Numerics;
using SharpGLTF.Schema2;
using GltfMaterial = SharpGLTF.Schema2.Material;

namespace DotnetGltfRenderer {
    /// <summary>
    /// KHR_materials_specular 扩展
    /// 支持镜面反射强度和颜色控制
    /// </summary>
    public class SpecularExtension : MaterialExtension {
        public override string ExtensionName => "KHR_materials_specular";

        /// <summary>
        /// 镜面因子 (0-1)
        /// </summary>
        public float SpecularFactor { get; set; } = 1f;

        /// <summary>
        /// 镜面颜色因子
        /// </summary>
        public Vector3 SpecularColorFactor { get; set; } = Vector3.One;

        /// <summary>
        /// 镜面强度纹理
        /// </summary>
        public MaterialTexture SpecularTexture { get; set; }

        /// <summary>
        /// 镜面颜色纹理
        /// </summary>
        public MaterialTexture SpecularColorTexture { get; set; }

        /// <summary>
        /// Specular 启用条件：因子不为默认值
        /// </summary>
        public override bool IsEnabled => SpecularFactor != 1f || SpecularColorFactor != Vector3.One;

        public override IEnumerable<MaterialTextureSlot> GetTextureSlots() {
            if (SpecularTexture != null) {
                yield return MaterialTextureSlot.Specular;
            }
            if (SpecularColorTexture != null) {
                yield return MaterialTextureSlot.SpecularColor;
            }
        }

        public override void LoadFromGltf(GltfMaterial material, Model model) {
            // SharpGLTF 使用 "SpecularFactor" 作为 KHR_materials_specular 的因子通道名
            MaterialChannel? channel = material.FindChannel("SpecularFactor");
            if (channel != null) {
                SpecularFactor = GetChannelFactor(channel, "SpecularFactor", 1f);
                SpecularTexture = LoadTextureFromChannel(model, channel);
            }
            channel = material.FindChannel("SpecularColor");
            if (channel != null) {
                Vector4 color = channel.Value.Color;
                SpecularColorFactor = new Vector3(color.X, color.Y, color.Z);
                SpecularColorTexture = LoadTextureFromChannel(model, channel);
            }
        }

        public override void AppendDefines(ShaderDefines defines) {
            if (IsEnabled) {
                defines.AddMaterialExtension("SPECULAR");
                if (SpecularTexture != null) {
                    defines.AddTextureMap("SPECULAR");
                    if (SpecularTexture.HasUVTransform) {
                        defines.AddUVTransform("SPECULAR");
                    }
                }
                if (SpecularColorTexture != null) {
                    defines.AddTextureMap("SPECULAR_COLOR");
                    if (SpecularColorTexture.HasUVTransform) {
                        defines.AddUVTransform("SPECULARCOLOR");
                    }
                }
            }
        }
    }
}