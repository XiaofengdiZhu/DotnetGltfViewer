using System;
using System.Numerics;
using ZLogger;

namespace DotnetGltfRenderer {
    /// <summary>
    /// 渲染器入口
    /// 协调所有渲染子系统，提供统一的渲染接口
    /// </summary>
    public class Renderer : IDisposable {
        // Uniform Buffer Objects
        readonly UniformBuffer<SceneData> _sceneUBO;
        readonly UniformBuffer<MaterialCoreData> _materialCoreUBO;
        readonly UniformBuffer<MaterialExtensionData> _materialExtUBO;
        readonly UniformBuffer<LightsData> _lightsUBO;
        readonly UniformBuffer<RenderStateData> _renderStateUBO;
        readonly UniformBuffer<UVTransformData> _uvTransformUBO;
        readonly UniformBuffer<VolumeScatterData> _volumeScatterUBO;

        // Subsystems
        readonly RenderPassManager _renderPassManager;

        // Frame state
        int _framebufferWidth = -1;
        int _framebufferHeight = -1;
        float _framebufferAspectRatio = -1.0f;

        /// <summary>
        /// 相机
        /// </summary>
        public Camera Camera { get; }

        /// <summary>
        /// 场景
        /// </summary>
        public Scene Scene { get; }

        /// <summary>
        /// 光照系统
        /// </summary>
        public LightingSystem LightingSystem { get; }

        /// <summary>
        /// IBL 管理器
        /// </summary>
        public IBLManager IBLManager { get; }

        /// <summary>
        /// 天空盒
        /// </summary>
        public Skybox Skybox { get; }

        /// <summary>
        /// 天空渲染器
        /// </summary>
        public SkyRenderer SkyRenderer { get; private set; }

        /// <summary>
        /// 帧缓冲区管理器
        /// </summary>
        public FramebufferManager FramebufferManager { get; }

        /// <summary>
        /// 色调映射模式
        /// </summary>
        public ToneMapMode ToneMapMode { get; set; } = ToneMapMode.KhrPbrNeutral;

        /// <summary>
        /// 创建渲染器
        /// </summary>
        /// <param name="scene">场景</param>
        /// <param name="environmentTexturePath">HDR 环境贴图路径</param>
        /// <param name="shadersDirectory">着色器目录</param>
        public Renderer(Scene scene, string environmentTexturePath, string shadersDirectory) {
            Scene = scene;
            Camera = new Camera();
            LightingSystem = new LightingSystem();
            IBLManager = new IBLManager();
            Skybox = new Skybox();

            // 收集场景光源
            UpdateLightsFromScene();

            // 初始化着色器缓存
            ShaderCache.Initialize(shadersDirectory);

            // 创建 UBO
            _sceneUBO = new UniformBuffer<SceneData>(0);
            _materialCoreUBO = new UniformBuffer<MaterialCoreData>(1);
            _materialExtUBO = new UniformBuffer<MaterialExtensionData>(6);
            _lightsUBO = new UniformBuffer<LightsData>(2);
            _renderStateUBO = new UniformBuffer<RenderStateData>(3);
            _uvTransformUBO = new UniformBuffer<UVTransformData>(4);
            _volumeScatterUBO = new UniformBuffer<VolumeScatterData>(5);

            // 加载环境贴图
            IBLManager.Load(environmentTexturePath);

            // 初始化子系统
            FramebufferManager = new FramebufferManager();
            MeshInstanceRenderer meshInstanceRenderer = new(
                _materialCoreUBO,
                _materialExtUBO,
                _sceneUBO,
                _lightsUBO,
                _renderStateUBO,
                _uvTransformUBO,
                _volumeScatterUBO
            );
            _renderPassManager = new RenderPassManager(FramebufferManager, meshInstanceRenderer);

            // 初始化天空渲染器
            InitializeSkyRenderer();
        }

        void InitializeSkyRenderer() {
            try {
                ShaderDefines skyVertDefines = new();
                ShaderDefines skyFragDefines = new();
                int skyVertHash = ShaderCache.SelectShader("cubemap.vert", skyVertDefines.GetDefinesList());
                int skyFragHash = ShaderCache.SelectShader("cubemap.frag", skyFragDefines.GetDefinesList());
                Shader skyShader = ShaderCache.GetShaderProgram(skyVertHash, skyFragHash);
                SkyRenderer = new SkyRenderer(Skybox, skyShader);
            }
            catch (Exception ex) {
                LogManager.Logger.ZLogError($"Failed to initialize sky renderer: {ex.Message}");
            }
        }

        /// <summary>
        /// 设置帧缓冲区尺寸
        /// </summary>
        public void SetFramebufferSize(int width, int height) {
            _framebufferWidth = width;
            _framebufferHeight = height;
            _framebufferAspectRatio = (float)width / height;
            FramebufferManager?.SetSize(width, height);
        }

