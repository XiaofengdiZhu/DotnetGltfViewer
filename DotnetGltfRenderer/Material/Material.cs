using System.Collections.Generic;
using System.Numerics;
using SharpGLTF.Schema2;
using GltfMaterial = SharpGLTF.Schema2.Material;

namespace DotnetGltfRenderer {
    /// <summary>
    /// 材质类，封装 PBR 材质属性和扩展
    /// </summary>
    public class Material {
        /// <summary>
        /// 源材质在 glTF 中的逻辑索引（用于 KHR_animation_pointer）
        /// </summary>
        public int SourceMaterialIndex { get; set; } = -1;

        // Core PBR factors
        public Vector4 BaseColorFactor { get; set; } = Vector4.One;
        public float MetallicFactor { get; set; } = 1f;
        public float RoughnessFactor { get; set; } = 1f;
        public Vector3 EmissiveFactor { get; set; } = Vector3.Zero;

        // Texture parameters
        public float NormalScale { get; set; } = 1f;
        public float OcclusionStrength { get; set; } = 1f;

        // Core PBR textures
        public MaterialTexture BaseColorTexture { get; set; }
        public MaterialTexture MetallicRoughnessTexture { get; set; }
        public MaterialTexture NormalTexture { get; set; }
        public MaterialTexture OcclusionTexture { get; set; }
        public MaterialTexture EmissiveTexture { get; set; }

        // Rendering properties
        public AlphaMode AlphaMode { get; set; } = AlphaMode.Opaque;
        public float AlphaCutoff { get; set; } = 0.5f;
        public bool DoubleSided { get; set; }

        // Extensions - known types for convenience
        public ClearCoatExtension ClearCoat { get; set; }
        public IridescenceExtension Iridescence { get; set; }
        public TransmissionExtension Transmission { get; set; }
        public VolumeExtension Volume { get; set; }
        public SheenExtension Sheen { get; set; }
        public SpecularExtension Specular { get; set; }
        public IorExtension Ior { get; set; }
        public EmissiveStrengthExtension EmissiveStrength { get; set; }
        public DispersionExtension Dispersion { get; set; }
        public AnisotropyExtension Anisotropy { get; set; }
        public DiffuseTransmissionExtension DiffuseTransmission { get; set; }
        public VolumeScatterExtension VolumeScatter { get; set; }
        public UnlitExtension Unlit { get; set; }
        public SpecularGlossinessExtension SpecularGlossiness { get; set; }

        // Dynamic extensions storage
        readonly Dictionary<string, MaterialExtension> _extensions = new();

        /// <summary>
        /// 获取所有启用的扩展
        /// </summary>
        public IEnumerable<MaterialExtension> GetEnabledExtensions() {
            if (ClearCoat?.IsEnabled == true) {
                yield return ClearCoat;
            }
            if (Iridescence?.IsEnabled == true) {
                yield return Iridescence;
            }
            if (Transmission?.IsEnabled == true) {
                yield return Transmission;
            }
            if (Volume?.IsEnabled == true) {
                yield return Volume;
            }
            if (Sheen?.IsEnabled == true) {
                yield return Sheen;
            }
            if (Specular?.IsEnabled == true) {
                yield return Specular;
            }
            if (Ior?.IsEnabled == true) {
                yield return Ior;
            }
            if (EmissiveStrength?.IsEnabled == true) {
                yield return EmissiveStrength;
            }
            if (Dispersion?.IsEnabled == true) {
                yield return Dispersion;
            }
            if (Anisotropy?.IsEnabled == true) {
                yield return Anisotropy;
            }
            if (DiffuseTransmission?.IsEnabled == true) {
                yield return DiffuseTransmission;
            }
            if (VolumeScatter?.IsEnabled == true) {
                yield return VolumeScatter;
            }
            if (Unlit?.IsEnabled == true) {
                yield return Unlit;
            }
            if (SpecularGlossiness?.IsEnabled == true) {
                yield return SpecularGlossiness;
            }
            foreach (MaterialExtension ext in _extensions.Values) {
                if (ext?.IsEnabled == true) {
                    yield return ext;
                }
            }
        }

