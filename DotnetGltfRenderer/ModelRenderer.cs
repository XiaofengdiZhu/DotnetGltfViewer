using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Silk.NET.OpenGLES;
using ZLogger;

namespace DotnetGltfRenderer {
    /// <summary>
    /// 模型渲染器，封装渲染流程和材质 uniform 设置
    /// </summary>
    public class ModelRenderer : IDisposable {
        readonly GL _gl;

        // Uniform Buffer Objects
        readonly UniformBuffer<SceneData> _sceneUBO;
        readonly UniformBuffer<MaterialData> _materialUBO;
        readonly UniformBuffer<LightsData> _lightsUBO;

        // IBL Samplers
        Texture _environmentTexture;
        IblSampler _iblSampler;
        bool _useIBL;
        float _environmentStrength = 1.0f;

        // Render modules
        FramebufferManager _framebufferManager;
        DrawableRenderer _drawableRenderer;
        SkyRenderer _skyRenderer;

        // Frame state
        int _framebufferWidth = -1;
        int _framebufferHeight = -1;
        float _framebufferAspectRatio = -1.0f;
        bool _useLinearOutput;
        bool _isScatterPass;

        public LightingSystem LightingSystem { get; }
        public Camera Camera { get; }
        public Skybox Skybox { get; }
        public Scene Scene { get; private set; }
        public string EnvironmentMapPath { get; private set; }
        public Shader MainShader { get; private set; }
        public Shader SkyShader { get; private set; }
        public RenderQueue RenderQueue { get; } = new();
        public ToneMapMode ToneMapMode { get; set; } = ToneMapMode.KhrPbrNeutral;

        /// <summary>
        /// 设置离屏帧缓冲区尺寸
        /// </summary>
        public void SetFramebufferSize(int width, int height) {
            _framebufferWidth = width;
            _framebufferHeight = height;
            _framebufferAspectRatio = (float)width / height;
            _framebufferManager?.SetSize(width, height);
        }

        /// <summary>
        /// 构造函数（Scene 模式，支持多模型）
        /// </summary>
        public ModelRenderer(GL gl, Scene scene, string environmentTexturePath, string shadersDirectory) {
            _gl = gl;
            Scene = scene;
            EnvironmentMapPath = environmentTexturePath;
            Camera = new Camera();
            LightingSystem = new LightingSystem();
            Skybox = new Skybox(gl);

            // 收集所有模型的光源
            UpdateLightsFromScene();

            // 初始化着色器缓存
            ShaderCache.Initialize(gl, shadersDirectory);

            // 使用 ShaderDefines 生成默认 defines
            ShaderDefines vertDefines = ShaderDefines.CreateVertexDefines(true, true, true, false, false);
            ShaderDefines fragDefines = ShaderDefines.CreateFragmentDefines();
            fragDefines.Add("USE_IBL");

            try {
                int vertHash = ShaderCache.SelectShader("primitive.vert", vertDefines.GetDefinesList());
                int fragHash = ShaderCache.SelectShader("pbr.frag", fragDefines.GetDefinesList());
                MainShader = ShaderCache.GetShaderProgram(vertHash, fragHash);
            }
            catch (Exception ex) {
                LogManager.Logger.ZLogError($"Failed to compile main shader: {ex.Message}");
                throw;
            }

            // 天空盒着色器
            ShaderDefines skyVertDefines = new();
            ShaderDefines skyFragDefines = new();
            try {
                int skyVertHash = ShaderCache.SelectShader("cubemap.vert", skyVertDefines.GetDefinesList());
                int skyFragHash = ShaderCache.SelectShader("cubemap.frag", skyFragDefines.GetDefinesList());
                SkyShader = ShaderCache.GetShaderProgram(skyVertHash, skyFragHash);
            }
            catch (Exception ex) {
                LogManager.Logger.ZLogError($"Failed to compile sky shader: {ex.Message}");
                throw;
            }

            // Create UBOs
            _sceneUBO = new UniformBuffer<SceneData>(gl, 0);
            _materialUBO = new UniformBuffer<MaterialData>(gl, 1);
            _lightsUBO = new UniformBuffer<LightsData>(gl, 2);

            // Bind UBOs to main shader
            MainShader.Use();
            _sceneUBO.BindToShader(MainShader.ProgramHandle, "SceneData");
            _materialUBO.BindToShader(MainShader.ProgramHandle, "MaterialData");
            _lightsUBO.BindToShader(MainShader.ProgramHandle, "LightsData");

            // Process IBL
            ProcessIBL(environmentTexturePath);

            // Initialize render modules
            _framebufferManager = new FramebufferManager(gl);
            _drawableRenderer = new DrawableRenderer(gl, _materialUBO);
            _skyRenderer = new SkyRenderer(gl, Skybox, SkyShader);
        }

