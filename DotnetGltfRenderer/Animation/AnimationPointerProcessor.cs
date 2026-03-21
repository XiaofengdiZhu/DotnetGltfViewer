using System;
using System.Collections.Generic;
using System.Numerics;
using SharpGLTF.Animations;
using SharpGLTF.Schema2;

namespace DotnetGltfRenderer {
    /// <summary>
    /// 动画指针处理器，用于处理 KHR_animation_pointer 扩展
    /// </summary>
    public class AnimationPointerProcessor {
        /// <summary>
        /// 动画指针目标
        /// </summary>
        public class PointerTarget {
            public string PointerPath { get; set; }
            public Action<float> UpdateAtTime { get; set; }
        }

        readonly List<PointerTarget> _targets = [];

        // glTF lights 数组索引 -> Model.Lights 列表索引的映射
        Dictionary<int, int> _gltfLightIndexToModelLightIndex;

        /// <summary>
        /// 所有动画指针目标
        /// </summary>
        public IReadOnlyList<PointerTarget> Targets => _targets;

        /// <summary>
        /// 是否有动画指针目标
        /// </summary>
        public bool HasTargets => _targets.Count > 0;

        /// <summary>
        /// 解析动画并收集所有动画指针目标
        /// </summary>
        public void ProcessAnimation(Animation animation, ModelRoot modelRoot, Model model) {
            _targets.Clear();
            _gltfLightIndexToModelLightIndex = BuildLightIndexMapping(modelRoot, model);
            if (animation == null) {
                return;
            }
            foreach (AnimationChannel channel in animation.Channels) {
                string pointerPath = channel.TargetPointerPath;
                if (string.IsNullOrEmpty(pointerPath)) {
                    continue;
                }

                // 跳过标准节点动画（已由 SharpGLTF 处理）
                if (IsStandardNodeAnimation(pointerPath)) {
                    continue;
                }
                PointerTarget target = CreatePointerTarget(pointerPath, channel, modelRoot, model);
                if (target != null) {
                    _targets.Add(target);
                    //Console.WriteLine($"[KHR_animation_pointer] Found: {pointerPath}");
                }
            }
        }

        /// <summary>
        /// 构建 glTF lights 数组索引到 Model.Lights 列表索引的映射
        /// </summary>
        static Dictionary<int, int> BuildLightIndexMapping(ModelRoot modelRoot, Model model) {
            Dictionary<int, int> mapping = new();

            // 遍历所有节点，找到带有 PunctualLight 的节点
            foreach (Node node in modelRoot.LogicalNodes) {
                PunctualLight punctualLight = node.PunctualLight;
                if (punctualLight == null) {
                    continue;
                }

                // 获取 glTF lights 数组中的索引
                int gltfLightIndex = punctualLight.LogicalIndex;

                // 在 Model.Lights 中找到对应的光源
                // 通过比较 Position 来匹配（因为 Light 对象是在 ProcessNodeRecursive 中创建的）
                // 获取节点的世界位置
                Matrix4x4 worldMatrix = node.WorldMatrix;
                Vector3 position = new(worldMatrix.M41, worldMatrix.M42, worldMatrix.M43);
                for (int i = 0; i < model.Lights.Count; i++) {
                    Light light = model.Lights[i];
                    // 通过位置和颜色匹配光源
                    if (Vector3.Distance(light.Position, position) < 0.001f
                        && Vector3.Distance(light.Color, punctualLight.Color) < 0.001f) {
                        mapping[gltfLightIndex] = i;
                        break;
                    }
                }
            }
            return mapping;
        }

        static bool IsStandardNodeAnimation(string path) {
            if (!path.StartsWith("/nodes/")) {
                return false;
            }
            return path.EndsWith("/translation") || path.EndsWith("/rotation") || path.EndsWith("/scale") || path.EndsWith("/weights");
        }

