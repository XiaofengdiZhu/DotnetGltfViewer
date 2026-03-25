// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Text.Json.Nodes;
using SharpGLTF.Animations;
using SharpGLTF.IO;
using SharpGLTF.Memory;
using SharpGLTF.Schema2;
using SharpGLTF.Validation;
using Silk.NET.OpenGLES;
using GltfScene = SharpGLTF.Schema2.Scene;

namespace DotnetGltfRenderer {
    public class Model : IDisposable {
        const int MaxSkinJoints = 64;

        public Model(GL gl, string path, bool gamma = false) {
            _gl = gl;
            LoadModel(path);
        }

        readonly GL _gl;
        Dictionary<int, Texture> _texturesLoaded = new();
        readonly List<MeshInstance> _meshInstances = [];
        readonly List<Mesh> _uniqueMeshes = [];
        readonly List<Light> _lights = [];

        // KHR_materials_variants: stores mappings by (meshIndex, primitiveIndex)
        // Each mapping is: variantIndex -> materialIndex
        readonly Dictionary<(int MeshIndex, int PrimitiveIndex), Dictionary<int, int>> _variantMappings = new();

        Animation _activeAnimation;
        float _animationTimeSeconds;

        // KHR_animation_pointer support
        AnimationPointerProcessor _pointerProcessor;
        ModelRoot _modelRoot;

        // 节点世界矩阵缓存（用于优化动画性能）
        readonly Dictionary<Node, Matrix4x4> _nodeWorldMatrixCache = new();

        // ========== 场景缓存支持 ==========
        readonly List<string> _sceneNames = new();
        int _activeSceneIndex;
        readonly Dictionary<int, List<MeshInstance>> _sceneMeshInstances = new();
        readonly Dictionary<int, List<Light>> _sceneLights = new();

        public sealed class MeshInstance {
            internal MeshInstance(Node node, Mesh mesh, Skin skin, GL gl) {
                Node = node;
                Mesh = mesh;
                Skin = skin;
                WorldMatrix = node.WorldMatrix;
                OriginalWorldMatrix = node.WorldMatrix;
                IsVisible = true; // 默认可见
                _gl = gl;
                _gizmoTransform = Matrix4x4.Identity;
                if (skin == null) {
                    _joints = Array.Empty<Node>();
                    _inverseBindMatrices = Array.Empty<Matrix4x4>();
                    JointMatrices = Array.Empty<Matrix4x4>();
                    return;
                }
                int jointCount = Math.Min(skin.JointsCount, MaxSkinJoints);
                _joints = new Node[jointCount];
                _inverseBindMatrices = new Matrix4x4[jointCount];
                JointMatrices = new Matrix4x4[jointCount];
                IReadOnlyList<Node> skinJoints = skin.Joints;
                IReadOnlyList<Matrix4x4> inverseBindMatrices = skin.InverseBindMatrices;
                for (int i = 0; i < jointCount; i++) {
                    _joints[i] = skinJoints[i];
                    _inverseBindMatrices[i] = i < inverseBindMatrices.Count ? inverseBindMatrices[i] : Matrix4x4.Identity;
                    JointMatrices[i] = Matrix4x4.Identity;
                }
                // 创建骨骼纹理
                JointTexture = new JointTexture(gl, jointCount);
            }

            readonly GL _gl;
            internal readonly Node[] _joints;
            readonly Matrix4x4[] _inverseBindMatrices;

            /// <summary>
            /// Gizmo 变换矩阵（独立存储，不会被动画覆盖）
            /// </summary>
            Matrix4x4 _gizmoTransform;

            internal Node Node { get; }
            public Mesh Mesh { get; }
            internal Skin Skin { get; }
            public Matrix4x4 WorldMatrix { get; internal set; }
            /// <summary>
            /// 原始世界矩阵（用于 Gizmo 变换计算）
            /// </summary>
            public Matrix4x4 OriginalWorldMatrix { get; private set; }
            public Matrix4x4[] JointMatrices { get; }
            public JointTexture JointTexture { get; }
            public bool HasSkinning => Skin != null && Mesh.HasSkinAttributes && JointMatrices.Length > 0;
            public bool IsNegativeScale { get; internal set; }

            /// <summary>
            /// 节点是否可见（用于 KHR_node_visibility 动画）
            /// </summary>
            public bool IsVisible { get; set; }

            /// <summary>
            /// 设置 Gizmo 变换矩阵
            /// </summary>
            /// <param name="transform">Gizmo 变换矩阵</param>
            public void SetGizmoTransform(Matrix4x4 transform) {
                _gizmoTransform = transform;
            }

            /// <summary>
            /// 获取 Gizmo 变换矩阵
            /// </summary>
            public Matrix4x4 GetGizmoTransform() => _gizmoTransform;

            /// <summary>
            /// 重置 Gizmo 变换矩阵为单位矩阵
            /// </summary>
            public void ResetGizmoTransform() {
                _gizmoTransform = Matrix4x4.Identity;
                // 同时重置 WorldMatrix 到原始值
                WorldMatrix = OriginalWorldMatrix;
                IsNegativeScale = WorldMatrix.GetDeterminant() < 0f;
            }

            /// <summary>
            /// 应用 Gizmo 变换到当前世界矩阵（在动画更新后调用）
            /// </summary>
            public void ApplyGizmoTransform() {
                if (_gizmoTransform != Matrix4x4.Identity) {
                    WorldMatrix *= _gizmoTransform;
                    IsNegativeScale = WorldMatrix.GetDeterminant() < 0f;
                }
            }

            /// <summary>
            /// 应用额外的变换矩阵（旧方法，保留兼容性）
            /// </summary>
            /// <param name="transform">要应用的变换矩阵</param>
            public void ApplyTransform(Matrix4x4 transform) {
                WorldMatrix = OriginalWorldMatrix * transform;
                IsNegativeScale = WorldMatrix.GetDeterminant() < 0f;
            }

