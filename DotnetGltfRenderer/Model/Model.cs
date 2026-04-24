using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using SharpGLTF.Animations;
using SharpGLTF.Memory;
using SharpGLTF.Schema2;
using SharpGLTF.Validation;
using GltfScene = SharpGLTF.Schema2.Scene;
using GltfPrimitiveType = SharpGLTF.Schema2.PrimitiveType;

namespace DotnetGltfRenderer {
    public class Model : IDisposable {
        static readonly Dictionary<string, Dictionary<(int MeshIndex, int PrimitiveIndex), Mesh>> _globalMeshCache = new();
        static readonly Dictionary<string, int> _globalMeshRefCount = new();
        static readonly object _cacheLock = new();

        Dictionary<int, Texture> _texturesLoaded;
        readonly List<MeshInstance> _meshInstances = [];
        readonly List<Mesh> _uniqueMeshes = [];
        readonly List<Light> _lights = [];

        // KHR_materials_variants: stores mappings by (meshIndex, primitiveIndex)
        readonly Dictionary<(int MeshIndex, int PrimitiveIndex), Dictionary<int, int>> _variantMappings = new();

        Animation _activeAnimation;
        float _animationTimeSeconds;
        int _activeAnimationIndex;

        // 动画名称列表
        readonly List<string> _animationNames = new();

        // KHR_animation_pointer support
        AnimationPointerProcessor _pointerProcessor;
        ModelRoot _modelRoot;

        // 节点世界矩阵缓存（用于优化动画性能）
        readonly Dictionary<Node, Matrix4x4> _nodeWorldMatrixCache = new();

        // 动画更新用的临时集合（避免每帧分配）
        readonly HashSet<Node> _nodesToUpdate = new();

        // morph target 动画 channel 缓存（Node -> weights channel）
        readonly Dictionary<Node, IAnimationSampler<float[]>> _morphSamplerCache = new();

        // ========== 场景缓存支持 ==========
        readonly List<string> _sceneNames = new();
        int _activeSceneIndex;
        readonly Dictionary<int, List<MeshInstance>> _sceneMeshInstances = new();
        readonly Dictionary<int, List<Light>> _sceneLights = new();

        public string Directory { get; protected set; } = string.Empty;
        public string FilePath { get; private set; }
        public IReadOnlyList<Mesh> Meshes => _uniqueMeshes;
        public IReadOnlyList<MeshInstance> MeshInstances => _meshInstances;
        public IReadOnlyList<Light> Lights => _lights;
        public IEnumerable<string> ExtensionsUsed => _modelRoot.ExtensionsUsed;

        // ========== 场景支持 ==========
        public IReadOnlyList<string> SceneNames => _sceneNames;

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

        public string ActiveSceneName => _activeSceneIndex >= 0 && _activeSceneIndex < _sceneNames.Count ? _sceneNames[_activeSceneIndex] : null;

        // KHR_materials_variants support
        readonly List<string> _variants = new();
        public IReadOnlyList<string> Variants => _variants;
        public int ActiveVariantIndex { get; set; } = -1;

        public string ActiveVariant {
            get => ActiveVariantIndex >= 0 && ActiveVariantIndex < _variants.Count ? _variants[ActiveVariantIndex] : null;
            set => ActiveVariantIndex = value == null ? -1 : _variants.IndexOf(value);
        }

        public bool HasAnimation => _activeAnimation != null;
        public float AnimationDurationSeconds { get; private set; }

        /// <summary>
        /// Current animation time in seconds
        /// </summary>
        public float AnimationTimeSeconds {
            get => _animationTimeSeconds;
            set {
                if (_activeAnimation == null
                    || AnimationDurationSeconds <= 0f) {
                    return;
                }
                _animationTimeSeconds = Math.Clamp(value, 0f, AnimationDurationSeconds);
            }
        }

        /// <summary>
        /// 动画是否暂停
        /// </summary>
        public bool IsAnimationPaused { get; set; }

        /// <summary>
        /// 所有动画名称列表
        /// </summary>
        public IReadOnlyList<string> AnimationNames => _animationNames;

        /// <summary>
        /// 当前活动动画索引
        /// </summary>
        public int ActiveAnimationIndex {
            get => _activeAnimationIndex;
            set {
                if (value < 0
                    || value >= _animationNames.Count) {
                    return;
                }
                if (value == _activeAnimationIndex) {
                    return;
                }
                SetActiveAnimation(value);
            }
        }

