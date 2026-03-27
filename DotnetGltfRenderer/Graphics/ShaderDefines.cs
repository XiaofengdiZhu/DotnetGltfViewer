using System.Collections.Generic;
using System.Text;

namespace DotnetGltfRenderer {
    /// <summary>
    /// 着色器预处理器定义列表
    /// 对应官方 renderer.js 中的 defines 数组
    /// </summary>
    public class ShaderDefines {
        readonly List<string> _defines = [];

        // 缓存的 hash，避免每次遍历计算
        int _cachedHash;
        bool _hashValid;

        /// <summary>
        /// 增量更新 hash
        /// </summary>
        void UpdateHash(string define) {
            unchecked {
                if (_hashValid) {
                    _cachedHash = _cachedHash * 31 + define.GetHashCode();
                }
                else {
                    _cachedHash = 17 * 31 + define.GetHashCode();
                    _hashValid = true;
                }
            }
        }

        /// <summary>
        /// 从另一个 ShaderDefines 复制 hash（用于 Clone）
        /// </summary>
        void CopyHashFrom(ShaderDefines other) {
            _cachedHash = other._cachedHash;
            _hashValid = other._hashValid;
        }

        /// <summary>
        /// 添加一个 define（如 "MATERIAL_CLEARCOAT 1"）
        /// </summary>
        public void Add(string define, int value = 1) {
            string str = $"{define} {value}";
            _defines.Add(str);
            UpdateHash(str);
        }

        /// <summary>
        /// 添加一个完整的 define 字符串（不带额外格式化）
        /// 如 "SCATTER_SAMPLES_COUNT 55"
        /// </summary>
        public void AddRaw(string define) {
            _defines.Add(define);
            UpdateHash(define);
        }

        /// <summary>
        /// 添加纹理 define（如 "HAS_NORMAL_MAP 1"）
        /// </summary>
        public void AddTextureMap(string textureName) {
            string str = $"HAS_{textureName.ToUpper()}_MAP 1";
            _defines.Add(str);
            UpdateHash(str);
        }

        /// <summary>
        /// 添加 UV Transform define（如 "HAS_BASECOLOR_UV_TRANSFORM 1"）
        /// </summary>
        public void AddUVTransform(string textureName) {
            string str = $"HAS_{textureName.ToUpper()}_UV_TRANSFORM 1";
            _defines.Add(str);
            UpdateHash(str);
        }

        /// <summary>
        /// 添加材质扩展 define（如 "MATERIAL_CLEARCOAT 1"）
        /// </summary>
        public void AddMaterialExtension(string extensionName) {
            string str = $"MATERIAL_{extensionName.ToUpper()} 1";
            _defines.Add(str);
            UpdateHash(str);
        }

        /// <summary>
        /// 添加顶点属性 define
        /// </summary>
        public void AddVertexAttribute(string attributeName, int componentCount) {
            string suffix = componentCount switch {
                2 => "VEC2",
                3 => "VEC3",
                4 => "VEC4",
                _ => ""
            };
            if (!string.IsNullOrEmpty(suffix)) {
                string str = $"HAS_{attributeName.ToUpper()}_{suffix} 1";
                _defines.Add(str);
                UpdateHash(str);
            }
        }

        /// <summary>
        /// 设置权重数量（用于骨骼动画）
        /// </summary>
        public void SetWeightCount(int count) {
            string str = $"WEIGHT_COUNT {count}";
            _defines.Add(str);
            UpdateHash(str);
        }

        /// <summary>
        /// 设置 Joint 数量（用于骨骼动画）
        /// </summary>
        public void SetJointCount(int count) {
            string str = $"JOINT_COUNT {count}";
            _defines.Add(str);
            UpdateHash(str);
        }