            /// <summary>
            /// 更新原始世界矩阵（由 Model 类内部调用）
            /// </summary>
            internal void UpdateOriginalWorldMatrix() {
                OriginalWorldMatrix = Node.WorldMatrix;
            }

            /// <summary>
            /// 是否使用 GPU 实例化（EXT_mesh_gpu_instancing）
            /// </summary>
            public bool UseGpuInstancing { get; set; }

            internal void UpdateSkinning(Animation animation, float time, Dictionary<Node, Matrix4x4> cache) {
                if (!HasSkinning) {
                    return;
                }
                for (int i = 0; i < JointMatrices.Length; i++) {
                    Matrix4x4 jointWorld;
                    if (animation != null
                        && cache != null
                        && cache.TryGetValue(_joints[i], out Matrix4x4 cached)) {
                        jointWorld = cached;
                    }
                    else {
                        jointWorld = animation == null ? _joints[i].WorldMatrix : _joints[i].GetWorldMatrix(animation, time);
                    }
                    JointMatrices[i] = Matrix4x4.Multiply(_inverseBindMatrices[i], jointWorld);
                }
                // 更新骨骼纹理
                JointTexture?.Update(JointMatrices);
            }
        }

        public string Directory { get; protected set; } = string.Empty;
        public IReadOnlyList<Mesh> Meshes => _uniqueMeshes;
        public IReadOnlyList<MeshInstance> MeshInstances => _meshInstances;
        public IReadOnlyList<Light> Lights => _lights;

        public IEnumerable<string> ExtensionsUsed => _modelRoot.ExtensionsUsed;

        // ========== 场景支持 ==========
        /// <summary>
        /// 获取所有场景名称列表
        /// </summary>
        public IReadOnlyList<string> SceneNames => _sceneNames;

        /// <summary>
        /// 获取当前激活的场景索引
        /// </summary>
        public int ActiveSceneIndex {
            get => _activeSceneIndex;
            set {
                if (value < 0
                    || value >= _sceneNames.Count) {
                    return;
                }
                if (value == _activeSceneIndex) {
                    return;
                }
                _activeSceneIndex = value;
                SwitchToScene(_activeSceneIndex);
            }
        }

        /// <summary>
        /// 获取当前激活的场景名称
        /// </summary>
        public string ActiveSceneName => _activeSceneIndex >= 0 && _activeSceneIndex < _sceneNames.Count ? _sceneNames[_activeSceneIndex] : null;

        // KHR_materials_variants support
        readonly List<string> _variants = new();

        /// <summary>
        /// 获取所有变体名称列表
        /// </summary>
        public IReadOnlyList<string> Variants => _variants;

        /// <summary>
        /// 获取或设置当前激活的变体索引（-1 表示使用默认材质）
        /// </summary>
        public int ActiveVariantIndex { get; set; } = -1;

        /// <summary>
        /// 获取当前激活的变体名称（null 表示使用默认材质）
        /// </summary>
        public string ActiveVariant {
            get => ActiveVariantIndex >= 0 && ActiveVariantIndex < _variants.Count ? _variants[ActiveVariantIndex] : null;
            set {
                if (value == null) {
                    ActiveVariantIndex = -1;
                }
                else {
                    ActiveVariantIndex = _variants.IndexOf(value);
                }
            }
        }

        public bool HasAnimation => _activeAnimation != null;
        public float AnimationDurationSeconds { get; private set; }

        /// <summary>
        /// 获取已加载的纹理
        /// </summary>
        public Texture GetTexture(int logicalIndex) => _texturesLoaded.TryGetValue(logicalIndex, out Texture tex) ? tex : null;

        void LoadModel(string path) {
            string fullPath = Path.GetFullPath(path);
            Directory = Path.GetDirectoryName(fullPath) ?? string.Empty;
            _modelRoot = ModelRoot.Load(fullPath, new ReadSettings { Validation = ValidationMode.Skip });
            //ExtensionSupport.Reset();
            DetectExtensions(_modelRoot);
            LoadVariants(_modelRoot);
            LoadTextures(_modelRoot);

            // 预缓存所有场景
            PreloadAllScenes();

            // 设置默认场景
            int defaultSceneIndex = 0;
            if (_modelRoot.DefaultScene != null) {
                defaultSceneIndex = _modelRoot.DefaultScene.LogicalIndex;
            }
            _activeSceneIndex = Math.Min(defaultSceneIndex, _sceneNames.Count - 1);
            if (_activeSceneIndex < 0) {
                _activeSceneIndex = 0;
            }

            // 切换到默认场景
            SwitchToScene(_activeSceneIndex);
            _activeAnimation = _modelRoot.LogicalAnimations.FirstOrDefault();
            AnimationDurationSeconds = _activeAnimation?.Duration ?? 0f;

            // Initialize KHR_animation_pointer processor
            _pointerProcessor = new AnimationPointerProcessor();
            _pointerProcessor.ProcessAnimation(_activeAnimation, _modelRoot, this);
            UpdateMeshInstanceTransforms();
        }

        /// <summary>
        /// 预缓存所有场景的节点结构（纹理、材质已共享）
        /// </summary>
        void PreloadAllScenes() {
            // 收集场景名称
            _sceneNames.Clear();
            foreach (GltfScene scene in _modelRoot.LogicalScenes) {
                _sceneNames.Add(scene.Name ?? $"Scene {scene.LogicalIndex}");
            }

            // 如果没有场景，创建一个虚拟场景
            if (_sceneNames.Count == 0) {
                _sceneNames.Add("Default Scene");
            }

            // 清空之前的缓存
            _sceneMeshInstances.Clear();
            _sceneLights.Clear();

            // 预处理每个场景
            Dictionary<(int MeshIndex, int PrimitiveIndex), Mesh> primitiveMeshCache = new();
            for (int sceneIndex = 0; sceneIndex < _modelRoot.LogicalScenes.Count; sceneIndex++) {
                GltfScene scene = _modelRoot.LogicalScenes[sceneIndex];
                ProcessScene(scene, sceneIndex, primitiveMeshCache);
            }

            // 如果没有场景，处理所有根节点
            if (_modelRoot.LogicalScenes.Count == 0) {
                ProcessScene(null, 0, primitiveMeshCache);
            }
        }

