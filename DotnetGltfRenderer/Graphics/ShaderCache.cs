using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Silk.NET.OpenGLES;

namespace DotnetGltfRenderer {
    /// <summary>
    /// 着色器缓存系统，参考官方 shader_cache.js 实现
    /// 支持 #include &lt;filename.glsl&gt; 语法和着色器变体编译
    /// </summary>
    public static class ShaderCache {
        static Dictionary<string, string> _sources;
        static Dictionary<int, uint> _shaderCache;
        static Dictionary<string, Shader> _programCache;
        static string _cacheDirectory;

        /// <summary>
        /// 是否已初始化
        /// </summary>
        public static bool IsInitialized { get; private set; }

        /// <summary>
        /// 初始化着色器缓存（应用启动时调用一次）
        /// </summary>
        public static void Initialize(string shadersDirectory) {
            if (IsInitialized) {
                return;
            }
            _cacheDirectory = Path.Combine(shadersDirectory, "caches");
            _sources = new Dictionary<string, string>();
            _shaderCache = new Dictionary<int, uint>();
            _programCache = new Dictionary<string, Shader>();

            // 确保缓存目录存在
            Directory.CreateDirectory(_cacheDirectory);
            LoadShaderSources(shadersDirectory);
            ResolveIncludes();
            IsInitialized = true;
        }

        /// <summary>
        /// 加载着色器源文件
        /// </summary>
        static void LoadShaderSources(string directory) {
            if (!Directory.Exists(directory)) {
                throw new DirectoryNotFoundException($"Shader directory not found: {directory}");
            }

            // 递归加载所有 .vert, .frag, .glsl 文件
            LoadFromDirectory(directory);
        }

        static void LoadFromDirectory(string dir) {
            foreach (string file in Directory.GetFiles(dir, "*.vert")) {
                string name = Path.GetFileName(file);
                _sources[name] = File.ReadAllText(file);
            }
            foreach (string file in Directory.GetFiles(dir, "*.frag")) {
                string name = Path.GetFileName(file);
                _sources[name] = File.ReadAllText(file);
            }
            foreach (string file in Directory.GetFiles(dir, "*.glsl")) {
                string name = Path.GetFileName(file);
                _sources[name] = File.ReadAllText(file);
            }

            // 递归处理子目录
            foreach (string subDir in Directory.GetDirectories(dir)) {
                LoadFromDirectory(subDir);
            }
        }

        /// <summary>
        /// 解析 #include 指令（官方 shader_cache.js 第 12-35 行）
        /// </summary>
        static void ResolveIncludes() {
            bool changed = true;
            while (changed) {
                changed = false;
                foreach (string key in _sources.Keys) {
                    string src = _sources[key];
                    foreach ((string includeName, string includeSource) in _sources) {
                        string pattern = $"#include <{includeName}>";
                        if (src.Contains(pattern)) {
                            StringBuilder sb = new(src.Length + includeSource.Length);
                            int index = 0;
                            int patternLength = pattern.Length;

                            // 替换所有出现的 include
                            while (true) {
                                int foundIndex = src.IndexOf(pattern, index);
                                if (foundIndex < 0) {
                                    sb.Append(src, index, src.Length - index);
                                    break;
                                }
                                // 只替换第一次出现，后续的删除
                                if (sb.Length == 0
                                    || foundIndex > index) {
                                    sb.Append(src, index, foundIndex - index);
                                    sb.Append(includeSource);
                                }
                                index = foundIndex + patternLength;
                            }
                            src = sb.ToString();
                            changed = true;
                        }
                    }
                    _sources[key] = src;
                }
            }
        }