        PointerTarget CreatePointerTarget(string path, AnimationChannel channel, ModelRoot modelRoot, Model model) {
            string[] segments = path.Split(['/'], StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length < 3) {
                return null;
            }
            try {
                switch (segments[0]) {
                    case "nodes": return CreateNodeTarget(segments, channel, modelRoot, model);
                    case "materials": return CreateMaterialTarget(segments, channel, modelRoot, model);
                    case "extensions":
                        if (segments.Length > 1
                            && segments[1] == "KHR_lights_punctual") {
                            return CreateLightTarget(segments, channel, modelRoot, model);
                        }
                        break;
                }
            }
            catch (Exception ex) {
                Console.WriteLine($"[Warning] Failed to create pointer target for {path}: {ex.Message}");
            }
            return null;
        }

        PointerTarget CreateNodeTarget(string[] segments, AnimationChannel channel, ModelRoot modelRoot, Model model) {
            // /nodes/{index}/extensions/KHR_node_visibility/visible
            if (segments.Length < 5) {
                return null;
            }
            if (!int.TryParse(segments[1], out int nodeIndex)) {
                return null;
            }

            // KHR_node_visibility
            if (segments[2] == "extensions"
                && segments[3] == "KHR_node_visibility"
                && segments[4] == "visible") {
                Node node = modelRoot.LogicalNodes[nodeIndex];
                IAnimationSampler<float> sampler = channel.GetSamplerOrNull<float>();
                if (sampler == null) {
                    return null;
                }

                // 使用 SharpGLTF 的曲线采样器
                ICurveSampler<float> curveSampler = sampler.CreateCurveSampler(true);

                // 检查节点是否有关联的光源
                PunctualLight punctualLight = node.PunctualLight;
                Light targetLight = null;
                float originalIntensity = 0f;
                if (punctualLight != null) {
                    // 找到对应的 Light 对象
                    Matrix4x4 worldMatrix = node.WorldMatrix;
                    Vector3 position = new(worldMatrix.M41, worldMatrix.M42, worldMatrix.M43);
                    for (int i = 0; i < model.Lights.Count; i++) {
                        Light light = model.Lights[i];
                        if (Vector3.Distance(light.Position, position) < 0.001f
                            && Vector3.Distance(light.Color, punctualLight.Color) < 0.001f) {
                            targetLight = light;
                            originalIntensity = punctualLight.Intensity; // 使用 glTF 中的原始强度
                            break;
                        }
                    }
                }

                // 查找节点对应的 MeshInstance
                List<Model.MeshInstance> targetMeshInstances = null;
                foreach (Model.MeshInstance instance in model.MeshInstances) {
                    if (instance.Node.LogicalIndex == nodeIndex) {
                        targetMeshInstances ??= [];
                        targetMeshInstances.Add(instance);
                    }
                }
                Console.WriteLine(
                    $"[KHR_animation_pointer] Node visibility target: node {nodeIndex}, hasLight: {targetLight != null}, meshCount: {targetMeshInstances?.Count ?? 0}"
                );
                return new PointerTarget {
                    PointerPath = string.Join("/", segments),
                    UpdateAtTime = time => {
                        float value = curveSampler.GetPoint(time);
                        bool visible = value > 0.5f;

                        // 如果节点关联了光源，通过控制强度来实现可见性
                        if (targetLight != null) {
                            targetLight.Intensity = visible ? originalIntensity : 0f;
                        }

                        // 如果节点关联了 MeshInstance，控制其可见性
                        if (targetMeshInstances != null) {
                            foreach (Model.MeshInstance instance in targetMeshInstances) {
                                instance.IsVisible = visible;
                            }
                        }
                    }
                };
            }
            return null;
        }

        PointerTarget CreateMaterialTarget(string[] segments, AnimationChannel channel, ModelRoot modelRoot, Model model) {
            if (segments.Length < 3) {
                return null;
            }
            if (!int.TryParse(segments[1], out int materialIndex)) {
                return null;
            }
            if (materialIndex >= modelRoot.LogicalMaterials.Count) {
                return null;
            }

            // 找到对应的材质实例
            Material material = null;
            foreach (Mesh mesh in model.Meshes) {
                if (mesh.Material?.SourceMaterialIndex == materialIndex) {
                    material = mesh.Material;
                    break;
                }
            }
            if (material == null) {
                return null;
            }
            string propertyPath = string.Join("/", segments, 2, segments.Length - 2);
            return CreateMaterialPropertyTarget(propertyPath, channel, material);
        }