        /// <summary>
        /// 当前活动动画名称
        /// </summary>
        public string ActiveAnimationName => _activeAnimationIndex >= 0 && _activeAnimationIndex < _animationNames.Count
            ? _animationNames[_activeAnimationIndex]
            : null;

        public Texture GetTexture(int logicalIndex) => _texturesLoaded.TryGetValue(logicalIndex, out Texture tex) ? tex : null;

        public Model(string path) {
            LoadModel(path);
        }

        void LoadModel(string path) {
            string fullPath = Path.GetFullPath(path);
            FilePath = fullPath;
            Directory = Path.GetDirectoryName(fullPath) ?? string.Empty;

            Dictionary<(int MeshIndex, int PrimitiveIndex), Mesh> cachedMeshes;
            lock (_cacheLock) {
                if (_globalMeshCache.TryGetValue(fullPath, out cachedMeshes)) {
                    foreach (Mesh mesh in cachedMeshes.Values) {
                        _uniqueMeshes.Add(mesh);
                    }
                    _globalMeshRefCount[fullPath] = _globalMeshRefCount.GetValueOrDefault(fullPath, 0) + 1;
                }
            }

            if (cachedMeshes != null) {
                _modelRoot = ModelRoot.Load(fullPath, new ReadSettings { Validation = ValidationMode.Skip });
                LoadMaterialsAndInstances(cachedMeshes);
                return;
            }

            _modelRoot = ModelRoot.Load(fullPath, new ReadSettings { Validation = ValidationMode.Skip });
            DetectExtensions(_modelRoot);

            List<string> variants = GltfVariantLoader.LoadVariants(_modelRoot);
            _variants.AddRange(variants);
            _variantMappings.Clear();
            foreach (KeyValuePair<(int MeshIndex, int PrimitiveIndex), Dictionary<int, int>> kvp in
                GltfVariantLoader.LoadVariantMappings(_modelRoot)) {
                _variantMappings[kvp.Key] = kvp.Value;
            }

            _texturesLoaded = GltfTextureLoader.LoadTextures(_modelRoot);
            Dictionary<(int MeshIndex, int PrimitiveIndex), Mesh> newCache = PreloadAllScenesAndCache();

            int defaultSceneIndex = 0;
            if (_modelRoot.DefaultScene != null) {
                defaultSceneIndex = _modelRoot.DefaultScene.LogicalIndex;
            }
            _activeSceneIndex = Math.Min(defaultSceneIndex, _sceneNames.Count - 1);
            if (_activeSceneIndex < 0) {
                _activeSceneIndex = 0;
            }

            SwitchToScene(_activeSceneIndex);

            _animationNames.Clear();
            foreach (Animation anim in _modelRoot.LogicalAnimations) {
                _animationNames.Add(anim.Name ?? $"Animation {_animationNames.Count}");
            }

            _activeAnimationIndex = _animationNames.Count > 0 ? 0 : -1;
            _activeAnimation = _activeAnimationIndex >= 0 ? _modelRoot.LogicalAnimations[_activeAnimationIndex] : null;
            AnimationDurationSeconds = _activeAnimation?.Duration ?? 0f;
            _animationTimeSeconds = 0f;

            BuildMorphSamplerCache();

            _pointerProcessor = new AnimationPointerProcessor();
            _pointerProcessor.ProcessAnimation(_activeAnimation, _modelRoot, this);
            UpdateMeshInstanceTransforms();

            lock (_cacheLock) {
                _globalMeshCache[fullPath] = newCache;
                _globalMeshRefCount[fullPath] = 1;
            }
        }

