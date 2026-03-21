using System;
using System.Collections.Generic;
using System.Numerics;
using Silk.NET.OpenGLES;

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

        // Offscreen Framebuffer for Transmission
        OffscreenFramebuffer _transmissionFramebuffer;

        // Scatter Framebuffer for VolumeScatter (Subsurface Scattering)
        ScatterFramebuffer _scatterFramebuffer;

        int _framebufferWidth = -1;
        int _framebufferHeight = -1;
        float _framebufferAspectRatio = -1.0f;

        public LightingSystem LightingSystem { get; }

        public Camera Camera { get; }

        public Skybox Skybox { get; }

        public Model Model { get; }

        /// <summary>
        /// 着色器缓存
        /// </summary>
        public ShaderCache ShaderCache { get; }

        /// <summary>
        /// 主着色器
        /// </summary>
        public Shader MainShader { get; }

        /// <summary>
        /// 天空盒着色器
        /// </summary>
        public Shader SkyShader { get; }

        /// <summary>
        /// 渲染队列
        /// </summary>
        public RenderQueue RenderQueue { get; } = new();

        /// <summary>
        /// 色调映射模式（修改后需要重新编译着色器）
        /// </summary>
        public ToneMapMode ToneMapMode { get; set; } = ToneMapMode.KhrPbrNeutral;

        /// <summary>
        /// 设置离屏帧缓冲区尺寸
        /// </summary>
        public void SetFramebufferSize(int width, int height) {
            _framebufferWidth = width;
            _framebufferHeight = height;
            _framebufferAspectRatio = (float)width / height;
        }

        /// <summary>
        /// 构造函数（ShaderCache 模式，用于官方着色器）
        /// </summary>
        public ModelRenderer(GL gl, Model model, string environmentTexturePath, string shadersDirectory) {
            _gl = gl;
            Model = model;
            Camera = new Camera();
            LightingSystem = new LightingSystem();
            Skybox = new Skybox(gl);
            if (model.Lights.Count > 0) {
                foreach (Light light in model.Lights) {
                    LightingSystem.AddLight(light);
                }
            }

            // 加载着色器缓存
            ShaderCache = new ShaderCache(gl, shadersDirectory);

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
                Console.WriteLine($"[ERROR] Failed to compile main shader: {ex.Message}");
                throw;
            }

            // 天空盒着色器（使用 cubemap 着色器）
            // cubemap.frag 使用 #include，需要通过 ShaderCache 加载
            ShaderDefines skyVertDefines = new();
            ShaderDefines skyFragDefines = new();
            try {
                int skyVertHash = ShaderCache.SelectShader("cubemap.vert", skyVertDefines.GetDefinesList());
                int skyFragHash = ShaderCache.SelectShader("cubemap.frag", skyFragDefines.GetDefinesList());
                SkyShader = ShaderCache.GetShaderProgram(skyVertHash, skyFragHash);
            }
            catch (Exception ex) {
                Console.WriteLine($"[ERROR] Failed to compile sky shader: {ex.Message}");
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
            ProcessIBL(environmentTexturePath);
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
        /// 获取或编译着色器变体
        /// </summary>
        public Shader GetShaderVariant(List<string> vertDefines,
            List<string> fragDefines,
            string vertShader = "primitive.vert",
            string fragShader = "pbr.frag") {
            try {
                int vertHash = ShaderCache.SelectShader(vertShader, vertDefines);
                int fragHash = ShaderCache.SelectShader(fragShader, fragDefines);
                return ShaderCache.GetShaderProgram(vertHash, fragHash);
            }
            catch (Exception ex) {
                Console.WriteLine($"[ERROR] Failed to compile shader variant for {vertShader} and {fragShader}: {ex.Message}");
                return null;
            }
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
        /// 确保离屏帧缓冲区已创建（使用视口尺寸）
        /// </summary>
        void EnsureTransmissionFramebuffer() {
            // 如果尺寸变化，需要重新创建
            if (_transmissionFramebuffer == null || _framebufferWidth != _transmissionFramebuffer.Width || _framebufferHeight != _transmissionFramebuffer.Height) {
                _transmissionFramebuffer?.Dispose();
                _transmissionFramebuffer = new OffscreenFramebuffer(_gl, _framebufferWidth, _framebufferHeight);
            }
        }

        /// <summary>
        /// 确保 Scatter 帧缓冲区已创建
        /// </summary>
        void EnsureScatterFramebuffer() {
            if (_scatterFramebuffer == null || _framebufferWidth != _scatterFramebuffer.Width || _framebufferHeight != _scatterFramebuffer.Height) {
                _scatterFramebuffer?.Dispose();
                _scatterFramebuffer = new ScatterFramebuffer(_gl, _framebufferWidth, _framebufferHeight);
            }
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
        /// 渲染天空盒（与官方 environment_renderer.js 一致）
        /// </summary>
        public void RenderSky(Matrix4x4 view,
            Matrix4x4 projection,
            float envIntensity = 1.0f,
            float envBlur = 0.0f,
            float envRotationDegrees = 0.0f) {
            if (Skybox == null
                || _iblSampler == null) {
                return;
            }

            // 计算 ViewProjection 矩阵（移除平移分量）
            Matrix4x4 viewRotation = view;
            viewRotation.M41 = 0f;
            viewRotation.M42 = 0f;
            viewRotation.M43 = 0f;
            Matrix4x4 viewProjection = viewRotation * projection;

            // 计算环境旋转矩阵（绕 Y 轴旋转）
            float rotRad = envRotationDegrees * MathF.PI / 180.0f;
            float cosR = MathF.Cos(rotRad);
            float sinR = MathF.Sin(rotRad);
            Vector3 envRotCol0 = new(cosR, 0, -sinR);
            Vector3 envRotCol1 = new(0, 1, 0);
            Vector3 envRotCol2 = new(sinR, 0, cosR);

            // 禁用深度测试（官方做法）
            _gl.Disable(EnableCap.DepthTest);
            _gl.Disable(EnableCap.Blend);
            _gl.FrontFace(FrontFaceDirection.Ccw);
            _gl.Enable(EnableCap.CullFace);
            SkyShader.Use();

            // 设置 uniforms
            SkyShader.SetUniform("u_ViewProjectionMatrix", viewProjection);
            SkyShader.SetUniformMatrix3("u_EnvRotation", envRotCol0, envRotCol1, envRotCol2);
            SkyShader.SetUniform("u_MipCount", _iblSampler.MipCount);
            SkyShader.SetUniform("u_EnvBlurNormalized", envBlur);
            SkyShader.SetUniform("u_EnvIntensity", envIntensity * _environmentStrength);
            SkyShader.SetUniform("u_Exposure", LightingSystem.Exposure);

            // 绑定 GGX 预过滤环境贴图
            _gl.ActiveTexture(TextureUnit.Texture0);
            _gl.BindTexture(TextureTarget.TextureCubeMap, _iblSampler.GGXTexture);
            SkyShader.SetUniform("u_GGXEnvSampler", 0);
            Skybox.Draw();

            // 恢复深度测试
            _gl.Enable(EnableCap.DepthTest);
        }

        /// <summary>
        /// 渲染天空盒（线性输出模式，用于离屏缓冲区）
        /// </summary>
        public void RenderSkyLinear(Matrix4x4 view, Matrix4x4 projection, float envIntensity = 1.0f) {
            if (Skybox == null
                || _iblSampler == null) {
                return;
            }

            // 计算 ViewProjection 矩阵（移除平移分量）
            Matrix4x4 viewRotation = view;
            viewRotation.M41 = 0f;
            viewRotation.M42 = 0f;
            viewRotation.M43 = 0f;
            Matrix4x4 viewProjection = viewRotation * projection;

            // 计算环境旋转矩阵（绕 Y 轴旋转）
            float rotRad = 0f;
            float cosR = MathF.Cos(rotRad);
            float sinR = MathF.Sin(rotRad);
            Vector3 envRotCol0 = new(cosR, 0, -sinR);
            Vector3 envRotCol1 = new(0, 1, 0);
            Vector3 envRotCol2 = new(sinR, 0, cosR);

            // 禁用深度测试（官方做法）
            _gl.Disable(EnableCap.DepthTest);
            _gl.Disable(EnableCap.Blend);
            _gl.FrontFace(FrontFaceDirection.Ccw);
            _gl.Enable(EnableCap.CullFace);

            // 使用 LINEAR_OUTPUT 版本的天空盒着色器
            ShaderDefines skyFragDefines = new();
            skyFragDefines.Add("LINEAR_OUTPUT");
            int skyVertHash = ShaderCache.SelectShader("cubemap.vert", new List<string>());
            int skyFragHash = ShaderCache.SelectShader("cubemap.frag", skyFragDefines.GetDefinesList());
            Shader linearSkyShader = ShaderCache.GetShaderProgram(skyVertHash, skyFragHash);
            linearSkyShader.Use();

            // 设置 uniforms
            linearSkyShader.SetUniform("u_ViewProjectionMatrix", viewProjection);
            linearSkyShader.SetUniformMatrix3("u_EnvRotation", envRotCol0, envRotCol1, envRotCol2);
            linearSkyShader.SetUniform("u_MipCount", _iblSampler.MipCount);
            linearSkyShader.SetUniform("u_EnvBlurNormalized", 0.0f);
            linearSkyShader.SetUniform("u_EnvIntensity", envIntensity * _environmentStrength);
            linearSkyShader.SetUniform("u_Exposure", LightingSystem.Exposure);

            // 绑定 GGX 预过滤环境贴图
            _gl.ActiveTexture(TextureUnit.Texture0);
            _gl.BindTexture(TextureTarget.TextureCubeMap, _iblSampler.GGXTexture);
            linearSkyShader.SetUniform("u_GGXEnvSampler", 0);
            Skybox.Draw();

            // 恢复深度测试
            _gl.Enable(EnableCap.DepthTest);
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
        /// 设置视图和投影矩阵（在渲染时调用）
        /// </summary>
        public void SetViewProjectionMatrices(Matrix4x4 view, Matrix4x4 projection) {
            // 缓存当前帧的矩阵
            _currentView = view;
            _currentProjection = projection;
            _currentViewProjection = view * projection;

            // 顶点着色器使用 u_ViewProjectionMatrix
            MainShader.SetUniform("u_ViewProjectionMatrix", _currentViewProjection);

            // 片段着色器使用 u_ViewMatrix 和 u_ProjectionMatrix（用于 transmission）
            MainShader.SetUniform("u_ViewMatrix", view);
            MainShader.SetUniform("u_ProjectionMatrix", projection);
        }

        /// <summary>
        /// 设置 IBL 相关 uniform
        /// </summary>
        void SetIBLUniforms() {
            // u_EnvIntensity 已通过 SceneData UBO 设置，无需独立 uniform

            // Set environment rotation matrix (identity for now)
            MainShader.SetUniformMatrix3("u_EnvRotation", new Vector3(1f, 0f, 0f), new Vector3(0f, 1f, 0f), new Vector3(0f, 0f, 1f));
            if (_useIBL && _iblSampler != null) {
                // Bind IBL cubemap textures
                BindCubemapTexture(_iblSampler.LambertianTexture, MaterialTextureSlot.IBLLambertian);
                BindCubemapTexture(_iblSampler.GGXTexture, MaterialTextureSlot.IBLGGX);
                BindCubemapTexture(_iblSampler.SheenTexture, MaterialTextureSlot.IBLCharlie);
                BindTexture2D(_iblSampler.GGXLut, MaterialTextureSlot.IBLGGXLUT);
                BindTexture2D(_iblSampler.CharlieLut, MaterialTextureSlot.IBLCharlieLUT);
            }
        }

        void BindCubemapTexture(uint texture, MaterialTextureSlot slot) {
            _gl.ActiveTexture((TextureUnit)((int)TextureUnit.Texture0 + (int)slot));
            _gl.BindTexture(TextureTarget.TextureCubeMap, texture);
        }

        void BindTexture2D(uint texture, MaterialTextureSlot slot) {
            _gl.ActiveTexture((TextureUnit)((int)TextureUnit.Texture0 + (int)slot));
            _gl.BindTexture(TextureTarget.Texture2D, texture);
        }

        /// <summary>
        /// 渲染模型（简单模式，无渲染队列，不渲染天空盒）
        /// 适用于不需要 Transmission/透明物体排序的场景
        /// </summary>
        public void RenderModel() {
            // 准备渲染队列但不使用 Transmission 流程
            RenderQueue.Prepare(Model.MeshInstances, Model.ActiveVariantIndex);
            PrepareModelRender();
            Matrix4x4 view = Camera.ViewMatrix;
            Matrix4x4 projection = Camera.GetProjectionMatrix(_framebufferAspectRatio);
            SetViewProjectionMatrices(view, projection);

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
            // 准备渲染队列
            RenderQueue.Prepare(Model.MeshInstances, Model.ActiveVariantIndex);
            Matrix4x4 view = Camera.ViewMatrix;
            Matrix4x4 projection = Camera.GetProjectionMatrix(_framebufferAspectRatio);
            RenderQueue.SortByDepth(view);

            // 检查是否有 Scatter 物体（VolumeScatter）
            bool hasScatter = RenderQueue.HasScatterDrawables;
            // 检查是否有 Transmission 物体
            bool hasTransmission = RenderQueue.HasTransmissionDrawables;

            // === SCATTER PASS: 渲染 VolumeScatter 物体到 scatter framebuffer ===
            if (hasScatter) {
                EnsureScatterFramebuffer();
                _scatterFramebuffer.Bind();
                _scatterFramebuffer.Clear();
                PrepareModelRender();
                SetViewProjectionMatrices(view, projection);

                // 使用 scatter.frag 渲染 VolumeScatter 物体
                _useLinearOutput = true;
                _isScatterPass = true;
                foreach (Drawable drawable in RenderQueue.ScatterDrawables) {
                    RenderDrawable(drawable);
                }
                _isScatterPass = false;
                _useLinearOutput = false;
                _scatterFramebuffer.Unbind(_framebufferWidth, _framebufferHeight);
            }
            // === TRANSMISSION PASS: 渲染到离屏帧缓冲区（LINEAR_OUTPUT 模式）===
            if (hasTransmission) {
                EnsureTransmissionFramebuffer();
                _transmissionFramebuffer.Bind();
                _transmissionFramebuffer.Clear();
                PrepareModelRender();
                SetViewProjectionMatrices(view, projection);

                // 渲染天空盒到离屏缓冲区（需要 LINEAR_OUTPUT）
                RenderSkyLinear(view, projection);

                // 重新使用主着色器
                MainShader.Use();

                // 渲染不透明物体（LINEAR_OUTPUT 模式）
                _useLinearOutput = true;
                foreach (Drawable drawable in RenderQueue.OpaqueDrawables) {
                    RenderDrawable(drawable);
                }

                // 渲染透明物体（从远到近，LINEAR_OUTPUT 模式）
                foreach (Drawable drawable in RenderQueue.TransparentDrawables) {
                    RenderDrawable(drawable);
                }
                _useLinearOutput = false;

                // 生成 Mipmap（用于粗糙度模糊）
                _transmissionFramebuffer.GenerateMipmap();
                _transmissionFramebuffer.Unbind(_framebufferWidth, _framebufferHeight);
            }

            // === MAIN PASS: 渲染到屏幕 ===
            PrepareModelRender();
            SetViewProjectionMatrices(view, projection);

            // 渲染天空盒
            RenderSky(view, projection);

            // 重新使用主着色器
            MainShader.Use();

            // 绑定 Scatter 纹理（如果需要）
            if (hasScatter) {
                BindScatterTextures();
            }

            // 渲染不透明物体
            foreach (Drawable drawable in RenderQueue.OpaqueDrawables) {
                RenderDrawable(drawable);
            }

            // 渲染 Transmission 物体
            if (hasTransmission) {
                BindTransmissionTexture();
                foreach (Drawable drawable in RenderQueue.TransmissionDrawables) {
                    RenderDrawable(drawable);
                }
            }

            // 渲染透明物体（从远到近）
            foreach (Drawable drawable in RenderQueue.TransparentDrawables) {
                RenderDrawable(drawable);
            }
        }

        /// <summary>
        /// 绑定 Scatter 帧缓冲区纹理
        /// </summary>
        void BindScatterTextures() {
            if (_scatterFramebuffer == null) {
                return;
            }

            // 绑定颜色纹理
            _scatterFramebuffer.BindColorTexture((TextureUnit)((int)TextureUnit.Texture0 + (int)MaterialTextureSlot.ScatterFramebuffer));
            // 绑定深度纹理
            _scatterFramebuffer.BindDepthTexture((TextureUnit)((int)TextureUnit.Texture0 + (int)MaterialTextureSlot.ScatterDepthFramebuffer));
        }

        /// <summary>
        /// 绑定 Transmission 背景纹理
        /// </summary>
        void BindTransmissionTexture() {
            if (_transmissionFramebuffer == null) {
                return;
            }

            // 使用专用的 TransmissionFramebuffer 槽位
            _transmissionFramebuffer.BindColorTexture((TextureUnit)((int)TextureUnit.Texture0 + (int)MaterialTextureSlot.TransmissionFramebuffer));

            // 设置 uniform（但这只对 _mainShader 生效，实际设置移到 RenderDrawable 中）
            MainShader.SetUniform("u_TransmissionFramebufferSampler", (int)MaterialTextureSlot.TransmissionFramebuffer);
            MainShader.SetUniformInt2("u_TransmissionFramebufferSize", _transmissionFramebuffer.Width, _transmissionFramebuffer.Height);
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
            shader.SetUniform("u_ViewProjectionMatrix", _currentViewProjection);
            shader.SetUniform("u_ViewMatrix", _currentView);
            shader.SetUniform("u_ProjectionMatrix", _currentProjection);

            // IBL uniform（u_EnvIntensity 已通过 SceneData UBO 统一设置）
            shader.SetUniformMatrix3("u_EnvRotation", new Vector3(1f, 0f, 0f), new Vector3(0f, 1f, 0f), new Vector3(0f, 0f, 1f));
            mesh.Bind();
            SetTransformUniformsFromDrawable(drawable, shader);

            // 设置 Morph Target 纹理和权重
            if (mesh.HasMorphTargets
                && mesh.MorphTargetTexture != null) {
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
            UpdateMaterialUBO(material, mesh.UseGeneratedTangents);
            if (material != null) {
                BindMaterialTextures(material, shader);
                SetUVTransforms(material, shader);
            }
            SetTextureSlotUniformsForShader(shader);

            // Transmission framebuffer uniforms (必须在每个 shader variant 中设置)
            if (material?.Transmission?.IsEnabled == true
                && _transmissionFramebuffer != null) {
                shader.SetUniform("u_TransmissionFramebufferSampler", (int)MaterialTextureSlot.TransmissionFramebuffer);
                shader.SetUniformInt2("u_TransmissionFramebufferSize", _transmissionFramebuffer.Width, _transmissionFramebuffer.Height);
                shader.SetUniformInt2("u_ScreenSize", _transmissionFramebuffer.Width, _transmissionFramebuffer.Height);
            }

            // VolumeScatter uniforms (用于 KHR_materials_volume_scatter)
            if (material?.VolumeScatter?.IsEnabled == true) {
                shader.SetUniform("u_MultiScatterColor", material.VolumeScatter.MultiscatterColor);
                shader.SetUniform("u_MinRadius", VolumeScatterExtension.ScatterMinRadius);

                // 设置散射样本数组
                SetScatterSamplesUniforms(shader);

                // 在 Scatter Pass 中设置 u_MaterialID（用于材质标识）
                if (_isScatterPass) {
                    shader.SetUniform("u_MaterialID", 1); // 使用 1 作为材质 ID
                }

                // 在主 Pass 中绑定 Scatter 帧缓冲区纹理
                // Scatter Pass 时不需要绑定（因为是在写入 scatter framebuffer）
                if (!_isScatterPass
                    && _scatterFramebuffer != null) {
                    shader.SetUniform("u_ScatterFramebufferSampler", (int)MaterialTextureSlot.ScatterFramebuffer);
                    shader.SetUniform("u_ScatterDepthFramebufferSampler", (int)MaterialTextureSlot.ScatterDepthFramebuffer);
                    shader.SetUniformInt2("u_FramebufferSize", _scatterFramebuffer.Width, _scatterFramebuffer.Height);
                }
            }
            SetCullModeFromDrawable(drawable);

            // 如果是 Transmission 物体且正在离屏渲染（LINEAR_OUTPUT），不启用 Blend
            // Transmission 物体通常 alphaMode 是 OPAQUE，所以不需要 Blend
            if (material?.Transmission?.IsEnabled == true && _useLinearOutput) {
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
                // 普通绘制（负缩放在 MeshInstance 中处理）
                _gl.DrawElements(PrimitiveType.Triangles, (uint)mesh.Indices.Length, DrawElementsType.UnsignedInt, null);
            }
        }

        // 缓存当前帧的视图投影矩阵
        Matrix4x4 _currentView;
        Matrix4x4 _currentProjection;
        Matrix4x4 _currentViewProjection;

        // 离屏渲染时使用线性输出（避免双重色调映射）
        bool _useLinearOutput;

        // 是否正在渲染 Scatter Pass
        bool _isScatterPass;

        /// <summary>
        /// 根据网格和材质获取或创建着色器变体
        /// </summary>
        Shader GetOrCreateShaderVariant(Mesh mesh, Material material) {
            // 生成顶点着色器 defines
            ShaderDefines vertDefines = ShaderDefines.CreateVertexDefines(
                mesh.HasSurfaceAttributes,
                mesh.HasSurfaceAttributes, // SurfaceVertices contains tangents (original or generated)
                true,
                mesh.HasUV1,
                mesh.HasColor0,
                mesh.HasSkinAttributes
            );

            // 添加 GPU 实例化支持
            if (mesh.UseInstancing) {
                vertDefines.Add("USE_INSTANCING");
            }

            // 添加 Morph Target 支持
            if (mesh.HasMorphTargets
                && mesh.MorphTargetTexture != null) {
                MorphTargetTexture tex = mesh.MorphTargetTexture;
                vertDefines.SetMorphTargetDefines(
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

            // 生成片段着色器 defines（从材质获取）
            ShaderDefines fragDefines = material?.GetDefines() ?? ShaderDefines.CreateFragmentDefines();

            // 片段着色器也需要顶点属性 defines（用于声明 varying 输入变量）
            // 否则 v_TBN/v_Normal/v_Color 不会被声明
            if (mesh.HasSurfaceAttributes) {
                fragDefines.AddVertexAttribute("NORMAL", 3);
                fragDefines.AddVertexAttribute("TANGENT", 4);
            }
            if (mesh.HasUV1) {
                fragDefines.AddVertexAttribute("TEXCOORD_1", 2);
            }
            if (mesh.HasColor0) {
                fragDefines.Add("HAS_COLOR_0_VEC4");
            }

            // 添加 IBL 支持
            // 注意：Diffuse Transmission 也需要 IBL 来采样背面环境光
            if (_useIBL || material?.DiffuseTransmission?.IsEnabled == true) {
                fragDefines.Add("USE_IBL");
            }

            // 添加 Punctual Lights 支持 (KHR_lights_punctual)
            // 注意：Unlit 材质不需要灯光计算
            // 重要：LIGHT_COUNT 必须与 ubos.glsl 中的固定数组大小一致（8）
            // 但我们使用 u_LightCount 动态控制实际光源数量
            if (LightingSystem.Lights.Count > 0
                && !(material?.Unlit?.IsEnabled ?? false)) {
                fragDefines.Add("USE_PUNCTUAL");
                // 使用固定的 LIGHT_COUNT（与 ubos.glsl 中数组大小一致）
                // 实际光源数量由 u_LightCount 控制
            }

            // 添加色调映射（当 _useLinearOutput 为 true 时跳过，输出保持线性空间）
            if (!_useLinearOutput) {
                fragDefines.Add(
                    ToneMapMode switch {
                        ToneMapMode.KhrPbrNeutral => "TONEMAP_KHR_PBR_NEUTRAL",
                        ToneMapMode.AcesNarkowicz => "TONEMAP_ACES_NARKOWICZ",
                        ToneMapMode.AcesHill => "TONEMAP_ACES_HILL 1",
                        ToneMapMode.AcesHillExposureBoost => "TONEMAP_ACES_HILL_EXPOSURE_BOOST",
                        ToneMapMode.None or _ => "LINEAR_OUTPUT"
                    }
                );
            }
            else {
                fragDefines.Add("LINEAR_OUTPUT");
            }

            // 选择片段着色器文件
            // Scatter Pass 使用 scatter.frag
            // SpecularGlossiness 工作流使用独立的着色器
            // 否则使用 pbr.frag
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
            return GetShaderVariant(vertDefines.GetDefinesList(), fragDefines.GetDefinesList(), "primitive.vert", fragShaderName);
        }

        void SetTransformUniformsFromDrawable(Drawable drawable, Shader shader) {
            // 对于 GPU 实例化，模型矩阵来自顶点属性，不需要设置 uniform
            if (drawable.Mesh.UseInstancing) {
                return;
            }
            Matrix4x4 modelMatrix = drawable.HasSkinning ? Matrix4x4.Identity : drawable.WorldMatrix;
            if (drawable.HasSkinning) {
                // 绑定骨骼纹理到 u_jointsSampler（使用 slot 30，避免与其他纹理冲突）
                const int jointTextureSlot = 30;
                drawable.JointTexture?.Bind((TextureUnit)((int)TextureUnit.Texture0 + jointTextureSlot));
                shader.SetUniform("u_jointsSampler", jointTextureSlot);
            }

            // Set model matrix uniform (used by both vertex and fragment shaders)
            shader.SetUniform("u_ModelMatrix", modelMatrix);

            // Calculate and set normal matrix (transpose of inverse of upper-left 3x3)
            // Extend to 4x4 for shader compatibility
            Matrix4x4.Invert(modelMatrix, out Matrix4x4 invModel);
            Matrix4x4 normalMatrix4x4 = Matrix4x4.Transpose(invModel);
            shader.SetUniform("u_NormalMatrix", normalMatrix4x4);
        }

        void SetTextureSlotUniformsForShader(Shader shader) {
            // 官方着色器使用 *Sampler 命名
            shader.SetUniform("u_BaseColorSampler", (int)MaterialTextureSlot.BaseColor);
            shader.SetUniform("u_MetallicRoughnessSampler", (int)MaterialTextureSlot.MetallicRoughness);
            shader.SetUniform("u_NormalSampler", (int)MaterialTextureSlot.Normal);
            shader.SetUniform("u_OcclusionSampler", (int)MaterialTextureSlot.Occlusion);
            shader.SetUniform("u_EmissiveSampler", (int)MaterialTextureSlot.Emissive);

            // Clearcoat samplers
            shader.SetUniform("u_ClearcoatSampler", (int)MaterialTextureSlot.ClearCoat);
            shader.SetUniform("u_ClearcoatRoughnessSampler", (int)MaterialTextureSlot.ClearCoatRoughness);
            shader.SetUniform("u_ClearcoatNormalSampler", (int)MaterialTextureSlot.ClearCoatNormal);

            // Iridescence samplers
            shader.SetUniform("u_IridescenceSampler", (int)MaterialTextureSlot.Iridescence);
            shader.SetUniform("u_IridescenceThicknessSampler", (int)MaterialTextureSlot.IridescenceThickness);

            // Transmission sampler
            shader.SetUniform("u_TransmissionSampler", (int)MaterialTextureSlot.Transmission);

            // Volume/Thickness sampler
            shader.SetUniform("u_ThicknessSampler", (int)MaterialTextureSlot.Thickness);

            // Sheen samplers
            shader.SetUniform("u_SheenColorSampler", (int)MaterialTextureSlot.SheenColor);
            shader.SetUniform("u_SheenRoughnessSampler", (int)MaterialTextureSlot.SheenRoughness);

            // Specular samplers
            shader.SetUniform("u_SpecularSampler", (int)MaterialTextureSlot.Specular);
            shader.SetUniform("u_SpecularColorSampler", (int)MaterialTextureSlot.SpecularColor);

            // Anisotropy sampler
            shader.SetUniform("u_AnisotropySampler", (int)MaterialTextureSlot.Anisotropy);

            // SpecularGlossiness samplers (KHR_materials_pbrSpecularGlossiness)
            shader.SetUniform("u_DiffuseSampler", (int)MaterialTextureSlot.Diffuse);
            shader.SetUniform("u_SpecularGlossinessSampler", (int)MaterialTextureSlot.SpecularGlossiness);

            // Diffuse Transmission samplers
            shader.SetUniform("u_DiffuseTransmissionSampler", (int)MaterialTextureSlot.DiffuseTransmission);
            shader.SetUniform("u_DiffuseTransmissionColorSampler", (int)MaterialTextureSlot.DiffuseTransmissionColor);

            // IBL Samplers
            shader.SetUniform("u_LambertianEnvSampler", (int)MaterialTextureSlot.IBLLambertian);
            shader.SetUniform("u_GGXEnvSampler", (int)MaterialTextureSlot.IBLGGX);
            shader.SetUniform("u_CharlieEnvSampler", (int)MaterialTextureSlot.IBLCharlie);
            shader.SetUniform("u_GGXLUT", (int)MaterialTextureSlot.IBLGGXLUT);
            shader.SetUniform("u_CharlieLUT", (int)MaterialTextureSlot.IBLCharlieLUT);

            // Scatter Samplers (VolumeScatter)
            shader.SetUniform("u_ScatterFramebufferSampler", (int)MaterialTextureSlot.ScatterFramebuffer);
            shader.SetUniform("u_ScatterDepthFramebufferSampler", (int)MaterialTextureSlot.ScatterDepthFramebuffer);

            // Morph Target Sampler
            shader.SetUniform("u_MorphTargetsSampler", (int)MaterialTextureSlot.MorphTargets);
        }

        /// <summary>
        /// 设置 VolumeScatter 散射样本 uniform 数组
        /// </summary>
        void SetScatterSamplesUniforms(Shader shader) {
            float[] samples = VolumeScatterExtension.ScatterSamples;
            if (samples == null) {
                return;
            }

            // u_ScatterSamples 是 vec3 数组，每个元素 3 个 float
            // 格式: [theta0, r0, pdf0, theta1, r1, pdf1, ...]
            int sampleCount = samples.Length / 3;
            for (int i = 0; i < sampleCount; i++) {
                int idx = i * 3;
                shader.SetUniform($"u_ScatterSamples[{i}]", new Vector3(samples[idx], samples[idx + 1], samples[idx + 2]));
            }
        }

        void BindMaterialTextures(Material material, Shader shader) {
            BindTexture(material.BaseColorTexture, MaterialTextureSlot.BaseColor);
            BindTexture(material.MetallicRoughnessTexture, MaterialTextureSlot.MetallicRoughness);
            BindTexture(material.NormalTexture, MaterialTextureSlot.Normal);
            BindTexture(material.OcclusionTexture, MaterialTextureSlot.Occlusion);
            BindTexture(material.EmissiveTexture, MaterialTextureSlot.Emissive);

            // 扩展纹理
            if (material.ClearCoat?.IsEnabled == true) {
                BindTexture(material.ClearCoat.Texture, MaterialTextureSlot.ClearCoat);
                BindTexture(material.ClearCoat.RoughnessTexture, MaterialTextureSlot.ClearCoatRoughness);
                BindTexture(material.ClearCoat.NormalTexture, MaterialTextureSlot.ClearCoatNormal);
            }
            if (material.Iridescence?.IsEnabled == true) {
                BindTexture(material.Iridescence.Texture, MaterialTextureSlot.Iridescence);
                BindTexture(material.Iridescence.ThicknessTexture, MaterialTextureSlot.IridescenceThickness);
            }
            if (material.Transmission?.IsEnabled == true) {
                BindTexture(material.Transmission.Texture, MaterialTextureSlot.Transmission);
            }
            if (material.Volume?.IsEnabled == true) {
                BindTexture(material.Volume.ThicknessTexture, MaterialTextureSlot.Thickness);
            }
            if (material.Sheen?.IsEnabled == true) {
                BindTexture(material.Sheen.ColorTexture, MaterialTextureSlot.SheenColor);
                BindTexture(material.Sheen.RoughnessTexture, MaterialTextureSlot.SheenRoughness);
            }
            if (material.Specular?.IsEnabled == true) {
                BindTexture(material.Specular.SpecularTexture, MaterialTextureSlot.Specular);
                BindTexture(material.Specular.SpecularColorTexture, MaterialTextureSlot.SpecularColor);
            }
            if (material.Anisotropy?.IsEnabled == true) {
                BindTexture(material.Anisotropy.AnisotropyTexture, MaterialTextureSlot.Anisotropy);
            }
            if (material.DiffuseTransmission?.IsEnabled == true) {
                BindTexture(material.DiffuseTransmission.Texture, MaterialTextureSlot.DiffuseTransmission);
                BindTexture(material.DiffuseTransmission.ColorTexture, MaterialTextureSlot.DiffuseTransmissionColor);
            }

            // SpecularGlossiness workflow textures
            if (material.SpecularGlossiness?.IsEnabled == true) {
                BindTexture(material.SpecularGlossiness.DiffuseTexture, MaterialTextureSlot.Diffuse);
                BindTexture(material.SpecularGlossiness.SpecularGlossinessTexture, MaterialTextureSlot.SpecularGlossiness);
            }
        }

        void SetUVTransforms(Material material, Shader shader) {
            // Core textures UV transforms
            SetUVTransform(material.BaseColorTexture, shader, "u_BaseColorUVTransform");
            SetUVTransform(material.NormalTexture, shader, "u_NormalUVTransform");
            SetUVTransform(material.MetallicRoughnessTexture, shader, "u_MetallicRoughnessUVTransform");
            SetUVTransform(material.OcclusionTexture, shader, "u_OcclusionUVTransform");
            SetUVTransform(material.EmissiveTexture, shader, "u_EmissiveUVTransform");

            // Extension textures UV transforms
            if (material.ClearCoat?.IsEnabled == true) {
                SetUVTransform(material.ClearCoat.Texture, shader, "u_ClearcoatUVTransform");
                SetUVTransform(material.ClearCoat.RoughnessTexture, shader, "u_ClearcoatRoughnessUVTransform");
                SetUVTransform(material.ClearCoat.NormalTexture, shader, "u_ClearcoatNormalUVTransform");
            }
            if (material.Iridescence?.IsEnabled == true) {
                SetUVTransform(material.Iridescence.Texture, shader, "u_IridescenceUVTransform");
                SetUVTransform(material.Iridescence.ThicknessTexture, shader, "u_IridescenceThicknessUVTransform");
            }
            if (material.Transmission?.IsEnabled == true) {
                SetUVTransform(material.Transmission.Texture, shader, "u_TransmissionUVTransform");
            }
            if (material.Volume?.IsEnabled == true) {
                SetUVTransform(material.Volume.ThicknessTexture, shader, "u_ThicknessUVTransform");
            }
            if (material.Sheen?.IsEnabled == true) {
                SetUVTransform(material.Sheen.ColorTexture, shader, "u_SheenColorUVTransform");
                SetUVTransform(material.Sheen.RoughnessTexture, shader, "u_SheenRoughnessUVTransform");
            }
            if (material.Specular?.IsEnabled == true) {
                SetUVTransform(material.Specular.SpecularTexture, shader, "u_SpecularUVTransform");
                SetUVTransform(material.Specular.SpecularColorTexture, shader, "u_SpecularColorUVTransform");
            }
            if (material.Anisotropy?.IsEnabled == true) {
                SetUVTransform(material.Anisotropy.AnisotropyTexture, shader, "u_AnisotropyUVTransform");
            }
            if (material.DiffuseTransmission?.IsEnabled == true) {
                SetUVTransform(material.DiffuseTransmission.Texture, shader, "u_DiffuseTransmissionUVTransform");
                SetUVTransform(material.DiffuseTransmission.ColorTexture, shader, "u_DiffuseTransmissionColorUVTransform");
            }

            // SpecularGlossiness UV transforms
            if (material.SpecularGlossiness?.IsEnabled == true) {
                SetUVTransform(material.SpecularGlossiness.DiffuseTexture, shader, "u_DiffuseUVTransform");
                SetUVTransform(material.SpecularGlossiness.SpecularGlossinessTexture, shader, "u_SpecularGlossinessUVTransform");
            }
        }

        void SetUVTransform(MaterialTexture matTex, Shader shader, string uniformName) {
            if (matTex?.HasUVTransform == true) {
                // Convert Matrix3x2 to mat3 (3x3 matrix for UV transform)
                //
                // Matrix3x2 stores the 2D affine transform as:
                // | M11 M12 |
                // | M21 M22 |
                // | M31 M32 |  (M31, M32 = translation)
                //
                // This corresponds to a 3x3 matrix (row-major):
                // | M11 M12 0  |
                // | M21 M22 0  |
                // | M31 M32 1  |
                //
                // For UV transform, we need the translation in the 3rd column (column-major):
                // | M11 M21 0  |     | sx*cos  -sx*sin  0 |
                // | M12 M22 0  | --> | sy*sin   sy*cos  0 |
                // | M31 M32 1  |     | tx       ty      1 |
                //
                // But GLSL mat3 * vec3 expects translation in 3rd column:
                // | sx*cos  -sx*sin  tx |
                // | sy*sin   sy*cos  ty |
                // | 0        0       1  |
                //
                // So we need to construct the matrix differently:
                // Column 0: [M11, M21, 0]    (scale*rotation part 1)
                // Column 1: [M12, M22, 0]    (scale*rotation part 2)
                // Column 2: [M31, M32, 1]   (translation)
                shader.SetUniformMatrix3(
                    uniformName,
                    new Vector3(matTex.UVTransform.M11, matTex.UVTransform.M21, 0f),
                    new Vector3(matTex.UVTransform.M12, matTex.UVTransform.M22, 0f),
                    new Vector3(matTex.UVTransform.M31, matTex.UVTransform.M32, 1f)
                );
            }
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

        void UpdateMaterialUBO(Material material, bool useGeneratedTangents) {
            // 检查是否使用 SpecularGlossiness 工作流
            bool useSG = material?.SpecularGlossiness?.IsEnabled == true;
            MaterialData matData = new() {
                // ============ PBR Core ============
                // 对于 SpecularGlossiness 工作流，BaseColorFactor 存储 DiffuseFactor
                BaseColorFactor = useSG ? material.SpecularGlossiness.DiffuseFactor : material?.BaseColorFactor ?? Vector4.One,
                EmissiveFactor = new Vector4(material?.EmissiveFactor ?? Vector3.Zero, 0f),
                MetallicFactor = useSG ? 0f : material?.MetallicFactor ?? MaterialDefaults.MetallicFactor,
                // 对于 SpecularGlossiness 工作流，Roughness = 1 - Glossiness
                RoughnessFactor =
                    useSG ? 1f - material.SpecularGlossiness.GlossinessFactor : material?.RoughnessFactor ?? MaterialDefaults.RoughnessFactor,
                NormalScale = material?.NormalScale ?? MaterialDefaults.NormalScale,
                OcclusionStrength = material?.OcclusionStrength ?? MaterialDefaults.OcclusionStrength,

                // ============ Alpha ============
                AlphaMode = (int)(material?.AlphaMode ?? AlphaMode.Opaque),
                AlphaCutoff = material?.AlphaCutoff ?? MaterialDefaults.AlphaCutoff,
                UseGeneratedTangents = useGeneratedTangents ? 1 : 0,
                UnlitPadding0 = 0f,

                // ============ IOR ============
                Ior = material?.Ior?.IsEnabled == true ? material.Ior.Ior : MaterialDefaults.Ior,
                IorPadding0 = 0f,
                IorPadding1 = 0f,
                IorPadding2 = 0f,

                // ============ Emissive Strength ============
                EmissiveStrength = material?.EmissiveStrength?.IsEnabled == true
                    ? material.EmissiveStrength.EmissiveStrength
                    : MaterialDefaults.EmissiveStrength,
                EmissiveStrengthPadding0 = 0f,
                EmissiveStrengthPadding1 = 0f,
                EmissiveStrengthPadding2 = 0f,

                // ============ Specular ============
                // 对于 SpecularGlossiness 工作流，SpecularColorFactor 存储 SpecularFactor
                SpecularFactor = useSG ? 1f :
                    material?.Specular?.IsEnabled == true ? material.Specular.SpecularFactor : MaterialDefaults.SpecularFactor,
                SpecularPadding0 = 0f,
                SpecularPadding1 = 0f,
                SpecularPadding2 = 0f,
                SpecularColorFactor = useSG ? new Vector4(material.SpecularGlossiness.SpecularFactor, 1f) :
                    material?.Specular?.IsEnabled == true ? new Vector4(material.Specular.SpecularColorFactor, 1f) : Vector4.One,

                // ============ Sheen ============
                SheenColorFactor = material?.Sheen?.IsEnabled == true ? new Vector4(material.Sheen.ColorFactor, 1f) : Vector4.Zero,
                SheenRoughnessFactor = material?.Sheen?.IsEnabled == true ? material.Sheen.RoughnessFactor : MaterialDefaults.SheenRoughnessFactor,
                SheenPadding0 = 0f,
                SheenPadding1 = 0f,
                SheenPadding2 = 0f,

                // ============ ClearCoat ============
                ClearCoatFactor = material?.ClearCoat?.IsEnabled == true ? material.ClearCoat.Factor : 0f,
                ClearCoatRoughness = material?.ClearCoat?.IsEnabled == true ? material.ClearCoat.RoughnessFactor : 0f,
                ClearCoatNormalScale = material?.ClearCoat?.IsEnabled == true ? material.ClearCoat.NormalScale : 1f,
                ClearCoatPadding0 = 0f,

                // ============ Transmission ============
                TransmissionFactor = material?.Transmission?.IsEnabled == true ? material.Transmission.Factor : 0f,
                TransmissionPadding0 = 0f,
                TransmissionPadding1 = 0f,
                TransmissionPadding2 = 0f,

                // ============ Volume ============
                ThicknessFactor = material?.Volume?.IsEnabled == true ? material.Volume.ThicknessFactor : 0f,
                AttenuationDistance = material?.Volume?.IsEnabled == true ? material.Volume.AttenuationDistance : 0f,
                VolumePadding0 = 0f,
                VolumePadding1 = 0f,
                AttenuationColor = material?.Volume?.IsEnabled == true ? new Vector4(material.Volume.AttenuationColor, 1f) : Vector4.One,

                // ============ Iridescence ============
                IridescenceFactor = material?.Iridescence?.IsEnabled == true ? material.Iridescence.Factor : 0f,
                IridescenceIor = material?.Iridescence?.IsEnabled == true ? material.Iridescence.IOR : 1.3f,
                IridescenceThicknessMin = material?.Iridescence?.IsEnabled == true ? material.Iridescence.ThicknessMinimum : 100f,
                IridescenceThicknessMax = material?.Iridescence?.IsEnabled == true ? material.Iridescence.ThicknessMaximum : 400f,

                // ============ Dispersion ============
                Dispersion = material?.Dispersion?.IsEnabled == true ? material.Dispersion.Dispersion : MaterialDefaults.Dispersion,
                DispersionPadding0 = 0f,
                DispersionPadding1 = 0f,
                DispersionPadding2 = 0f,

                // ============ Diffuse Transmission ============
                DiffuseTransmissionFactor =
                    material?.DiffuseTransmission?.IsEnabled == true
                        ? material.DiffuseTransmission.Factor
                        : MaterialDefaults.DiffuseTransmissionFactor,
                DiffuseTransmissionPadding0 = 0f,
                DiffuseTransmissionPadding1 = 0f,
                DiffuseTransmissionPadding2 = 0f,
                DiffuseTransmissionColorFactor = material?.DiffuseTransmission?.IsEnabled == true
                    ? new Vector4(material.DiffuseTransmission.ColorFactor, 1f)
                    : Vector4.One,

                // ============ Anisotropy ============
                Anisotropy = material?.Anisotropy?.IsEnabled == true
                    ? new Vector4(
                        MathF.Cos(material.Anisotropy.AnisotropyRotation),
                        MathF.Sin(material.Anisotropy.AnisotropyRotation),
                        material.Anisotropy.AnisotropyStrength,
                        0f
                    )
                    : Vector4.Zero,

                // ============ UV Sets ============
                BaseColorUVSet = material?.BaseColorTexture?.UVIndex ?? 0,
                MetallicRoughnessUVSet = material?.MetallicRoughnessTexture?.UVIndex ?? 0,
                NormalUVSet = material?.NormalTexture?.UVIndex ?? 0,
                OcclusionUVSet = material?.OcclusionTexture?.UVIndex ?? 0,
                EmissiveUVSet = material?.EmissiveTexture?.UVIndex ?? 0,
                DiffuseUVSet = material?.SpecularGlossiness?.DiffuseTexture?.UVIndex ?? 0,
                SpecularGlossinessUVSet = material?.SpecularGlossiness?.SpecularGlossinessTexture?.UVIndex ?? 0,
                UVPadding0 = 0,
                ClearCoatUVSet = material?.ClearCoat?.Texture?.UVIndex ?? 0,
                ClearCoatRoughnessUVSet = material?.ClearCoat?.RoughnessTexture?.UVIndex ?? 0,
                ClearCoatNormalUVSet = material?.ClearCoat?.NormalTexture?.UVIndex ?? 0,
                IridescenceUVSet = material?.Iridescence?.Texture?.UVIndex ?? 0,
                IridescenceThicknessUVSet = material?.Iridescence?.ThicknessTexture?.UVIndex ?? 0,
                SheenColorUVSet = material?.Sheen?.ColorTexture?.UVIndex ?? 0,
                SheenRoughnessUVSet = material?.Sheen?.RoughnessTexture?.UVIndex ?? 0,
                SpecularUVSet = material?.Specular?.SpecularTexture?.UVIndex ?? 0,
                SpecularColorUVSet = material?.Specular?.SpecularColorTexture?.UVIndex ?? 0,
                TransmissionUVSet = material?.Transmission?.Texture?.UVIndex ?? 0,
                ThicknessUVSet = material?.Volume?.ThicknessTexture?.UVIndex ?? 0,
                DiffuseTransmissionUVSet = material?.DiffuseTransmission?.Texture?.UVIndex ?? 0,
                DiffuseTransmissionColorUVSet = material?.DiffuseTransmission?.ColorTexture?.UVIndex ?? 0,
                AnisotropyUVSet = material?.Anisotropy?.AnisotropyTexture?.UVIndex ?? 0,
                UVSetPadding0 = 0,
                UVSetPadding1 = 0,

                // ============ Flags ============
                ExtensionFlags = (int)BuildExtensionFlags(material),
                TextureFlags = (int)BuildTextureFlags(material),
                FlagsPadding0 = 0,
                FlagsPadding1 = 0,

                // ============ SpecularGlossiness ============
                SpecularFactorSG = useSG ? new Vector4(material.SpecularGlossiness.SpecularFactor, 1f) : Vector4.One,
                GlossinessFactor = useSG ? material.SpecularGlossiness.GlossinessFactor : 1f
            };
            _materialUBO.Update(ref matData);
        }

        ExtensionFlags BuildExtensionFlags(Material material) {
            if (material == null) {
                return ExtensionFlags.MetallicRoughness;
            }
            ExtensionFlags flags = ExtensionFlags.MetallicRoughness;
            if (material.ClearCoat?.IsEnabled == true) {
                flags |= ExtensionFlags.ClearCoat;
            }
            if (material.Iridescence?.IsEnabled == true) {
                flags |= ExtensionFlags.Iridescence;
            }
            if (material.Transmission?.IsEnabled == true) {
                flags |= ExtensionFlags.Transmission;
            }
            if (material.Volume?.IsEnabled == true) {
                flags |= ExtensionFlags.Volume;
            }
            if (material.Sheen?.IsEnabled == true) {
                flags |= ExtensionFlags.Sheen;
            }
            if (material.Specular?.IsEnabled == true) {
                flags |= ExtensionFlags.Specular;
            }
            if (material.Ior?.IsEnabled == true) {
                flags |= ExtensionFlags.Ior;
            }
            if (material.EmissiveStrength?.IsEnabled == true) {
                flags |= ExtensionFlags.EmissiveStrength;
            }
            if (material.Dispersion?.IsEnabled == true) {
                flags |= ExtensionFlags.Dispersion;
            }
            if (material.Anisotropy?.IsEnabled == true) {
                flags |= ExtensionFlags.Anisotropy;
            }
            if (material.DiffuseTransmission?.IsEnabled == true) {
                flags |= ExtensionFlags.DiffuseTransmission;
            }
            if (material.VolumeScatter?.IsEnabled == true) {
                flags |= ExtensionFlags.VolumeScatter;
            }
            if (material.Unlit?.IsEnabled == true) {
                flags |= ExtensionFlags.Unlit;
            }
            return flags;
        }

        TextureFlags BuildTextureFlags(Material material) {
            if (material == null) {
                return TextureFlags.None;
            }
            TextureFlags flags = TextureFlags.None;
            if (material.BaseColorTexture != null) {
                flags |= TextureFlags.BaseColor;
            }
            if (material.MetallicRoughnessTexture != null) {
                flags |= TextureFlags.MetallicRoughness;
            }
            if (material.NormalTexture != null) {
                flags |= TextureFlags.Normal;
            }
            if (material.OcclusionTexture != null) {
                flags |= TextureFlags.Occlusion;
            }
            if (material.EmissiveTexture != null) {
                flags |= TextureFlags.Emissive;
            }
            if (material.ClearCoat?.IsEnabled == true) {
                if (material.ClearCoat.Texture != null) {
                    flags |= TextureFlags.ClearCoat;
                }
                if (material.ClearCoat.RoughnessTexture != null) {
                    flags |= TextureFlags.ClearCoatRoughness;
                }
                if (material.ClearCoat.NormalTexture != null) {
                    flags |= TextureFlags.ClearCoatNormal;
                }
            }
            if (material.Iridescence?.IsEnabled == true) {
                if (material.Iridescence.Texture != null) {
                    flags |= TextureFlags.Iridescence;
                }
                if (material.Iridescence.ThicknessTexture != null) {
                    flags |= TextureFlags.IridescenceThickness;
                }
            }
            if (material.Transmission?.IsEnabled == true) {
                if (material.Transmission.Texture != null) {
                    flags |= TextureFlags.Transmission;
                }
            }
            if (material.Volume?.IsEnabled == true) {
                if (material.Volume.ThicknessTexture != null) {
                    flags |= TextureFlags.Thickness;
                }
            }
            if (material.Sheen?.IsEnabled == true) {
                if (material.Sheen.ColorTexture != null) {
                    flags |= TextureFlags.SheenColor;
                }
                if (material.Sheen.RoughnessTexture != null) {
                    flags |= TextureFlags.SheenRoughness;
                }
            }
            if (material.Specular?.IsEnabled == true) {
                if (material.Specular.SpecularTexture != null) {
                    flags |= TextureFlags.Specular;
                }
                if (material.Specular.SpecularColorTexture != null) {
                    flags |= TextureFlags.SpecularColor;
                }
            }
            if (material.Anisotropy?.IsEnabled == true) {
                if (material.Anisotropy.AnisotropyTexture != null) {
                    flags |= TextureFlags.Anisotropy;
                }
            }
            if (material.DiffuseTransmission?.IsEnabled == true) {
                if (material.DiffuseTransmission.Texture != null) {
                    flags |= TextureFlags.DiffuseTransmission;
                }
                if (material.DiffuseTransmission.ColorTexture != null) {
                    flags |= TextureFlags.DiffuseTransmissionColor;
                }
            }
            return flags;
        }

        void BindTexture(MaterialTexture matTex, MaterialTextureSlot slot) {
            if (matTex?.Texture != null) {
                matTex.Texture.Bind((TextureUnit)((int)TextureUnit.Texture0 + (int)slot));
            }
        }

        void SetCullMode(Material material, Model.MeshInstance instance) {
            if (material.DoubleSided) {
                _gl.Disable(EnableCap.CullFace);
            }
            else {
                _gl.Enable(EnableCap.CullFace);
                _gl.FrontFace(instance.IsNegativeScale ? FrontFaceDirection.CW : FrontFaceDirection.Ccw);
            }
        }

        void SetBlendMode(Material material) {
            if (material.AlphaMode == AlphaMode.Blend) {
                _gl.Enable(EnableCap.Blend);
                // 使用与官方渲染器一致的 blendFuncSeparate
                // RGB: src * srcAlpha + dst * (1 - srcAlpha)
                // Alpha: 1 * srcAlpha + dst * (1 - srcAlpha)
                _gl.BlendFuncSeparate(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha, BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha);
                _gl.BlendEquation(BlendEquationModeEXT.FuncAdd);
            }
            else {
                _gl.Disable(EnableCap.Blend);
            }
        }

        public void Dispose() {
            Model?.Dispose();
            Skybox?.Dispose();
            _environmentTexture?.Dispose();
            _iblSampler?.Dispose();
            MainShader?.Dispose();
            SkyShader?.Dispose();
            _sceneUBO?.Dispose();
            _materialUBO?.Dispose();
            _lightsUBO?.Dispose();
            _transmissionFramebuffer?.Dispose();
        }
    }
}