        /// <summary>
        /// 选择或编译着色器变体（参考官方 selectShader）
        /// </summary>
        /// <param name="shaderName">着色器文件名（如 "pbr.frag", "primitive.vert"）</param>
        /// <param name="defines">预处理器定义列表</param>
        /// <returns>着色器 hash</returns>
        public static int SelectShader(string shaderName, List<string> defines) {
            if (!IsInitialized) {
                throw new InvalidOperationException("ShaderCache not initialized. Call Initialize() first.");
            }
            if (!_sources.TryGetValue(shaderName, out string src)) {
                throw new FileNotFoundException($"Shader source not found: {shaderName}");
            }
            bool isVert = shaderName.EndsWith(".vert");
            int hash = ComputeHash(shaderName);

            // 构建 defines 代码
            StringBuilder definesBuilder = new(defines.Count * 32);
            foreach (string define in defines) {
                hash ^= ComputeHash(define);
                definesBuilder.AppendLine($"#define {define}");
            }

            // 检查缓存
            if (_shaderCache.ContainsKey(hash)) {
                return hash;
            }

            // 编译新着色器变体
            return CompileAndCacheShader(shaderName, src, definesBuilder.ToString(), hash, isVert);
        }

        /// <summary>
        /// 选择或编译着色器变体（使用预计算的 hash，避免遍历）
        /// </summary>
        /// <param name="shaderName">着色器文件名</param>
        /// <param name="defines">ShaderDefines 对象（使用其缓存的 hash）</param>
        /// <returns>着色器 hash</returns>
        public static int SelectShader(string shaderName, ShaderDefines defines) {
            if (!IsInitialized) {
                throw new InvalidOperationException("ShaderCache not initialized. Call Initialize() first.");
            }
            if (!_sources.TryGetValue(shaderName, out string src)) {
                throw new FileNotFoundException($"Shader source not found: {shaderName}");
            }
            bool isVert = shaderName.EndsWith(".vert");

            // 使用 ShaderDefines 的预计算 hash，结合 shaderName 的 hash
            int hash = ComputeHash(shaderName) ^ defines.ComputeHash();

            // 检查缓存
            if (_shaderCache.ContainsKey(hash)) {
                return hash;
            }

            // 构建 defines 代码（仅在需要编译时）
            string definesStr = defines.GetDefinesCodeWithoutVersion();
            return CompileAndCacheShader(shaderName, src, definesStr, hash, isVert);
        }

        /// <summary>
        /// 编译并缓存着色器
        /// </summary>
        static int CompileAndCacheShader(string shaderName, string src, string definesStr, int hash, bool isVert) {
            // 如果着色器已有 #version，将 defines 插入到 #version 之后
            // 如果没有 #version，添加默认的 #version 300 es
            string fullSource;
            int versionIndex = src.IndexOf("#version");
            if (versionIndex >= 0) {
                // 找到 #version 行的结束位置
                int lineEnd = src.IndexOf('\n', versionIndex);
                if (lineEnd >= 0) {
                    // 将 defines 插入到 #version 行之后
                    fullSource = src.Substring(0, lineEnd + 1) + definesStr + src.Substring(lineEnd + 1);
                }
                else {
                    // #version 是最后一行（不太可能）
                    fullSource = src + "\n" + definesStr;
                }
            }
            else {
                // 没有 #version，添加默认的
                fullSource = "#version 300 es\n" + definesStr + src;
            }
            uint shader = CompileShader(isVert ? ShaderType.VertexShader : ShaderType.FragmentShader, fullSource, shaderName);
            _shaderCache[hash] = shader;
            return hash;
        }

        /// <summary>
        /// 尝试用预计算的 hash 获取着色器程序
        /// 如果 hash 不在缓存中，返回 null
        /// </summary>
        public static Shader TryGetShaderProgram(int vertexShaderHash, int fragmentShaderHash) {
            if (!IsInitialized) {
                return null;
            }
            string cacheKey = $"{vertexShaderHash},{fragmentShaderHash}";
            if (_programCache.TryGetValue(cacheKey, out Shader program)) {
                return program;
            }

            // 检查着色器 hash 是否存在
            if (!_shaderCache.ContainsKey(vertexShaderHash)
                || !_shaderCache.ContainsKey(fragmentShaderHash)) {
                return null;
            }

            // 尝试从二进制缓存加载
            uint programHandle = TryLoadProgramBinary(cacheKey);
            if (programHandle == 0) {
                return null;
            }

            // 从二进制缓存加载成功，绑定 UBO
            BindUniformBlockBindings(programHandle);
            program = new Shader(programHandle, 0, 0);
            _programCache[cacheKey] = program;
            return program;
        }

