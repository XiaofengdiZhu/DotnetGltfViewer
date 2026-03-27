using System;
using System.Collections.Generic;
using System.IO;
using DotnetGltfRenderer;

namespace DotnetGltfViewer.Windows.Sidebar;

/// <summary>
/// 侧边栏状态管理类
/// </summary>
public class SidebarState {
    // ========== Models Tab ==========
    /// <summary>所有可用模型名称（Models 文件夹下的子文件夹名称，排除 Environments）</summary>
    public List<string> AvailableModels { get; private set; } = new();

    /// <summary>当前选中的模型索引</summary>
    public int SelectedModelIndex { get; set; } = -1;

    /// <summary>当前模型的 Flavor 选项</summary>
    public List<string> AvailableFlavors { get; private set; } = new();

    /// <summary>当前选中的 Flavor 索引</summary>
    public int SelectedFlavorIndex { get; set; } = -1;

    /// <summary>当前模型的场景列表</summary>
    public List<string> AvailableScenes { get; private set; } = new();

    /// <summary>当前选中的场景索引</summary>
    public int SelectedSceneIndex { get; set; } = -1;

    /// <summary>当前模型的 Variants 列表</summary>
    public List<string> AvailableVariants { get; private set; } = new();

    /// <summary>当前选中的 Variant 索引（-1 表示无变体或未选择）</summary>
    public int SelectedVariantIndex { get; set; } = -1;

    // ========== Display Tab - Lighting ==========
    /// <summary>Image Based Lighting 开关</summary>
    public bool UseIBL { get; set; } = true;

    /// <summary>Punctual Lighting 开关</summary>
    public bool UsePunctualLighting { get; set; } = true;

    /// <summary>IBL 强度</summary>
    public float IBLIntensity { get; set; } = 1.0f;

    /// <summary>曝光度</summary>
    public float Exposure { get; set; } = 1.0f;

    /// <summary>天空盒亮度（独立于模型反射强度）</summary>
    public float SkyboxIntensity { get; set; } = 1.0f;

    /// <summary>色调映射模式索引</summary>
    public int ToneMapIndex { get; set; } = 0;

    /// <summary>色调映射模式名称列表</summary>
    public static readonly string[] ToneMapModes = {
        "Khr Pbr Neutral",
        "ACES Narkowicz",
        "ACES Hill",
        "ACES Hill Exposure Boost",
        "None"
    };

    // ========== Display Tab - Background ==========
    /// <summary>Skybox 开关</summary>
    public bool ShowSkybox { get; set; } = true;

    /// <summary>Skybox Blur 模糊程度 (0.0 = 无模糊, 1.0 = 最大模糊)</summary>
    public float SkyboxBlur { get; set; } = 0.5f;

    /// <summary>背景颜色</summary>
    public System.Numerics.Vector3 BackgroundColor { get; set; } = new(0.1f, 0.1f, 0.1f);

    /// <summary>Skybox 旋转索引（0: +X, 1: +Z, 2: -X, 3: -Z）</summary>
    public int SkyboxRotationIndex { get; set; } = 1;

    /// <summary>Skybox 旋转名称列表</summary>
    public static readonly string[] SkyboxRotations = { "+X", "+Z", "-X", "-Z" };

    /// <summary>可用环境贴图列表</summary>
    public List<string> AvailableEnvironments { get; private set; } = new();

    /// <summary>当前选中的环境贴图索引</summary>
    public int SelectedEnvironmentIndex { get; set; } = 0;

    // ========== Animation Tab ==========
    /// <summary>动画是否暂停</summary>
    public bool IsAnimationPaused { get; set; } = false;

    /// <summary>可用动画列表</summary>
    public List<string> AvailableAnimations { get; private set; } = new();

    /// <summary>当前选中的动画索引</summary>
    public int SelectedAnimationIndex { get; set; } = -1;

    // ========== Advanced Controls Tab ==========
    /// <summary>Debug Channel 索引（映射到 DebugChannel 枚举值）</summary>
    public int DebugChannelIndex { get; set; } = 0;

    /// <summary>Debug Channel 名称列表</summary>
    public static readonly string[] DebugChannels = {
        "None",
        "Normal",
        "Normal Map",
        "Geometric Normal",
        "Tangent",
        "Bitangent",
        "Base Color",
        "Base Color Alpha",
        "Metallic",
        "Roughness",
        "Occlusion",
        "Emissive",
        "Clearcoat",
        "Clearcoat Roughness",
        "Sheen",
        "Transmission",
        "Volume Thickness"
    };