        void ProcessIBL(string environmentTexturePath) {
            EnvironmentMap environmentMap = EnvironmentMap.LoadHDR(environmentTexturePath);
            _environmentTexture = Texture.FromEnvironmentMap(_gl, environmentMap);
            _iblSampler = new IblSampler(_gl);
            _iblSampler.Process(environmentMap);
            _useIBL = true;
            environmentMap.Dispose();
        }

        /// <summary>
        /// 从场景中更新光源
        /// </summary>
        public void UpdateLightsFromScene() {
            LightingSystem.ClearLights();
            if (Scene != null) {
                foreach (SceneModel sceneModel in Scene.Models) {
                    if (!sceneModel.IsVisible) continue;
                    foreach (Light light in sceneModel.Model.Lights) {
                        LightingSystem.AddLight(light);
                    }
                }
            }
        }

        /// <summary>
        /// 设置新的环境贴图
        /// </summary>
        public void SetEnvironmentMap(string environmentTexturePath) {
            _environmentTexture?.Dispose();
            _iblSampler?.Dispose();
            ProcessIBL(environmentTexturePath);
            EnvironmentMapPath = environmentTexturePath;
        }

        /// <summary>
        /// 绑定 UBO 到指定着色器程序
        /// </summary>
        public void BindUBOsToShader(uint programHandle) {
            _sceneUBO.BindToShader(programHandle, "SceneData");
            _materialUBO.BindToShader(programHandle, "MaterialData");
            _lightsUBO.BindToShader(programHandle, "LightsData");
        }

        /// <summary>
        /// 开始帧，清除缓冲区并设置默认状态
        /// </summary>
        public void BeginFrame() {
            _gl.Enable(EnableCap.DepthTest);
            _gl.DepthMask(true);
            _gl.DepthFunc(DepthFunction.Less);
        }

        /// <summary>
        /// 准备模型渲染（设置全局 uniform）
        /// </summary>
        public void PrepareModelRender() {
            _gl.Enable(EnableCap.CullFace);
            _gl.CullFace(TriangleFace.Back);
            MainShader.Use();

            // Store environment strength for IBL
            _environmentStrength = LightingSystem.EnvironmentStrength;

            // Update SceneData UBO
            SceneData sceneData = new() {
                CameraPos = new Vector4(Camera.Position, 0f),
                Exposure = LightingSystem.Exposure,
                EnvironmentStrength = LightingSystem.EnvironmentStrength,
                MipCount = _useIBL ? _iblSampler.MipCount : 0,
                Padding0 = 0f
            };
            _sceneUBO.Update(ref sceneData);

            // Update LightsData UBO
            LightsData lightsData = LightingSystem.GetLightsData();
            _lightsUBO.Update(ref lightsData);

            SetIBLUniforms();
        }

        /// <summary>
        /// 设置 IBL 相关 uniform
        /// </summary>
        void SetIBLUniforms() {
            MainShader.SetUniformMatrix3("u_EnvRotation", new Vector3(1f, 0f, 0f), new Vector3(0f, 1f, 0f), new Vector3(0f, 0f, 1f));
            if (_useIBL && _iblSampler != null) {
                MaterialTextureBinder.BindIBLTextures(_gl, _iblSampler);
            }
        }