        /// <summary>
        /// 处理单个场景，缓存其 MeshInstance 和 Light
        /// </summary>
        void ProcessScene(GltfScene scene, int sceneIndex, Dictionary<(int MeshIndex, int PrimitiveIndex), Mesh> primitiveMeshCache) {
            List<MeshInstance> instances = new();
            List<Light> lights = new();
            if (scene != null) {
                foreach (Node rootNode in scene.VisualChildren) {
                    ProcessNodeForScene(rootNode, primitiveMeshCache, Matrix4x4.Identity, instances, lights);
                }
            }
            else {
                // 没有场景时，处理所有根节点
                foreach (Node rootNode in _modelRoot.LogicalNodes.Where(node => node.VisualParent == null)) {
                    ProcessNodeForScene(rootNode, primitiveMeshCache, Matrix4x4.Identity, instances, lights);
                }
            }
            _sceneMeshInstances[sceneIndex] = instances;
            _sceneLights[sceneIndex] = lights;
        }

        /// <summary>
        /// 处理场景中的节点（用于预缓存）
        /// </summary>
        void ProcessNodeForScene(Node node,
            Dictionary<(int MeshIndex, int PrimitiveIndex), Mesh> primitiveMeshCache,
            Matrix4x4 parentWorldMatrix,
            List<MeshInstance> instances,
            List<Light> lights) {
            // Check for KHR_node_visibility extension
            if (node.TryGetVisibility(out bool isVisible)
                && !isVisible) {
                return;
            }
            Matrix4x4 worldMatrix = node.LocalMatrix * parentWorldMatrix;

            // Check for KHR_lights_punctual extension
            PunctualLight punctualLight = node.PunctualLight;
            if (punctualLight != null) {
                Light light = ConvertPunctualLight(punctualLight, worldMatrix, node);
                if (light != null) {
                    lights.Add(light);
                }
            }

            // Check for EXT_mesh_gpu_instancing
            MeshGpuInstancing gpuInstancing = node.GetGpuInstancing();
            if (node.Mesh != null) {
                if (gpuInstancing != null
                    && gpuInstancing.Count > 0) {
                    ProcessInstancedNodeForScene(node, gpuInstancing, primitiveMeshCache, worldMatrix, instances);
                }
                else {
                    for (int i = 0; i < node.Mesh.Primitives.Count; i++) {
                        MeshPrimitive primitive = node.Mesh.Primitives[i];
                        if (primitive.DrawPrimitiveType != SharpGLTF.Schema2.PrimitiveType.TRIANGLES) {
                            continue;
                        }
                        (int LogicalIndex, int i) key = (node.Mesh.LogicalIndex, i);
                        if (!primitiveMeshCache.TryGetValue(key, out Mesh mesh)) {
                            Dictionary<int, int> variantMatMapping = null;
                            _variantMappings.TryGetValue(key, out variantMatMapping);
                            mesh = ProcessPrimitive(primitive, variantMatMapping);
                            primitiveMeshCache[key] = mesh;
                            _uniqueMeshes.Add(mesh);
                        }
                        instances.Add(new MeshInstance(node, mesh, node.Skin, _gl));
                    }
                }
            }
            foreach (Node child in node.VisualChildren) {
                ProcessNodeForScene(child, primitiveMeshCache, worldMatrix, instances, lights);
            }
        }

        /// <summary>
        /// 处理 GPU 实例化节点（用于预缓存）
        /// </summary>
        void ProcessInstancedNodeForScene(Node node,
            MeshGpuInstancing gpuInstancing,
            Dictionary<(int MeshIndex, int PrimitiveIndex), Mesh> primitiveMeshCache,
            Matrix4x4 nodeWorldMatrix,
            List<MeshInstance> instances) {
            int instanceCount = gpuInstancing.Count;
            if (instanceCount == 0) {
                return;
            }

            // 蒙皮网格不支持 GPU 实例化
            if (node.Skin != null) {
                for (int i = 0; i < node.Mesh.Primitives.Count; i++) {
                    MeshPrimitive primitive = node.Mesh.Primitives[i];
                    if (primitive.DrawPrimitiveType != SharpGLTF.Schema2.PrimitiveType.TRIANGLES) {
                        continue;
                    }
                    (int LogicalIndex, int i) key = (node.Mesh.LogicalIndex, i);
                    if (!primitiveMeshCache.TryGetValue(key, out Mesh mesh)) {
                        Dictionary<int, int> variantMatMapping = null;
                        _variantMappings.TryGetValue(key, out variantMatMapping);
                        mesh = ProcessPrimitive(primitive, variantMatMapping);
                        primitiveMeshCache[key] = mesh;
                        _uniqueMeshes.Add(mesh);
                    }
                    instances.Add(new MeshInstance(node, mesh, node.Skin, _gl));
                }
                return;
            }
            Matrix4x4[] instanceWorldMatrices = new Matrix4x4[instanceCount];
            for (int i = 0; i < instanceCount; i++) {
                instanceWorldMatrices[i] = gpuInstancing.GetWorldMatrix(i);
            }
            List<int> positiveScaleInstances = new();
            List<int> negativeScaleInstances = new();
            for (int i = 0; i < instanceCount; i++) {
                float determinant = instanceWorldMatrices[i].GetDeterminant();
                if (determinant < 0) {
                    negativeScaleInstances.Add(i);
                }
                else {
                    positiveScaleInstances.Add(i);
                }
            }
            for (int primIdx = 0; primIdx < node.Mesh.Primitives.Count; primIdx++) {
                MeshPrimitive primitive = node.Mesh.Primitives[primIdx];
                if (primitive.DrawPrimitiveType != SharpGLTF.Schema2.PrimitiveType.TRIANGLES) {
                    continue;
                }

                // Morph targets 不支持 GPU 实例化
                if (primitive.MorphTargetsCount > 0) {
                    (int LogicalIndex, int i) key = (node.Mesh.LogicalIndex, primIdx);
                    if (!primitiveMeshCache.TryGetValue(key, out Mesh mesh)) {
                        Dictionary<int, int> morphVarMapping = null;
                        _variantMappings.TryGetValue(key, out morphVarMapping);
                        mesh = ProcessPrimitive(primitive, morphVarMapping);
                        primitiveMeshCache[key] = mesh;
                        _uniqueMeshes.Add(mesh);
                    }
                    instances.Add(new MeshInstance(node, mesh, node.Skin, _gl));
                    continue;
                }
                (int LogicalIndex, int i) primKey = (node.Mesh.LogicalIndex, primIdx);
                Dictionary<int, int> variantMatMapping = null;
                _variantMappings.TryGetValue(primKey, out variantMatMapping);
                if (positiveScaleInstances.Count > 0) {
                    CreateInstancedMeshForScene(
                        node,
                        primitive,
                        positiveScaleInstances,
                        instanceWorldMatrices,
                        variantMatMapping,
                        false,
                        primitiveMeshCache,
                        instances
                    );
                }
                if (negativeScaleInstances.Count > 0) {
                    CreateInstancedMeshForScene(
                        node,
                        primitive,
                        negativeScaleInstances,
                        instanceWorldMatrices,
                        variantMatMapping,
                        true,
                        primitiveMeshCache,
                        instances
                    );
                }
            }
        }

