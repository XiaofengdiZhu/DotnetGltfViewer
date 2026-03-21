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
    public class ShaderCache : IDisposable {
        readonly Dictionary<string, string> _sources = new();
        readonly Dictionary<int, uint> _shaderCache = new();
        readonly Dictionary<string, Shader> _programCache = new();
        readonly GL _gl;

        public ShaderCache(GL gl, string shadersDirectory) {
            _gl = gl;
            LoadShaderSources(shadersDirectory);
            ResolveIncludes();
        }

        /// <summary>
        /// 加载着色器源文件
        /// </summary>
        void LoadShaderSources(string directory) {
            if (!Directory.Exists(directory)) {
                throw new DirectoryNotFoundException($"Shader directory not found: {directory}");
            }

            // 递归加载所有 .vert, .frag, .glsl 文件
            LoadFromDirectory(directory);
        }

        void LoadFromDirectory(string dir) {
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
        void ResolveIncludes() {
            bool changed = true;
            while (changed) {
                changed = false;
                foreach (string key in _sources.Keys) {
                    string src = _sources[key];
                    foreach ((string includeName, string includeSource) in _sources) {
                        string pattern = $"#include <{includeName}>";
                        if (src.Contains(pattern)) {
                            // 替换第一次出现的 include
                            src = src.Replace(pattern, includeSource);

                            // 删除后续重复的 include（防止重复包含）
                            while (src.Contains(pattern)) {
                                src = src.Replace(pattern, "");
                            }
                            changed = true;
                        }
                    }
                    if (src != _sources[key]) {
                        _sources[key] = src;
                    }
                }
            }
        }

        /// <summary>
        /// 选择或编译着色器变体（参考官方 selectShader）
        /// </summary>
        /// <param name="shaderName">着色器文件名（如 "pbr.frag", "primitive.vert"）</param>
        /// <param name="defines">预处理器定义列表</param>
        /// <returns>着色器 hash</returns>
        public int SelectShader(string shaderName, List<string> defines) {
            if (!_sources.TryGetValue(shaderName, out string src)) {
                throw new FileNotFoundException($"Shader source not found: {shaderName}");
            }
            bool isVert = shaderName.EndsWith(".vert");
            int hash = ComputeHash(shaderName);

            // 构建 defines 代码
            StringBuilder definesBuilder = new();
            foreach (string define in defines) {
                hash ^= ComputeHash(define);
                definesBuilder.AppendLine($"#define {define}");
            }

            // 检查缓存
            if (_shaderCache.TryGetValue(hash, out uint cachedShader)) {
                return hash;
            }

            // 编译新着色器变体
            // 如果着色器已有 #version，将 defines 插入到 #version 之后
            // 如果没有 #version，添加默认的 #version 300 es
            string fullSource;
            string definesStr = definesBuilder.ToString();
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
        /// 获取或链接着色器程序
        /// </summary>
        public Shader GetShaderProgram(int vertexShaderHash, int fragmentShaderHash) {
            string cacheKey = $"{vertexShaderHash},{fragmentShaderHash}";
            if (_programCache.TryGetValue(cacheKey, out Shader program)) {
                return program;
            }
            if (!_shaderCache.TryGetValue(vertexShaderHash, out uint vertShader)) {
                throw new InvalidOperationException($"Vertex shader not found: {vertexShaderHash}");
            }
            if (!_shaderCache.TryGetValue(fragmentShaderHash, out uint fragShader)) {
                throw new InvalidOperationException($"Fragment shader not found: {fragmentShaderHash}");
            }

            // 链接程序
            uint programHandle = _gl.CreateProgram();
            _gl.AttachShader(programHandle, vertShader);
            _gl.AttachShader(programHandle, fragShader);

            // Bind vertex attribute locations (must be done BEFORE linking)
            // Layout matches Mesh.cs: 0=position, 1=normal, 2=texcoord_0, 3=tangent,
            //                         4=color, 5=joints, 6=weights, 7=texcoord_1
            //                         8-11=instance_matrix (mat4, 4 columns)
            _gl.BindAttribLocation(programHandle, 0, "a_position");
            _gl.BindAttribLocation(programHandle, 1, "a_normal");
            _gl.BindAttribLocation(programHandle, 2, "a_texcoord_0");
            _gl.BindAttribLocation(programHandle, 3, "a_tangent");
            _gl.BindAttribLocation(programHandle, 4, "a_color_0");
            _gl.BindAttribLocation(programHandle, 5, "a_joints_0");
            _gl.BindAttribLocation(programHandle, 6, "a_weights_0");
            _gl.BindAttribLocation(programHandle, 7, "a_texcoord_1");

            // Instance matrix attributes (mat4 requires 4 vec4 slots)
            // a_instance_model_matrix is declared as "in mat4" in primitive.vert
            // For mat4, we only bind the base name, OpenGL automatically uses consecutive locations
            _gl.BindAttribLocation(programHandle, 8, "a_instance_model_matrix");
            _gl.LinkProgram(programHandle);
            _gl.GetProgram(programHandle, GLEnum.LinkStatus, out int status);
            if (status == 0) {
                string log = _gl.GetProgramInfoLog(programHandle);
                throw new Exception($"Program link failed: {log}");
            }

            // 创建 Shader 对象（注意：这里简化处理，实际可能需要调整 Shader 类以支持外部程序句柄）
            program = new Shader(_gl, programHandle, vertShader, fragShader);
            _programCache[cacheKey] = program;
            return program;
        }

        uint CompileShader(ShaderType type, string source, string shaderName) {
            uint shader = _gl.CreateShader(type);
            _gl.ShaderSource(shader, source);
            _gl.CompileShader(shader);
            _gl.GetShader(shader, GLEnum.CompileStatus, out int status);
            if (status == 0) {
                string log = _gl.GetShaderInfoLog(shader);
                _gl.DeleteShader(shader);
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
        public string GetSource(string shaderName) => _sources.TryGetValue(shaderName, out string src) ? src : null;

        public void Dispose() {
            // 删除着色器
            foreach (uint shader in _shaderCache.Values) {
                _gl.DeleteShader(shader);
            }
            _shaderCache.Clear();

            // 删除程序
            foreach (Shader program in _programCache.Values) {
                program.Dispose();
            }
            _programCache.Clear();
        }
    }
}