        /// <summary>
        /// 获取指定名称的扩展
        /// </summary>
        public T GetExtension<T>() where T : MaterialExtension {
            foreach (MaterialExtension ext in _extensions.Values) {
                if (ext is T typed) {
                    return typed;
                }
            }
            // Check known types
            if (typeof(T) == typeof(ClearCoatExtension)) {
                return ClearCoat as T;
            }
            if (typeof(T) == typeof(IridescenceExtension)) {
                return Iridescence as T;
            }
            if (typeof(T) == typeof(TransmissionExtension)) {
                return Transmission as T;
            }
            if (typeof(T) == typeof(VolumeExtension)) {
                return Volume as T;
            }
            if (typeof(T) == typeof(SheenExtension)) {
                return Sheen as T;
            }
            if (typeof(T) == typeof(SpecularExtension)) {
                return Specular as T;
            }
            if (typeof(T) == typeof(IorExtension)) {
                return Ior as T;
            }
            if (typeof(T) == typeof(EmissiveStrengthExtension)) {
                return EmissiveStrength as T;
            }
            if (typeof(T) == typeof(DispersionExtension)) {
                return Dispersion as T;
            }
            if (typeof(T) == typeof(AnisotropyExtension)) {
                return Anisotropy as T;
            }
            if (typeof(T) == typeof(DiffuseTransmissionExtension)) {
                return DiffuseTransmission as T;
            }
            if (typeof(T) == typeof(VolumeScatterExtension)) {
                return VolumeScatter as T;
            }
            if (typeof(T) == typeof(UnlitExtension)) {
                return Unlit as T;
            }
            if (typeof(T) == typeof(SpecularGlossinessExtension)) {
                return SpecularGlossiness as T;
            }
            return null;
        }

        /// <summary>
        /// 生成片段着色器 defines（参考官方 material.getDefines()）
        /// </summary>
        public ShaderDefines GetDefines() {
            ShaderDefines defines = ShaderDefines.CreateFragmentDefines();

            // Alpha mode
            defines.SetAlphaMode(AlphaMode);

            // Core textures + UV transforms
            if (BaseColorTexture != null) {
                defines.AddTextureMap("BASE_COLOR");
                if (BaseColorTexture.HasUVTransform) {
                    defines.AddUVTransform("BASECOLOR");
                }
            }
            if (NormalTexture != null) {
                defines.AddTextureMap("NORMAL");
                if (NormalTexture.HasUVTransform) {
                    defines.AddUVTransform("NORMAL");
                }
            }
            if (MetallicRoughnessTexture != null) {
                defines.AddTextureMap("METALLIC_ROUGHNESS");
                if (MetallicRoughnessTexture.HasUVTransform) {
                    defines.AddUVTransform("METALLICROUGHNESS");
                }
            }
            if (OcclusionTexture != null) {
                defines.AddTextureMap("OCCLUSION");
                if (OcclusionTexture.HasUVTransform) {
                    defines.AddUVTransform("OCCLUSION");
                }
            }
            if (EmissiveTexture != null) {
                defines.AddTextureMap("EMISSIVE");
                if (EmissiveTexture.HasUVTransform) {
                    defines.AddUVTransform("EMISSIVE");
                }
            }

            // Extensions
            AppendExtensionDefines(defines);
            return defines;
        }

        /// <summary>
        /// 附加扩展 defines（统一调用各扩展的 AppendDefines 方法）
        /// </summary>
        void AppendExtensionDefines(ShaderDefines defines) {
            // 调用所有已知扩展的 AppendDefines 方法（仅当扩展启用时）
            if (ClearCoat?.IsEnabled == true) {
                ClearCoat.AppendDefines(defines);
            }
            if (Iridescence?.IsEnabled == true) {
                Iridescence.AppendDefines(defines);
            }
            if (Transmission?.IsEnabled == true) {
                Transmission.AppendDefines(defines);
            }
            if (Volume?.IsEnabled == true) {
                Volume.AppendDefines(defines);
            }
            if (Sheen?.IsEnabled == true) {
                Sheen.AppendDefines(defines);
            }
            if (Specular?.IsEnabled == true) {
                Specular.AppendDefines(defines);
            }
            if (Ior?.IsEnabled == true) {
                Ior.AppendDefines(defines);
            }
            if (EmissiveStrength?.IsEnabled == true) {
                EmissiveStrength.AppendDefines(defines);
            }
            if (Dispersion?.IsEnabled == true) {
                Dispersion.AppendDefines(defines);
            }
            if (Anisotropy?.IsEnabled == true) {
                Anisotropy.AppendDefines(defines);
            }
            if (DiffuseTransmission?.IsEnabled == true) {
                DiffuseTransmission.AppendDefines(defines);
            }
            if (VolumeScatter?.IsEnabled == true) {
                VolumeScatter.AppendDefines(defines);
            }
            if (Unlit?.IsEnabled == true) {
                Unlit.AppendDefines(defines);
            }
            if (SpecularGlossiness?.IsEnabled == true) {
                SpecularGlossiness.AppendDefines(defines);
            }

            // 其他动态注册的扩展
            foreach (MaterialExtension ext in _extensions.Values) {
                if (ext?.IsEnabled == true) {
                    ext.AppendDefines(defines);
                }
            }
        }

