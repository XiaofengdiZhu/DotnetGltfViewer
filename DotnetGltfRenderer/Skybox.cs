using System;
using Silk.NET.OpenGLES;

namespace DotnetGltfRenderer {
    /// <summary>
    /// 环境背景立方体（与官方 environment_renderer.js 一致）
    /// 使用立方体采样 GGX 预过滤环境贴图
    /// </summary>
    public sealed class Skybox : IDisposable {
        readonly GL _gl;
        readonly uint _vertexBuffer;
        readonly uint _indexBuffer;
        readonly uint _vao;
        const int IndexCount = 36;

        public Skybox(GL gl) {
            _gl = gl;

            // 立方体顶点（与官方 environment_renderer.js 一致）
            float[] vertices = [
                -1,
                -1,
                -1, // 0
                1,
                -1,
                -1, // 1
                1,
                1,
                -1, // 2
                -1,
                1,
                -1, // 3
                -1,
                -1,
                1, // 4
                1,
                -1,
                1, // 5
                1,
                1,
                1, // 6
                -1,
                1,
                1 // 7
            ];

            // 立方体索引（与官方 environment_renderer.js 一致）
            ushort[] indices = [
                1,
                2,
                0,
                2,
                3,
                0,
                6,
                2,
                1,
                1,
                5,
                6,
                6,
                5,
                4,
                4,
                7,
                6,
                6,
                3,
                2,
                7,
                3,
                6,
                3,
                7,
                0,
                7,
                4,
                0,
                5,
                1,
                0,
                4,
                5,
                0
            ];

            // 创建 VAO
            _vao = _gl.GenVertexArray();
            _gl.BindVertexArray(_vao);

            // 创建顶点缓冲区
            _vertexBuffer = _gl.GenBuffer();
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vertexBuffer);
            unsafe {
                fixed (float* v = vertices) {
                    _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(vertices.Length * sizeof(float)), v, BufferUsageARB.StaticDraw);
                }
            }

            // 创建索引缓冲区
            _indexBuffer = _gl.GenBuffer();
            _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _indexBuffer);
            unsafe {
                fixed (ushort* i = indices) {
                    _gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(indices.Length * sizeof(ushort)), i, BufferUsageARB.StaticDraw);
                }
            }

            // 设置顶点属性 (location = 0, a_position)
            _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), 0);
            _gl.EnableVertexAttribArray(0);
            _gl.BindVertexArray(0);
        }

        public unsafe void Draw() {
            _gl.BindVertexArray(_vao);
            _gl.DrawElements(PrimitiveType.Triangles, IndexCount, DrawElementsType.UnsignedShort, null);
            _gl.BindVertexArray(0);
        }

        public void Dispose() {
            _gl.DeleteVertexArray(_vao);
            _gl.DeleteBuffer(_vertexBuffer);
            _gl.DeleteBuffer(_indexBuffer);
        }
    }
}