    /// <summary>Debug Channel UI 索引到枚举值的映射</summary>
    public static readonly DebugChannel[] DebugChannelMapping = {
        DebugChannel.None,           // 0
        DebugChannel.Normal,         // 1
        DebugChannel.NormalMap,      // 2
        DebugChannel.GeometricNormal,// 3
        DebugChannel.Tangent,        // 4
        DebugChannel.Bitangent,      // 5
        DebugChannel.BaseColor,      // 6
        DebugChannel.BaseColorAlpha, // 7
        DebugChannel.Metallic,       // 8
        DebugChannel.Roughness,      // 9
        DebugChannel.Occlusion,      // 10
        DebugChannel.Emissive,       // 11
        DebugChannel.Clearcoat,      // 12
        DebugChannel.ClearcoatRoughness, // 13
        DebugChannel.Sheen,          // 14
        DebugChannel.Transmission,   // 15
        DebugChannel.VolumeThickness // 16
    };

    /// <summary>Skinning 开关</summary>
    public bool EnableSkinning { get; set; } = true;

    /// <summary>Morphing 开关</summary>
    public bool EnableMorphing { get; set; } = true;

    /// <summary>材质扩展开关状态</summary>
    public Dictionary<string, bool> ExtensionEnabled { get; } = new();

    // ========== Statistics ==========
    /// <summary>模型数量</summary>
    public int ModelCount { get; set; }

    /// <summary>Mesh 数量</summary>
    public int MeshCount { get; set; }

    /// <summary>三角形数量</summary>
    public int TriangleCount { get; set; }

    /// <summary>不透明材质数量</summary>
    public int OpaqueMaterialCount { get; set; }

    /// <summary>透明材质数量</summary>
    public int TransparentMaterialCount { get; set; }

    // ========== UI State ==========
    /// <summary>侧边栏是否可见</summary>
    public bool IsVisible { get; set; } = true;

    /// <summary>当前选中的 Tab（0: Models, 1: Display, 2: Animation, 3: Advanced Controls）</summary>
    public int SelectedTabIndex { get; set; } = 0;

    /// <summary>
    /// 初始化侧边栏状态
    /// </summary>
    public void Initialize() {
        ScanAvailableModels();
        ScanAvailableEnvironments();
        InitializeExtensionStates();

        // 默认选择 DamagedHelmet
        int damagedHelmetIndex = AvailableModels.IndexOf("DamagedHelmet");
        if (damagedHelmetIndex >= 0) {
            SelectedModelIndex = damagedHelmetIndex;
            ScanAvailableFlavors();
        }
    }

    /// <summary>
    /// 扫描可用模型
    /// </summary>
    public void ScanAvailableModels() {
        AvailableModels.Clear();
        string modelsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Models");
        if (!Directory.Exists(modelsPath)) {
            return;
        }
        foreach (string dir in Directory.GetDirectories(modelsPath)) {
            string name = Path.GetFileName(dir);
            AvailableModels.Add(name);
        }
        AvailableModels.Sort();
    }

    /// <summary>
    /// 扫描选中模型的 Flavor 选项
    /// </summary>
    public void ScanAvailableFlavors() {
        AvailableFlavors.Clear();
        SelectedFlavorIndex = -1;

        if (SelectedModelIndex < 0 || SelectedModelIndex >= AvailableModels.Count) {
            return;
        }

        string modelName = AvailableModels[SelectedModelIndex];
        string modelPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Models", modelName);
        if (!Directory.Exists(modelPath)) {
            return;
        }

        foreach (string dir in Directory.GetDirectories(modelPath)) {
            string name = Path.GetFileName(dir);
            if (name.StartsWith("glTF", StringComparison.OrdinalIgnoreCase)) {
                AvailableFlavors.Add(name);
            }
        }
        AvailableFlavors.Sort();

        // 默认选择第一个
        if (AvailableFlavors.Count > 0) {
            SelectedFlavorIndex = 0;
        }
    }

