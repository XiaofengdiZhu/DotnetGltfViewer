using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using Silk.NET.OpenGLES;

namespace DotnetGltfRenderer {
    public class Shader : IDisposable {
        readonly bool _ownsHandle;
        readonly Dictionary<string, int> _uniformCache = new();

        /// <summary>
        /// 着色器程序句柄
        /// </summary>
        public uint ProgramHandle { get; }

        /// <summary>
        /// 从外部程序句柄创建 Shader（用于 ShaderCache）
        /// </summary>
        internal Shader(uint programHandle, uint vertexShader, uint fragmentShader) {
            ProgramHandle = programHandle;
            _ownsHandle = true;

            // 分离着色器（链接后不再需要）
            GlContext.GL.DetachShader(ProgramHandle, vertexShader);
            GlContext.GL.DetachShader(ProgramHandle, fragmentShader);

            // 缓存所有 active uniforms
            CacheActiveUniforms();
        }

        /// <summary>
        /// 从现有程序句柄创建 Shader（不拥有句柄）
        /// </summary>
        public Shader(uint programHandle) {
            ProgramHandle = programHandle;
            _ownsHandle = false;

            // 缓存所有 active uniforms
            CacheActiveUniforms();
        }

        /// <summary>
        /// 缓存所有 active uniform locations
        /// </summary>
        unsafe void CacheActiveUniforms() {
            GlContext.GL.GetProgram(ProgramHandle, ProgramPropertyARB.ActiveUniforms, out int uniformCount);

            // 在循环外分配缓冲区，避免堆栈溢出
            const int bufferSize = 256;
            byte* nameBuffer = stackalloc byte[bufferSize];
            for (uint i = 0; i < uniformCount; i++) {
                // 获取 uniform 信息
                uint length;
                int size;
                UniformType type;
                GlContext.GL.GetActiveUniform(
                    ProgramHandle,
                    i,
                    bufferSize,
                    &length,
                    &size,
                    &type,
                    nameBuffer
                );
                if (length <= 0u) {
                    continue;
                }

                // 转换名称
                string name = Encoding.UTF8.GetString(nameBuffer, (int)length);

                // 处理数组 uniform（如 u_Lights[0]）
                // 只缓存基础名称，数组成员会自动映射
                int bracketIndex = name.IndexOf('[');
                if (bracketIndex > 0) {
                    string baseName = name.Substring(0, bracketIndex);
                    if (!_uniformCache.ContainsKey(baseName)) {
                        int location = GlContext.GL.GetUniformLocation(ProgramHandle, baseName);
                        if (location >= 0) {
                            _uniformCache[baseName] = location;
                        }
                    }
                }

                // 缓存完整名称
                int loc = GlContext.GL.GetUniformLocation(ProgramHandle, name);
                if (loc >= 0) {
                    _uniformCache[name] = loc;
                }
            }
        }

        /// <summary>
        /// 从缓存获取 uniform location，未找到则直接查询 OpenGL
        /// </summary>
        int GetCachedLocation(string name) {
            if (_uniformCache.TryGetValue(name, out int location)) {
                return location;
            }
            // 如果缓存中没有，直接查询 OpenGL
            location = GlContext.GL.GetUniformLocation(ProgramHandle, name);
            if (location >= 0) {
                _uniformCache[name] = location;
            }
            return location;
        }

        public void Use() {
            GlContext.GL.UseProgram(ProgramHandle);
        }

        public void SetUniform(string name, int value) {
            int location = GetCachedLocation(name);
            if (location == -1) {
                return;
            }
            GlContext.GL.Uniform1(location, value);
        }

        public void SetUniformOptional(string name, int value) {
            int location = GetCachedLocation(name);
            if (location != -1) {
                GlContext.GL.Uniform1(location, value);
            }
        }

        public unsafe void SetUniform(string name, Matrix4x4 value) {
            int location = GetCachedLocation(name);
            if (location == -1) {
                return;
            }
            GlContext.GL.UniformMatrix4(location, 1, false, (float*)&value);
        }

