using System.Collections.Generic;
using System.Numerics;
using Silk.NET.OpenGLES;

namespace DotnetGltfRenderer {
    /// <summary>
    /// MeshInstance 渲染器
    /// 负责渲染单个 MeshInstance，包括着色器选择、材质绑定、绘制调用
    /// </summary>
    public class MeshInstanceRenderer {
        readonly UniformBuffer<MaterialCoreData> _materialCoreUBO;
        readonly UniformBuffer<MaterialExtensionData> _materialExtUBO;
        readonly UniformBuffer<SceneData> _sceneUBO;
        readonly UniformBuffer<LightsData> _lightsUBO;
        readonly UniformBuffer<RenderStateData> _renderStateUBO;
        readonly UniformBuffer<UVTransformData> _uvTransformUBO;
        readonly UniformBuffer<VolumeScatterData> _volumeScatterUBO;

        // 当前渲染状态数据（用于 UBO 更新）
        RenderStateData _renderStateData;

        // ScatterSamples 静态数据已设置的着色器程序集合（一次性设置）
        readonly HashSet<uint> _scatterSamplesSetShaders = new();

        // 材质缓存（用于避免重复更新相同材质的 UBO）
        Material _lastMaterial;
        int _lastExtensionFlags;

        // UV 变换 dirty flag
        bool _uvTransformDirty = true;

        // 缓存的 context hash（每个 Pass 更新一次）
        int _cachedContextHash;

        (bool useIBL, bool useLinearOutput, ToneMapMode toneMapMode, int lightCount, bool isScatterPass, DebugChannel debugChannel)
            _lastContextParams;

        // 缓存的顶点着色器 hash（每帧更新一次，用于所有 MeshInstance）
        int _cachedVertShaderBaseHash;

        /// <summary>
        /// 当前视图投影矩阵
        /// </summary>
        public Matrix4x4 CurrentViewProjection { get; private set; }

        /// <summary>
        /// 当前视图矩阵
        /// </summary>
        public Matrix4x4 CurrentView { get; private set; }

        /// <summary>
        /// 当前投影矩阵
        /// </summary>
        public Matrix4x4 CurrentProjection { get; private set; }

        /// <summary>
        /// 创建 Drawable 渲染器
        /// </summary>
        public MeshInstanceRenderer(UniformBuffer<MaterialCoreData> materialCoreUBO,
            UniformBuffer<MaterialExtensionData> materialExtUBO,
            UniformBuffer<SceneData> sceneUBO,
            UniformBuffer<LightsData> lightsUBO,
            UniformBuffer<RenderStateData> renderStateUBO,
            UniformBuffer<UVTransformData> uvTransformUBO,
            UniformBuffer<VolumeScatterData> volumeScatterUBO) {
            _materialCoreUBO = materialCoreUBO;
            _materialExtUBO = materialExtUBO;
            _sceneUBO = sceneUBO;
            _lightsUBO = lightsUBO;
            _renderStateUBO = renderStateUBO;
            _uvTransformUBO = uvTransformUBO;
            _volumeScatterUBO = volumeScatterUBO;
        }

        /// <summary>
        /// 设置视图投影矩阵
        /// </summary>
        public void SetViewProjectionMatrices(Matrix4x4 view, Matrix4x4 projection) {
            CurrentView = view;
            CurrentProjection = projection;
            CurrentViewProjection = view * projection;

            // 更新 RenderStateData UBO 中的视图投影矩阵
            _renderStateData.ViewProjectionMatrix = CurrentViewProjection;
            _renderStateData.ViewMatrix = view;
            _renderStateData.ProjectionMatrix = projection;

            // 预计算顶点着色器基础 hash（shaderName 部分）
            _cachedVertShaderBaseHash = ComputeHash("primitive.vert");
        }

