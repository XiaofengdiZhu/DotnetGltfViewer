using System.Collections.Generic;
using System.Numerics;
using SharpGLTF.Schema2;
using GltfMaterial = SharpGLTF.Schema2.Material;

namespace DotnetGltfRenderer {
    /// <summary>
    /// KHR_materials_volume 扩展
    /// 支持体积吸收、厚度控制
    /// </summary>
    public class VolumeExtension : MaterialExtension {
        public override string ExtensionName => "KHR_materials_volume";

        /// <summary>
        /// 厚度因子
        /// </summary>
        public float ThicknessFactor { get; set; }

        /// <summary>
        /// 厚度纹理
        /// </summary>
        public MaterialTexture ThicknessTexture { get; set; }

        /// <summary>
        /// 衰减距离（0 或 float.MaxValue 表示无限，不衰减）
        /// 官方规范默认值是无限大
        /// </summary>
        public float AttenuationDistance { get; set; } = float.MaxValue;

        /// <summary>
        /// 衰减颜色
        /// </summary>
        public Vector3 AttenuationColor { get; set; } = Vector3.One;

        /// <summary>
        /// 标记扩展是否被加载（从 glTF 加载成功后为 true）
        /// 官方渲染器：只要扩展存在就启用，不管 thicknessFactor 值
        /// </summary>
        public bool IsLoaded { get; private set; }

        /// <summary>
        /// 只要扩展被加载就认为是启用的（与官方渲染器一致）
        /// </summary>
        public override bool IsEnabled => IsLoaded;

        public override IEnumerable<MaterialTextureSlot> GetTextureSlots() {
            if (ThicknessTexture != null) {
                yield return MaterialTextureSlot.Thickness;
            }
        }

        public override void LoadFromGltf(GltfMaterial material, Model model) {
            // SharpGLTF 使用两个不同的 channel:
            // - "VolumeThickness" 包含 thicknessFactor 和 thicknessTexture
            // - "VolumeAttenuation" 包含 attenuationColor 和 attenuationDistance

            // 检查是否有 Volume 扩展
            MaterialChannel? thicknessChannel = material.FindChannel("VolumeThickness");
            MaterialChannel? attenuationChannel = material.FindChannel("VolumeAttenuation");
            if (thicknessChannel != null
                || attenuationChannel != null) {
                // 标记扩展已加载（官方渲染器：只要扩展存在就启用）
                IsLoaded = true;

                // 从 VolumeThickness channel 读取
                if (thicknessChannel != null) {
                    ThicknessFactor = GetChannelFactor(thicknessChannel, "ThicknessFactor", 0f);
                    ThicknessTexture = LoadTextureFromChannel(model, thicknessChannel);
                }

                // 从 VolumeAttenuation channel 读取
                if (attenuationChannel != null) {
                    // 注意：AttenuationDistance 默认值应为无限大（float.MaxValue）
                    // SharpGLTF 的 GetFactor 返回 0 表示未设置，需要转换为 float.MaxValue
                    float attDist = GetChannelFactor(attenuationChannel, "AttenuationDistance", float.MaxValue);
                    AttenuationDistance = attDist == 0f ? float.MaxValue : attDist;

                    // AttenuationColor 存储在 Color 属性中
                    Vector4 color = attenuationChannel.Value.Color;
                    AttenuationColor = new Vector3(color.X, color.Y, color.Z);
                }
            }
        }

        public override void AppendDefines(ShaderDefines defines) {
            if (IsEnabled) {
                defines.AddMaterialExtension("VOLUME");
                if (ThicknessTexture != null) {
                    defines.AddTextureMap("THICKNESS");
                    if (ThicknessTexture.HasUVTransform) {
                        defines.AddUVTransform("THICKNESS");
                    }
                }
            }
        }
    }
}