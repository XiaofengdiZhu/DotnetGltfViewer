using System;
using Silk.NET.OpenGLES;

namespace DotnetGltfRenderer {
    public class VertexArrayObject<TVertexType, TIndexType> : IDisposable where TVertexType : unmanaged where TIndexType : unmanaged {
        readonly uint _handle;

        public VertexArrayObject() => _handle = GlContext.GL.GenVertexArray();

        public VertexArrayObject(BufferObject<TVertexType> vbo, BufferObject<TIndexType> ebo) : this() {
            Bind();
            vbo.Bind();
            ebo.Bind();
        }

        public unsafe void VertexAttributePointer(uint index, int count, VertexAttribPointerType type, uint vertexSize, int offSet) {
            GlContext.GL.VertexAttribPointer(
                index,
                count,
                type,
                false,
                vertexSize * (uint)sizeof(TVertexType),
                (void*)(offSet * sizeof(TVertexType))
            );
            GlContext.GL.EnableVertexAttribArray(index);
        }

        /// <summary>
        /// 设置实例化顶点属性指针（用于 GPU 实例化）
        /// </summary>
        /// <param name="index">属性索引</param>
        /// <param name="count">分量数量</param>
        /// <param name="type">数据类型</param>
        /// <param name="stride">步长（字节）</param>
        /// <param name="offSet">偏移量（字节）</param>
        /// <param name="divisor">实例除数（1 表示每个实例读取一次）</param>
        public unsafe void VertexAttributePointerInstanced(uint index,
            int count,
            VertexAttribPointerType type,
            int stride,
            int offSet,
            uint divisor = 1) {
            GlContext.GL.VertexAttribPointer(index, count, type, false, (uint)stride, (void*)offSet);
            GlContext.GL.EnableVertexAttribArray(index);
            GlContext.GL.VertexAttribDivisor(index, divisor);
        }

        /// <summary>
        /// 设置 mat4 实例矩阵属性（占用 4 个连续属性位置）
        /// </summary>
        /// <param name="baseIndex">基础属性索引（会使用 baseIndex, baseIndex+1, baseIndex+2, baseIndex+3）</param>
        /// <param name="stride">步长（字节），通常是 64（16 * 4 bytes）</param>
        public unsafe void SetInstancedMatrixAttribute(uint baseIndex, int stride = 64) {
            // mat4 需要 4 个 vec4 属性
            for (int i = 0; i < 4; i++) {
                uint location = baseIndex + (uint)i;
                int offset = i * 16; // 每个 vec4 偏移 16 字节
                GlContext.GL.VertexAttribPointer(location, 4, VertexAttribPointerType.Float, false, (uint)stride, (void*)offset);
                GlContext.GL.EnableVertexAttribArray(location);
                GlContext.GL.VertexAttribDivisor(location, 1); // 每个实例读取一次
            }
        }

        /// <summary>
        /// 禁用实例化属性（清理状态）
        /// </summary>
        public void DisableInstancedMatrixAttribute(uint baseIndex) {
            for (int i = 0; i < 4; i++) {
                uint location = baseIndex + (uint)i;
                GlContext.GL.VertexAttribDivisor(location, 0);
                GlContext.GL.DisableVertexAttribArray(location);
            }
        }

        public void Bind() {
            GlContext.GL.BindVertexArray(_handle);
        }

        public void Dispose() {
            GlContext.GL.DeleteVertexArray(_handle);
        }
    }
}