        PointerTarget CreateMaterialPropertyTarget(string propertyPath, AnimationChannel channel, Material material) {
            // 基础材质属性
            switch (propertyPath) {
                case "pbrMetallicRoughness/baseColorFactor":
                    return CreateVector4Target(channel, () => material.BaseColorFactor, v => material.BaseColorFactor = v);
                case "pbrMetallicRoughness/metallicFactor":
                    return CreateFloatTarget(channel, () => material.MetallicFactor, v => material.MetallicFactor = v);
                case "pbrMetallicRoughness/roughnessFactor":
                    return CreateFloatTarget(channel, () => material.RoughnessFactor, v => material.RoughnessFactor = v);
                case "emissiveFactor": return CreateVector3Target(channel, () => material.EmissiveFactor, v => material.EmissiveFactor = v);
                case "alphaCutoff": return CreateFloatTarget(channel, () => material.AlphaCutoff, v => material.AlphaCutoff = v);
            }

            // 纹理变换属性
            if (propertyPath.Contains("/extensions/KHR_texture_transform/")) {
                return CreateTextureTransformTarget(propertyPath, channel, material);
            }

            // 扩展属性
            if (propertyPath.StartsWith("extensions/")) {
                return CreateExtensionPropertyTarget(propertyPath.Substring(11), channel, material);
            }
            return null;
        }

        PointerTarget CreateTextureTransformTarget(string propertyPath, AnimationChannel channel, Material material) {
            // 解析路径: {textureSlot}/extensions/KHR_texture_transform/{property}
            // 例如: pbrMetallicRoughness/baseColorTexture/extensions/KHR_texture_transform/offset
            // 或: normalTexture/extensions/KHR_texture_transform/rotation
            const string transformSuffix = "/extensions/KHR_texture_transform/";
            int transformIdx = propertyPath.IndexOf(transformSuffix);
            if (transformIdx < 0) {
                return null;
            }
            string texturePath = propertyPath.Substring(0, transformIdx);
            string propertyName = propertyPath.Substring(transformIdx + transformSuffix.Length);

            // 获取对应的 MaterialTexture
            MaterialTexture matTex = GetMaterialTexture(material, texturePath);
            if (matTex == null) {
                return null;
            }
            switch (propertyName) {
                case "offset":
                    return CreateVector2Target(
                        channel,
                        () => matTex.Offset,
                        v => {
                            matTex.Offset = v;
                            matTex.RecomputeUVTransform();
                        }
                    );
                case "scale":
                    return CreateVector2Target(
                        channel,
                        () => matTex.Scale,
                        v => {
                            matTex.Scale = v;
                            matTex.RecomputeUVTransform();
                        }
                    );
                case "rotation":
                    return CreateFloatTarget(
                        channel,
                        () => matTex.Rotation,
                        v => {
                            matTex.Rotation = v;
                            matTex.RecomputeUVTransform();
                        }
                    );
            }
            return null;
        }

