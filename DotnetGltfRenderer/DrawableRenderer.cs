using System.Numerics;
using Silk.NET.OpenGLES;

namespace DotnetGltfRenderer {
    /// <summary>
    /// Drawable 渲染器
    /// </summary>
    public class DrawableRenderer {
        readonly GL _gl;
        readonly UniformBuffer<MaterialData> _materialUBO;

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

        public DrawableRenderer(GL gl, UniformBuffer<MaterialData> materialUBO) {
            _gl = gl;
            _materialUBO = materialUBO;
        }

        /// <summary>
        /// 设置视图投影矩阵
        /// </summary>
        public void SetViewProjectionMatrices(Matrix4x4 view, Matrix4x4 projection) {
            CurrentView = view;
            CurrentProjection = projection;
            CurrentViewProjection = view * projection;
        }

        /// <summary>
        /// 渲染单个 Drawable
        /// </summary>
        public unsafe void Render(Drawable drawable,
            IblSampler iblSampler,
            FramebufferManager framebufferManager,
            bool useLinearOutput,
            bool isScatterPass,
            ToneMapMode toneMapMode,
            LightingSystem lightingSystem,
            bool useIBL) {
            Mesh mesh = drawable.Mesh;
            Material material = drawable.Material;

            // 根据材质获取着色器变体
            Shader shader = GetOrCreateShaderVariant(mesh, material, useIBL, useLinearOutput, isScatterPass, toneMapMode, lightingSystem.Lights.Count);
            if (shader == null) {
                return;
            }
            shader.Use();

            // 绑定 UBO 到当前着色器
            BindUBOsToShader(shader);

            // 设置视图投影矩阵
            shader.SetUniform("u_ViewProjectionMatrix", CurrentViewProjection);
            shader.SetUniform("u_ViewMatrix", CurrentView);
            shader.SetUniform("u_ProjectionMatrix", CurrentProjection);

            // IBL uniform
            shader.SetUniformMatrix3("u_EnvRotation", new Vector3(1f, 0f, 0f), new Vector3(0f, 1f, 0f), new Vector3(0f, 0f, 1f));
            mesh.Bind();
            SetTransformUniformsFromDrawable(drawable, shader);

            // 设置 Morph Target 纹理和权重
            if (mesh.HasMorphTargets && mesh.MorphTargetTexture != null) {
                const int morphTextureSlot = 30;
                mesh.MorphTargetTexture.Bind((TextureUnit)((int)TextureUnit.Texture0 + morphTextureSlot));
                shader.SetUniform("u_MorphTargetsSampler", morphTextureSlot);

                // 设置 morph weights uniform 数组
                float[] weights = mesh.MorphWeights;
                if (weights != null) {
                    for (int i = 0; i < weights.Length; i++) {
                        shader.SetUniform($"u_morphWeights[{i}]", weights[i]);
                    }
                }
            }

            // 更新材质 UBO
            MaterialData matData = MaterialUboBuilder.BuildMaterialData(material, mesh.UseGeneratedTangents);
            _materialUBO.Update(ref matData);

            if (material != null) {
                MaterialTextureBinder.BindMaterialTextures(_gl, material, shader);
                MaterialTextureBinder.SetUVTransforms(material, shader);
            }
            MaterialTextureBinder.SetTextureSlotUniforms(shader);

            // Transmission framebuffer uniforms
            if (material?.Transmission?.IsEnabled == true && framebufferManager?.HasTransmissionFramebuffer == true) {
                shader.SetUniform("u_TransmissionFramebufferSampler", (int)MaterialTextureSlot.TransmissionFramebuffer);
                shader.SetUniformInt2("u_TransmissionFramebufferSize", framebufferManager.Width, framebufferManager.Height);
                shader.SetUniformInt2("u_ScreenSize", framebufferManager.Width, framebufferManager.Height);
            }

            // VolumeScatter uniforms
            if (material?.VolumeScatter?.IsEnabled == true) {
                shader.SetUniform("u_MultiScatterColor", material.VolumeScatter.MultiscatterColor);
                shader.SetUniform("u_MinRadius", VolumeScatterExtension.ScatterMinRadius);

                // 设置散射样本数组
                SetScatterSamplesUniforms(shader);

                // 在 Scatter Pass 中设置 u_MaterialID
                if (isScatterPass) {
                    shader.SetUniform("u_MaterialID", 1);
                }

                // 在主 Pass 中绑定 Scatter 帧缓冲区纹理
                if (!isScatterPass && framebufferManager?.HasScatterFramebuffer == true) {
                    shader.SetUniform("u_ScatterFramebufferSampler", (int)MaterialTextureSlot.ScatterFramebuffer);
                    shader.SetUniform("u_ScatterDepthFramebufferSampler", (int)MaterialTextureSlot.ScatterDepthFramebuffer);
                    shader.SetUniformInt2("u_FramebufferSize", framebufferManager.Width, framebufferManager.Height);
                }
            }

            SetCullModeFromDrawable(drawable);

            // 如果是 Transmission 物体且正在离屏渲染（LINEAR_OUTPUT），不启用 Blend
            if (material?.Transmission?.IsEnabled == true && useLinearOutput) {
                _gl.Disable(EnableCap.Blend);
            }
            else if (material != null) {
                SetBlendMode(material);
            }

            // 使用 GPU 实例化或普通绘制
            if (mesh.UseInstancing) {
                // 负缩放实例需要翻转面绕序
                if (mesh.IsNegativeScaleInstance) {
                    _gl.FrontFace(FrontFaceDirection.CW);
                }
                // GPU 实例化绘制
                _gl.DrawElementsInstanced(
                    PrimitiveType.Triangles,
                    (uint)mesh.Indices.Length,
                    DrawElementsType.UnsignedInt,
                    null,
                    (uint)mesh.InstanceCount
                );
                // 恢复默认绕序
                if (mesh.IsNegativeScaleInstance) {
                    _gl.FrontFace(FrontFaceDirection.Ccw);
                }
            }
            else {
                // 普通绘制
                _gl.DrawElements(PrimitiveType.Triangles, (uint)mesh.Indices.Length, DrawElementsType.UnsignedInt, null);
            }
        }

