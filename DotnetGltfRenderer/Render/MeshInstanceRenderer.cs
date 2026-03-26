using System.Numerics;
using Silk.NET.OpenGLES;

namespace DotnetGltfRenderer {
    /// <summary>
    /// MeshInstance 渲染器
    /// 负责渲染单个 MeshInstance，包括着色器选择、材质绑定、绘制调用
    /// </summary>
    public class MeshInstanceRenderer {
        readonly UniformBuffer<MaterialData> _materialUBO;
        readonly UniformBuffer<SceneData> _sceneUBO;
        readonly UniformBuffer<LightsData> _lightsUBO;
        readonly UniformBuffer<RenderStateData> _renderStateUBO;
        readonly UniformBuffer<UVTransformData> _uvTransformUBO;
        readonly UniformBuffer<VolumeScatterData> _volumeScatterUBO;

        // 当前渲染状态数据（用于 UBO 更新）
        RenderStateData _renderStateData;

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
        public MeshInstanceRenderer(UniformBuffer<MaterialData> materialUBO, UniformBuffer<SceneData> sceneUBO, UniformBuffer<LightsData> lightsUBO, UniformBuffer<RenderStateData> renderStateUBO, UniformBuffer<UVTransformData> uvTransformUBO, UniformBuffer<VolumeScatterData> volumeScatterUBO) {
            _materialUBO = materialUBO;
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
        }

        /// <summary>
        /// 渲染单个 MeshInstance
        /// </summary>
        public void Render(MeshInstance instance, in RenderContext context) {
            Mesh mesh = instance.Mesh;
            Material material = instance.CurrentMaterial;

            // 根据材质获取着色器变体
            Shader shader = GetOrCreateShaderVariant(mesh, material, in context);
            if (shader == null) return;

            shader.Use();

            // 绑定 UBO
            BindUBOsToShader(shader);

            // 绑定网格
            mesh.Bind();

            // 设置变换 uniform
            SetTransformUniforms(instance, shader);

            // 设置 Morph Target
            SetupMorphTargets(mesh, shader);

            // 更新材质 UBO
            MaterialData matData = MaterialUboBuilder.BuildMaterialData(material, mesh.UseGeneratedTangents);
            _materialUBO.Update(ref matData);

            // 更新 UV 变换 UBO
            UVTransformData uvTransformData = MaterialTextureBinder.BuildUVTransformData(material);
            _uvTransformUBO.Update(ref uvTransformData);

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

        /// <summary>
        /// 绑定 UBO 到着色器
        /// </summary>
        void BindUBOsToShader(Shader shader) {
            _sceneUBO.BindToShader(shader.ProgramHandle, "SceneData");
            _materialUBO.BindToShader(shader.ProgramHandle, "MaterialData");
            _lightsUBO.BindToShader(shader.ProgramHandle, "LightsData");
            _renderStateUBO.BindToShader(shader.ProgramHandle, "RenderStateData");
            _uvTransformUBO.BindToShader(shader.ProgramHandle, "UVTransformData");
            _volumeScatterUBO.BindToShader(shader.ProgramHandle, "VolumeScatterData");
        }

        /// <summary>
        /// 获取或创建着色器变体
        /// </summary>
        Shader GetOrCreateShaderVariant(Mesh mesh, Material material, in RenderContext context) {
            // 使用缓存的顶点着色器 defines
            ShaderDefines vertDefines = mesh.GetVertDefines();
            ShaderDefines fragDefines = ShaderDefines.CreateFromMaterial(
                material,
                context.UseIBL,
                context.UseLinearOutput,
                context.IsScatterPass,
                context.ToneMapMode,
                context.LightCount,
                mesh
            );

            string fragShaderName = context.IsScatterPass ? "scatter.frag"
                : material?.SpecularGlossiness?.IsEnabled == true ? "specular_glossiness.frag"
                : "pbr.frag";

            return ShaderCache.GetShaderProgram(
                ShaderCache.SelectShader("primitive.vert", vertDefines.GetDefinesList()),
                ShaderCache.SelectShader(fragShaderName, fragDefines.GetDefinesList())
            );
        }

        /// <summary>
        /// 设置变换 uniform
        /// </summary>
        void SetTransformUniforms(MeshInstance instance, Shader shader) {
            if (instance.Mesh.UseInstancing) return;

            Matrix4x4 modelMatrix = instance.HasSkinning ? Matrix4x4.Identity : instance.WorldMatrix;

            if (instance.HasSkinning) {
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
        void SetupMorphTargets(Mesh mesh, Shader shader) {
            if (!mesh.HasMorphTargets || mesh.MorphTargetTexture == null) return;

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
            if (material?.Transmission?.IsEnabled != true || !context.HasTransmissionFramebuffer) return;

            shader.SetUniform("u_TransmissionFramebufferSampler", (int)MaterialTextureSlot.TransmissionFramebuffer);
            shader.SetUniformInt2("u_TransmissionFramebufferSize", context.FramebufferWidth, context.FramebufferHeight);
            shader.SetUniformInt2("u_ScreenSize", context.FramebufferWidth, context.FramebufferHeight);
        }

        /// <summary>
        /// 设置 VolumeScatter uniform
        /// </summary>
        void SetupVolumeScatterUniforms(Material material, Shader shader, in RenderContext context) {
            if (material?.VolumeScatter?.IsEnabled != true) return;

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
            SetScatterSamplesUniforms(shader);

            if (!context.IsScatterPass && context.HasScatterFramebuffer) {
                shader.SetUniform("u_ScatterFramebufferSampler", (int)MaterialTextureSlot.ScatterFramebuffer);
                shader.SetUniform("u_ScatterDepthFramebufferSampler", (int)MaterialTextureSlot.ScatterDepthFramebuffer);
            }
        }

        /// <summary>
        /// 设置散射样本 uniform
        /// </summary>
        void SetScatterSamplesUniforms(Shader shader) {
            float[] samples = VolumeScatterExtension.ScatterSamples;
            if (samples == null) return;

            int sampleCount = samples.Length / 3;
            for (int i = 0; i < sampleCount; i++) {
                int idx = i * 3;
                shader.SetUniform($"u_ScatterSamples[{i}]", new Vector3(samples[idx], samples[idx + 1], samples[idx + 2]));
            }
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
            if (material?.Transmission?.IsEnabled == true && context.UseLinearOutput) {
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