        static MaterialTexture GetMaterialTexture(Material material, string texturePath) {
            // 基础 PBR 纹理
            if (texturePath == "pbrMetallicRoughness/baseColorTexture") {
                return material.BaseColorTexture;
            }
            if (texturePath == "pbrMetallicRoughness/metallicRoughnessTexture") {
                return material.MetallicRoughnessTexture;
            }
            if (texturePath == "normalTexture") {
                return material.NormalTexture;
            }
            if (texturePath == "occlusionTexture") {
                return material.OcclusionTexture;
            }
            if (texturePath == "emissiveTexture") {
                return material.EmissiveTexture;
            }

            // 扩展纹理
            if (texturePath == "extensions/KHR_materials_clearcoat/clearcoatTexture"
                && material.ClearCoat != null) {
                return material.ClearCoat.Texture;
            }
            if (texturePath == "extensions/KHR_materials_clearcoat/clearcoatRoughnessTexture"
                && material.ClearCoat != null) {
                return material.ClearCoat.RoughnessTexture;
            }
            if (texturePath == "extensions/KHR_materials_clearcoat/clearcoatNormalTexture"
                && material.ClearCoat != null) {
                return material.ClearCoat.NormalTexture;
            }
            if (texturePath == "extensions/KHR_materials_sheen/sheenColorTexture"
                && material.Sheen != null) {
                return material.Sheen.ColorTexture;
            }
            if (texturePath == "extensions/KHR_materials_sheen/sheenRoughnessTexture"
                && material.Sheen != null) {
                return material.Sheen.RoughnessTexture;
            }
            if (texturePath == "extensions/KHR_materials_transmission/transmissionTexture"
                && material.Transmission != null) {
                return material.Transmission.Texture;
            }
            if (texturePath == "extensions/KHR_materials_volume/thicknessTexture"
                && material.Volume != null) {
                return material.Volume.ThicknessTexture;
            }
            if (texturePath == "extensions/KHR_materials_iridescence/iridescenceTexture"
                && material.Iridescence != null) {
                return material.Iridescence.Texture;
            }
            if (texturePath == "extensions/KHR_materials_iridescence/iridescenceThicknessTexture"
                && material.Iridescence != null) {
                return material.Iridescence.ThicknessTexture;
            }
            if (texturePath == "extensions/KHR_materials_specular/specularTexture"
                && material.Specular != null) {
                return material.Specular.SpecularTexture;
            }
            if (texturePath == "extensions/KHR_materials_specular/specularColorTexture"
                && material.Specular != null) {
                return material.Specular.SpecularColorTexture;
            }
            if (texturePath == "extensions/KHR_materials_anisotropy/anisotropyTexture"
                && material.Anisotropy != null) {
                return material.Anisotropy.AnisotropyTexture;
            }
            if (texturePath == "extensions/KHR_materials_diffuse_transmission/diffuseTransmissionTexture"
                && material.DiffuseTransmission != null) {
                return material.DiffuseTransmission.Texture;
            }
            if (texturePath == "extensions/KHR_materials_diffuse_transmission/diffuseTransmissionColorTexture"
                && material.DiffuseTransmission != null) {
                return material.DiffuseTransmission.ColorTexture;
            }
            return null;
        }