        public unsafe void SetUniformMatrix4Array(string name, ReadOnlySpan<Matrix4x4> values) {
            if (values.Length == 0) {
                return;
            }
            int location = GetCachedLocation(name);
            if (location == -1) {
                return;
            }
            fixed (Matrix4x4* valuePtr = values) {
                GlContext.GL.UniformMatrix4(location, (uint)values.Length, false, (float*)valuePtr);
            }
        }

        public void SetUniform(string name, float value) {
            int location = GetCachedLocation(name);
            if (location == -1) {
                return;
            }
            GlContext.GL.Uniform1(location, value);
        }

        public void SetUniformOptional(string name, float value) {
            int location = GetCachedLocation(name);
            if (location != -1) {
                GlContext.GL.Uniform1(location, value);
            }
        }

        public void SetUniform(string name, Vector4 value) {
            int location = GetCachedLocation(name);
            if (location == -1) {
                return;
            }
            GlContext.GL.Uniform4(location, value.X, value.Y, value.Z, value.W);
        }

        public void SetUniform(string name, Vector3 value) {
            int location = GetCachedLocation(name);
            if (location == -1) {
                return;
            }
            GlContext.GL.Uniform3(location, value.X, value.Y, value.Z);
        }

        public void SetUniform(string name, Vector2 value) {
            int location = GetCachedLocation(name);
            if (location == -1) {
                return;
            }
            GlContext.GL.Uniform2(location, value.X, value.Y);
        }

        /// <summary>
        /// 设置 ivec2 uniform（两个整数）
        /// </summary>
        public void SetUniformInt2(string name, int x, int y) {
            int location = GetCachedLocation(name);
            if (location == -1) {
                return;
            }
            GlContext.GL.Uniform2(location, x, y);
        }

        /// <summary>
        /// 设置 3x3 矩阵 uniform（列主序）
        /// </summary>
        public unsafe void SetUniformMatrix3(string name, float* value) {
            int location = GetCachedLocation(name);
            if (location == -1) {
                return;
            }
            GlContext.GL.UniformMatrix3(location, 1, false, value);
        }

        /// <summary>
        /// 设置 3x3 矩阵 uniform（从 Vector3 列向量）
        /// </summary>
        public unsafe void SetUniformMatrix3(string name, Vector3 col0, Vector3 col1, Vector3 col2) {
            int location = GetCachedLocation(name);
            if (location == -1) {
                return;
            }
            float* ptr = stackalloc float[9];
            ptr[0] = col0.X;
            ptr[1] = col0.Y;
            ptr[2] = col0.Z;
            ptr[3] = col1.X;
            ptr[4] = col1.Y;
            ptr[5] = col1.Z;
            ptr[6] = col2.X;
            ptr[7] = col2.Y;
            ptr[8] = col2.Z;
            GlContext.GL.UniformMatrix3(location, 1, false, ptr);
        }

        /// <summary>
        /// 设置 3x3 矩阵 uniform（从 9 个 float 元素，列主序）
        /// </summary>
        public unsafe void SetUniformMatrix3(string name,
            float m00,
            float m01,
            float m02,
            float m10,
            float m11,
            float m12,
            float m20,
            float m21,
            float m22) {
            int location = GetCachedLocation(name);
            if (location == -1) {
                return;
            }
            float* ptr = stackalloc float[9];
            // OpenGL 列主序：ptr[0]=col0.x, ptr[1]=col0.y, ptr[2]=col0.z, ptr[3]=col1.x, ...
            ptr[0] = m00;
            ptr[1] = m10;
            ptr[2] = m20;
            ptr[3] = m01;
            ptr[4] = m11;
            ptr[5] = m21;
            ptr[6] = m02;
            ptr[7] = m12;
            ptr[8] = m22;
            GlContext.GL.UniformMatrix3(location, 1, false, ptr);
        }

        public void Dispose() {
            if (_ownsHandle) {
                GlContext.GL.DeleteProgram(ProgramHandle);
            }
        }
    }
}