        /// <summary>
        /// 更新渲染上下文（每个 Pass 调用一次）
        /// 计算 context defines hash，避免每个 MeshInstance 重复计算
        /// </summary>
        void UpdateContextHashIfNeeded(in RenderContext context) {
            (bool UseIBL, bool UseLinearOutput, ToneMapMode ToneMapMode, int LightCount, bool IsScatterPass, DebugChannel DebugChannel)
                contextParams = (context.UseIBL, context.UseLinearOutput, context.ToneMapMode, context.LightCount, context.IsScatterPass,
                    context.DebugChannel);
            if (_lastContextParams == contextParams) {
                return; // context 未变化，使用缓存的 hash
            }
            _lastContextParams = contextParams;
            _cachedContextHash = ComputeContextHash(context);
        }

        /// <summary>
        /// 计算渲染上下文的 defines hash
        /// </summary>
        static int ComputeContextHash(in RenderContext context) {
            unchecked {
                int hash = 17;

                // USE_IBL
                if (context.UseIBL) {
                    hash = hash * 31 + "USE_IBL 1".GetHashCode();
                }

                // USE_PUNCTUAL (LIGHT_COUNT)
                if (context.LightCount > 0) {
                    hash = hash * 31 + "USE_PUNCTUAL 1".GetHashCode();
                }

                // ToneMap / LINEAR_OUTPUT
                if (context.UseLinearOutput) {
                    hash = hash * 31 + "LINEAR_OUTPUT 1".GetHashCode();
                }
                else {
                    string tonemapDefine = context.ToneMapMode switch {
                        ToneMapMode.KhrPbrNeutral => "TONEMAP_KHR_PBR_NEUTRAL 1",
                        ToneMapMode.AcesNarkowicz => "TONEMAP_ACES_NARKOWICZ 1",
                        ToneMapMode.AcesHill => "TONEMAP_ACES_HILL 1",
                        ToneMapMode.AcesHillExposureBoost => "TONEMAP_ACES_HILL_EXPOSURE_BOOST 1",
                        _ => "LINEAR_OUTPUT 1"
                    };
                    hash = hash * 31 + tonemapDefine.GetHashCode();
                }

                // DEBUG channel
                if (context.DebugChannel != DebugChannel.None) {
                    hash = hash * 31 + $"DEBUG {(int)context.DebugChannel}".GetHashCode();
                }
                return hash;
            }
        }

        static int ComputeHash(string input) {
            unchecked {
                int hash = 17;
                foreach (char c in input) {
                    hash = hash * 31 + c;
                }
                return hash;
            }
        }

        /// <summary>
        /// 渲染单个 MeshInstance
        /// </summary>
        public void Render(MeshInstance instance, in RenderContext context) {
            // 更新 context hash（如果 context 参数变化）
            UpdateContextHashIfNeeded(in context);
            Mesh mesh = instance.Mesh;
            Material material = instance.CurrentMaterial;

            // 根据材质获取着色器变体
            Shader shader = GetOrCreateShaderVariant(mesh, material, in context);
            if (shader == null) {
                return;
            }
            shader.Use();

            // UBO binding points 已在 ShaderCache.GetShaderProgram 链接时设置，无需每帧绑定

            // 绑定网格
            mesh.Bind();

            // 设置变换 uniform
            SetTransformUniforms(instance, shader, in context);

            // 设置 Morph Target
            SetupMorphTargets(mesh, shader, in context);

            // 更新材质 UBO（带缓存优化）
            UpdateMaterialUBOs(material, mesh.UseGeneratedTangents);

            // 更新 UV 变换 UBO（懒更新）
            UpdateUVTransformUBO(material);

            // 绑定材质纹理
            if (material != null) {
                MaterialTextureBinder.BindMaterialTextures(material, shader);
            }
            MaterialTextureBinder.SetTextureSlotUniforms(shader);

            // Transmission uniform
            SetupTransmissionUniforms(material, shader, context);

            // VolumeScatter uniform
            SetupVolumeScatterUniforms(material, shader, context);

            // 设置剔除模式
            SetCullMode(instance);

            // 设置混合模式
            SetupBlendMode(material, context);

            // 绘制
            DrawMesh(mesh);
        }