        PointerTarget CreateExtensionPropertyTarget(string extPath, AnimationChannel channel, Material material) {
            string[] parts = extPath.Split('/');
            if (parts.Length < 2) {
                return null;
            }
            string extensionName = parts[0];
            string propertyName = parts[1];
            switch (extensionName) {
                case "KHR_materials_emissive_strength":
                    if (propertyName == "emissiveStrength"
                        && material.EmissiveStrength != null) {
                        return CreateFloatTarget(
                            channel,
                            () => material.EmissiveStrength.EmissiveStrength,
                            v => material.EmissiveStrength.EmissiveStrength = v
                        );
                    }
                    break;
                case "KHR_materials_ior":
                    if (propertyName == "ior"
                        && material.Ior != null) {
                        return CreateFloatTarget(channel, () => material.Ior.Ior, v => material.Ior.Ior = v);
                    }
                    break;
                case "KHR_materials_specular":
                    if (material.Specular != null) {
                        switch (propertyName) {
                            case "specularFactor":
                                return CreateFloatTarget(channel, () => material.Specular.SpecularFactor, v => material.Specular.SpecularFactor = v);
                            case "specularColorFactor":
                                return CreateVector3Target(
                                    channel,
                                    () => material.Specular.SpecularColorFactor,
                                    v => material.Specular.SpecularColorFactor = v
                                );
                        }
                    }
                    break;
                case "KHR_materials_sheen":
                    if (material.Sheen != null) {
                        switch (propertyName) {
                            case "sheenColorFactor":
                                return CreateVector3Target(channel, () => material.Sheen.ColorFactor, v => material.Sheen.ColorFactor = v);
                            case "sheenRoughnessFactor":
                                return CreateFloatTarget(channel, () => material.Sheen.RoughnessFactor, v => material.Sheen.RoughnessFactor = v);
                        }
                    }
                    break;
                case "KHR_materials_clearcoat":
                    if (material.ClearCoat != null) {
                        switch (propertyName) {
                            case "clearcoatFactor":
                                return CreateFloatTarget(channel, () => material.ClearCoat.Factor, v => material.ClearCoat.Factor = v);
                            case "clearcoatRoughnessFactor":
                                return CreateFloatTarget(
                                    channel,
                                    () => material.ClearCoat.RoughnessFactor,
                                    v => material.ClearCoat.RoughnessFactor = v
                                );
                        }
                    }
                    break;
                case "KHR_materials_transmission":
                    if (propertyName == "transmissionFactor"
                        && material.Transmission != null) {
                        return CreateFloatTarget(channel, () => material.Transmission.Factor, v => material.Transmission.Factor = v);
                    }
                    break;
                case "KHR_materials_volume":
                    if (material.Volume != null) {
                        switch (propertyName) {
                            case "thicknessFactor":
                                return CreateFloatTarget(channel, () => material.Volume.ThicknessFactor, v => material.Volume.ThicknessFactor = v);
                            case "attenuationDistance":
                                return CreateFloatTarget(
                                    channel,
                                    () => material.Volume.AttenuationDistance,
                                    v => material.Volume.AttenuationDistance = v
                                );
                            case "attenuationColor":
                                return CreateVector3Target(
                                    channel,
                                    () => material.Volume.AttenuationColor,
                                    v => material.Volume.AttenuationColor = v
                                );
                        }
                    }
                    break;
                case "KHR_materials_iridescence":
                    if (material.Iridescence != null) {
                        switch (propertyName) {
                            case "iridescenceFactor":
                                return CreateFloatTarget(channel, () => material.Iridescence.Factor, v => material.Iridescence.Factor = v);
                            case "iridescenceIor":
                                return CreateFloatTarget(channel, () => material.Iridescence.IOR, v => material.Iridescence.IOR = v);
                            case "iridescenceThicknessMinimum":
                                return CreateFloatTarget(
                                    channel,
                                    () => material.Iridescence.ThicknessMinimum,
                                    v => material.Iridescence.ThicknessMinimum = v
                                );
                            case "iridescenceThicknessMaximum":
                                return CreateFloatTarget(
                                    channel,
                                    () => material.Iridescence.ThicknessMaximum,
                                    v => material.Iridescence.ThicknessMaximum = v
                                );
                        }
                    }
                    break;
                case "KHR_materials_anisotropy":
                    if (material.Anisotropy != null) {
                        switch (propertyName) {
                            case "anisotropyStrength":
                                return CreateFloatTarget(
                                    channel,
                                    () => material.Anisotropy.AnisotropyStrength,
                                    v => material.Anisotropy.AnisotropyStrength = v
                                );
                            case "anisotropyRotation":
                                return CreateFloatTarget(
                                    channel,
                                    () => material.Anisotropy.AnisotropyRotation,
                                    v => material.Anisotropy.AnisotropyRotation = v
                                );
                        }
                    }
                    break;
                case "KHR_materials_dispersion":
                    if (propertyName == "dispersion"
                        && material.Dispersion != null) {
                        return CreateFloatTarget(channel, () => material.Dispersion.Dispersion, v => material.Dispersion.Dispersion = v);
                    }
                    break;
                case "KHR_materials_volume_scatter":
                    if (material.VolumeScatter != null) {
                        switch (propertyName) {
                            case "multiscatterColor":
                                return CreateVector3Target(
                                    channel,
                                    () => material.VolumeScatter.MultiscatterColor,
                                    v => material.VolumeScatter.MultiscatterColor = v
                                );
                            case "scatterAnisotropy":
                                return CreateFloatTarget(
                                    channel,
                                    () => material.VolumeScatter.ScatterAnisotropy,
                                    v => material.VolumeScatter.ScatterAnisotropy = v
                                );
                        }
                    }
                    break;
            }
            return null;
        }

