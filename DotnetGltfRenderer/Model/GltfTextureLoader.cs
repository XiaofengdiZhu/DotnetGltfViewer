using System.Collections.Generic;
using SharpGLTF.Schema2;
using GltfTexture = SharpGLTF.Schema2.Texture;

namespace DotnetGltfRenderer {
    /// <summary>
    /// glTF 纹理加载器
    /// </summary>
    public static class GltfTextureLoader {
        /// <summary>
        /// 加载所有纹理
        /// </summary>
        public static Dictionary<int, Texture> LoadTextures(ModelRoot modelRoot) {
            Dictionary<int, Texture> texturesLoaded = new();

            // 首先分析纹理用途，确定 sRGB vs linear
            Dictionary<int, bool> textureIsSrgb = new();
            foreach (GltfTexture gltfTexture in modelRoot.LogicalTextures) {
                textureIsSrgb[gltfTexture.LogicalIndex] = true; // 默认 sRGB
            }
            foreach (SharpGLTF.Schema2.Material material in modelRoot.LogicalMaterials) {
                AnalyzeTextureUsage(material, textureIsSrgb);
            }

            // 使用正确的格式加载纹理
            foreach (GltfTexture gltfTexture in modelRoot.LogicalTextures) {
                Image image = gltfTexture.PrimaryImage ?? gltfTexture.FallbackImage;
                if (image?.Content == null) {
                    continue;
                }
                int texIndex = gltfTexture.LogicalIndex;
                bool isSrgb = textureIsSrgb.GetValueOrDefault(texIndex, true);
                Texture texture = new(image.Content, ModelTextureType.None, gltfTexture.Sampler, isSrgb);
                texture.Path = image.Content.SourcePath ?? texIndex.ToString();
                texturesLoaded[texIndex] = texture;
            }
            return texturesLoaded;
        }

        /// <summary>
        /// 分析纹理颜色空间（sRGB vs Linear）
        /// </summary>
        public static void AnalyzeTextureUsage(SharpGLTF.Schema2.Material material, Dictionary<int, bool> textureIsSrgb) {
            // sRGB channels: BaseColor, Emissive
            // Linear channels: Normal, MetallicRoughness, Occlusion, ClearCoat, etc.

            void MarkTexture(string channelKey, bool isSrgb) {
                MaterialChannel? channel = material.FindChannel(channelKey);
                if (channel?.Texture is { } tex) {
                    textureIsSrgb[tex.LogicalIndex] = isSrgb;
                }
            }

            MarkTexture("BaseColor", true);
            MarkTexture("Emissive", true);
            MarkTexture("Normal", false);
            MarkTexture("MetallicRoughness", false);
            MarkTexture("Occlusion", false);
            MarkTexture("ClearCoat", false);
            MarkTexture("ClearCoatRoughness", false);
            MarkTexture("ClearCoatNormal", false);
            MarkTexture("Iridescence", false);
            MarkTexture("IridescenceThickness", false);
        }
    }
}