        /// <summary>
        /// 渲染天空盒
        /// </summary>
        public void RenderSky(Matrix4x4 view, Matrix4x4 projection,
            float envIntensity = 1.0f, float envBlur = 0.0f, float envRotationDegrees = 0.0f) {
            _skyRenderer?.Render(view, projection, envIntensity * _environmentStrength, envBlur, envRotationDegrees,
                LightingSystem.Exposure, _iblSampler);
        }

        /// <summary>
        /// 渲染天空盒（线性输出模式）
        /// </summary>
        public void RenderSkyLinear(Matrix4x4 view, Matrix4x4 projection, float envIntensity = 1.0f) {
            _skyRenderer?.RenderLinear(view, projection, envIntensity * _environmentStrength,
                LightingSystem.Exposure, _iblSampler);
        }

        /// <summary>
        /// 渲染模型（简单模式，无渲染队列，不渲染天空盒）
        /// </summary>
        public void RenderModel() {
            IEnumerable<Model.MeshInstance> allInstances = Scene?.GetAllMeshInstances() ?? Enumerable.Empty<Model.MeshInstance>();
            int activeVariantIndex = Scene?.Models.Count > 0 ? Scene.Models[0].Model.ActiveVariantIndex : -1;
            RenderQueue.Prepare(allInstances, activeVariantIndex);
            PrepareModelRender();

            Matrix4x4 view = Camera.ViewMatrix;
            Matrix4x4 projection = Camera.GetProjectionMatrix(_framebufferAspectRatio);

            _drawableRenderer.SetViewProjectionMatrices(view, projection);

            // 渲染所有不透明物体
            foreach (Drawable drawable in RenderQueue.OpaqueDrawables) {
                RenderDrawable(drawable);
            }

            // 渲染透明物体
            foreach (Drawable drawable in RenderQueue.TransparentDrawables) {
                RenderDrawable(drawable);
            }
        }

        /// <summary>
        /// 渲染模型（使用渲染队列，支持 Transmission 和 VolumeScatter）
        /// </summary>
        public void RenderModelWithQueue() {
            IEnumerable<Model.MeshInstance> allInstances = Scene?.GetAllMeshInstances() ?? Enumerable.Empty<Model.MeshInstance>();
            int activeVariantIndex = Scene?.Models.Count > 0 ? Scene.Models[0].Model.ActiveVariantIndex : -1;
            RenderQueue.Prepare(allInstances, activeVariantIndex);

            Matrix4x4 view = Camera.ViewMatrix;
            Matrix4x4 projection = Camera.GetProjectionMatrix(_framebufferAspectRatio);
            RenderQueue.SortByDepth(view);

            bool hasScatter = RenderQueue.HasScatterDrawables;
            bool hasTransmission = RenderQueue.HasTransmissionDrawables;

            // === SCATTER PASS ===
            if (hasScatter) {
                _framebufferManager.EnsureScatterFramebuffer();
                _framebufferManager.BindScatterFramebuffer();
                _framebufferManager.ClearScatterFramebuffer();
                PrepareModelRender();

                _drawableRenderer.SetViewProjectionMatrices(view, projection);

                _useLinearOutput = true;
                _isScatterPass = true;
                foreach (Drawable drawable in RenderQueue.ScatterDrawables) {
                    RenderDrawable(drawable);
                }
                _isScatterPass = false;
                _useLinearOutput = false;
                _framebufferManager.UnbindFramebuffer();
            }

            // === TRANSMISSION PASS ===
            if (hasTransmission) {
                _framebufferManager.EnsureTransmissionFramebuffer();
                _framebufferManager.BindTransmissionFramebuffer();
                _framebufferManager.ClearTransmissionFramebuffer();
                PrepareModelRender();

                _drawableRenderer.SetViewProjectionMatrices(view, projection);

                // 渲染天空盒到离屏缓冲区
                RenderSkyLinear(view, projection);

                MainShader.Use();

                // 渲染不透明物体和透明物体（LINEAR_OUTPUT 模式）
                _useLinearOutput = true;
                foreach (Drawable drawable in RenderQueue.OpaqueDrawables) {
                    RenderDrawable(drawable);
                }
                foreach (Drawable drawable in RenderQueue.TransparentDrawables) {
                    RenderDrawable(drawable);
                }
                _useLinearOutput = false;

                _framebufferManager.GenerateTransmissionMipmap();
                _framebufferManager.UnbindFramebuffer();
            }

            // === MAIN PASS ===
            PrepareModelRender();

            _drawableRenderer.SetViewProjectionMatrices(view, projection);

            // 渲染天空盒
            RenderSky(view, projection);

            MainShader.Use();

            // 绑定 Scatter 纹理
            if (hasScatter) {
                _framebufferManager.BindScatterTextures(MainShader);
            }

            // 渲染不透明物体
            foreach (Drawable drawable in RenderQueue.OpaqueDrawables) {
                RenderDrawable(drawable);
            }

            // 渲染 Transmission 物体
            if (hasTransmission) {
                _framebufferManager.BindTransmissionTexture(MainShader);
                foreach (Drawable drawable in RenderQueue.TransmissionDrawables) {
                    RenderDrawable(drawable);
                }
            }

            // 渲染透明物体
            foreach (Drawable drawable in RenderQueue.TransparentDrawables) {
                RenderDrawable(drawable);
            }
        }

