# .NET GlTF Viewer

## 项目概述

.NET glTF 查看器，使用 OpenGL ES 3.0 实现 PBR 渲染。支持完整的 glTF 2.0 规范及多种材质扩展。

## 构建与运行

```bash
# 构建项目
dotnet build

# 运行 Windows 桌面应用
dotnet run --project DotnetGltfViewer.Windows

# Release 构建
dotnet build -c Release
```

## 项目结构

```
DotnetGltfViewer/
├── DotnetGltfRenderer/       # 核心渲染库
│   ├── Core/                 # 基础设施：Camera, UBO, VAO, Texture 等
│   ├── Material/             # PBR 材质系统及扩展
│   │   └── Extensions/       # glTF 材质扩展实现
│   ├── Model/                # glTF 模型加载与处理
│   ├── Render/               # 渲染器与渲染流程管理
│   ├── Graphics/             # Shader, Framebuffer, ShaderCache
│   ├── Lighting/             # 光照系统
│   ├── Environment/          # IBL 环境贴图
│   ├── Animation/            # 骨骼动画与 Morph Target
│   └── shaders/              # GLSL 着色器文件
└── DotnetGltfViewer.Windows/ # Windows 桌面应用程序
    ├── MainWindow.cs         # 窗口与渲染循环
    ├── ImGuiManager.cs       # ImGui UI 管理
    └── InputManager.cs       # 输入处理
```

## 核心架构

### 渲染流程

`Renderer` 是渲染入口，协调以下子系统：
- `Camera`：视图投影矩阵计算
- `LightingSystem`：光源管理
- `IBLManager`：基于图像的光照
- `RenderPassManager`：多 Pass 渲染流程
- `FramebufferManager`：离屏帧缓冲区管理

### 多 Pass 渲染

`RenderPassManager` 按顺序执行：
1. **Scatter Pass**：渲染 VolumeScatter 物体到离屏缓冲区
2. **Transmission Pass**：渲染场景用于折射效果
3. **Main Pass**：渲染天空盒、不透明物体、Transmission 物体、透明物体

### 材质系统

`Material` 类封装 PBR 材质属性。每个材质扩展（如 `ClearCoatExtension`）继承自 `MaterialExtension`，负责：
- `LoadFromGltf()`：从 glTF 加载数据
- `AppendDefines()`：添加着色器宏定义
- `AppendUniforms()`：添加 UBO 数据

### 着色器系统

- `ShaderCache`：着色器缓存与选择
- `ShaderDefines`：动态生成着色器宏定义
- 着色器使用 UBO 传递数据，绑定槽位：
  - 0: SceneData
  - 1: MaterialCoreData
  - 2: LightsData
  - 3: RenderStateData
  - 4: UVTransformData
  - 5: VolumeScatterData
  - 6: MaterialExtensionData

### 模型加载

`Model` 类使用 SharpGLTF 加载 glTF 文件：
- 解析场景图并创建 `MeshInstance`
- 处理骨骼动画和 Morph Target
- 支持 GPU 实例化（EXT_mesh_gpu_instancing）
- 支持材质变体（KHR_materials_variants）

## 支持的 glTF 扩展

材质扩展：`KHR_materials_clearcoat`, `iridescence`, `transmission`, `volume`, `sheen`, `specular`, `ior`, `emissive_strength`, `dispersion`, `anisotropy`, `diffuse_transmission`, `volume_scatter`, `unlit`, `pbrSpecularGlossiness`

其他扩展：`KHR_lights_punctual`, `KHR_materials_variants`, `KHR_animation_pointer`, `KHR_node_visibility`, `EXT_mesh_gpu_instancing`, `KHR_mesh_quantization`, `EXT_texture_webp`

## 代码规范

- 私有实例字段使用 camelCase（如 `_sceneUBO`）
- 公共成员使用 PascalCase
- 大括号不换行
- 方法体优先使用表达式体
- 使用 XML 文档注释