        /// <summary>
        /// 创建实例化的 Mesh（用于预缓存）
        /// </summary>
        void CreateInstancedMeshForScene(Node node,
            MeshPrimitive primitive,
            List<int> instanceIndices,
            Matrix4x4[] allMatrices,
            Dictionary<int, int> variantMatMapping,
            bool isNegativeScale,
            Dictionary<(int MeshIndex, int PrimitiveIndex), Mesh> primitiveMeshCache,
            List<MeshInstance> instances) {
            Matrix4x4[] instanceMatrices = new Matrix4x4[instanceIndices.Count];
            for (int i = 0; i < instanceIndices.Count; i++) {
                instanceMatrices[i] = allMatrices[instanceIndices[i]];
            }
            Mesh mesh = ProcessPrimitive(primitive, variantMatMapping);
            mesh.InstanceCount = instanceIndices.Count;
            mesh.InstanceMatrices = instanceMatrices;
            mesh.IsNegativeScaleInstance = isNegativeScale;
            mesh.SetupInstancingBuffer();
            _uniqueMeshes.Add(mesh);
            MeshInstance instance = new(node, mesh, null, _gl);
            instance.UseGpuInstancing = true;
            instance.IsNegativeScale = isNegativeScale;
            instances.Add(instance);
        }

        /// <summary>
        /// 切换到指定场景
        /// </summary>
        void SwitchToScene(int sceneIndex) {
            _meshInstances.Clear();
            _lights.Clear();
            if (_sceneMeshInstances.TryGetValue(sceneIndex, out List<MeshInstance> instances)) {
                _meshInstances.AddRange(instances);
            }
            if (_sceneLights.TryGetValue(sceneIndex, out List<Light> lights)) {
                _lights.AddRange(lights);
            }

            // 重新计算变换
            UpdateMeshInstanceTransforms();
        }

        void LoadVariants(ModelRoot modelRoot) {
            // KHR_materials_variants is not natively supported by SharpGLTF,
            // but we can access the raw extension data via reflection since UnknownNode is internal.

            // Step 1: Get variants list from ModelRoot extension
            object variantsExt = GetUnknownExtension(modelRoot, "KHR_materials_variants");
            if (variantsExt == null) {
                return;
            }

            // Parse variants array
            JsonArray variantsArray = GetExtensionProperty(variantsExt, "variants") as JsonArray;
            if (variantsArray != null) {
                foreach (JsonNode variant in variantsArray) {
                    string name = variant?["name"]?.GetValue<string>();
                    if (!string.IsNullOrEmpty(name)) {
                        _variants.Add(name);
                    }
                }
            }

            // Step 2: Get mappings from each MeshPrimitive's extension
            int meshIndex = 0;
            foreach (SharpGLTF.Schema2.Mesh mesh in modelRoot.LogicalMeshes) {
                int primitiveIndex = 0;
                foreach (MeshPrimitive primitive in mesh.Primitives) {
                    object primExt = GetUnknownExtension(primitive, "KHR_materials_variants");
                    if (primExt != null) {
                        JsonArray mappingsArray = GetExtensionProperty(primExt, "mappings") as JsonArray;
                        if (mappingsArray != null) {
                            Dictionary<int, int> mappingDict = new();
                            foreach (JsonNode mapping in mappingsArray) {
                                int? materialIdx = mapping?["material"]?.GetValue<int>();
                                JsonArray variantIndices = mapping?["variants"] as JsonArray;
                                if (materialIdx.HasValue
                                    && variantIndices != null) {
                                    foreach (JsonNode variantIdx in variantIndices) {
                                        int? vIdx = variantIdx?.GetValue<int>();
                                        if (vIdx.HasValue) {
                                            mappingDict[vIdx.Value] = materialIdx.Value;
                                        }
                                    }
                                }
                            }
                            if (mappingDict.Count > 0) {
                                _variantMappings[(meshIndex, primitiveIndex)] = mappingDict;
                            }
                        }
                    }
                    primitiveIndex++;
                }
                meshIndex++;
            }
        }

