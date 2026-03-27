# .NET GlTF Viewer

使用 C# .NET 10 + [Silk.NET.OpenGLES](https://dotnet.github.io/Silk.NET) + [SharpGLTF](https://github.com/vpenades/SharpGLTF) 实现的 glTF 2.0 查看器，支持完整的 glTF 2.0 规范及多种材质扩展

几乎完全由 [OpenCode](https://opencode.ai/) + [GLM-5](https://z.ai/blog/glm-5) 编写

默认模型：https://github.com/KhronosGroup/glTF-Sample-Assets/tree/main/Models/DamagedHelmet  
默认环境贴图：https://github.com/KhronosGroup/glTF-Sample-Environments/blob/low_resolution_hdrs/Cannon_Exterior.hdr

## 支持的扩展

本项目使用的着色器来自 [Khronos glTF Sample Renderer](https://github.com/KhronosGroup/glTF-Sample-Renderer) ，因此它支持的扩展，本项目基本都支持

* glTF 2.0
* [KHR_animation_pointer](https://github.com/KhronosGroup/glTF/tree/main/extensions/2.0/Khronos/KHR_animation_pointer) - 提供了一种标准化方法，用于根据 [glTF 2.0 资产对象模型](https://github.com/KhronosGroup/glTF/blob/main/specification/2.0/ObjectModel.adoc) 对任意 glTF 属性进行动画处理
* [KHR_lights_punctual](https://github.com/KhronosGroup/glTF/tree/main/extensions/2.0/Khronos/KHR_lights_punctual) - 定义了一组用于 glTF 2.0 的光源
* [KHR_materials_anisotropy](https://github.com/KhronosGroup/glTF/tree/main/extensions/2.0/Khronos/KHR_materials_anisotropy) - 定义了材料的各向异性属性，例如拉丝金属中可观察到的效果
* [KHR_materials_clearcoat](https://github.com/KhronosGroup/glTF/tree/main/extensions/2.0/Khronos/KHR_materials_clearcoat) - 定义了一种可以叠加在现有 glTF 材质定义之上的透明涂层
* [KHR_materials_diffuse_transmission](https://github.com/KhronosGroup/glTF/blob/main/extensions/2.0/Khronos/KHR_materials_diffuse_transmission/README.md) - 模拟光通过无限薄材料进行漫透射的物理现象
* [KHR_materials_dispersion](https://github.com/KhronosGroup/glTF/tree/main/extensions/2.0/Khronos/KHR_materials_dispersion) - 向金属度-粗糙度材质添加一个参数：色散（dispersion）
* [KHR_materials_emissive_strength](https://github.com/KhronosGroup/glTF/tree/main/extensions/2.0/Khronos/KHR_materials_emissive_strength) - 提供一个新的 emissiveStrength 标量因子，用于控制每个材质的发射强度上限
* [KHR_materials_ior](https://github.com/KhronosGroup/glTF/tree/main/extensions/2.0/Khronos/KHR_materials_ior) - 允许用户将折射率（IOR）设置为特定值
* [KHR_materials_iridescence](https://github.com/KhronosGroup/glTF/tree/main/extensions/2.0/Khronos/KHR_materials_iridescence) - 描述一种色调随观察角度和照明角度变化的效果
* [KHR_materials_pbrSpecularGlossiness](https://github.com/KhronosGroup/glTF/tree/main/extensions/2.0/Khronos/KHR_materials_pbrSpecularGlossiness) - 定义了一种使用镜面反射颜色和光泽度参数的替代 PBR 工作流
* [KHR_materials_sheen](https://github.com/KhronosGroup/glTF/tree/main/extensions/2.0/Khronos/KHR_materials_sheen) - 定义了一种可以叠加在现有 glTF 材质定义之上的光泽层，常用于布料和织物材质
* [KHR_materials_specular](https://github.com/KhronosGroup/glTF/tree/main/extensions/2.0/Khronos/KHR_materials_specular) - 向金属度-粗糙度材质添加两个参数：specular 和 specularColor
* [KHR_materials_transmission](https://github.com/KhronosGroup/glTF/tree/main/extensions/2.0/Khronos/KHR_materials_transmission) - 提供一种以物理合理的方式定义透明 glTF 2.0 材质的方法
* [KHR_materials_unlit](https://github.com/KhronosGroup/glTF/tree/main/extensions/2.0/Khronos/KHR_materials_unlit) - 定义了一种不需要光照计算的材质，用于表示纯色或纹理表面
* [KHR_materials_variants](https://github.com/KhronosGroup/glTF/tree/main/extensions/2.0/Khronos/KHR_materials_variants) - 允许以紧凑的 glTF 形式表示资产的多种材质变体，支持运行时低延迟切换
* [KHR_materials_volume](https://github.com/KhronosGroup/glTF/tree/main/extensions/2.0/Khronos/KHR_materials_volume) - 使表面成为体积之间的界面，提供折射、吸收和散射等效果
* [KHR_materials_volume_scatter](https://github.com/KhronosGroup/glTF/blob/e17468db6fd9ae3ce73504a9f317bd853af01a30/extensions/2.0/Khronos/KHR_materials_volume_scatter/README.md) - 为 KHR_materials_volume 添加散射定义
    * 仅适用于使用 `KHR_materials_diffuse_transmission` 的稠密（高密度）体积，不适用于稀疏（低密度）体积
* [KHR_mesh_quantization](https://github.com/KhronosGroup/glTF/tree/main/extensions/2.0/Khronos/KHR_mesh_quantization) - 允许使用量化的顶点属性数据来减少 glTF 资源的大小
    * 本项目支持不完美，存在 UV 问题，不建议使用
* [KHR_texture_transform](https://github.com/KhronosGroup/glTF/tree/main/extensions/2.0/Khronos/KHR_texture_transform) - 通过偏移、旋转和缩放属性支持纹理图集等优化技术
* [KHR_xmp_json_ld](https://github.com/KhronosGroup/glTF/tree/main/extensions/2.0/Khronos/KHR_xmp_json_ld) - 提供一种在 glTF 资产中嵌入 XMP 元数据的方法
* [EXT_mesh_gpu_instancing](https://github.com/KhronosGroup/glTF/tree/main/extensions/2.0/Vendor/EXT_mesh_gpu_instancing) - 提供一种在 GPU 上高效渲染同一网格多个实例的方法
* [EXT_texture_webp](https://github.com/KhronosGroup/glTF/tree/main/extensions/2.0/Vendor/EXT_texture_webp) - 允许在 glTF 中使用 WebP 格式的纹理

### 因 SharpGLTF 而不支持

* [KHR_draco_mesh_compression](https://github.com/KhronosGroup/glTF/tree/main/extensions/2.0/Khronos/KHR_draco_mesh_compression) - 使用 Draco 库对网格和点云数据进行压缩
* [KHR_meshopt_compression](https://github.com/KhronosGroup/glTF/pull/2517) - 使用 meshoptimizer 库对网格和动画数据进行压缩
* [EXT_meshopt_compression](https://github.com/KhronosGroup/glTF/tree/main/extensions/2.0/Vendor/EXT_meshopt_compression) - 使用 meshoptimizer 库对缓冲区数据进行压缩

### 其他不支持

* [KHR_texture_basisu](https://github.com/KhronosGroup/glTF/tree/main/extensions/2.0/Khronos/KHR_texture_basisu) - 允许在 glTF 中使用 Basis Universal / KTX2 格式的压缩纹理
    * 无计划支持该特殊的压缩纹理格式