        /// <summary>
        /// 渲染单个 Drawable
        /// </summary>
        unsafe void RenderDrawable(Drawable drawable) {
            Mesh mesh = drawable.Mesh;
            Material material = drawable.Material;

            // 根据材质获取着色器变体
            Shader shader = GetOrCreateShaderVariant(mesh, material);
            if (shader == null) {
                return;
            }
            shader.Use();

            // 绑定 UBO 到当前着色器
            BindUBOsToShader(shader.ProgramHandle);

            // 设置视图投影矩阵
            shader.SetUniform("u_ViewProjectionMatrix", _drawableRenderer.CurrentViewProjection);
            shader.SetUniform("u_ViewMatrix", _drawableRenderer.CurrentView);
            shader.SetUniform("u_ProjectionMatrix", _drawableRenderer.CurrentProjection);

            // IBL uniform
            shader.SetUniformMatrix3("u_EnvRotation", new Vector3(1f, 0f, 0f), new Vector3(0f, 1f, 0f), new Vector3(0f, 0f, 1f));
            mesh.Bind();
            SetTransformUniformsFromDrawable(drawable, shader);

            // 设置 Morph Target 纹理和权重
            if (mesh.HasMorphTargets && mesh.MorphTargetTexture != null) {
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

            // 更新材质 UBO
            MaterialData matData = MaterialUboBuilder.BuildMaterialData(material, mesh.UseGeneratedTangents);
            _materialUBO.Update(ref matData);

            if (material != null) {
                MaterialTextureBinder.BindMaterialTextures(_gl, material, shader);
                MaterialTextureBinder.SetUVTransforms(material, shader);
            }
            MaterialTextureBinder.SetTextureSlotUniforms(shader);

            // Transmission framebuffer uniforms
            if (material?.Transmission?.IsEnabled == true && _framebufferManager.HasTransmissionFramebuffer) {
                shader.SetUniform("u_TransmissionFramebufferSampler", (int)MaterialTextureSlot.TransmissionFramebuffer);
                shader.SetUniformInt2("u_TransmissionFramebufferSize", _framebufferManager.Width, _framebufferManager.Height);
                shader.SetUniformInt2("u_ScreenSize", _framebufferManager.Width, _framebufferManager.Height);
            }

            // VolumeScatter uniforms
            if (material?.VolumeScatter?.IsEnabled == true) {
                shader.SetUniform("u_MultiScatterColor", material.VolumeScatter.MultiscatterColor);
                shader.SetUniform("u_MinRadius", VolumeScatterExtension.ScatterMinRadius);
                SetScatterSamplesUniforms(shader);

                if (_isScatterPass) {
                    shader.SetUniform("u_MaterialID", 1);
                }

                if (!_isScatterPass && _framebufferManager.HasScatterFramebuffer) {
                    shader.SetUniform("u_ScatterFramebufferSampler", (int)MaterialTextureSlot.ScatterFramebuffer);
                    shader.SetUniform("u_ScatterDepthFramebufferSampler", (int)MaterialTextureSlot.ScatterDepthFramebuffer);
                    shader.SetUniformInt2("u_FramebufferSize", _framebufferManager.Width, _framebufferManager.Height);
                }
            }

            SetCullModeFromDrawable(drawable);

            // Blend mode
            if (material?.Transmission?.IsEnabled == true && _useLinearOutput) {
                _gl.Disable(EnableCap.Blend);
            }
            else if (material != null) {
                SetBlendMode(material);
            }

            // Draw
            if (mesh.UseInstancing) {
                if (mesh.IsNegativeScaleInstance) {
                    _gl.FrontFace(FrontFaceDirection.CW);
                }
                _gl.DrawElementsInstanced(
                    PrimitiveType.Triangles,
                    (uint)mesh.Indices.Length,
                    DrawElementsType.UnsignedInt,
                    null,
                    (uint)mesh.InstanceCount
                );
                if (mesh.IsNegativeScaleInstance) {
                    _gl.FrontFace(FrontFaceDirection.Ccw);
                }
            }
            else {
                _gl.DrawElements(PrimitiveType.Triangles, (uint)mesh.Indices.Length, DrawElementsType.UnsignedInt, null);
            }
        }

        /// <summary>
        /// 根据网格和材质获取或创建着色器变体
        /// </summary>
        Shader GetOrCreateShaderVariant(Mesh mesh, Material material) {
            ShaderDefines vertDefines = ShaderDefines.CreateFromMesh(mesh);
            ShaderDefines fragDefines = ShaderDefines.CreateFromMaterial(material, _useIBL, _useLinearOutput, _isScatterPass, ToneMapMode, LightingSystem.Lights.Count, mesh);

            string fragShaderName;
            if (_isScatterPass) {
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
            if (drawable.Mesh.UseInstancing) {
                return;
            }

            Matrix4x4 modelMatrix = drawable.HasSkinning ? Matrix4x4.Identity : drawable.WorldMatrix;
            if (drawable.HasSkinning) {
                const int jointTextureSlot = 30;
                drawable.JointTexture?.Bind((TextureUnit)((int)TextureUnit.Texture0 + jointTextureSlot));
                shader.SetUniform("u_jointsSampler", jointTextureSlot);
            }

            shader.SetUniform("u_ModelMatrix", modelMatrix);

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
                _gl.BlendFuncSeparate(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha, BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha);
                _gl.BlendEquation(BlendEquationModeEXT.FuncAdd);
            }
            else {
                _gl.Disable(EnableCap.Blend);
            }
        }

        void SetScatterSamplesUniforms(Shader shader) {
            float[] samples = VolumeScatterExtension.ScatterSamples;
            if (samples == null) return;

            int sampleCount = samples.Length / 3;
            for (int i = 0; i < sampleCount; i++) {
                int idx = i * 3;
                shader.SetUniform($"u_ScatterSamples[{i}]", new Vector3(samples[idx], samples[idx + 1], samples[idx + 2]));
            }
        }

        public void Dispose() {
            Scene?.Dispose();
            Skybox?.Dispose();
            _environmentTexture?.Dispose();
            _iblSampler?.Dispose();
            MainShader?.Dispose();
            SkyShader?.Dispose();
            _sceneUBO?.Dispose();
            _materialUBO?.Dispose();
            _lightsUBO?.Dispose();
            _framebufferManager?.Dispose();
        }
    }
}
