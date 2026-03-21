using System;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using SharpGLTF.Schema2;
using GltfMaterial = SharpGLTF.Schema2.Material;

namespace DotnetGltfRenderer {
    /// <summary>
    /// KHR_materials_diffuse_transmission 扩展
    /// 支持漫反射透射（如薄纸、树叶）
    /// SharpGLTF 原生支持此扩展（MaterialDiffuseTransmission），但类型是 internal
    /// </summary>
    public class DiffuseTransmissionExtension : MaterialExtension {
        public override string ExtensionName => "KHR_materials_diffuse_transmission";

        /// <summary>
        /// 漫反射透射因子 (0-1)
        /// </summary>
        public float Factor { get; set; }

        /// <summary>
        /// 漫反射透射颜色因子
        /// </summary>
        public Vector3 ColorFactor { get; set; } = Vector3.One;

        /// <summary>
        /// 漫反射透射强度纹理
        /// </summary>
        public MaterialTexture Texture { get; set; }

        /// <summary>
        /// 漫反射透射颜色纹理
        /// </summary>
        public MaterialTexture ColorTexture { get; set; }

        /// <summary>
        /// Diffuse Transmission 启用条件：因子大于 0
        /// </summary>
        public override bool IsEnabled => Factor > 0f;

        public override IEnumerable<MaterialTextureSlot> GetTextureSlots() {
            if (Texture != null) {
                yield return MaterialTextureSlot.DiffuseTransmission;
            }
            if (ColorTexture != null) {
                yield return MaterialTextureSlot.DiffuseTransmissionColor;
            }
        }

        public override void LoadFromGltf(GltfMaterial material, Model model) {
            MaterialChannel? channel = material.FindChannel("DiffuseTransmissionFactor");
            if (channel != null) {
                Factor = GetChannelFactor(channel, "DiffuseTransmissionFactor", 0f);
                Texture = LoadTextureFromChannel(model, channel);
            }
            channel = material.FindChannel("DiffuseTransmissionColor");
            if (channel != null) {
                Vector4 color = channel.Value.Color;
                ColorFactor = new Vector3(color.X, color.Y, color.Z);
                ColorTexture = LoadTextureFromChannel(model, channel);
            }
        }

        MaterialTexture LoadTextureFromTextureInfo(Model model, object textureInfo) {
            // TextureInfo 也是 internal 类型，通过反射访问
            Type type = textureInfo.GetType();

            // 获取 Texture 属性
            PropertyInfo textureProp = type.GetProperty("Texture", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (textureProp == null) {
                return null;
            }
            object texture = textureProp.GetValue(textureInfo);
            if (texture == null) {
                return null;
            }

            // 获取 LogicalIndex
            PropertyInfo logicalIndexProp = texture.GetType()
                .GetProperty("LogicalIndex", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (logicalIndexProp == null) {
                return null;
            }
            int textureIndex = (int)logicalIndexProp.GetValue(texture);
            Texture tex = model.GetTexture(textureIndex);
            if (tex == null) {
                return null;
            }

            // 获取 TextureCoordinate
            PropertyInfo texCoordProp = type.GetProperty("TextureCoordinate", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            int texCoord = texCoordProp != null ? (int)texCoordProp.GetValue(textureInfo) : 0;
            MaterialTexture matTex = new(tex, texCoord);

            // 读取 KHR_texture_transform
            PropertyInfo transformProp = type.GetProperty("TextureTransform", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (transformProp != null) {
                object transform = transformProp.GetValue(textureInfo);
                if (transform != null) {
                    Type transformType = transform.GetType();
                    PropertyInfo offsetProp = transformType.GetProperty("Offset");
                    PropertyInfo scaleProp = transformType.GetProperty("Scale");
                    PropertyInfo rotationProp = transformType.GetProperty("Rotation");
                    Vector2 offset = offsetProp != null ? (Vector2)offsetProp.GetValue(transform) : Vector2.Zero;
                    Vector2 scale = scaleProp != null ? (Vector2)scaleProp.GetValue(transform) : Vector2.One;
                    float rotation = rotationProp != null ? (float)rotationProp.GetValue(transform) : 0f;
                    matTex.SetTransform(offset, scale, rotation);
                }
            }
            return matTex;
        }

        public override void AppendDefines(ShaderDefines defines) {
            if (IsEnabled) {
                defines.AddMaterialExtension("DIFFUSE_TRANSMISSION");
                if (Texture != null) {
                    defines.AddTextureMap("DIFFUSE_TRANSMISSION");
                    if (Texture.HasUVTransform) {
                        defines.AddUVTransform("DIFFUSETRANSMISSION");
                    }
                }
                if (ColorTexture != null) {
                    defines.AddTextureMap("DIFFUSE_TRANSMISSION_COLOR");
                    if (ColorTexture.HasUVTransform) {
                        defines.AddUVTransform("DIFFUSETRANSMISSIONCOLOR");
                    }
                }
            }
        }
    }
}