        void BindUBOsToShader(Shader shader) {
            // 注意：这里需要 ModelRenderer 传入 UBO，或者在构造时接收
            // 暂时跳过，由 ModelRenderer 处理
        }

        Shader GetOrCreateShaderVariant(Mesh mesh, Material material, bool useIBL, bool useLinearOutput, bool isScatterPass, ToneMapMode toneMapMode, int lightCount) {
            // 生成顶点着色器 defines
            ShaderDefines vertDefines = ShaderDefines.CreateFromMesh(mesh);

            // 生成片段着色器 defines
            ShaderDefines fragDefines = ShaderDefines.CreateFromMaterial(material, useIBL, useLinearOutput, isScatterPass, toneMapMode, lightCount, mesh);

            // 选择片段着色器文件
            string fragShaderName;
            if (isScatterPass) {
                fragShaderName = "scatter.frag";
            }
            else if (material?.SpecularGlossiness?.IsEnabled == true) {
                fragShaderName = "specular_glossiness.frag";
            }
            else {
                fragShaderName = "pbr.frag";
            }

            return ShaderCache.GetShaderProgram(
                ShaderCache.SelectShader("primitive.vert", vertDefines.GetDefinesList()),
                ShaderCache.SelectShader(fragShaderName, fragDefines.GetDefinesList())
            );
        }

        void SetTransformUniformsFromDrawable(Drawable drawable, Shader shader) {
            // 对于 GPU 实例化，模型矩阵来自顶点属性，不需要设置 uniform
            if (drawable.Mesh.UseInstancing) {
                return;
            }

            Matrix4x4 modelMatrix = drawable.HasSkinning ? Matrix4x4.Identity : drawable.WorldMatrix;
            if (drawable.HasSkinning) {
                // 绑定骨骼纹理
                const int jointTextureSlot = 30;
                drawable.JointTexture?.Bind((TextureUnit)((int)TextureUnit.Texture0 + jointTextureSlot));
                shader.SetUniform("u_jointsSampler", jointTextureSlot);
            }

            // Set model matrix uniform
            shader.SetUniform("u_ModelMatrix", modelMatrix);

            // Calculate and set normal matrix
            Matrix4x4.Invert(modelMatrix, out Matrix4x4 invModel);
            Matrix4x4 normalMatrix4x4 = Matrix4x4.Transpose(invModel);
            shader.SetUniform("u_NormalMatrix", normalMatrix4x4);
        }

        void SetCullModeFromDrawable(Drawable drawable) {
            if (drawable.Material?.DoubleSided == true) {
                _gl.Disable(EnableCap.CullFace);
            }
            else {
                _gl.Enable(EnableCap.CullFace);
                _gl.FrontFace(drawable.IsNegativeScale ? FrontFaceDirection.CW : FrontFaceDirection.Ccw);
            }
        }

        void SetBlendMode(Material material) {
            if (material.AlphaMode == AlphaMode.Blend) {
                _gl.Enable(EnableCap.Blend);
                _gl.BlendFuncSeparate(
                    BlendingFactor.SrcAlpha,
                    BlendingFactor.OneMinusSrcAlpha,
                    BlendingFactor.One,
                    BlendingFactor.OneMinusSrcAlpha
                );
                _gl.BlendEquation(BlendEquationModeEXT.FuncAdd);
            }
            else {
                _gl.Disable(EnableCap.Blend);
            }
        }

        void SetScatterSamplesUniforms(Shader shader) {
            float[] samples = VolumeScatterExtension.ScatterSamples;
            if (samples == null) {
                return;
            }

            // u_ScatterSamples 是 vec3 数组，每个元素 3 个 float
            int sampleCount = samples.Length / 3;
            for (int i = 0; i < sampleCount; i++) {
                int idx = i * 3;
                shader.SetUniform($"u_ScatterSamples[{i}]", new Vector3(samples[idx], samples[idx + 1], samples[idx + 2]));
            }
        }
    }
}