        /// <summary>
        /// 获取或链接着色器程序
        /// </summary>
        public static Shader GetShaderProgram(int vertexShaderHash, int fragmentShaderHash) {
            if (!IsInitialized) {
                throw new InvalidOperationException("ShaderCache not initialized. Call Initialize() first.");
            }
            string cacheKey = $"{vertexShaderHash},{fragmentShaderHash}";
            if (_programCache.TryGetValue(cacheKey, out Shader program)) {
                return program;
            }

            // 尝试从二进制缓存加载
            uint programHandle = TryLoadProgramBinary(cacheKey);
            bool fromCache = programHandle != 0;
            if (!fromCache) {
                // 缓存加载失败，走原有编译链接流程
                if (!_shaderCache.TryGetValue(vertexShaderHash, out uint vertShader)) {
                    throw new InvalidOperationException($"Vertex shader not found: {vertexShaderHash}");
                }
                if (!_shaderCache.TryGetValue(fragmentShaderHash, out uint fragShader)) {
                    throw new InvalidOperationException($"Fragment shader not found: {fragmentShaderHash}");
                }
                programHandle = GlContext.GL.CreateProgram();
                GlContext.GL.AttachShader(programHandle, vertShader);
                GlContext.GL.AttachShader(programHandle, fragShader);

                // Bind vertex attribute locations (must be done BEFORE linking)
                BindAttributeLocations(programHandle);
                GlContext.GL.LinkProgram(programHandle);
                GlContext.GL.GetProgram(programHandle, ProgramPropertyARB.LinkStatus, out int status);
                if (status == 0) {
                    string log = GlContext.GL.GetProgramInfoLog(programHandle);
                    throw new Exception($"Program link failed: {log}");
                }

                // 链接后立即绑定所有 UBO binding points（只需一次）
                BindUniformBlockBindings(programHandle);

                // 保存二进制缓存
                SaveProgramBinary(programHandle, cacheKey);

                // 创建 Shader 对象（需要着色器句柄用于清理）
                program = new Shader(programHandle, vertShader, fragShader);
            }
            else {
                // 从缓存加载成功，仍需绑定 UBO（ProgramBinary 不保存 binding points）
                BindUniformBlockBindings(programHandle);
                // 创建 Shader 对象（着色器句柄设为 0，因为不再需要）
                program = new Shader(programHandle, 0, 0);
            }
            _programCache[cacheKey] = program;
            return program;
        }

        static void BindAttributeLocations(uint programHandle) {
            // Layout matches Mesh.cs: 0=position, 1=normal, 2=texcoord_0, 3=tangent,
            //                         4=color, 5=joints, 6=weights, 7=texcoord_1
            //                         8-11=instance_matrix (mat4, 4 columns)
            GlContext.GL.BindAttribLocation(programHandle, 0, "a_position");
            GlContext.GL.BindAttribLocation(programHandle, 1, "a_normal");
            GlContext.GL.BindAttribLocation(programHandle, 2, "a_texcoord_0");
            GlContext.GL.BindAttribLocation(programHandle, 3, "a_tangent");
            GlContext.GL.BindAttribLocation(programHandle, 4, "a_color_0");
            GlContext.GL.BindAttribLocation(programHandle, 5, "a_joints_0");
            GlContext.GL.BindAttribLocation(programHandle, 6, "a_weights_0");
            GlContext.GL.BindAttribLocation(programHandle, 7, "a_texcoord_1");

            // Instance matrix attributes (mat4 requires 4 vec4 slots)
            GlContext.GL.BindAttribLocation(programHandle, 8, "a_instance_model_matrix");
        }

        /// <summary>
        /// 绑定所有 UBO binding points（链接着色器后调用一次）
        /// 避免每帧重复调用 glGetUniformBlockIndex + glUniformBlockBinding
        /// </summary>
        static void BindUniformBlockBindings(uint programHandle) {
            // Binding points 与 UniformBuffer.cs 中的定义一致
            // 0: SceneData, 1: MaterialCoreData, 2: LightsData, 3: RenderStateData
            // 4: UVTransformData, 5: VolumeScatterData, 6: MaterialExtensionData
            BindUniformBlock(programHandle, "SceneData", 0);
            BindUniformBlock(programHandle, "MaterialCoreData", 1);
            BindUniformBlock(programHandle, "LightsData", 2);
            BindUniformBlock(programHandle, "RenderStateData", 3);
            BindUniformBlock(programHandle, "UVTransformData", 4);
            BindUniformBlock(programHandle, "VolumeScatterData", 5);
            BindUniformBlock(programHandle, "MaterialExtensionData", 6);
        }