        public void RenderBatch(DynamicInstancingBatch batch, in RenderContext context) {
            if (batch == null || batch.IsEmpty) {
                return;
            }

            UpdateContextHashIfNeeded(in context);
            Mesh mesh = batch.Mesh;
            Material material = batch.Material;
            if (material == null) {
                return;
            }

            Shader shader = GetOrCreateInstancingShaderVariant(mesh, material, in context);
            if (shader == null) {
                return;
            }
            shader.Use();

            mesh.Bind();

            _renderStateData.ModelMatrix = Matrix4x4.Identity;
            _renderStateData.NormalMatrix = Matrix4x4.Identity;
            _renderStateUBO.Update(ref _renderStateData);

            UpdateMaterialUBOs(material, mesh.UseGeneratedTangents);
            UpdateUVTransformUBO(material);

            if (material != null) {
                MaterialTextureBinder.BindMaterialTextures(material, shader);
            }
            MaterialTextureBinder.SetTextureSlotUniforms(shader);

            SetupTransmissionUniforms(material, shader, context);
            SetupVolumeScatterUniforms(material, shader, context);

            if (material?.DoubleSided == true) {
                GlContext.DisableCullFace();
            }
            else {
                GlContext.EnableCullFace();
            }
            SetupBlendMode(material, context);

            batch.UpdateMatrices();
            batch.Draw();
        }

        Shader GetOrCreateInstancingShaderVariant(Mesh mesh, Material material, in RenderContext context) {
            int vertHash = _cachedVertShaderBaseHash ^ GetInstancingVertDefinesHash(mesh);

            string fragShaderName = context.IsScatterPass ? "scatter.frag" :
                material?.SpecularGlossiness?.IsEnabled == true ? "specular_glossiness.frag" : "pbr.frag";
            int fragShaderNameHash = ComputeHash(fragShaderName);
            int materialHash = material?.GetDefines().ComputeHash() ?? 0;
            int meshFragAttrHash = mesh.GetFragAttrHash();

            int contextHash = _cachedContextHash;
            if (material?.DiffuseTransmission?.IsEnabled == true && !context.UseIBL) {
                contextHash ^= "USE_IBL 1".GetHashCode();
            }
            if (material?.Unlit?.IsEnabled == true && context.LightCount > 0) {
                contextHash ^= "USE_PUNCTUAL 1".GetHashCode();
            }
            int fragHash = fragShaderNameHash ^ materialHash ^ meshFragAttrHash ^ contextHash;

            Shader shader = ShaderCache.TryGetShaderProgram(vertHash, fragHash);
            if (shader != null) {
                return shader;
            }

            return CreateInstancingShaderWithDefines(mesh, material, in context, fragShaderName);
        }

        static int GetInstancingVertDefinesHash(Mesh mesh) {
            unchecked {
                int hash = mesh.GetVertDefines().ComputeHash();
                hash ^= "USE_INSTANCING".GetHashCode();
                return hash;
            }
        }

        static Shader CreateInstancingShaderWithDefines(Mesh mesh, Material material, in RenderContext context, string fragShaderName) {
            ShaderDefines vertDefines = ShaderDefines.CreateFromMesh(mesh, false, false);
            vertDefines.Add("USE_INSTANCING");

            ShaderDefines fragDefines = ShaderDefines.CreateFromMaterial(
                material,
                context.UseIBL,
                context.UseLinearOutput,
                context.IsScatterPass,
                context.ToneMapMode,
                context.LightCount,
                mesh,
                false,
                context.DebugChannel
            );
            return ShaderCache.GetShaderProgram(
                ShaderCache.SelectShader("primitive.vert", vertDefines.GetDefinesList()),
                ShaderCache.SelectShader(fragShaderName, fragDefines.GetDefinesList())
            );
        }