        /// <summary>
        /// 从 glTF 材质加载
        /// </summary>
        public void LoadFromGltf(GltfMaterial material, Model model) {
            if (material == null) {
                return;
            }

            // Core PBR properties
            LoadCoreProperties(material, model);

            // Alpha mode
            AlphaMode = material.Alpha switch {
                SharpGLTF.Schema2.AlphaMode.BLEND => AlphaMode.Blend,
                SharpGLTF.Schema2.AlphaMode.MASK => AlphaMode.Mask,
                _ => AlphaMode.Opaque
            };
            AlphaCutoff = material.Alpha == SharpGLTF.Schema2.AlphaMode.MASK ? material.AlphaCutoff : 0.5f;
            DoubleSided = material.DoubleSided;

            // Extensions via registry
            LoadExtensionsFromRegistry(material, model);
        }

        void LoadCoreProperties(GltfMaterial material, Model model) {
            // BaseColor
            MaterialChannel? channel = material.FindChannel("BaseColor");
            BaseColorFactor = channel?.Color ?? Vector4.One;
            BaseColorTexture = LoadTextureFromChannel(model, channel);

            // MetallicRoughness
            channel = material.FindChannel("MetallicRoughness");
            MetallicFactor = GetChannelFactor(channel, "MetallicFactor", 1f);
            RoughnessFactor = GetChannelFactor(channel, "RoughnessFactor", 1f);
            MetallicRoughnessTexture = LoadTextureFromChannel(model, channel);

            // Normal
            channel = material.FindChannel("Normal");
            NormalScale = GetChannelFactor(channel, "NormalScale", 1f);
            NormalTexture = LoadTextureFromChannel(model, channel);

            // Occlusion
            channel = material.FindChannel("Occlusion");
            OcclusionStrength = GetChannelFactor(channel, "OcclusionStrength", 1f);
            OcclusionTexture = LoadTextureFromChannel(model, channel);

            // Emissive
            channel = material.FindChannel("Emissive");
            if (channel != null) {
                Vector4 emissiveColor = channel.Value.Color;
                float emissiveStrength = GetChannelFactor(channel, "EmissiveStrength", 1f);
                EmissiveFactor = new Vector3(emissiveColor.X, emissiveColor.Y, emissiveColor.Z) * emissiveStrength;
                EmissiveTexture = LoadTextureFromChannel(model, channel);
            }
        }

        void LoadExtensionsFromRegistry(GltfMaterial material, Model model) {
            foreach (string extName in MaterialExtensionRegistry.RegisteredExtensions) {
                // Create extension via registry
                MaterialExtension extension = MaterialExtensionRegistry.Create(extName);
                if (extension == null) {
                    continue;
                }

                // Load data from glTF
                extension.LoadFromGltf(material, model);

                // Store in appropriate property
                if (extension is ClearCoatExtension cc) {
                    ClearCoat = cc;
                }
                else if (extension is IridescenceExtension irid) {
                    Iridescence = irid;
                }
                else if (extension is TransmissionExtension trans) {
                    Transmission = trans;
                }
                else if (extension is VolumeExtension vol) {
                    Volume = vol;
                }
                else if (extension is SheenExtension sheen) {
                    Sheen = sheen;
                }
                else if (extension is SpecularExtension spec) {
                    Specular = spec;
                }
                else if (extension is IorExtension ior) {
                    Ior = ior;
                }
                else if (extension is EmissiveStrengthExtension emissiveStr) {
                    EmissiveStrength = emissiveStr;
                }
                else if (extension is DispersionExtension disp) {
                    Dispersion = disp;
                }
                else if (extension is AnisotropyExtension aniso) {
                    Anisotropy = aniso;
                }
                else if (extension is DiffuseTransmissionExtension diffTrans) {
                    DiffuseTransmission = diffTrans;
                }
                else if (extension is VolumeScatterExtension volScatter) {
                    VolumeScatter = volScatter;
                }
                else if (extension is UnlitExtension unlit) {
                    Unlit = unlit;
                }
                else if (extension is SpecularGlossinessExtension sg) {
                    SpecularGlossiness = sg;
                }
                else {
                    _extensions[extName] = extension;
                }
            }
        }

        MaterialTexture LoadTextureFromChannel(Model model, MaterialChannel? channel) {
            if (channel?.Texture == null) {
                return null;
            }
            Texture tex = model.GetTexture(channel.Value.Texture.LogicalIndex);
            if (tex == null) {
                return null;
            }
            MaterialTexture matTex = new(tex, channel.Value.TextureCoordinate);

            // 读取 KHR_texture_transform 扩展
            TextureTransform transform = channel.Value.TextureTransform;
            if (transform != null) {
                matTex.SetTransform(transform.Offset, transform.Scale, transform.Rotation);
            }
            return matTex;
        }

        static float GetChannelFactor(MaterialChannel? channel, string factorName, float defaultValue) {
            if (channel == null) {
                return defaultValue;
            }
            try {
                return channel.Value.GetFactor(factorName);
            }
            catch {
                return defaultValue;
            }
        }
    }
}