using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using SharpGLTF.Animations;
using SharpGLTF.Memory;
using SharpGLTF.Schema2;
using SharpGLTF.Validation;
using Silk.NET.OpenGLES;
using GltfScene = SharpGLTF.Schema2.Scene;
using GltfPrimitiveType = SharpGLTF.Schema2.PrimitiveType;

namespace DotnetGltfRenderer {
    public class Model : IDisposable {
        readonly GL _gl;
        Dictionary<int, Texture> _texturesLoaded;
        readonly List<MeshInstance> _meshInstances = [];
        readonly List<Mesh> _uniqueMeshes = [];
        readonly List<Light> _lights = [];

        // KHR_materials_variants: stores mappings by (meshIndex, primitiveIndex)
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

        public string Directory { get; protected set; } = string.Empty;
        public IReadOnlyList<Mesh> Meshes => _uniqueMeshes;
        public IReadOnlyList<MeshInstance> MeshInstances => _meshInstances;
        public IReadOnlyList<Light> Lights => _lights;
        public IEnumerable<string> ExtensionsUsed => _modelRoot.ExtensionsUsed;

        // ========== 场景支持 ==========
        public IReadOnlyList<string> SceneNames => _sceneNames;

        public int ActiveSceneIndex {
            get => _activeSceneIndex;
            set {
                if (value < 0 || value >= _sceneNames.Count) return;
                if (value == _activeSceneIndex) return;
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
            set {
                ActiveVariantIndex = value == null ? -1 : _variants.IndexOf(value);
            }
        }

        public bool HasAnimation => _activeAnimation != null;
        public float AnimationDurationSeconds { get; private set; }

        public Texture GetTexture(int logicalIndex) => _texturesLoaded.TryGetValue(logicalIndex, out Texture tex) ? tex : null;

        public Model(GL gl, string path) {
            _gl = gl;
            LoadModel(path);
        }

        void LoadModel(string path) {
            string fullPath = Path.GetFullPath(path);
            Directory = Path.GetDirectoryName(fullPath) ?? string.Empty;
            _modelRoot = ModelRoot.Load(fullPath, new ReadSettings { Validation = ValidationMode.Skip });

            DetectExtensions(_modelRoot);

            // 加载变体
            List<string> variants = GltfVariantLoader.LoadVariants(_modelRoot);
            _variants.AddRange(variants);
            _variantMappings.Clear();
            foreach (var kvp in GltfVariantLoader.LoadVariantMappings(_modelRoot)) {
                _variantMappings[kvp.Key] = kvp.Value;
            }

            // 加载纹理
            _texturesLoaded = GltfTextureLoader.LoadTextures(_gl, _modelRoot);

            // 预缓存所有场景
            PreloadAllScenes();

            // 设置默认场景
            int defaultSceneIndex = 0;
            if (_modelRoot.DefaultScene != null) {
                defaultSceneIndex = _modelRoot.DefaultScene.LogicalIndex;
            }
            _activeSceneIndex = Math.Min(defaultSceneIndex, _sceneNames.Count - 1);
            if (_activeSceneIndex < 0) _activeSceneIndex = 0;

            // 切换到默认场景
            SwitchToScene(_activeSceneIndex);

            _activeAnimation = _modelRoot.LogicalAnimations.FirstOrDefault();
            AnimationDurationSeconds = _activeAnimation?.Duration ?? 0f;

            // Initialize KHR_animation_pointer processor
            _pointerProcessor = new AnimationPointerProcessor();
            _pointerProcessor.ProcessAnimation(_activeAnimation, _modelRoot, this);
            UpdateMeshInstanceTransforms();
        }

        void DetectExtensions(ModelRoot modelRoot) {
            foreach (string extension in modelRoot.ExtensionsRequired) {
                if (!ExtensionManager.IsExtensionEnabled(extension)) {
                    throw new Exception($"Detected unsupported or disabled extension: {extension}");
                }
            }
        }

        void PreloadAllScenes() {
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
            if (node.TryGetVisibility(out bool isVisible) && !isVisible) return;

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
                if (gpuInstancing != null && gpuInstancing.Count > 0) {
                    ProcessInstancedNodeForScene(node, gpuInstancing, primitiveMeshCache, instances);
                }
                else {
                    for (int i = 0; i < node.Mesh.Primitives.Count; i++) {
                        MeshPrimitive primitive = node.Mesh.Primitives[i];
                        if (primitive.DrawPrimitiveType != GltfPrimitiveType.TRIANGLES) continue;

                        (int LogicalIndex, int i) key = (node.Mesh.LogicalIndex, i);
                        if (!primitiveMeshCache.TryGetValue(key, out Mesh mesh)) {
                            _variantMappings.TryGetValue(key, out Dictionary<int, int> variantMatMapping);
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

        void ProcessInstancedNodeForScene(Node node,
            MeshGpuInstancing gpuInstancing,
            Dictionary<(int MeshIndex, int PrimitiveIndex), Mesh> primitiveMeshCache,
            List<MeshInstance> instances) {
            int instanceCount = gpuInstancing.Count;
            if (instanceCount == 0) return;

            // 蒙皮网格不支持 GPU 实例化
            if (node.Skin != null) {
                for (int i = 0; i < node.Mesh.Primitives.Count; i++) {
                    MeshPrimitive primitive = node.Mesh.Primitives[i];
                    if (primitive.DrawPrimitiveType != GltfPrimitiveType.TRIANGLES) continue;

                    (int LogicalIndex, int i) key = (node.Mesh.LogicalIndex, i);
                    if (!primitiveMeshCache.TryGetValue(key, out Mesh mesh)) {
                        _variantMappings.TryGetValue(key, out Dictionary<int, int> variantMatMapping);
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
                if (determinant < 0) negativeScaleInstances.Add(i);
                else positiveScaleInstances.Add(i);
            }

            for (int primIdx = 0; primIdx < node.Mesh.Primitives.Count; primIdx++) {
                MeshPrimitive primitive = node.Mesh.Primitives[primIdx];
                if (primitive.DrawPrimitiveType != GltfPrimitiveType.TRIANGLES) continue;

                // Morph targets 不支持 GPU 实例化
                if (primitive.MorphTargetsCount > 0) {
                    (int LogicalIndex, int i) key = (node.Mesh.LogicalIndex, primIdx);
                    if (!primitiveMeshCache.TryGetValue(key, out Mesh mesh)) {
                        _variantMappings.TryGetValue(key, out Dictionary<int, int> variantMatMapping);
                        mesh = ProcessPrimitive(primitive, variantMatMapping);
                        primitiveMeshCache[key] = mesh;
                        _uniqueMeshes.Add(mesh);
                    }
                    instances.Add(new MeshInstance(node, mesh, node.Skin, _gl));
                    continue;
                }

                (int LogicalIndex, int i) primKey = (node.Mesh.LogicalIndex, primIdx);
                _variantMappings.TryGetValue(primKey, out Dictionary<int, int> primVariantMapping);

                if (positiveScaleInstances.Count > 0) {
                    CreateInstancedMeshForScene(node, primitive, positiveScaleInstances, instanceWorldMatrices, primVariantMapping, false, instances);
                }
                if (negativeScaleInstances.Count > 0) {
                    CreateInstancedMeshForScene(node, primitive, negativeScaleInstances, instanceWorldMatrices, primVariantMapping, true, instances);
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

            MeshInstance instance = new(node, mesh, null, _gl) {
                UseGpuInstancing = true,
                IsNegativeScale = isNegativeScale
            };
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
            Mesh mesh = new() { GL = _gl };
            SharpGLTF.Schema2.Material material = primitive.Material;

            Accessor posAcc = primitive.GetVertexAccessor("POSITION") ?? throw new InvalidOperationException("glTF primitive is missing POSITION accessor.");
            IAccessorArray<Vector3> positions = posAcc.AsVector3Array();
            IAccessorArray<Vector3> normals = primitive.GetVertexAccessor("NORMAL")?.AsVector3Array();
            IAccessorArray<Vector2> uv0 = primitive.GetVertexAccessor("TEXCOORD_0")?.AsVector2Array();
            IAccessorArray<Vector2> uv1 = primitive.GetVertexAccessor("TEXCOORD_1")?.AsVector2Array();
            IAccessorArray<Vector4> colors = primitive.GetVertexAccessor("COLOR_0")?.AsColorArray();
            IAccessorArray<Vector4> tangents = primitive.GetVertexAccessor("TANGENT")?.AsVector4Array();
            IAccessorArray<Vector4> joints = primitive.GetVertexAccessor("JOINTS_0")?.AsVector4Array();
            IAccessorArray<Vector4> weights = primitive.GetVertexAccessor("WEIGHTS_0")?.AsVector4Array();

            mesh.Indices = primitive.GetIndices()?.ToArray() ?? Enumerable.Range(0, positions.Count).Select(i => (uint)i).ToArray();
            Vector4[] generatedTangents = GltfGeometryProcessor.GenerateTangents(positions, normals, uv0, mesh.Indices);

            // Morph Target 纹理化支持
            int morphTargetCount = primitive.MorphTargetsCount;
            IReadOnlyList<float> morphWeights = primitive.LogicalParent.MorphWeights;

            if (morphTargetCount > 0) {
                HashSet<string> morphAttributes = new();
                for (int t = 0; t < morphTargetCount; t++) {
                    IReadOnlyDictionary<string, Accessor> targetAccessors = primitive.GetMorphTargetAccessors(t);
                    foreach (string attr in targetAccessors.Keys) {
                        morphAttributes.Add(attr);
                    }
                }

                mesh.MorphTargetCount = morphTargetCount;
                mesh.MorphTargetTexture = new MorphTargetTexture(_gl, positions.Count, morphTargetCount, morphAttributes);

                List<IReadOnlyList<Vector3>> morphPositions = new();
                List<IReadOnlyList<Vector3>> morphNormals = new();
                List<IReadOnlyList<Vector4>> morphTangents = new();
                List<IReadOnlyList<Vector2>> morphTexCoords0 = new();
                List<IReadOnlyList<Vector2>> morphTexCoords1 = new();
                List<IReadOnlyList<Vector4>> morphColors0 = new();

                for (int t = 0; t < morphTargetCount; t++) {
                    IReadOnlyDictionary<string, Accessor> targetAccessors = primitive.GetMorphTargetAccessors(t);
                    morphPositions.Add(targetAccessors.TryGetValue("POSITION", out Accessor morphPosAcc) ? morphPosAcc.AsVector3Array() : null);
                    morphNormals.Add(targetAccessors.TryGetValue("NORMAL", out Accessor morphNrmAcc) ? morphNrmAcc.AsVector3Array() : null);
                    morphTangents.Add(targetAccessors.TryGetValue("TANGENT", out Accessor morphTanAcc) ? morphTanAcc.AsVector4Array() : null);
                    morphTexCoords0.Add(targetAccessors.TryGetValue("TEXCOORD_0", out Accessor morphUv0Acc) ? morphUv0Acc.AsVector2Array() : null);
                    morphTexCoords1.Add(targetAccessors.TryGetValue("TEXCOORD_1", out Accessor morphUv1Acc) ? morphUv1Acc.AsVector2Array() : null);
                    morphColors0.Add(targetAccessors.TryGetValue("COLOR_0", out Accessor morphColAcc) ? morphColAcc.AsColorArray() : null);
                }

                mesh.MorphTargetTexture.UploadData(morphPositions.ToArray(), morphNormals.ToArray(), morphTangents.ToArray(),
                    morphTexCoords0.ToArray(), morphTexCoords1.ToArray(), morphColors0.ToArray());

                mesh.MorphWeights = new float[morphTargetCount];
                if (morphWeights != null) {
                    for (int i = 0; i < Math.Min(morphWeights.Count, morphTargetCount); i++) {
                        mesh.MorphWeights[i] = morphWeights[i];
                    }
                }
            }

            mesh.HasUV1 = uv1 != null;
            mesh.HasColor0 = colors != null;
            GltfGeometryProcessor.FillVertexBuffers(mesh, positions, normals, uv0, uv1, colors, tangents, joints, weights, generatedTangents);
            mesh.UseGeneratedTangents = tangents == null && generatedTangents != null;

            // Load material
            mesh.Material = new Material {
                SourceMaterialIndex = material?.LogicalIndex ?? -1
            };
            mesh.Material.LoadFromGltf(material, this);

            // Load variant materials
            if (variantMatMapping != null && variantMatMapping.Count > 0) {
                ModelRoot modelRoot = primitive.LogicalParent.LogicalParent;
                foreach (KeyValuePair<int, int> kvp in variantMatMapping) {
                    int variantIndex = kvp.Key;
                    int materialIndex = kvp.Value;
                    if (materialIndex >= 0 && materialIndex < modelRoot.LogicalMaterials.Count) {
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

        public void Update(float deltaTimeSeconds) {
            if (_activeAnimation != null && AnimationDurationSeconds > 0f) {
                _animationTimeSeconds += MathF.Max(0f, deltaTimeSeconds);
                _animationTimeSeconds %= AnimationDurationSeconds;
            }

            _pointerProcessor?.Update(_animationTimeSeconds);
            UpdateMeshInstanceTransforms();
        }

        void UpdateLightTransforms() {
            foreach (Light light in _lights) {
                if (light.SourceNode == null) continue;

                Matrix4x4 worldMatrix;
                if (_activeAnimation != null && _nodeWorldMatrixCache.TryGetValue(light.SourceNode, out Matrix4x4 cached)) {
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
                HashSet<Node> nodesToUpdate = new();
                foreach (MeshInstance instance in _meshInstances) {
                    nodesToUpdate.Add(instance.Node);
                    if (instance.HasSkinning && instance._joints != null) {
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

                foreach (Node node in nodesToUpdate) {
                    _nodeWorldMatrixCache[node] = node.GetWorldMatrix(_activeAnimation, _animationTimeSeconds);
                }
            }

            foreach (MeshInstance instance in _meshInstances) {
                if (_activeAnimation != null && _nodeWorldMatrixCache.TryGetValue(instance.Node, out Matrix4x4 cachedMatrix)) {
                    instance.WorldMatrix = cachedMatrix;
                }
                else {
                    instance.WorldMatrix = instance.Node.WorldMatrix;
                }

                instance.ApplyGizmoTransform();
                instance.IsNegativeScale = instance.WorldMatrix.GetDeterminant() < 0f;
                instance.UpdateSkinning(_activeAnimation, _animationTimeSeconds, _nodeWorldMatrixCache);

                // Update morph weights
                if (_activeAnimation != null && instance.Mesh.HasMorphTargets) {
                    float[] weights = instance.Mesh.MorphWeights;
                    if (weights != null) {
                        foreach (AnimationChannel channel in _activeAnimation.Channels) {
                            if (channel.TargetNode == instance.Node && channel.TargetNodePath == PropertyPath.weights) {
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

            UpdateLightTransforms();
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