        /// <summary>
        /// 更新材质 UBO（带缓存优化，避免重复更新相同材质）
        /// </summary>
        void UpdateMaterialUBOs(Material material, bool useGeneratedTangents) {
            int extensionFlags = (int)MaterialUboBuilder.BuildExtensionFlags(material);

            // 仅当材质变化时更新 MaterialCoreData UBO
            if (_lastMaterial != material) {
                MaterialCoreData coreData = MaterialUboBuilder.BuildMaterialCoreData(material, useGeneratedTangents);
                _materialCoreUBO.Update(ref coreData);
                _lastMaterial = material;
                _lastExtensionFlags = extensionFlags;
                _uvTransformDirty = true;

                // 材质变化时也需要更新 MaterialExtensionData UBO
                MaterialExtensionData extData = MaterialUboBuilder.BuildMaterialExtensionData(material);
                _materialExtUBO.Update(ref extData);
            }
            else if (_lastExtensionFlags != extensionFlags) {
                // 仅扩展标志变化时更新扩展数据
                MaterialExtensionData extData = MaterialUboBuilder.BuildMaterialExtensionData(material);
                _materialExtUBO.Update(ref extData);
                _lastExtensionFlags = extensionFlags;
            }
        }

        /// <summary>
        /// 更新 UV 变换 UBO（懒更新）
        /// </summary>
        void UpdateUVTransformUBO(Material material) {
            if (!_uvTransformDirty) {
                return;
            }
            UVTransformData uvTransformData = MaterialTextureBinder.BuildUVTransformData(material);
            _uvTransformUBO.Update(ref uvTransformData);
            _uvTransformDirty = false;
        }

        /// <summary>
        /// 获取或创建着色器变体
        /// 使用预计算的 hash 避免每帧创建 ShaderDefines 对象
        /// </summary>
        Shader GetOrCreateShaderVariant(Mesh mesh, Material material, in RenderContext context) {
            // 顶点着色器：考虑 EnableSkinning 和 EnableMorphing 标志
            int vertHash = _cachedVertShaderBaseHash ^ GetVertDefinesHash(mesh, context.EnableSkinning, context.EnableMorphing);

            // 片段着色器：组合各部分 hash
            // hash = shaderName ^ materialHash ^ meshFragAttrHash ^ contextHash
            string fragShaderName = context.IsScatterPass ? "scatter.frag" :
                material?.SpecularGlossiness?.IsEnabled == true ? "specular_glossiness.frag" : "pbr.frag";
            int fragShaderNameHash = ComputeHash(fragShaderName);
            int materialHash = material?.GetDefines().ComputeHash() ?? 0;
            int meshFragAttrHash = mesh.GetFragAttrHash();

            // Diffuse Transmission 也需要 IBL
            int contextHash = _cachedContextHash;
            if (material?.DiffuseTransmission?.IsEnabled == true
                && !context.UseIBL) {
                contextHash ^= "USE_IBL 1".GetHashCode();
            }

            // Unlit 材质不需要灯光
            if (material?.Unlit?.IsEnabled == true
                && context.LightCount > 0) {
                contextHash ^= "USE_PUNCTUAL 1".GetHashCode(); // 移除 USE_PUNCTUAL
            }
            int fragHash = fragShaderNameHash ^ materialHash ^ meshFragAttrHash ^ contextHash;

            // 尝试用 hash 直接获取程序
            Shader shader = ShaderCache.TryGetShaderProgram(vertHash, fragHash);
            if (shader != null) {
                return shader;
            }

            // Hash 不在缓存中，回退到创建 ShaderDefines 并编译
            return CreateShaderWithDefines(mesh, material, in context, fragShaderName);
        }

        /// <summary>
        /// 计算顶点着色器 defines hash（考虑 EnableSkinning 和 EnableMorphing）
        /// </summary>
        static int GetVertDefinesHash(Mesh mesh, bool enableSkinning, bool enableMorphing) {
            unchecked {
                int hash = mesh.GetVertDefines().ComputeHash();

                // 如果禁用蒙皮，移除蒙皮相关的 hash
                if (!enableSkinning
                    && mesh.HasSkinAttributes) {
                    hash ^= "HAS_JOINTS".GetHashCode();
                    hash ^= "HAS_WEIGHTS".GetHashCode();
                }

                // 如果禁用 Morph Target，移除相关 hash
                if (!enableMorphing
                    && mesh.HasMorphTargets) {
                    hash ^= "HAS_MORPH_TARGETS".GetHashCode();
                }
                return hash;
            }
        }