        void LoadMaterialsAndInstances(Dictionary<(int MeshIndex, int PrimitiveIndex), Mesh> cachedMeshes) {
            DetectExtensions(_modelRoot);

            List<string> variants = GltfVariantLoader.LoadVariants(_modelRoot);
            _variants.AddRange(variants);
            _variantMappings.Clear();
            foreach (KeyValuePair<(int MeshIndex, int PrimitiveIndex), Dictionary<int, int>> kvp in
                GltfVariantLoader.LoadVariantMappings(_modelRoot)) {
                _variantMappings[kvp.Key] = kvp.Value;
            }

            if (_texturesLoaded != null) {
                foreach (Texture texture in _texturesLoaded.Values) {
                    texture.Dispose();
                }
            }
            _texturesLoaded = GltfTextureLoader.LoadTextures(_modelRoot);
            PreloadAllScenesWithCachedMeshes(cachedMeshes);

            int defaultSceneIndex = 0;
            if (_modelRoot.DefaultScene != null) {
                defaultSceneIndex = _modelRoot.DefaultScene.LogicalIndex;
            }
            _activeSceneIndex = Math.Min(defaultSceneIndex, _sceneNames.Count - 1);
            if (_activeSceneIndex < 0) {
                _activeSceneIndex = 0;
            }

            SwitchToScene(_activeSceneIndex);

            _animationNames.Clear();
            foreach (Animation anim in _modelRoot.LogicalAnimations) {
                _animationNames.Add(anim.Name ?? $"Animation {_animationNames.Count}");
            }

            _activeAnimationIndex = _animationNames.Count > 0 ? 0 : -1;
            _activeAnimation = _activeAnimationIndex >= 0 ? _modelRoot.LogicalAnimations[_activeAnimationIndex] : null;
            AnimationDurationSeconds = _activeAnimation?.Duration ?? 0f;
            _animationTimeSeconds = 0f;

            BuildMorphSamplerCache();

            _pointerProcessor = new AnimationPointerProcessor();
            _pointerProcessor.ProcessAnimation(_activeAnimation, _modelRoot, this);
            UpdateMeshInstanceTransforms();
        }

        Dictionary<(int MeshIndex, int PrimitiveIndex), Mesh> PreloadAllScenesAndCache() {
            _sceneNames.Clear();
            foreach (GltfScene scene in _modelRoot.LogicalScenes) {
                _sceneNames.Add(scene.Name ?? $"Scene {scene.LogicalIndex}");
            }
            if (_sceneNames.Count == 0) {
                _sceneNames.Add("Default Scene");
            }
            _sceneMeshInstances.Clear();
            _sceneLights.Clear();
            Dictionary<(int MeshIndex, int PrimitiveIndex), Mesh> primitiveMeshCache = new();
            for (int sceneIndex = 0; sceneIndex < _modelRoot.LogicalScenes.Count; sceneIndex++) {
                GltfScene scene = _modelRoot.LogicalScenes[sceneIndex];
                ProcessScene(scene, sceneIndex, primitiveMeshCache);
            }
            if (_modelRoot.LogicalScenes.Count == 0) {
                ProcessScene(null, 0, primitiveMeshCache);
            }
            return new Dictionary<(int MeshIndex, int PrimitiveIndex), Mesh>(primitiveMeshCache);
        }

        void PreloadAllScenesWithCachedMeshes(Dictionary<(int MeshIndex, int PrimitiveIndex), Mesh> cachedMeshes) {
            _sceneNames.Clear();
            foreach (GltfScene scene in _modelRoot.LogicalScenes) {
                _sceneNames.Add(scene.Name ?? $"Scene {scene.LogicalIndex}");
            }
            if (_sceneNames.Count == 0) {
                _sceneNames.Add("Default Scene");
            }
            _sceneMeshInstances.Clear();
            _sceneLights.Clear();

            for (int sceneIndex = 0; sceneIndex < _modelRoot.LogicalScenes.Count; sceneIndex++) {
                GltfScene scene = _modelRoot.LogicalScenes[sceneIndex];
                ProcessSceneWithCachedMeshes(scene, sceneIndex, cachedMeshes);
            }
            if (_modelRoot.LogicalScenes.Count == 0) {
                ProcessSceneWithCachedMeshes(null, 0, cachedMeshes);
            }
        }

        void ProcessSceneWithCachedMeshes(GltfScene scene, int sceneIndex, Dictionary<(int MeshIndex, int PrimitiveIndex), Mesh> cachedMeshes) {
            List<MeshInstance> instances = new();
            List<Light> lights = new();

            if (scene != null) {
                foreach (Node rootNode in scene.VisualChildren) {
                    ProcessNodeWithCachedMeshes(rootNode, Matrix4x4.Identity, instances, lights, cachedMeshes);
                }
            }
            else {
                foreach (Node rootNode in _modelRoot.LogicalNodes.Where(node => node.VisualParent == null)) {
                    ProcessNodeWithCachedMeshes(rootNode, Matrix4x4.Identity, instances, lights, cachedMeshes);
                }
            }
            _sceneMeshInstances[sceneIndex] = instances;
            _sceneLights[sceneIndex] = lights;
        }

