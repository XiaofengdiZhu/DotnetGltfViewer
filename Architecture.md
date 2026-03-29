# .NET GlTF Viewer 架构文档

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
│   │   ├── Renderer.cs            # 渲染入口
│   │   ├── RenderPassManager.cs   # 多 Pass 渲染流程
│   │   ├── MeshInstanceRenderer.cs # 网格实例渲染器
│   │   ├── DynamicInstancingBatch.cs  # 动态实例化批次
│   │   └── InstancingBatchManager.cs  # 实例化批次管理
│   ├── Graphics/             # Shader, Framebuffer, ShaderCache
│   ├── Lighting/             # 光照系统
│   ├── Environment/          # IBL 环境贴图
│   ├── Animation/            # 骨骼动画与 Morph Target
│   └── shaders/              # GLSL 着色器文件，改编自 gltf 官方渲染器项目
└── DotnetGltfViewer.Windows/ # Windows 桌面应用程序
    ├── Program.cs            # 应用程序入口
    ├── MainWindow.cs         # 窗口与渲染循环
    ├── AppContext.cs         # 共享上下文，传递核心引用
    ├── SceneLoader.cs        # 模型与环境贴图加载
    ├── CameraController.cs   # 相机操作
    ├── InputManager.cs       # 输入处理
    ├── ImGuiManager.cs       # ImGui UI 管理
    ├── GizmoManager.cs       # Gizmo 变换操作
    ├── SelectionManager.cs   # 模型选择管理
    ├── RayPicker.cs          # 射线拾取
    ├── PerformanceManager.cs # FPS 统计
    └── Sidebar/              # 侧边栏 UI 组件
        ├── SidebarPanel.cs   # 侧边栏容器
        ├── SidebarState.cs   # 侧边栏状态
        ├── ModelsTab.cs      # Models Tab
        ├── DisplayTab.cs     # Display Tab
        ├── AnimationTab.cs   # Animation Tab
        └── AdvancedTab.cs    # Advanced Tab
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

### 渲染队列分类

`Scene` 维护四种渲染队列，根据材质特性自动分类：
- `OpaqueInstances`：不透明物体，可利用 Z-buffer 优化
- `TransparentInstances`：Alpha Blend 透明物体
- `TransmissionInstances`：使用 KHR_materials_transmission 的物体
- `ScatterInstances`：使用 KHR_materials_volume_scatter 的物体

透明、Transmission 和 Scatter 物体按深度从远到近排序，确保正确渲染。

### 动态实例化

`InstancingBatchManager` 和 `DynamicInstancingBatch` 实现了运行时动态批处理：
- 自动合并相同 Mesh 和 Material 的非蒙皮、非 Morph Target 实例
- 单批次最大 1024 个实例
- 显著减少 Draw Call，提升渲染性能
- GPU 实例化着色器变体自动生成

### 材质系统

`Material` 类封装 PBR 材质属性。每个材质扩展（如 `ClearCoatExtension`）继承自 `MaterialExtension`，负责：
- `LoadFromGltf()`：从 glTF 加载数据
- `AppendDefines()`：添加着色器宏定义
- `AppendUniforms()`：添加 UBO 数据

### 着色器系统

- `ShaderCache`：着色器缓存与选择，支持 Hash 快速查找
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
- 全局 Mesh 缓存，避免重复加载相同模型

### MeshInstance

`MeshInstance` 表示场景中的网格实例：
- 独立存储 Gizmo 变换矩阵，不受动画覆盖
- 支持蒙皮动画（JointTexture 纹理存储骨骼矩阵）
- 自动计算渲染队列类型
- 支持节点可见性动画（KHR_node_visibility）

## Windows 应用程序架构

### AppContext

`AppContext` 是共享上下文类，持有核心引用（Scene、Renderer、Camera），用于：
- 传递依赖引用，避免循环依赖
- 提供事件机制（FocusRequested、CloseRequested）

### 管理器类

| 类名 | 职责 |
|------|------|
| `MainWindow` | 窗口创建、渲染循环协调 |
| `InputManager` | 键盘鼠标输入、相机控制 |
| `ImGuiManager` | ImGui 初始化与渲染 |
| `GizmoManager` | Gizmo 变换操作（平移/旋转/缩放） |
| `SelectionManager` | 模型选择状态管理 |
| `SceneLoader` | 模型与环境贴图加载 |
| `CameraController` | 相机操作（聚焦、重置） |

### 侧边栏

侧边栏采用 Tab 结构，每个 Tab 独立文件：
- `ModelsTab`：模型选择、场景切换、变体选择
- `DisplayTab`：光照设置、天空盒设置
- `AnimationTab`：动画播放控制
- `AdvancedTab`：调试通道、材质扩展开关、统计信息

## 支持的 glTF 扩展

材质扩展：`KHR_materials_clearcoat`, `iridescence`, `transmission`, `volume`, `sheen`, `specular`, `ior`, `emissive_strength`, `dispersion`, `anisotropy`, `diffuse_transmission`, `volume_scatter`, `unlit`, `pbrSpecularGlossiness`

其他扩展：`KHR_lights_punctual`, `KHR_materials_variants`, `KHR_animation_pointer`, `KHR_node_visibility`, `EXT_mesh_gpu_instancing`, `KHR_mesh_quantization`, `EXT_texture_webp`

## 代码规范

- 私有实例字段使用 camelCase（如 `_sceneUBO`）
- 公共成员使用 PascalCase
- 大括号不换行
- 方法体优先使用表达式体
- 使用 XML 文档注释