        /// <summary>
        /// 创建着色器（首次编译时使用）
        /// </summary>
        static Shader CreateShaderWithDefines(Mesh mesh, Material material, in RenderContext context, string fragShaderName) {
            ShaderDefines vertDefines = ShaderDefines.CreateFromMesh(mesh, context.EnableSkinning, context.EnableMorphing);
            ShaderDefines fragDefines = ShaderDefines.CreateFromMaterial(
                material,
                context.UseIBL,
                context.UseLinearOutput,
                context.IsScatterPass,
                context.ToneMapMode,
                context.LightCount,
                mesh,
                context.EnableMorphing,
                context.DebugChannel
            );
            return ShaderCache.GetShaderProgram(
                ShaderCache.SelectShader("primitive.vert", vertDefines),
                ShaderCache.SelectShader(fragShaderName, fragDefines)
            );
        }

        /// <summary>
        /// 设置变换 uniform
        /// </summary>
        void SetTransformUniforms(MeshInstance instance, Shader shader, in RenderContext context) {
            if (instance.Mesh.UseInstancing) {
                return;
            }
            bool useSkinning = context.EnableSkinning && instance.HasSkinning;
            Matrix4x4 modelMatrix = useSkinning ? instance.GetGizmoTransform() : instance.WorldMatrix;
            if (useSkinning) {
                const int jointTextureSlot = 30;
                instance.JointTexture?.Bind((TextureUnit)((int)TextureUnit.Texture0 + jointTextureSlot));
                shader.SetUniform("u_jointsSampler", jointTextureSlot);
            }

            // 更新 RenderStateData UBO 中的 ModelMatrix 和 NormalMatrix
            _renderStateData.ModelMatrix = modelMatrix;
            Matrix4x4.Invert(modelMatrix, out Matrix4x4 invModel);
            _renderStateData.NormalMatrix = Matrix4x4.Transpose(invModel);

            // 更新整个 RenderStateData UBO
            _renderStateUBO.Update(ref _renderStateData);
        }

        /// <summary>
        /// 设置 Morph Target 纹理和权重
        /// </summary>
        void SetupMorphTargets(Mesh mesh, Shader shader, in RenderContext context) {
            // 只有当启用 Morph Target 且网格有 Morph Target 数据时才设置
            if (!context.EnableMorphing
                || !mesh.HasMorphTargets
                || mesh.MorphTargetTexture == null) {
                return;
            }
            const int morphTextureSlot = 30;
            mesh.MorphTargetTexture.Bind((TextureUnit)((int)TextureUnit.Texture0 + morphTextureSlot));
            shader.SetUniform("u_MorphTargetsSampler", morphTextureSlot);
            float[] weights = mesh.MorphWeights;
            if (weights != null) {
                for (int i = 0; i < weights.Length; i++) {
                    shader.SetUniform($"u_morphWeights[{i}]", weights[i]);
                }
            }
        }

        /// <summary>
        /// 设置 Transmission uniform
        /// </summary>
        void SetupTransmissionUniforms(Material material, Shader shader, in RenderContext context) {
            if (material?.Transmission?.IsEnabled != true
                || !context.HasTransmissionFramebuffer) {
                return;
            }
            shader.SetUniform("u_TransmissionFramebufferSampler", (int)MaterialTextureSlot.TransmissionFramebuffer);
            shader.SetUniformInt2("u_TransmissionFramebufferSize", context.FramebufferWidth, context.FramebufferHeight);
            shader.SetUniformInt2("u_ScreenSize", context.FramebufferWidth, context.FramebufferHeight);
        }

