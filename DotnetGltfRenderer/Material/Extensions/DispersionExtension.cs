using System.Collections.Generic;
using GltfMaterial = SharpGLTF.Schema2.Material;

namespace DotnetGltfRenderer {
    /// <summary>
    /// KHR_materials_dispersion 扩展
    /// 支持色散效果（棱镜分解光）
    /// </summary>
    public class DispersionExtension : MaterialExtension {
        public override string ExtensionName => "KHR_materials_dispersion";

        /// <summary>
        /// 色散强度（Abbe number 的倒数）
        /// </summary>
        public float Dispersion { get; set; }

        /// <summary>
        /// Dispersion 启用条件：色散值大于 0
        /// </summary>
        public override bool IsEnabled => Dispersion > 0f;

        public override IEnumerable<MaterialTextureSlot> GetTextureSlots() {
            // Dispersion 没有纹理
            yield break;
        }

        public override void LoadFromGltf(GltfMaterial material, Model model) {
            // SharpGLTF 直接通过 Material.Dispersion 属性访问
            // 不使用 Channel API
            Dispersion = material.Dispersion;
        }

        public override void AppendDefines(ShaderDefines defines) {
            if (IsEnabled) {
                defines.AddMaterialExtension("DISPERSION");
            }
        }
    }
}