        void ProcessNodeWithCachedMeshes(Node node, Matrix4x4 parentWorldMatrix, List<MeshInstance> instances, List<Light> lights, Dictionary<(int MeshIndex, int PrimitiveIndex), Mesh> cachedMeshes) {
            if (node.TryGetVisibility(out bool isVisible) && !isVisible) {
                return;
            }
            Matrix4x4 worldMatrix = node.LocalMatrix * parentWorldMatrix;

            PunctualLight punctualLight = node.PunctualLight;
            if (punctualLight != null) {
                Light light = Light.ConvertFromGltf(punctualLight, worldMatrix, node);
                if (light != null) {
                    lights.Add(light);
                }
            }

            MeshGpuInstancing gpuInstancing = node.GetGpuInstancing();
            if (node.Mesh != null) {
                if (gpuInstancing != null && gpuInstancing.Count > 0) {
                    ProcessInstancedNodeWithCachedMeshes(node, instances, cachedMeshes);
                }
                else {
                    for (int i = 0; i < node.Mesh.Primitives.Count; i++) {
                        MeshPrimitive primitive = node.Mesh.Primitives[i];
                        if (primitive.DrawPrimitiveType != GltfPrimitiveType.TRIANGLES) {
                            continue;
                        }
                        (int MeshIndex, int PrimitiveIndex) key = (node.Mesh.LogicalIndex, i);
                        if (cachedMeshes.TryGetValue(key, out Mesh mesh)) {
                            instances.Add(new MeshInstance(node, mesh, node.Skin));
                        }
                    }
                }
            }

            foreach (Node child in node.VisualChildren) {
                ProcessNodeWithCachedMeshes(child, worldMatrix, instances, lights, cachedMeshes);
            }
        }

        void ProcessInstancedNodeWithCachedMeshes(Node node, List<MeshInstance> instances, Dictionary<(int MeshIndex, int PrimitiveIndex), Mesh> cachedMeshes) {
            if (node.Skin != null) {
                for (int i = 0; i < node.Mesh.Primitives.Count; i++) {
                    MeshPrimitive primitive = node.Mesh.Primitives[i];
                    if (primitive.DrawPrimitiveType != GltfPrimitiveType.TRIANGLES) {
                        continue;
                    }
                    (int MeshIndex, int PrimitiveIndex) key = (node.Mesh.LogicalIndex, i);
                    if (cachedMeshes.TryGetValue(key, out Mesh mesh)) {
                        instances.Add(new MeshInstance(node, mesh, node.Skin));
                    }
                }
            }
        }

        void BuildMorphSamplerCache() {
            _morphSamplerCache.Clear();
            if (_activeAnimation == null) {
                return;
            }
            foreach (AnimationChannel channel in _activeAnimation.Channels) {
                if (channel.TargetNodePath == PropertyPath.weights) {
                    IAnimationSampler<float[]> sampler = channel.GetMorphSampler();
                    if (sampler != null) {
                        _morphSamplerCache[channel.TargetNode] = sampler;
                    }
                }
            }
        }

        void DetectExtensions(ModelRoot modelRoot) {
            foreach (string extension in modelRoot.ExtensionsRequired) {
                if (!ExtensionManager.IsExtensionEnabled(extension)) {
                    throw new Exception($"Detected unsupported or disabled extension: {extension}");
                }
            }
        }

        void ProcessScene(GltfScene scene, int sceneIndex, Dictionary<(int MeshIndex, int PrimitiveIndex), Mesh> primitiveMeshCache) {
            List<MeshInstance> instances = new();
            List<Light> lights = new();
            if (scene != null) {
                foreach (Node rootNode in scene.VisualChildren) {
                    ProcessNodeForScene(rootNode, primitiveMeshCache, Matrix4x4.Identity, instances, lights);
                }
            }
            else {
                foreach (Node rootNode in _modelRoot.LogicalNodes.Where(node => node.VisualParent == null)) {
                    ProcessNodeForScene(rootNode, primitiveMeshCache, Matrix4x4.Identity, instances, lights);
                }
            }
            _sceneMeshInstances[sceneIndex] = instances;
            _sceneLights[sceneIndex] = lights;
        }