    /// <summary>
    /// 扫描可用环境贴图
    /// </summary>
    public void ScanAvailableEnvironments() {
        AvailableEnvironments.Clear();
        string envPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Environments");
        if (!Directory.Exists(envPath)) {
            return;
        }
        foreach (string file in Directory.GetFiles(envPath, "*.hdr")) {
            string name = Path.GetFileNameWithoutExtension(file);
            AvailableEnvironments.Add(name);
        }
        AvailableEnvironments.Sort();

        // 设置默认值 Cannon_Exterior
        int cannonIndex = AvailableEnvironments.IndexOf("Cannon_Exterior");
        SelectedEnvironmentIndex = cannonIndex >= 0 ? cannonIndex : (AvailableEnvironments.Count > 0 ? 0 : -1);
    }

    /// <summary>
    /// 初始化材质扩展开关状态
    /// </summary>
    void InitializeExtensionStates() {
        string[] materialExtensions = {
            "KHR_materials_clearcoat",
            "KHR_materials_iridescence",
            "KHR_materials_transmission",
            "KHR_materials_volume",
            "KHR_materials_sheen",
            "KHR_materials_specular",
            "KHR_materials_ior",
            "KHR_materials_emissive_strength",
            "KHR_materials_dispersion",
            "KHR_materials_anisotropy",
            "KHR_materials_diffuse_transmission",
            "KHR_materials_volume_scatter",
            "KHR_materials_unlit",
            "KHR_materials_pbrSpecularGlossiness"
        };
        foreach (string ext in materialExtensions) {
            ExtensionEnabled[ext] = true;
        }
    }

    /// <summary>
    /// 更新可用场景列表
    /// </summary>
    public void UpdateAvailableScenes(IReadOnlyList<string> sceneNames) {
        AvailableScenes.Clear();
        if (sceneNames == null) {
            return;
        }
        for (int i = 0; i < sceneNames.Count; i++) {
            AvailableScenes.Add($"Scene {i}");
        }
        SelectedSceneIndex = 0;
    }

    /// <summary>
    /// 更新可用动画列表
    /// </summary>
    public void UpdateAvailableAnimations(IReadOnlyList<string> animationNames) {
        AvailableAnimations.Clear();
        if (animationNames == null) {
            return;
        }
        for (int i = 0; i < animationNames.Count; i++) {
            string name = string.IsNullOrEmpty(animationNames[i]) ? "Animation" : animationNames[i];
            AvailableAnimations.Add($"[{i + 1}] {name}");
        }
        SelectedAnimationIndex = AvailableAnimations.Count > 0 ? 0 : -1;
    }

    /// <summary>
    /// 更新可用变体列表
    /// </summary>
    public void UpdateAvailableVariants(IReadOnlyList<string> variantNames, int activeIndex) {
        AvailableVariants.Clear();
        if (variantNames == null) {
            SelectedVariantIndex = -1;
            return;
        }
        AvailableVariants.Add("None");
        for (int i = 0; i < variantNames.Count; i++) {
            string name = string.IsNullOrEmpty(variantNames[i]) ? "Variant" : variantNames[i];
            AvailableVariants.Add($"[{i + 1}] {name}");
        }
        SelectedVariantIndex = activeIndex >= 0 && activeIndex < variantNames.Count ? activeIndex + 1 : 0;
    }

    /// <summary>
    /// 获取当前选中模型的路径
    /// </summary>
    public string GetSelectedModelPath() {
        if (SelectedModelIndex < 0 || SelectedModelIndex >= AvailableModels.Count) {
            return null;
        }
        string modelName = AvailableModels[SelectedModelIndex];
        if (SelectedFlavorIndex < 0 || SelectedFlavorIndex >= AvailableFlavors.Count) {
            return null;
        }
        string flavor = AvailableFlavors[SelectedFlavorIndex];

        // 尝试 .gltf
        string gltfPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Models", modelName, flavor, $"{modelName}.gltf");
        if (File.Exists(gltfPath)) {
            return gltfPath;
        }

        // 尝试 .glb
        string glbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Models", modelName, flavor, $"{modelName}.glb");
        if (File.Exists(glbPath)) {
            return glbPath;
        }

        return null;
    }

    /// <summary>
    /// 获取当前选中的环境贴图路径
    /// </summary>
    public string GetSelectedEnvironmentPath() {
        if (SelectedEnvironmentIndex < 0 || SelectedEnvironmentIndex >= AvailableEnvironments.Count) {
            return null;
        }
        string envName = AvailableEnvironments[SelectedEnvironmentIndex];
        return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Environments", $"{envName}.hdr");
    }
}