        PointerTarget CreateLightTarget(string[] segments, AnimationChannel channel, ModelRoot modelRoot, Model model) {
            // /extensions/KHR_lights_punctual/lights/{index}/{property}
            // /extensions/KHR_lights_punctual/lights/{index}/spot/{property}
            if (segments.Length < 5) {
                return null;
            }
            if (segments[2] != "lights") {
                return null;
            }
            if (!int.TryParse(segments[3], out int gltfLightIndex)) {
                return null;
            }

            // 使用映射表找到正确的 Model.Lights 索引
            if (!_gltfLightIndexToModelLightIndex.TryGetValue(gltfLightIndex, out int modelLightIndex)) {
                Console.WriteLine($"[KHR_animation_pointer] Warning: No mapping found for glTF light index {gltfLightIndex}");
                return null;
            }
            if (modelLightIndex >= model.Lights.Count) {
                return null;
            }
            Light light = model.Lights[modelLightIndex];

            // 检查是否是 spot 子属性 (segments[4] == "spot", segments[5] == property)
            if (segments.Length >= 6
                && segments[4] == "spot") {
                string spotProperty = segments[5];
                switch (spotProperty) {
                    case "innerConeAngle":
                        // glTF 使用弧度，我们的 Light 类使用度数
                        return CreateFloatTarget(
                            channel,
                            () => light.InnerConeAngle * MathF.PI / 180f,
                            v => light.InnerConeAngle = v * 180f / MathF.PI
                        );
                    case "outerConeAngle":
                        return CreateFloatTarget(
                            channel,
                            () => light.OuterConeAngle * MathF.PI / 180f,
                            v => light.OuterConeAngle = v * 180f / MathF.PI
                        );
                }
                return null;
            }
            string property = segments[4];
            switch (property) {
                case "color": return CreateVector3Target(channel, () => light.Color, v => light.Color = v);
                case "intensity": return CreateFloatTarget(channel, () => light.Intensity, v => light.Intensity = v);
                case "range": return CreateFloatTarget(channel, () => light.Range, v => light.Range = v);
            }
            return null;
        }

        PointerTarget CreateFloatTarget(AnimationChannel channel, Func<float> get, Action<float> set) {
            IAnimationSampler<float> sampler = channel.GetSamplerOrNull<float>();
            if (sampler == null) {
                return null;
            }

            // 使用 SharpGLTF 的曲线采样器，自动处理 STEP/LINEAR/CUBICSPLINE 插值
            ICurveSampler<float> curveSampler = sampler.CreateCurveSampler(true);
            return new PointerTarget {
                PointerPath = channel.TargetPointerPath,
                UpdateAtTime = time => {
                    float value = curveSampler.GetPoint(time);
                    set(value);
                }
            };
        }

        PointerTarget CreateVector3Target(AnimationChannel channel, Func<Vector3> get, Action<Vector3> set) {
            IAnimationSampler<Vector3> sampler = channel.GetSamplerOrNull<Vector3>();
            if (sampler == null) {
                return null;
            }
            ICurveSampler<Vector3> curveSampler = sampler.CreateCurveSampler(true);
            return new PointerTarget {
                PointerPath = channel.TargetPointerPath,
                UpdateAtTime = time => {
                    Vector3 value = curveSampler.GetPoint(time);
                    set(value);
                }
            };
        }

        PointerTarget CreateVector2Target(AnimationChannel channel, Func<Vector2> get, Action<Vector2> set) {
            IAnimationSampler<Vector2> sampler = channel.GetSamplerOrNull<Vector2>();
            if (sampler == null) {
                return null;
            }
            ICurveSampler<Vector2> curveSampler = sampler.CreateCurveSampler(true);
            return new PointerTarget {
                PointerPath = channel.TargetPointerPath,
                UpdateAtTime = time => {
                    Vector2 value = curveSampler.GetPoint(time);
                    set(value);
                }
            };
        }

        PointerTarget CreateVector4Target(AnimationChannel channel, Func<Vector4> get, Action<Vector4> set) {
            IAnimationSampler<Vector4> sampler = channel.GetSamplerOrNull<Vector4>();
            if (sampler == null) {
                return null;
            }
            ICurveSampler<Vector4> curveSampler = sampler.CreateCurveSampler(true);
            return new PointerTarget {
                PointerPath = channel.TargetPointerPath,
                UpdateAtTime = time => {
                    Vector4 value = curveSampler.GetPoint(time);
                    set(value);
                }
            };
        }

        /// <summary>
        /// 更新所有动画指针目标
        /// </summary>
        public void Update(float time) {
            foreach (PointerTarget target in _targets) {
                target.UpdateAtTime?.Invoke(time);
            }
        }
    }
}