        /// <summary>
        /// Gets an unknown extension by name using reflection (since UnknownNode is internal)
        /// </summary>
        static object GetUnknownExtension(IExtraProperties target, string extensionName) {
            foreach (JsonSerializable ext in target.Extensions) {
                // Check if it's UnknownNode by type name (since it's internal)
                if (ext.GetType().FullName == "SharpGLTF.IO.UnknownNode") {
                    PropertyInfo nameProp = ext.GetType().GetProperty("Name", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (nameProp?.GetValue(ext)?.ToString() == extensionName) {
                        return ext;
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Gets a property value from an unknown extension using reflection
        /// </summary>
        static object GetExtensionProperty(object unknownExtension, string propertyName) {
            Type extType = unknownExtension.GetType();
            PropertyInfo propsProp = extType.GetProperty("Properties", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            IReadOnlyDictionary<string, JsonNode> properties = propsProp?.GetValue(unknownExtension) as IReadOnlyDictionary<string, JsonNode>;
            if (properties != null
                && properties.TryGetValue(propertyName, out JsonNode value)) {
                return value;
            }
            return null;
        }

        void DetectExtensions(ModelRoot modelRoot) {
            foreach (string extension in modelRoot.ExtensionsRequired) {
                if (!ExtensionManager.IsExtensionEnabled(extension)) {
                    throw new Exception($"Detected unsupported or disabled extension: {extension}");
                }
            }
        }

        void LoadTextures(ModelRoot modelRoot) {
            // First, analyze texture usage to determine sRGB vs linear
            Dictionary<int, bool> textureIsSrgb = new();
            foreach (SharpGLTF.Schema2.Texture gltfTexture in modelRoot.LogicalTextures) {
                textureIsSrgb[gltfTexture.LogicalIndex] = true; // Default to sRGB
            }
            foreach (SharpGLTF.Schema2.Material material in modelRoot.LogicalMaterials) {
                AnalyzeTextureUsage(material, textureIsSrgb);
            }
            // Now load textures with correct format
            foreach (SharpGLTF.Schema2.Texture gltfTexture in modelRoot.LogicalTextures) {
                Image image = gltfTexture.PrimaryImage ?? gltfTexture.FallbackImage;
                if (image?.Content == null) {
                    continue;
                }
                int texIndex = gltfTexture.LogicalIndex;
                bool isSrgb = textureIsSrgb.GetValueOrDefault(texIndex, true);
                Texture texture = new(_gl, image.Content, ModelTextureType.None, gltfTexture.Sampler, isSrgb);
                texture.Path = image.Content.SourcePath ?? texIndex.ToString();
                _texturesLoaded[texIndex] = texture;
            }
        }

        void AnalyzeTextureUsage(SharpGLTF.Schema2.Material material, Dictionary<int, bool> textureIsSrgb) {
            // sRGB channels: BaseColor, Emissive
            // Linear channels: Normal, MetallicRoughness, Occlusion, ClearCoat, ClearCoatRoughness, ClearCoatNormal, Iridescence, IridescenceThickness
            void MarkTexture(string channelKey, bool isSrgb) {
                MaterialChannel? channel = material.FindChannel(channelKey);
                if (channel?.Texture is SharpGLTF.Schema2.Texture tex) {
                    textureIsSrgb[tex.LogicalIndex] = isSrgb;
                }
            }

            MarkTexture("BaseColor", true);
            MarkTexture("Emissive", true);
            MarkTexture("Normal", false);
            MarkTexture("MetallicRoughness", false);
            MarkTexture("Occlusion", false);
            MarkTexture("ClearCoat", false);
            MarkTexture("ClearCoatRoughness", false);
            MarkTexture("ClearCoatNormal", false);
            MarkTexture("Iridescence", false);
            MarkTexture("IridescenceThickness", false);
        }

        public void Update(float deltaTimeSeconds) {
            if (_activeAnimation != null
                && AnimationDurationSeconds > 0f) {
                _animationTimeSeconds += MathF.Max(0f, deltaTimeSeconds);
                _animationTimeSeconds %= AnimationDurationSeconds;
            }

            // Update KHR_animation_pointer targets
            _pointerProcessor?.Update(_animationTimeSeconds);
            UpdateMeshInstanceTransforms();
        }

        void UpdateLightTransforms() {
            foreach (Light light in _lights) {
                if (light.SourceNode == null) {
                    continue;
                }

                // Get animated world matrix (use cache if available)
                Matrix4x4 worldMatrix;
                if (_activeAnimation != null
                    && _nodeWorldMatrixCache.TryGetValue(light.SourceNode, out Matrix4x4 cached)) {
                    worldMatrix = cached;
                }
                else {
                    worldMatrix = _activeAnimation == null
                        ? light.SourceNode.WorldMatrix
                        : light.SourceNode.GetWorldMatrix(_activeAnimation, _animationTimeSeconds);
                }

                // Update position from world matrix
                light.Position = new Vector3(worldMatrix.M41, worldMatrix.M42, worldMatrix.M43);

                // Update direction from world matrix (forward direction: -Z in local space)
                Vector3 forward = new(-worldMatrix.M31, -worldMatrix.M32, -worldMatrix.M33);
                light.Direction = Vector3.Normalize(forward);
            }
        }

        void UpdateMeshInstanceTransforms() {
            // 如果有动画，先缓存所有需要更新的节点世界矩阵
            // 这样可以避免对同一个节点多次调用 GetWorldMatrix
            if (_activeAnimation != null) {
                // 重用字典，直接更新值而不清空，避免 GC 压力

                // 收集所有需要更新的节点（MeshInstance节点 + 骨骼节点 + 光源节点）
                HashSet<Node> nodesToUpdate = new();
                foreach (MeshInstance instance in _meshInstances) {
                    nodesToUpdate.Add(instance.Node);
                    if (instance.HasSkinning
                        && instance._joints != null) {
                        foreach (Node joint in instance._joints) {
                            nodesToUpdate.Add(joint);
                        }
                    }
                }
                foreach (Light light in _lights) {
                    if (light.SourceNode != null) {
                        nodesToUpdate.Add(light.SourceNode);
                    }
                }

                // 批量计算并缓存所有节点的世界矩阵（直接更新，不清空字典）
                foreach (Node node in nodesToUpdate) {
                    _nodeWorldMatrixCache[node] = node.GetWorldMatrix(_activeAnimation, _animationTimeSeconds);
                }
            }
            foreach (MeshInstance instance in _meshInstances) {
                if (_activeAnimation != null
                    && _nodeWorldMatrixCache.TryGetValue(instance.Node, out Matrix4x4 cachedMatrix)) {
                    instance.WorldMatrix = cachedMatrix;
                }
                else {
                    instance.WorldMatrix = instance.Node.WorldMatrix;
                }
                
                // 在动画更新后应用 Gizmo 变换（如果有）
                instance.ApplyGizmoTransform();
                
                instance.IsNegativeScale = instance.WorldMatrix.GetDeterminant() < 0f;
                instance.UpdateSkinning(_activeAnimation, _animationTimeSeconds, _nodeWorldMatrixCache);

                // 更新 morph weights（如果有动画且有 morph targets）
                if (_activeAnimation != null
                    && instance.Mesh.HasMorphTargets) {
                    float[] weights = instance.Mesh.MorphWeights;
                    if (weights != null) {
                        // 查找该节点的 morph weights 动画通道
                        foreach (AnimationChannel channel in _activeAnimation.Channels) {
                            if (channel.TargetNode == instance.Node
                                && channel.TargetNodePath == PropertyPath.weights) {
                                IAnimationSampler<float[]> sampler = channel.GetMorphSampler();
                                if (sampler != null) {
                                    ICurveSampler<float[]> curveSampler = sampler.CreateCurveSampler();
                                    float[] animatedWeights = curveSampler.GetPoint(_animationTimeSeconds);
                                    if (animatedWeights != null) {
                                        for (int i = 0; i < Math.Min(weights.Length, animatedWeights.Length); i++) {
                                            weights[i] = animatedWeights[i];
                                        }
                                    }
                                }
                                break;
                            }
                        }
                    }
                }
            }

            // 更新光源变换
            UpdateLightTransforms();
        }

        static Light ConvertPunctualLight(PunctualLight punctualLight, Matrix4x4 worldMatrix, Node sourceNode) {
            // Extract position from world matrix
            Vector3 position = new(worldMatrix.M41, worldMatrix.M42, worldMatrix.M43);

            // Extract direction from world matrix (forward direction: -Z in local space)
            Vector3 forward = new(-worldMatrix.M31, -worldMatrix.M32, -worldMatrix.M33);
            Vector3 direction = Vector3.Normalize(forward);
            Light light = new() {
                Color = punctualLight.Color,
                Intensity = punctualLight.Intensity,
                Range = punctualLight.Range,
                Position = position,
                Direction = direction,
                SourceNode = sourceNode
            };
            switch (punctualLight.LightType) {
                case PunctualLightType.Directional: light.Type = LightType.Directional; break;
                case PunctualLightType.Point: light.Type = LightType.Point; break;
                case PunctualLightType.Spot:
                    light.Type = LightType.Spot;
                    // Convert radians to degrees
                    light.InnerConeAngle = punctualLight.InnerConeAngle * 180f / MathF.PI;
                    light.OuterConeAngle = punctualLight.OuterConeAngle * 180f / MathF.PI;
                    break;
            }
            return light;
        }

        Mesh ProcessPrimitive(MeshPrimitive primitive, Dictionary<int, int> variantMatMapping) {
            Mesh mesh = new() { GL = _gl };
            SharpGLTF.Schema2.Material material = primitive.Material;

            // Load geometry
            Accessor posAcc = primitive.GetVertexAccessor("POSITION")
                ?? throw new InvalidOperationException("glTF primitive is missing POSITION accessor.");
            IAccessorArray<Vector3> positions = posAcc.AsVector3Array();
            IAccessorArray<Vector3> normals = primitive.GetVertexAccessor("NORMAL")?.AsVector3Array();
            IAccessorArray<Vector2> uv0 = primitive.GetVertexAccessor("TEXCOORD_0")?.AsVector2Array();
            IAccessorArray<Vector2> uv1 = primitive.GetVertexAccessor("TEXCOORD_1")?.AsVector2Array();
            IAccessorArray<Vector4> colors = primitive.GetVertexAccessor("COLOR_0")?.AsColorArray();
            IAccessorArray<Vector4> tangents = primitive.GetVertexAccessor("TANGENT")?.AsVector4Array();
            IAccessorArray<Vector4> joints = primitive.GetVertexAccessor("JOINTS_0")?.AsVector4Array();
            IAccessorArray<Vector4> weights = primitive.GetVertexAccessor("WEIGHTS_0")?.AsVector4Array();
            mesh.Indices = primitive.GetIndices()?.ToArray() ?? Enumerable.Range(0, positions.Count).Select(i => (uint)i).ToArray();
            Vector4[] generatedTangents = GenerateTangents(positions, normals, uv0, mesh.Indices);

            // ========== Morph Target 纹理化支持 ==========
            // 参考 glTF-Sample-Renderer 的 primitive.js 实现
            // 将 morph targets 数据打包到 TEXTURE_2D_ARRAY 中，由 GPU 在顶点着色器中采样
            int morphTargetCount = primitive.MorphTargetsCount;
            IReadOnlyList<float> morphWeights = primitive.LogicalParent.MorphWeights;
            if (morphTargetCount > 0) {
                // 收集所有 morph target 的属性类型
                HashSet<string> morphAttributes = new();
                for (int t = 0; t < morphTargetCount; t++) {
                    IReadOnlyDictionary<string, Accessor> targetAccessors = primitive.GetMorphTargetAccessors(t);
                    foreach (string attr in targetAccessors.Keys) {
                        morphAttributes.Add(attr);
                    }
                }

                // 创建 MorphTargetTexture
                mesh.MorphTargetCount = morphTargetCount;
                mesh.MorphTargetTexture = new MorphTargetTexture(_gl, positions.Count, morphTargetCount, morphAttributes);

                // 收集每个 target 的属性数据
                List<IReadOnlyList<Vector3>> morphPositions = new();
                List<IReadOnlyList<Vector3>> morphNormals = new();
                List<IReadOnlyList<Vector4>> morphTangents = new();
                List<IReadOnlyList<Vector2>> morphTexCoords0 = new();
                List<IReadOnlyList<Vector2>> morphTexCoords1 = new();
                List<IReadOnlyList<Vector4>> morphColors0 = new();
                for (int t = 0; t < morphTargetCount; t++) {
                    IReadOnlyDictionary<string, Accessor> targetAccessors = primitive.GetMorphTargetAccessors(t);

                    // POSITION
                    if (targetAccessors.TryGetValue("POSITION", out Accessor morphPosAcc)) {
                        morphPositions.Add(morphPosAcc.AsVector3Array());
                    }
                    else {
                        morphPositions.Add(null);
                    }

                    // NORMAL
                    if (targetAccessors.TryGetValue("NORMAL", out Accessor morphNrmAcc)) {
                        morphNormals.Add(morphNrmAcc.AsVector3Array());
                    }
                    else {
                        morphNormals.Add(null);
                    }

                    // TANGENT
                    if (targetAccessors.TryGetValue("TANGENT", out Accessor morphTanAcc)) {
                        morphTangents.Add(morphTanAcc.AsVector4Array());
                    }
                    else {
                        morphTangents.Add(null);
                    }

                    // TEXCOORD_0
                    if (targetAccessors.TryGetValue("TEXCOORD_0", out Accessor morphUv0Acc)) {
                        morphTexCoords0.Add(morphUv0Acc.AsVector2Array());
                    }
                    else {
                        morphTexCoords0.Add(null);
                    }

                    // TEXCOORD_1
                    if (targetAccessors.TryGetValue("TEXCOORD_1", out Accessor morphUv1Acc)) {
                        morphTexCoords1.Add(morphUv1Acc.AsVector2Array());
                    }
                    else {
                        morphTexCoords1.Add(null);
                    }

                    // COLOR_0
                    if (targetAccessors.TryGetValue("COLOR_0", out Accessor morphColAcc)) {
                        morphColors0.Add(morphColAcc.AsColorArray());
                    }
                    else {
                        morphColors0.Add(null);
                    }
                }

                // 上传数据到纹理
                mesh.MorphTargetTexture.UploadData(
                    morphPositions.ToArray(),
                    morphNormals.ToArray(),
                    morphTangents.ToArray(),
                    morphTexCoords0.ToArray(),
                    morphTexCoords1.ToArray(),
                    morphColors0.ToArray()
                );

                // 初始化权重数组
                mesh.MorphWeights = new float[morphTargetCount];
                if (morphWeights != null) {
                    for (int i = 0; i < Math.Min(morphWeights.Count, morphTargetCount); i++) {
                        mesh.MorphWeights[i] = morphWeights[i];
                    }
                }
            }

            // 使用原始顶点数据（不再预计算 morph target）
            mesh.HasUV1 = uv1 != null;
            mesh.HasColor0 = colors != null;
            FillVertexBuffers(
                mesh,
                positions,
                normals,
                uv0,
                uv1,
                colors,
                tangents,
                joints,
                weights,
                generatedTangents
            );
            mesh.UseGeneratedTangents = tangents == null && generatedTangents != null;

            // Load material using new Material class
            mesh.Material = new Material();
            mesh.Material.SourceMaterialIndex = material?.LogicalIndex ?? -1;
            mesh.Material.LoadFromGltf(material, this);

            // Load variant materials
            if (variantMatMapping != null
                && variantMatMapping.Count > 0) {
                ModelRoot modelRoot = primitive.LogicalParent.LogicalParent;
                foreach (KeyValuePair<int, int> kvp in variantMatMapping) {
                    int variantIndex = kvp.Key;
                    int materialIndex = kvp.Value;
                    if (materialIndex >= 0
                        && materialIndex < modelRoot.LogicalMaterials.Count) {
                        SharpGLTF.Schema2.Material variantMaterial = modelRoot.LogicalMaterials[materialIndex];
                        Material mat = new();
                        mat.LoadFromGltf(variantMaterial, this);
                        mesh.SetMaterialForVariant(variantIndex, mat);
                    }
                }
            }
            mesh.SetupMesh();
            return mesh;
        }

        void FillVertexBuffers(Mesh mesh,
            IReadOnlyList<Vector3> pos,
            IReadOnlyList<Vector3> norm,
            IReadOnlyList<Vector2> uv0,
            IReadOnlyList<Vector2> uv1,
            IReadOnlyList<Vector4> colors,
            IReadOnlyList<Vector4> tangents,
            IReadOnlyList<Vector4> joints,
            IReadOnlyList<Vector4> weights,
            Vector4[] genTangents) {
            int count = pos.Count;
            bool hasTangents = (tangents != null && tangents.Count > 0) || genTangents != null;
            bool hasSurface = norm != null || colors != null || hasTangents;
            bool hasSkin = joints != null && weights != null;
            bool hasUV1 = uv1 != null;
            float[] baseBuf = new float[count * Mesh.BaseVertexStride];
            float[] uv1Buf = hasUV1 ? new float[count * Mesh.UV1VertexStride] : null;
            float[] surfaceBuf = hasSurface ? new float[count * Mesh.SurfaceVertexStride] : null;
            float[] skinBuf = hasSkin ? new float[count * Mesh.SkinVertexStride] : null;
            int bPtr = 0, uPtr = 0, sPtr = 0, kPtr = 0;
            for (int i = 0; i < count; i++) {
                // Base Buffer: [Pos.x, Pos.y, Pos.z, UV0.x, UV0.y]
                Vector3 p = pos[i];
                Vector2 uv0Val = uv0 != null && i < uv0.Count ? uv0[i] : Vector2.Zero;
                baseBuf[bPtr++] = p.X;
                baseBuf[bPtr++] = p.Y;
                baseBuf[bPtr++] = p.Z;
                baseBuf[bPtr++] = uv0Val.X;
                baseBuf[bPtr++] = uv0Val.Y;
                // UV1 Buffer: [UV1.x, UV1.y]
                if (hasUV1) {
                    Vector2 uv1Val = i < uv1.Count ? uv1[i] : Vector2.Zero;
                    uv1Buf[uPtr++] = uv1Val.X;
                    uv1Buf[uPtr++] = uv1Val.Y;
                }
                // Surface Buffer: [Normal, Color, Tangent]
                if (hasSurface) {
                    Vector3 n = norm != null && i < norm.Count ? norm[i] : InferFallbackNormal(p);
                    Vector4 c = colors != null && i < colors.Count ? colors[i] : Vector4.One;
                    Vector4 t = tangents != null && i < tangents.Count ? tangents[i] :
                        genTangents != null && i < genTangents.Length ? genTangents[i] : new Vector4(1, 0, 0, 1);
                    surfaceBuf[sPtr++] = n.X;
                    surfaceBuf[sPtr++] = n.Y;
                    surfaceBuf[sPtr++] = n.Z;
                    surfaceBuf[sPtr++] = c.X;
                    surfaceBuf[sPtr++] = c.Y;
                    surfaceBuf[sPtr++] = c.Z;
                    surfaceBuf[sPtr++] = c.W;
                    surfaceBuf[sPtr++] = t.X;
                    surfaceBuf[sPtr++] = t.Y;
                    surfaceBuf[sPtr++] = t.Z;
                    surfaceBuf[sPtr++] = t.W;
                }
                // Skin Buffer: [Joints, Weights]
                if (hasSkin) {
                    Vector4 j = i < joints.Count ? joints[i] : Vector4.Zero;
                    Vector4 w = i < weights.Count ? weights[i] : new Vector4(1, 0, 0, 0);
                    skinBuf[kPtr++] = j.X;
                    skinBuf[kPtr++] = j.Y;
                    skinBuf[kPtr++] = j.Z;
                    skinBuf[kPtr++] = j.W;
                    skinBuf[kPtr++] = w.X;
                    skinBuf[kPtr++] = w.Y;
                    skinBuf[kPtr++] = w.Z;
                    skinBuf[kPtr++] = w.W;
                }
            }
            mesh.BaseVertices = baseBuf;
            mesh.UV1Vertices = uv1Buf;
            mesh.SurfaceVertices = surfaceBuf;
            mesh.SkinVertices = skinBuf;
        }

        static Vector4[] GenerateTangents(IReadOnlyList<Vector3> positions,
            IReadOnlyList<Vector3> normals,
            IReadOnlyList<Vector2> uvs,
            IReadOnlyList<uint> indices) {
            if (positions == null
                || normals == null
                || uvs == null
                || indices == null) {
                return null;
            }
            int vertexCount = positions.Count;
            if (vertexCount == 0
                || normals.Count < vertexCount
                || uvs.Count < vertexCount) {
                return null;
            }
            Vector3[] tan1 = new Vector3[vertexCount];
            Vector3[] tan2 = new Vector3[vertexCount];
            for (int i = 0; i + 2 < indices.Count; i += 3) {
                int i0 = (int)indices[i];
                int i1 = (int)indices[i + 1];
                int i2 = (int)indices[i + 2];
                if (i0 < 0
                    || i0 >= vertexCount
                    || i1 < 0
                    || i1 >= vertexCount
                    || i2 < 0
                    || i2 >= vertexCount) {
                    continue;
                }
                Vector3 p0 = positions[i0];
                Vector3 p1 = positions[i1];
                Vector3 p2 = positions[i2];
                Vector2 uv0 = uvs[i0];
                Vector2 uv1 = uvs[i1];
                Vector2 uv2 = uvs[i2];
                Vector3 edge1 = p1 - p0;
                Vector3 edge2 = p2 - p0;
                Vector2 duv1 = uv1 - uv0;
                Vector2 duv2 = uv2 - uv0;
                float denominator = duv1.X * duv2.Y - duv2.X * duv1.Y;
                if (MathF.Abs(denominator) < 1e-8f) {
                    continue;
                }
                float inverse = 1f / denominator;
                Vector3 sdir = (edge1 * duv2.Y - edge2 * duv1.Y) * inverse;
                Vector3 tdir = (edge2 * duv1.X - edge1 * duv2.X) * inverse;
                tan1[i0] += sdir;
                tan1[i1] += sdir;
                tan1[i2] += sdir;
                tan2[i0] += tdir;
                tan2[i1] += tdir;
                tan2[i2] += tdir;
            }
            Vector4[] tangents = new Vector4[vertexCount];
            for (int i = 0; i < vertexCount; i++) {
                Vector3 n = normals[i];
                if (n.LengthSquared() < float.Epsilon) {
                    n = InferFallbackNormal(positions[i]);
                }
                else {
                    n = Vector3.Normalize(n);
                }
                Vector3 t = tan1[i];
                if (t.LengthSquared() < 1e-12f) {
                    Vector3 axis = MathF.Abs(n.Y) < 0.999f ? Vector3.UnitY : Vector3.UnitX;
                    t = Vector3.Cross(axis, n);
                    if (t.LengthSquared() < 1e-12f) {
                        t = Vector3.UnitX;
                    }
                    tangents[i] = new Vector4(Vector3.Normalize(t), 1f);
                    continue;
                }
                t = Vector3.Normalize(t - n * Vector3.Dot(n, t));
                Vector3 b = Vector3.Cross(n, t);
                float w = Vector3.Dot(b, tan2[i]) < 0f ? -1f : 1f;
                tangents[i] = new Vector4(t, w);
            }
            return tangents;
        }

        static Vector3 InferFallbackNormal(Vector3 position) {
            if (position.LengthSquared() < float.Epsilon) {
                return Vector3.UnitY;
            }
            return Vector3.Normalize(position);
        }

        public void Dispose() {
            foreach (Mesh mesh in _uniqueMeshes) {
                mesh.Dispose();
            }
            foreach (Texture texture in _texturesLoaded.Values) {
                texture.Dispose();
            }
            _texturesLoaded.Clear();
            _texturesLoaded = null;
            _meshInstances.Clear();
            _uniqueMeshes.Clear();
        }
    }
}