        static void BindUniformBlock(uint programHandle, string blockName, uint bindingPoint) {
            uint blockIndex = GlContext.GL.GetUniformBlockIndex(programHandle, blockName);
            if (blockIndex != uint.MaxValue) {
                GlContext.GL.UniformBlockBinding(programHandle, blockIndex, bindingPoint);
            }
        }

        static uint TryLoadProgramBinary(string cacheKey) {
            string cacheFile = Path.Combine(_cacheDirectory, $"{cacheKey}.bin");
            if (!File.Exists(cacheFile)) {
                return 0;
            }
            try {
                byte[] fileData = File.ReadAllBytes(cacheFile);
                if (fileData.Length < 8) {
                    return 0;
                }

                // 前 4 字节是格式
                uint formatValue = BitConverter.ToUInt32(fileData, 0);
                // 剩余是二进制数据
                int binaryLength = fileData.Length - 4;
                uint programHandle = GlContext.GL.CreateProgram();
                GlContext.GL.ProgramBinary(programHandle, (GLEnum)formatValue, fileData.AsSpan(4, binaryLength), (uint)binaryLength);
                GlContext.GL.GetProgram(programHandle, ProgramPropertyARB.LinkStatus, out int status);
                if (status == 0) {
                    GlContext.GL.DeleteProgram(programHandle);
                    return 0;
                }
                return programHandle;
            }
            catch {
                try {
                    if (File.Exists(cacheFile)) {
                        File.Delete(cacheFile);
                    }
                }
                catch {
                    // ignored
                }
                return 0;
            }
        }

        static unsafe void SaveProgramBinary(uint programHandle, string cacheKey) {
            try {
                GlContext.GL.GetProgram(programHandle, ProgramPropertyARB.ProgramBinaryLength, out int binaryLength);
                if (binaryLength <= 0) {
                    return;
                }
                byte[] binary = new byte[binaryLength + 4];
                uint formatValue;
                fixed (byte* ptr = &binary[4]) {
                    GlContext.GL.GetProgramBinary(programHandle, (uint)binaryLength, out _, out GLEnum format, ptr);
                    formatValue = (uint)format;
                }

                // 前 4 字节存储格式
                BitConverter.TryWriteBytes(binary, formatValue);
                string cacheFile = Path.Combine(_cacheDirectory, $"{cacheKey}.bin");
                File.WriteAllBytes(cacheFile, binary);
            }
            catch {
                // 保存失败不影响正常流程
            }
        }

        static uint CompileShader(ShaderType type, string source, string shaderName) {
            uint shader = GlContext.GL.CreateShader(type);
            GlContext.GL.ShaderSource(shader, source);
            GlContext.GL.CompileShader(shader);
            GlContext.GL.GetShader(shader, ShaderParameterName.CompileStatus, out int status);
            if (status == 0) {
                string log = GlContext.GL.GetShaderInfoLog(shader);
                GlContext.GL.DeleteShader(shader);
                throw new Exception($"Shader compilation failed ({shaderName}): {log}");
            }
            return shader;
        }

        static int ComputeHash(string input) {
            // 简单的字符串 hash
            unchecked {
                int hash = 17;
                foreach (char c in input) {
                    hash = hash * 31 + c;
                }
                return hash;
            }
        }

        /// <summary>
        /// 获取已解析的着色器源代码
        /// </summary>
        public static string GetSource(string shaderName) => _sources.TryGetValue(shaderName, out string src) ? src : null;

        /// <summary>
        /// 释放资源
        /// </summary>
        public static void Dispose() {
            if (!IsInitialized) {
                return;
            }

            // 删除着色器
            foreach (uint shader in _shaderCache.Values) {
                GlContext.GL.DeleteShader(shader);
            }
            _shaderCache.Clear();

            // 删除程序
            foreach (Shader program in _programCache.Values) {
                program.Dispose();
            }
            _programCache.Clear();
            _sources.Clear();
            IsInitialized = false;
        }
    }
}