        /// <summary>
        /// 添加 Morph Target 相关 defines
        /// </summary>
        public void SetMorphTargetDefines(int targetCount,
            bool hasPosition,
            bool hasNormal,
            bool hasTangent,
            bool hasTexCoord0,
            bool hasTexCoord1,
            bool hasColor0,
            int positionOffset,
            int normalOffset,
            int tangentOffset,
            int texCoord0Offset,
            int texCoord1Offset,
            int color0Offset) {
            if (targetCount <= 0) {
                return;
            }
            Add("USE_MORPHING");
            Add("HAS_MORPH_TARGETS");
            AddRaw($"WEIGHT_COUNT {targetCount}");
            if (hasPosition) {
                Add("HAS_MORPH_TARGET_POSITION");
                AddRaw($"MORPH_TARGET_POSITION_OFFSET {positionOffset}");
            }
            if (hasNormal) {
                Add("HAS_MORPH_TARGET_NORMAL");
                AddRaw($"MORPH_TARGET_NORMAL_OFFSET {normalOffset}");
            }
            if (hasTangent) {
                Add("HAS_MORPH_TARGET_TANGENT");
                AddRaw($"MORPH_TARGET_TANGENT_OFFSET {tangentOffset}");
            }
            if (hasTexCoord0) {
                Add("HAS_MORPH_TARGET_TEXCOORD_0");
                AddRaw($"MORPH_TARGET_TEXCOORD_0_OFFSET {texCoord0Offset}");
            }
            if (hasTexCoord1) {
                Add("HAS_MORPH_TARGET_TEXCOORD_1");
                AddRaw($"MORPH_TARGET_TEXCOORD_1_OFFSET {texCoord1Offset}");
            }
            if (hasColor0) {
                Add("HAS_MORPH_TARGET_COLOR_0");
                AddRaw($"MORPH_TARGET_COLOR_0_OFFSET {color0Offset}");
            }
        }

        /// <summary>
        /// 设置 Alpha 模式
        /// </summary>
        public void SetAlphaMode(AlphaMode mode) {
            int modeValue = mode switch {
                AlphaMode.Opaque => 0,
                AlphaMode.Mask => 1,
                AlphaMode.Blend => 2,
                _ => 0
            };
            // Remove existing ALPHAMODE define if present
            // 注意：移除后需要重新计算 hash
            bool removed = _defines.RemoveAll(d => d.StartsWith("ALPHAMODE ")) > 0;
            string str = $"ALPHAMODE {modeValue}";
            _defines.Add(str);

            // 如果移除了旧值，需要重新计算整个 hash
            if (removed) {
                _hashValid = false;
                _cachedHash = 0;
                foreach (string define in _defines) {
                    UpdateHash(define);
                }
            }
            else {
                UpdateHash(str);
            }
        }

        /// <summary>
        /// 生成完整的 defines 代码（包含 #version）
        /// </summary>
        public string GetDefinesCode() {
            StringBuilder sb = new(_defines.Count * 32 + 20);
            sb.AppendLine("#version 300 es");
            foreach (string define in _defines) {
                sb.AppendLine($"#define {define}");
            }
            return sb.ToString();
        }

        /// <summary>
        /// 生成 defines 代码（不包含 #version，用于插入到着色器中）
        /// </summary>
        internal string GetDefinesCodeWithoutVersion() {
            StringBuilder sb = new(_defines.Count * 32);
            foreach (string define in _defines) {
                sb.AppendLine($"#define {define}");
            }
            return sb.ToString();
        }

        /// <summary>
        /// 获取 defines 列表（用于 ShaderCache.SelectShader）
        /// </summary>
        public List<string> GetDefinesList() => [.. _defines];

        /// <summary>
        /// 计算组合 hash（用于着色器缓存）
        /// 使用缓存避免重复计算
        /// </summary>
        public int ComputeHash() {
            if (_hashValid) {
                return _cachedHash;
            }
            unchecked {
                _cachedHash = 17;
                foreach (string define in _defines) {
                    _cachedHash = _cachedHash * 31 + define.GetHashCode();
                }
                _hashValid = true;
            }
            return _cachedHash;
        }

        public override string ToString() => string.Join(", ", _defines);

        /// <summary>
        /// 创建当前 ShaderDefines 的副本
        /// </summary>
        public ShaderDefines Clone() {
            ShaderDefines clone = new();
            clone._defines.AddRange(_defines);
            clone.CopyHashFrom(this);
            return clone;
        }