        /// <summary>
        /// 设置环境贴图
        /// </summary>
        public void SetEnvironmentMap(string environmentTexturePath) {
            IBLManager.Load(environmentTexturePath);
        }

        /// <summary>
        /// 从场景更新光源
        /// </summary>
        public void UpdateLightsFromScene() {
            LightingSystem.ClearLights();
            if (Scene != null) {
                foreach (SceneModel sceneModel in Scene.Models) {
                    if (!sceneModel.IsVisible) {
                        continue;
                    }
                    foreach (Light light in sceneModel.Model.Lights) {
                        LightingSystem.AddLight(light);
                    }
                }
            }
        }

        /// <summary>
        /// 执行渲染
        /// </summary>
        public void Render() {
            if (Scene == null) {
                return;
            }

            // 计算视图投影矩阵
            Matrix4x4 view = Camera.ViewMatrix;
            Matrix4x4 projection = Camera.GetProjectionMatrix(_framebufferAspectRatio);

            // 排序渲染队列
            Scene.SortByDepth(view);

            // 构建渲染上下文
            RenderContext context = new() {
                View = view,
                Projection = projection,
                UseIBL = IBLManager.IsEnabled,
                ToneMapMode = ToneMapMode,
                LightCount = LightingSystem.Lights.Count,
                FramebufferWidth = _framebufferWidth,
                FramebufferHeight = _framebufferHeight,
                HasTransmissionFramebuffer = FramebufferManager.HasTransmissionFramebuffer,
                HasScatterFramebuffer = FramebufferManager.HasScatterFramebuffer
            };

            // 更新 Scene UBO
            UpdateSceneUBO();

            // 更新 Lights UBO
            LightsData lightsData = LightingSystem.GetLightsData();
            _lightsUBO.Update(ref lightsData);

            // 绑定 IBL 纹理（只需绑定一次）
            if (IBLManager.IsEnabled) {
                MaterialTextureBinder.BindIBLTextures(IBLManager.IblSampler);
            }

            // === Scatter Pass ===
            if (Scene.HasScatterInstances) {
                _renderPassManager.ExecuteScatterPass(Scene.ScatterInstances, in context);
            }

            // === Transmission Pass ===
            if (Scene.HasTransmissionInstances) {
                _renderPassManager.ExecuteTransmissionPass(Scene, in context, (v, p) => RenderSkyLinear(v, p));
            }

            // === Main Pass ===
            _renderPassManager.ExecuteMainPass(
                Scene,
                in context,
                (v, p) => RenderSky(v, p),
                () => FramebufferManager.BindScatterTextures(),
                () => FramebufferManager.BindTransmissionTexture()
            );
        }

        void UpdateSceneUBO() {
            // EnvRotation is identity matrix by default
            // Can be modified for environment map rotation
            SceneData sceneData = new() {
                CameraPos = new Vector4(Camera.Position, 0f),
                Exposure = LightingSystem.Exposure,
                EnvironmentStrength = IBLManager.EnvironmentStrength,
                MipCount = IBLManager.MipCount,
                Padding0 = 0f,
                // Identity rotation matrix columns
                EnvRotationCol0 = new Vector4(1f, 0f, 0f, 0f),
                EnvRotationCol1 = new Vector4(0f, 1f, 0f, 0f),
                EnvRotationCol2 = new Vector4(0f, 0f, 1f, 0f)
            };
            _sceneUBO.Update(ref sceneData);
        }

        /// <summary>
        /// 渲染天空盒
        /// </summary>
        public void RenderSky(Matrix4x4 view,
            Matrix4x4 projection,
            float envIntensity = 1.0f,
            float envBlur = 0.0f,
            float envRotationDegrees = 0.0f) {
            SkyRenderer?.Render(
                view,
                projection,
                envIntensity * IBLManager.EnvironmentStrength,
                envBlur,
                envRotationDegrees,
                LightingSystem.Exposure,
                IBLManager.IblSampler
            );
        }

        /// <summary>
        /// 渲染天空盒（线性输出模式）
        /// </summary>
        public void RenderSkyLinear(Matrix4x4 view, Matrix4x4 projection, float envIntensity = 1.0f) {
            SkyRenderer?.RenderLinear(
                view,
                projection,
                envIntensity * IBLManager.EnvironmentStrength,
                LightingSystem.Exposure,
                IBLManager.IblSampler
            );
        }

        public void Dispose() {
            Scene?.Dispose();
            Skybox?.Dispose();
            IBLManager?.Dispose();
            _sceneUBO?.Dispose();
            _materialCoreUBO?.Dispose();
            _materialExtUBO?.Dispose();
            _lightsUBO?.Dispose();
            _renderStateUBO?.Dispose();
            _uvTransformUBO?.Dispose();
            _volumeScatterUBO?.Dispose();
            FramebufferManager?.Dispose();
            SkyRenderer = null;
        }
    }
}