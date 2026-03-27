using System.Numerics;
using DotnetGltfRenderer;
using Hexa.NET.ImGui;
using ZLogger;

namespace DotnetGltfViewer.Windows.Sidebar;

/// <summary>
/// Display Tab 渲染
/// </summary>
public static class DisplayTab {
    static Renderer _renderer;
    static SidebarState _state;

    public static void Initialize(Renderer renderer, SidebarState state) {
        _renderer = renderer;
        _state = state;
    }

    public static void Render() {
        if (!ImGui.BeginTabItem("Display")) {
            return;
        }

        RenderLightingSection();
        ImGui.Separator();
        RenderBackgroundSection();

        ImGui.EndTabItem();
    }

    static void RenderLightingSection() {
        ImGui.Text("Lighting");

        // Image Based Lighting 开关
        bool useIBL = _state.UseIBL;
        if (ImGui.Checkbox("Image Based Lighting", ref useIBL)) {
            _state.UseIBL = useIBL;
            ApplyIBLSetting();
        }

        // Punctual Lighting 开关
        bool usePunctual = _state.UsePunctualLighting;
        if (ImGui.Checkbox("Punctual Lighting", ref usePunctual)) {
            _state.UsePunctualLighting = usePunctual;
            // TODO: Apply punctual lighting setting
        }

        // IBL Intensity 滑动条（只影响模型反射）
        float iblIntensity = _state.IBLIntensity;
        ImGui.SetNextItemWidth(380);
        if (ImGui.SliderFloat("IBL Intensity", ref iblIntensity, 0.0f, 10.0f)) {
            _state.IBLIntensity = iblIntensity;
            ApplyIBLSetting();
        }

        // Exposure 滑动条
        float exposure = _state.Exposure;
        ImGui.SetNextItemWidth(380);
        if (ImGui.SliderFloat("Exposure", ref exposure, 0.0f, 5.0f)) {
            _state.Exposure = exposure;
            ApplyExposureSetting();
        }

        // Tone Map 下拉菜单
        ImGui.Text("Tone Map");
        ImGui.SetNextItemWidth(-1);
        int toneMapIndex = _state.ToneMapIndex;
        if (ImGui.Combo("##ToneMap", ref toneMapIndex, SidebarState.ToneMapModes, SidebarState.ToneMapModes.Length)) {
            _state.ToneMapIndex = toneMapIndex;
            ApplyToneMapSetting();
        }
    }

    static void RenderBackgroundSection() {
        ImGui.Text("Background");

        // Skybox 开关
        bool showSkybox = _state.ShowSkybox;
        if (ImGui.Checkbox("Skybox", ref showSkybox)) {
            _state.ShowSkybox = showSkybox;
            ApplyEnvironmentMapVisibility();
        }

        // Skybox Intensity 滑动条（只影响天空盒亮度）
        if (showSkybox) {
            float skyboxIntensity = _state.SkyboxIntensity;
            ImGui.SetNextItemWidth(380);
            if (ImGui.SliderFloat("Skybox Intensity", ref skyboxIntensity, 0.0f, 10.0f)) {
                _state.SkyboxIntensity = skyboxIntensity;
                ApplySkyboxIntensity();
            }

            // Skybox Blur 滑动条
            float skyboxBlur = _state.SkyboxBlur;
            ImGui.SetNextItemWidth(380);
            if (ImGui.SliderFloat("Skybox Blur", ref skyboxBlur, 0.0f, 1.0f)) {
                _state.SkyboxBlur = skyboxBlur;
                ApplySkyboxBlur();
            }
        }

        // Background Color 颜色选择器
        Vector3 bgColor = _state.BackgroundColor;
        if (ImGui.ColorEdit3("Background Color", ref bgColor)) {
            _state.BackgroundColor = bgColor;
            ApplyBackgroundColor();
        }

        // Skybox Rotation 下拉菜单
        ImGui.Text("Skybox Rotation");
        ImGui.SetNextItemWidth(-1);
        int envRotIndex = _state.SkyboxRotationIndex;
        if (ImGui.Combo("##EnvRotation", ref envRotIndex, SidebarState.SkyboxRotations, SidebarState.SkyboxRotations.Length)) {
            _state.SkyboxRotationIndex = envRotIndex;
            ApplySkyboxRotation();
        }

        // Active Skybox 下拉菜单
        ImGui.Text("Active Skybox");
        ImGui.SetNextItemWidth(-1);
        int envIndex = _state.SelectedEnvironmentIndex;
        if (ImGui.Combo("##Environment", ref envIndex, _state.AvailableEnvironments.ToArray(), _state.AvailableEnvironments.Count)) {
            _state.SelectedEnvironmentIndex = envIndex;
            ApplyEnvironmentMap();
        }
    }

    static void ApplyIBLSetting() {
        if (_renderer?.IBLManager != null) {
            _renderer.IBLManager.EnvironmentStrength = _state.UseIBL ? _state.IBLIntensity : 0.0f;
        }
    }

    static void ApplyExposureSetting() {
        if (_renderer?.LightingSystem != null) {
            _renderer.LightingSystem.Exposure = _state.Exposure;
        }
    }

    static void ApplySkyboxIntensity() {
        if (_renderer != null) {
            _renderer.SkyboxIntensity = _state.SkyboxIntensity;
        }
    }

    static void ApplySkyboxBlur() {
        if (_renderer != null) {
            _renderer.SkyboxBlur = _state.SkyboxBlur;
        }
    }

    static void ApplyToneMapSetting() {
        if (_renderer != null) {
            _renderer.ToneMapMode = (ToneMapMode)_state.ToneMapIndex;
        }
    }

    static void ApplySkyboxRotation() {
        if (_renderer == null) {
            return;
        }
        // 根据索引转换为角度：0: +Z (0°), 1: -X (90°), 2: -Z (180°), 3: +X (270°)
        _renderer.EnvironmentRotation = _state.SkyboxRotationIndex * 90f;
    }

    static void ApplyEnvironmentMapVisibility() {
        if (_renderer != null) {
            _renderer.ShowEnvironmentMap = _state.ShowSkybox;
        }
    }

    static void ApplyBackgroundColor() {
        if (_renderer != null) {
            _renderer.BackgroundColor = _state.BackgroundColor;
        }
    }

    static void ApplyEnvironmentMap() {
        string envPath = _state.GetSelectedEnvironmentPath();
        if (string.IsNullOrEmpty(envPath)) {
            return;
        }
        try {
            _renderer.SetEnvironmentMap(envPath);
        }
        catch (System.Exception ex) {
            LogManager.Logger.ZLogError($"Failed to load environment map: {ex.Message}");
        }
    }
}