        #region Static Factory Methods

        /// <summary>
        /// 创建默认的顶点着色器 defines
        /// </summary>
        public static ShaderDefines CreateVertexDefines(bool hasNormals,
            bool hasTangents,
            bool hasTexcoord0,
            bool hasTexcoord1,
            bool hasColor0,
            bool useSkinning = false,
            int weightCount = 4,
            int jointCount = 4,
            bool useMorphing = false,
            int morphTargetCount = 0) {
            ShaderDefines defines = new();
            if (hasNormals) {
                defines.AddVertexAttribute("NORMAL", 3);
            }
            if (hasTangents) {
                defines.AddVertexAttribute("TANGENT", 4);
            }
            if (hasTexcoord0) {
                defines.AddVertexAttribute("TEXCOORD_0", 2);
            }
            if (hasTexcoord1) {
                defines.AddVertexAttribute("TEXCOORD_1", 2);
            }
            if (hasColor0) {
                defines.Add("HAS_COLOR_0_VEC4"); // Assume vec4 by default
            }
            if (useSkinning) {
                defines.Add("USE_SKINNING");
                defines.Add("HAS_JOINTS_0_VEC4");
                defines.Add("HAS_WEIGHTS_0_VEC4");
                defines.SetWeightCount(weightCount);
                defines.SetJointCount(jointCount);
            }
            if (useMorphing && morphTargetCount > 0) {
                defines.Add("USE_MORPHING");
                defines.AddRaw($"MORPH_TARGET_COUNT {morphTargetCount}");
            }
            return defines;
        }

        /// <summary>
        /// 创建默认的片段着色器 defines（仅 PBR 核心）
        /// </summary>
        public static ShaderDefines CreateFragmentDefines() {
            ShaderDefines defines = new();

            // ALPHAMODE_* constants are defined in functions.glsl
            // ALPHAMODE will be set via SetAlphaMode() based on material

            // Vertex attribute defines (needed for fragment shader debug views)
            // These should match what the vertex shader provides
            defines.Add("HAS_TEXCOORD_0_VEC2");
            defines.Add("MATERIAL_METALLICROUGHNESS");
            return defines;
        }

        /// <summary>
        /// 从网格创建顶点着色器 defines
        /// </summary>
        public static ShaderDefines CreateFromMesh(Mesh mesh) => CreateFromMesh(mesh, true, true);

        /// <summary>
        /// 从网格创建顶点着色器 defines（支持禁用蒙皮和 Morph Target）
        /// </summary>
        public static ShaderDefines CreateFromMesh(Mesh mesh, bool enableSkinning, bool enableMorphing) {
            ShaderDefines defines = CreateVertexDefines(
                mesh.HasSurfaceAttributes,
                mesh.HasSurfaceAttributes, // SurfaceVertices contains tangents (original or generated)
                true,
                mesh.HasUV1,
                mesh.HasColor0,
                enableSkinning && mesh.HasSkinAttributes
            );

            // 添加 GPU 实例化支持
            if (mesh.UseInstancing) {
                defines.Add("USE_INSTANCING");
            }

            // 添加 Morph Target 支持
            if (enableMorphing
                && mesh.HasMorphTargets
                && mesh.MorphTargetTexture != null) {
                MorphTargetTexture tex = mesh.MorphTargetTexture;
                defines.SetMorphTargetDefines(
                    mesh.MorphTargetCount,
                    tex.HasPosition,
                    tex.HasNormal,
                    tex.HasTangent,
                    tex.HasTexCoord0,
                    tex.HasTexCoord1,
                    tex.HasColor0,
                    tex.PositionOffset,
                    tex.NormalOffset,
                    tex.TangentOffset,
                    tex.TexCoord0Offset,
                    tex.TexCoord1Offset,
                    tex.Color0Offset
                );
            }
            return defines;
        }