        /// <summary>
        /// 设置 VolumeScatter uniform
        /// </summary>
        void SetupVolumeScatterUniforms(Material material, Shader shader, in RenderContext context) {
            if (material?.VolumeScatter?.IsEnabled != true) {
                return;
            }

            // 更新 VolumeScatterData UBO
            VolumeScatterData scatterData = new() {
                MultiScatterColor = new Vector4(material.VolumeScatter.MultiscatterColor, 0f),
                MinRadius = VolumeScatterExtension.ScatterMinRadius,
                MaterialID = context.IsScatterPass ? 1 : 0,
                FramebufferWidth = context.FramebufferWidth,
                FramebufferHeight = context.FramebufferHeight
            };
            _volumeScatterUBO.Update(ref scatterData);

            // Scatter samples 保持独立 uniform（数组太大不适合 UBO）
            // 只在首次使用该着色器时设置（静态数据）
            SetScatterSamplesUniformsOnce(shader);
            if (!context.IsScatterPass
                && context.HasScatterFramebuffer) {
                shader.SetUniform("u_ScatterFramebufferSampler", (int)MaterialTextureSlot.ScatterFramebuffer);
                shader.SetUniform("u_ScatterDepthFramebufferSampler", (int)MaterialTextureSlot.ScatterDepthFramebuffer);
            }
        }

        /// <summary>
        /// 设置散射样本 uniform（仅在首次使用该着色器时设置）
        /// ScatterSamples 是静态数据，无需每帧更新
        /// </summary>
        void SetScatterSamplesUniformsOnce(Shader shader) {
            // 检查是否已设置过
            if (_scatterSamplesSetShaders.Contains(shader.ProgramHandle)) {
                return;
            }
            float[] samples = VolumeScatterExtension.ScatterSamples;
            if (samples == null) {
                return;
            }
            int sampleCount = samples.Length / 3;
            for (int i = 0; i < sampleCount; i++) {
                int idx = i * 3;
                shader.SetUniform($"u_ScatterSamples[{i}]", new Vector3(samples[idx], samples[idx + 1], samples[idx + 2]));
            }

            // 标记为已设置
            _scatterSamplesSetShaders.Add(shader.ProgramHandle);
        }

        /// <summary>
        /// 设置剔除模式
        /// </summary>
        void SetCullMode(MeshInstance instance) {
            if (instance.CurrentMaterial?.DoubleSided == true) {
                GlContext.DisableCullFace();
            }
            else {
                GlContext.EnableCullFace();
                GlContext.FrontFace(instance.IsNegativeScale ? FrontFaceDirection.CW : FrontFaceDirection.Ccw);
            }
        }

        /// <summary>
        /// 设置混合模式
        /// </summary>
        void SetupBlendMode(Material material, in RenderContext context) {
            if (material?.Transmission?.IsEnabled == true
                && context.UseLinearOutput) {
                GlContext.DisableBlend();
            }
            else if (material != null) {
                SetBlendMode(material);
            }
        }

        /// <summary>
        /// 设置混合模式
        /// </summary>
        void SetBlendMode(Material material) {
            if (material.AlphaMode == AlphaMode.Blend) {
                GlContext.EnableBlend();
                GlContext.SetAlphaBlend();
                GlContext.GL.BlendEquation(BlendEquationModeEXT.FuncAdd);
            }
            else {
                GlContext.DisableBlend();
            }
        }

        /// <summary>
        /// 绘制网格
        /// </summary>
        unsafe void DrawMesh(Mesh mesh) {
            if (mesh.UseInstancing) {
                if (mesh.IsNegativeScaleInstance) {
                    GlContext.FrontFace(FrontFaceDirection.CW);
                }
                GlContext.GL.DrawElementsInstanced(
                    PrimitiveType.Triangles,
                    (uint)mesh.Indices.Length,
                    DrawElementsType.UnsignedInt,
                    null,
                    (uint)mesh.InstanceCount
                );
                if (mesh.IsNegativeScaleInstance) {
                    GlContext.FrontFace(FrontFaceDirection.Ccw);
                }
            }
            else {
                GlContext.GL.DrawElements(PrimitiveType.Triangles, (uint)mesh.Indices.Length, DrawElementsType.UnsignedInt, null);
            }
        }
    }
}