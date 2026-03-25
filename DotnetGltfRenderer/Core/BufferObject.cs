using System;
using Silk.NET.OpenGLES;

namespace DotnetGltfRenderer {
    public class BufferObject<TDataType> : IDisposable where TDataType : unmanaged {
        readonly uint _handle;
        readonly BufferTargetARB _bufferType;

        public unsafe BufferObject(ReadOnlySpan<TDataType> data, BufferTargetARB bufferType) {
            _bufferType = bufferType;
            _handle = GlContext.GL.GenBuffer();
            Bind();
            fixed (void* d = data) {
                GlContext.GL.BufferData(bufferType, (nuint)(data.Length * sizeof(TDataType)), d, BufferUsageARB.StaticDraw);
            }
        }

        public void Bind() {
            GlContext.GL.BindBuffer(_bufferType, _handle);
        }

        public void Dispose() {
            GlContext.GL.DeleteBuffer(_handle);
        }
    }
}