        void ProcessNodeForScene(Node node,
            Dictionary<(int MeshIndex, int PrimitiveIndex), Mesh> primitiveMeshCache,
            Matrix4x4 parentWorldMatrix,
            List<MeshInstance> instances,
            List<Light> lights) {
            if (node.TryGetVisibility(out bool isVisible)
                && !isVisible) {
                return;
            }
            Matrix4x4 worldMatrix = node.LocalMatrix * parentWorldMatrix;

            // Check for KHR_lights_punctual extension
            PunctualLight punctualLight = node.PunctualLight;
            if (punctualLight != null) {
                Light light = Light.ConvertFromGltf(punctualLight, worldMatrix, node);
                if (light != null) {
                    lights.Add(light);
                }
            }
            MeshGpuInstancing gpuInstancing = node.GetGpuInstancing();
            if (node.Mesh != null) {
                if (gpuInstancing != null
                    && gpuInstancing.Count > 0) {
                    ProcessInstancedNodeForScene(node, gpuInstancing, primitiveMeshCache, instances);
                }
                else {
                    for (int i = 0; i < node.Mesh.Primitives.Count; i++) {
                        MeshPrimitive primitive = node.Mesh.Primitives[i];
                        if (primitive.DrawPrimitiveType != GltfPrimitiveType.TRIANGLES) {
                            continue;
                        }
                        (int LogicalIndex, int i) key = (node.Mesh.LogicalIndex, i);
                        if (!primitiveMeshCache.TryGetValue(key, out Mesh mesh)) {
                            _variantMappings.TryGetValue(key, out Dictionary<int, int> variantMatMapping);
                            mesh = ProcessPrimitive(primitive, variantMatMapping);
                            primitiveMeshCache[key] = mesh;
                            _uniqueMeshes.Add(mesh);
                        }
                        instances.Add(new MeshInstance(node, mesh, node.Skin));
                    }
                }
            }
            foreach (Node child in node.VisualChildren) {
                ProcessNodeForScene(child, primitiveMeshCache, worldMatrix, instances, lights);
            }
        }

