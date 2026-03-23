using System.Collections.Generic;
using GltfMaterial = SharpGLTF.Schema2.Material;

namespace DotnetGltfRenderer {
    /// <summary>
    /// KHR_materials_ior 扩展
    /// 支持材质的折射率控制
    /// </summary>
    public class IorExtension : MaterialExtension {
        public override string ExtensionName => "KHR_materials_ior";

        /// <summary>
        /// 折射率 (默认 1.5，如玻璃)
        /// </summary>
        public float Ior { get; set; } = 1.5f;

        /// <summary>
        /// IOR 启用条件：非默认值
        /// </summary>
        public override bool IsEnabled => Ior != 1.5f;

        public override IEnumerable<MaterialTextureSlot> GetTextureSlots() {
            // IOR 没有纹理
            yield break;
        }

        public override void LoadFromGltf(GltfMaterial material, Model model) {
            // SharpGLTF 直接通过 Material.IndexOfRefraction 属性访问 IOR 值
            // 不使用 Channel API
            Ior = material.IndexOfRefraction;
        }

        public override void AppendDefines(ShaderDefines defines) {
            if (IsEnabled) {
                defines.AddMaterialExtension("IOR");
            }
        }
    }
}