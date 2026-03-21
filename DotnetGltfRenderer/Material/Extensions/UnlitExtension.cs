using System.Collections.Generic;
using GltfMaterial = SharpGLTF.Schema2.Material;

namespace DotnetGltfRenderer {
    /// <summary>
    /// KHR_materials_unlit 扩展
    /// 不进行光照计算，直接输出基础颜色
    /// </summary>
    public class UnlitExtension : MaterialExtension {
        public override string ExtensionName => "KHR_materials_unlit";

        bool _isEnabled;

        /// <summary>
        /// 只有当材质实际有 KHR_materials_unlit 扩展时才启用
        /// </summary>
        public override bool IsEnabled => _isEnabled;

        public override IEnumerable<MaterialTextureSlot> GetTextureSlots() {
            // Unlit 使用基础颜色纹理，没有额外的纹理槽
            yield break;
        }

        public override void LoadFromGltf(GltfMaterial material, Model model) {
            // 检查材质是否实际有 KHR_materials_unlit 扩展
            // SharpGLTF 的 material.Unlit 属性在材质有此扩展时返回 true
            _isEnabled = material.Unlit;
        }

        public override void AppendDefines(ShaderDefines defines) {
            if (IsEnabled) {
                defines.AddMaterialExtension("UNLIT");
            }
        }
    }
}