        void ProcessInstancedNodeForScene(Node node,
            MeshGpuInstancing gpuInstancing,
            Dictionary<(int MeshIndex, int PrimitiveIndex), Mesh> primitiveMeshCache,
            List<MeshInstance> instances) {
            int instanceCount = gpuInstancing.Count;
            if (instanceCount == 0) {
                return;
            }

            // 蒙皮网格不支持 GPU 实例化
            if (node.Skin != null) {
                for (int i = 0; i < node.Mesh.Primitives.Count; i++) {
                    MeshPrimitive primitive = node.Mesh.Primitives[i];
                    if (primitive.DrawPrimitiveType != GltfPrimitiveType.TRIANGLES) {
                        continue;
                    }
                    (int LogicalIndex, int i) key = (node.Mesh.LogicalIndex, i);
                    if (!primitiveMeshCache.TryGetValue(key, out Mesh mesh)) {
                        _variantMappings.TryGetValue(key, out Dictionary<int, int> variantMatMapping);
                        mesh = ProcessPrimitive(primitive, variantMatMapping);
                        primitiveMeshCache[key] = mesh;
                        _uniqueMeshes.Add(mesh);
                    }
                    instances.Add(new MeshInstance(node, mesh, node.Skin));
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
                if (primitive.DrawPrimitiveType != GltfPrimitiveType.TRIANGLES) {
                    continue;
                }

                // Morph targets 不支持 GPU 实例化
                if (primitive.MorphTargetsCount > 0) {
                    (int LogicalIndex, int i) key = (node.Mesh.LogicalIndex, primIdx);
                    if (!primitiveMeshCache.TryGetValue(key, out Mesh mesh)) {
                        _variantMappings.TryGetValue(key, out Dictionary<int, int> variantMatMapping);
                        mesh = ProcessPrimitive(primitive, variantMatMapping);
                        primitiveMeshCache[key] = mesh;
                        _uniqueMeshes.Add(mesh);
                    }
                    instances.Add(new MeshInstance(node, mesh, node.Skin));
                    continue;
                }
                (int LogicalIndex, int i) primKey = (node.Mesh.LogicalIndex, primIdx);
                _variantMappings.TryGetValue(primKey, out Dictionary<int, int> primVariantMapping);
                if (positiveScaleInstances.Count > 0) {
                    CreateInstancedMeshForScene(
                        node,
                        primitive,
                        positiveScaleInstances,
                        instanceWorldMatrices,
                        primVariantMapping,
                        false,
                        instances
                    );
                }
                if (negativeScaleInstances.Count > 0) {
                    CreateInstancedMeshForScene(
                        node,
                        primitive,
                        negativeScaleInstances,
                        instanceWorldMatrices,
                        primVariantMapping,
                        true,
                        instances
                    );
                }
            }
        }

        void CreateInstancedMeshForScene(Node node,
            MeshPrimitive primitive,
            List<int> instanceIndices,
            Matrix4x4[] allMatrices,
            Dictionary<int, int> variantMatMapping,
            bool isNegativeScale,
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
            MeshInstance instance = new(node, mesh, null) { UseGpuInstancing = true, IsNegativeScale = isNegativeScale };
            instances.Add(instance);
        }

        void SwitchToScene(int sceneIndex) {
            _meshInstances.Clear();
            _lights.Clear();
            if (_sceneMeshInstances.TryGetValue(sceneIndex, out List<MeshInstance> instances)) {
                _meshInstances.AddRange(instances);
            }
            if (_sceneLights.TryGetValue(sceneIndex, out List<Light> lights)) {
                _lights.AddRange(lights);
            }
            UpdateMeshInstanceTransforms();
        }

        Mesh ProcessPrimitive(MeshPrimitive primitive, Dictionary<int, int> variantMatMapping) {
            Mesh mesh = new();
            SharpGLTF.Schema2.Material material = primitive.Material;
            IAccessorArray<Vector3> posAcc = primitive.GetVertexAccessor("POSITION")?.AsVector3Array()
                ?? throw new InvalidOperationException("glTF primitive is missing POSITION accessor.");
            IAccessorArray<Vector3> normalsAcc = primitive.GetVertexAccessor("NORMAL")?.AsVector3Array();
            IAccessorArray<Vector2> uv0Acc = primitive.GetVertexAccessor("TEXCOORD_0")?.AsVector2Array();
            IAccessorArray<Vector2> uv1Acc = primitive.GetVertexAccessor("TEXCOORD_1")?.AsVector2Array();
            IAccessorArray<Vector4> colorsAcc = primitive.GetVertexAccessor("COLOR_0")?.AsColorArray();
            IAccessorArray<Vector4> tangentsAcc = primitive.GetVertexAccessor("TANGENT")?.AsVector4Array();
            IAccessorArray<Vector4> jointsAcc = primitive.GetVertexAccessor("JOINTS_0")?.AsVector4Array();
            IAccessorArray<Vector4> weightsAcc = primitive.GetVertexAccessor("WEIGHTS_0")?.AsVector4Array();
            uint[] originalGltfIndices = primitive.GetIndices()?.ToArray();
            mesh.Indices = originalGltfIndices ?? Enumerable.Range(0, posAcc.Count).Select(i => (uint)i).ToArray();

            // 准备可变顶点数据（用于 unweld 时修改）
            IReadOnlyList<Vector3> positions = posAcc;
            IReadOnlyList<Vector3> normals = normalsAcc;
            IReadOnlyList<Vector2> uv0 = uv0Acc;
            IReadOnlyList<Vector2> uv1 = uv1Acc;
            IReadOnlyList<Vector4> colors = colorsAcc;
            IReadOnlyList<Vector4> joints = jointsAcc;
            IReadOnlyList<Vector4> weights = weightsAcc;
            IReadOnlyList<Vector4> tangents = tangentsAcc;

            // Morph Target 数据收集
            int morphTargetCount = primitive.MorphTargetsCount;
            IReadOnlyList<float> morphWeights = primitive.LogicalParent.MorphWeights;
            IReadOnlyList<Vector3>[] morphPositions = null;
            IReadOnlyList<Vector3>[] morphNormals = null;
            IReadOnlyList<Vector4>[] morphTangentsArr = null;
            IReadOnlyList<Vector2>[] morphTexCoords0 = null;
            IReadOnlyList<Vector2>[] morphTexCoords1 = null;
            IReadOnlyList<Vector4>[] morphColors0 = null;
            if (morphTargetCount > 0) {
                morphPositions = new IReadOnlyList<Vector3>[morphTargetCount];
                morphNormals = new IReadOnlyList<Vector3>[morphTargetCount];
                morphTangentsArr = new IReadOnlyList<Vector4>[morphTargetCount];
                morphTexCoords0 = new IReadOnlyList<Vector2>[morphTargetCount];
                morphTexCoords1 = new IReadOnlyList<Vector2>[morphTargetCount];
                morphColors0 = new IReadOnlyList<Vector4>[morphTargetCount];
                for (int t = 0; t < morphTargetCount; t++) {
                    IReadOnlyDictionary<string, Accessor> targetAccessors = primitive.GetMorphTargetAccessors(t);
                    morphPositions[t] = targetAccessors.TryGetValue("POSITION", out Accessor morphPosAcc) ? morphPosAcc.AsVector3Array() : null;
                    morphNormals[t] = targetAccessors.TryGetValue("NORMAL", out Accessor morphNrmAcc) ? morphNrmAcc.AsVector3Array() : null;
                    morphTangentsArr[t] = targetAccessors.TryGetValue("TANGENT", out Accessor morphTanAcc) ? morphTanAcc.AsVector4Array() : null;
                    morphTexCoords0[t] = targetAccessors.TryGetValue("TEXCOORD_0", out Accessor morphUv0Acc) ? morphUv0Acc.AsVector2Array() : null;
                    morphTexCoords1[t] = targetAccessors.TryGetValue("TEXCOORD_1", out Accessor morphUv1Acc) ? morphUv1Acc.AsVector2Array() : null;
                    morphColors0[t] = targetAccessors.TryGetValue("COLOR_0", out Accessor morphColAcc) ? morphColAcc.AsColorArray() : null;
                }
            }

            // 没有预计算切线且有索引缓冲区时，先 unweld 再生成切线
            Vector4[] generatedTangents;
            if (tangentsAcc == null && originalGltfIndices != null) {
                uint[] originalIndices = mesh.Indices;
                List<Vector3> posList = new(posAcc);
                List<Vector3> nrmList = normalsAcc != null ? new List<Vector3>(normalsAcc) : null;
                List<Vector2> uv0List = uv0Acc != null ? new List<Vector2>(uv0Acc) : null;
                List<Vector2> uv1List = uv1Acc != null ? new List<Vector2>(uv1Acc) : null;
                List<Vector4> colList = colorsAcc != null ? new List<Vector4>(colorsAcc) : null;

                mesh.Indices = GltfGeometryProcessor.Unweld(posList, nrmList, uv0List, uv1List, colList, mesh.Indices);
                mesh.IsUnwelded = true;
                mesh.VertexCount = posList.Count;
                positions = posList;
                normals = nrmList;
                uv0 = uv0List;
                uv1 = uv1List;
                colors = colList;

                // Unweld 蒙皮属性
                if (jointsAcc != null && weightsAcc != null) {
                    List<Vector4> jointsList = new(jointsAcc);
                    List<Vector4> weightsList = new(weightsAcc);
                    GltfGeometryProcessor.UnweldSkinAttributes(jointsList, weightsList, originalIndices);
                    joints = jointsList;
                    weights = weightsList;
                }

                // Unweld Morph Targets
                if (morphTargetCount > 0) {
                    GltfGeometryProcessor.UnweldMorphTargets(
                        morphPositions, morphNormals, morphTangentsArr,
                        morphTexCoords0, morphTexCoords1, morphColors0,
                        originalIndices);
                }

                generatedTangents = GltfGeometryProcessor.GenerateTangents(positions, normals, uv0, mesh.Indices);
            }
            else {
                generatedTangents = GltfGeometryProcessor.GenerateTangents(positions, normals, uv0, mesh.Indices);
            }

            // Morph Target 纹理化
            if (morphTargetCount > 0) {
                HashSet<string> morphAttributes = new();
                for (int t = 0; t < morphTargetCount; t++) {
                    IReadOnlyDictionary<string, Accessor> targetAccessors = primitive.GetMorphTargetAccessors(t);
                    foreach (string attr in targetAccessors.Keys) {
                        morphAttributes.Add(attr);
                    }
                }
                mesh.MorphTargetCount = morphTargetCount;
                mesh.MorphTargetTexture = new MorphTargetTexture(positions.Count, morphTargetCount, morphAttributes);
                mesh.MorphTargetTexture.UploadData(
                    morphPositions,
                    morphNormals,
                    morphTangentsArr,
                    morphTexCoords0,
                    morphTexCoords1,
                    morphColors0
                );
                mesh.MorphWeights = new float[morphTargetCount];
                if (morphWeights != null) {
                    for (int i = 0; i < Math.Min(morphWeights.Count, morphTargetCount); i++) {
                        mesh.MorphWeights[i] = morphWeights[i];
                    }
                }
            }
            mesh.HasUV1 = uv1 != null;
            mesh.HasColor0 = colors != null;
            GltfGeometryProcessor.FillVertexBuffers(
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

            // Load material
            mesh.Material = new Material { SourceMaterialIndex = material?.LogicalIndex ?? -1 };
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

        /// <summary>
        /// 设置活动动画
        /// </summary>
        public void SetActiveAnimation(int index) {
            if (index < 0
                || index >= _animationNames.Count) {
                return;
            }
            _activeAnimationIndex = index;
            _activeAnimation = _modelRoot.LogicalAnimations[index];
            AnimationDurationSeconds = _activeAnimation?.Duration ?? 0f;
            _animationTimeSeconds = 0f;

            // 重新构建 morph target sampler 缓存
            BuildMorphSamplerCache();

            // 重新初始化 animation pointer processor
            _pointerProcessor?.ProcessAnimation(_activeAnimation, _modelRoot, this);
        }

        public void Update(float deltaTimeSeconds) {
            // 检查暂停状态
            if (IsAnimationPaused) {
                // 即使暂停也需要更新变换（使用当前时间）
                _pointerProcessor?.Update(_animationTimeSeconds);
                UpdateMeshInstanceTransforms();
                return;
            }
            if (_activeAnimation != null
                && AnimationDurationSeconds > 0f) {
                _animationTimeSeconds += MathF.Max(0f, deltaTimeSeconds);
                _animationTimeSeconds %= AnimationDurationSeconds;
            }
            _pointerProcessor?.Update(_animationTimeSeconds);
            UpdateMeshInstanceTransforms();
        }

        void UpdateLightTransforms() {
            foreach (Light light in _lights) {
                if (light.SourceNode == null) {
                    continue;
                }
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
                light.Position = new Vector3(worldMatrix.M41, worldMatrix.M42, worldMatrix.M43);
                Vector3 forward = new(-worldMatrix.M31, -worldMatrix.M32, -worldMatrix.M33);
                light.Direction = Vector3.Normalize(forward);
            }
        }

        void UpdateMeshInstanceTransforms() {
            if (_activeAnimation != null) {
                _nodesToUpdate.Clear();
                foreach (MeshInstance instance in _meshInstances) {
                    _nodesToUpdate.Add(instance.Node);
                    if (instance.HasSkinning
                        && instance._joints != null) {
                        foreach (Node joint in instance._joints) {
                            _nodesToUpdate.Add(joint);
                        }
                    }
                }
                foreach (Light light in _lights) {
                    if (light.SourceNode != null) {
                        _nodesToUpdate.Add(light.SourceNode);
                    }
                }
                foreach (Node node in _nodesToUpdate) {
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
                instance.ApplyGizmoTransform();
                instance.IsNegativeScale = instance.WorldMatrix.GetDeterminant() < 0f;
                instance.UpdateSkinning(_activeAnimation, _animationTimeSeconds, _nodeWorldMatrixCache);

                // Update morph weights (使用缓存的 sampler)
                if (_activeAnimation != null
                    && instance.Mesh.HasMorphTargets) {
                    float[] weights = instance.Mesh.MorphWeights;
                    if (weights != null
                        && _morphSamplerCache.TryGetValue(instance.Node, out IAnimationSampler<float[]> sampler)) {
                        ICurveSampler<float[]> curveSampler = sampler.CreateCurveSampler();
                        float[] animatedWeights = curveSampler.GetPoint(_animationTimeSeconds);
                        if (animatedWeights != null) {
                            for (int i = 0; i < Math.Min(weights.Length, animatedWeights.Length); i++) {
                                weights[i] = animatedWeights[i];
                            }
                        }
                    }
                }
            }
            UpdateLightTransforms();
        }

        public void Dispose() {
            if (!string.IsNullOrEmpty(FilePath)) {
                lock (_cacheLock) {
                    if (_globalMeshRefCount.TryGetValue(FilePath, out int count)) {
                        count--;
                        if (count <= 0) {
                            if (_globalMeshCache.TryGetValue(FilePath, out Dictionary<(int MeshIndex, int PrimitiveIndex), Mesh> meshes)) {
                                foreach (Mesh mesh in meshes.Values) {
                                    mesh.Dispose();
                                }
                            }
                            _globalMeshCache.Remove(FilePath);
                            _globalMeshRefCount.Remove(FilePath);
                        }
                        else {
                            _globalMeshRefCount[FilePath] = count;
                        }
                    }
                }
            }
            else {
                foreach (Mesh mesh in _uniqueMeshes) {
                    mesh.Dispose();
                }
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