        /// <summary>
        /// 从材质和渲染状态创建片段着色器 defines
        /// </summary>
        public static ShaderDefines CreateFromMaterial(Material material,
            bool useIBL,
            bool useLinearOutput,
            bool isScatterPass,
            ToneMapMode toneMapMode,
            int lightCount,
            Mesh mesh = null) => CreateFromMaterial(
            material,
            useIBL,
            useLinearOutput,
            isScatterPass,
            toneMapMode,
            lightCount,
            mesh,
            true,
            DebugChannel.None
        );

        /// <summary>
        /// 从材质和渲染状态创建片段着色器 defines（支持禁用 Morph Target）
        /// </summary>
        public static ShaderDefines CreateFromMaterial(Material material,
            bool useIBL,
            bool useLinearOutput,
            bool isScatterPass,
            ToneMapMode toneMapMode,
            int lightCount,
            Mesh mesh,
            bool enableMorphing) => CreateFromMaterial(
            material,
            useIBL,
            useLinearOutput,
            isScatterPass,
            toneMapMode,
            lightCount,
            mesh,
            enableMorphing,
            DebugChannel.None
        );

        /// <summary>
        /// 从材质和渲染状态创建片段着色器 defines（支持禁用 Morph Target 和 Debug Channel）
        /// </summary>
        public static ShaderDefines CreateFromMaterial(Material material,
            bool useIBL,
            bool useLinearOutput,
            bool isScatterPass,
            ToneMapMode toneMapMode,
            int lightCount,
            Mesh mesh,
            bool enableMorphing,
            DebugChannel debugChannel) {
            // 从材质获取基础 defines（已缓存），然后克隆一份添加上下文相关的 defines
            ShaderDefines defines = (material?.GetDefines() ?? CreateFragmentDefines()).Clone();

            // 片段着色器也需要顶点属性 defines（用于声明 varying 输入变量）
            // 否则 v_TBN/v_Normal/v_Color 不会被声明
            if (mesh != null) {
                if (mesh.HasSurfaceAttributes) {
                    defines.AddVertexAttribute("NORMAL", 3);
                    defines.AddVertexAttribute("TANGENT", 4);
                }
                if (mesh.HasUV1) {
                    defines.AddVertexAttribute("TEXCOORD_1", 2);
                }
                // 如果启用了 Morphing 且网格有颜色属性，才添加颜色定义
                if (mesh.HasColor0) {
                    defines.Add("HAS_COLOR_0_VEC4");
                }
            }

            // 添加 IBL 支持
            // 注意：Diffuse Transmission 也需要 IBL 来采样背面环境光
            if (useIBL || material?.DiffuseTransmission?.IsEnabled == true) {
                defines.Add("USE_IBL");
            }

            // 添加 Punctual Lights 支持 (KHR_lights_punctual)
            // 注意：Unlit 材质不需要灯光计算
            // 重要：LIGHT_COUNT 必须与 ubos.glsl 中的固定数组大小一致（8）
            // 但我们使用 u_LightCount 动态控制实际光源数量
            if (lightCount > 0
                && !(material?.Unlit?.IsEnabled ?? false)) {
                defines.Add("USE_PUNCTUAL");
                // 使用固定的 LIGHT_COUNT（与 ubos.glsl 中数组大小一致）
                // 实际光源数量由 u_LightCount 控制
            }

            // 添加色调映射（当 useLinearOutput 为 true 时跳过，输出保持线性空间）
            if (!useLinearOutput) {
                defines.Add(
                    toneMapMode switch {
                        ToneMapMode.KhrPbrNeutral => "TONEMAP_KHR_PBR_NEUTRAL",
                        ToneMapMode.AcesNarkowicz => "TONEMAP_ACES_NARKOWICZ",
                        ToneMapMode.AcesHill => "TONEMAP_ACES_HILL 1",
                        ToneMapMode.AcesHillExposureBoost => "TONEMAP_ACES_HILL_EXPOSURE_BOOST",
                        _ => "LINEAR_OUTPUT"
                    }
                );
            }
            else {
                defines.Add("LINEAR_OUTPUT");
            }

            // 添加 Debug Channel
            if (debugChannel != DebugChannel.None) {
                defines.AddRaw($"DEBUG {(int)debugChannel}");
            }
            return defines;
        }

        #endregion
    }
}