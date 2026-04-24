using System;
using System.Collections.Generic;
using System.Numerics;
using Silk.NET.OpenGLES;

namespace DotnetGltfRenderer {
    public class DynamicInstancingBatch : IDisposable {
        readonly Mesh _mesh;
        readonly List<MeshInstance> _instances = new();
        readonly float[] _matrixData;
        uint _instanceBufferHandle;
        bool _vboDirty = true;
        bool _hasNegativeScale;
        bool _negativeScaleDirty = true;
        bool _disposed;

        const int MaxInstancesPerBatch = 1024;

        public Mesh Mesh => _mesh;
        public int InstanceCount => _instances.Count;
        public bool IsEmpty => _instances.Count == 0;
        public Material Material => _instances.Count > 0 ? _instances[0].CurrentMaterial : null;

        public DynamicInstancingBatch(Mesh mesh) {
            _mesh = mesh ?? throw new ArgumentNullException(nameof(mesh));
            _matrixData = new float[MaxInstancesPerBatch * 16];
        }

        public bool CanAddInstance => _instances.Count < MaxInstancesPerBatch;

        public void AddInstance(MeshInstance instance) {
            if (instance.Mesh != _mesh) {
                throw new ArgumentException("Instance mesh must match batch mesh");
            }
            if (_instances.Count >= MaxInstancesPerBatch) {
                throw new InvalidOperationException("Batch is full");
            }
            _instances.Add(instance);
            _vboDirty = true;
            _negativeScaleDirty = true;
        }

        public void Clear() {
            _instances.Clear();
            _vboDirty = true;
            _negativeScaleDirty = true;
        }

        static void CopyMatrixToArray(Matrix4x4 matrix, float[] data, int offset) {
            if (offset < 0 || offset + 15 >= data.Length) {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }
            data[offset + 0] = matrix.M11;
            data[offset + 1] = matrix.M12;
            data[offset + 2] = matrix.M13;
            data[offset + 3] = matrix.M14;
            data[offset + 4] = matrix.M21;
            data[offset + 5] = matrix.M22;
            data[offset + 6] = matrix.M23;
            data[offset + 7] = matrix.M24;
            data[offset + 8] = matrix.M31;
            data[offset + 9] = matrix.M32;
            data[offset + 10] = matrix.M33;
            data[offset + 11] = matrix.M34;
            data[offset + 12] = matrix.M41;
            data[offset + 13] = matrix.M42;
            data[offset + 14] = matrix.M43;
            data[offset + 15] = matrix.M44;
        }

        public void UpdateMatrices() {
            int count = _instances.Count;
            if (count == 0) {
                return;
            }
            if (count > MaxInstancesPerBatch) {
                throw new InvalidOperationException($"Instance count {count} exceeds max {MaxInstancesPerBatch}");
            }
            for (int i = 0; i < count; i++) {
                CopyMatrixToArray(_instances[i].WorldMatrix, _matrixData, i * 16);
            }
            _vboDirty = true;
        }

        void UpdateNegativeScaleFlag() {
            if (!_negativeScaleDirty) {
                return;
            }
            _hasNegativeScale = false;
            foreach (MeshInstance instance in _instances) {
                if (instance.IsNegativeScale) {
                    _hasNegativeScale = true;
                    break;
                }
            }
            _negativeScaleDirty = false;
        }

        public void EnsureVBO() {
            int count = _instances.Count;
            if (count == 0) {
                return;
            }

            if (_instanceBufferHandle == 0) {
                CreateVBO();
                return;
            }

            if (_vboDirty) {
                UpdateVBO(count);
            }
        }

        unsafe void CreateVBO() {
            _instanceBufferHandle = GlContext.GL.GenBuffer();
            GlContext.GL.BindBuffer(BufferTargetARB.ArrayBuffer, _instanceBufferHandle);
            fixed (float* ptr = _matrixData) {
                GlContext.GL.BufferData(
                    BufferTargetARB.ArrayBuffer,
                    MaxInstancesPerBatch * 16 * sizeof(float),
                    ptr,
                    BufferUsageARB.DynamicDraw
                );
            }
            _mesh.VAO.Bind();
            GlContext.GL.BindBuffer(BufferTargetARB.ArrayBuffer, _instanceBufferHandle);
            _mesh.VAO.SetInstancedMatrixAttribute(8);
            _vboDirty = false;
        }

        unsafe void UpdateVBO(int count) {
            GlContext.GL.BindBuffer(BufferTargetARB.ArrayBuffer, _instanceBufferHandle);
            fixed (float* ptr = _matrixData) {
                GlContext.GL.BufferSubData(
                    BufferTargetARB.ArrayBuffer,
                    0,
                    (nuint)(count * 16 * sizeof(float)),
                    ptr
                );
            }
            _vboDirty = false;
        }

        public unsafe void Draw() {
            int count = _instances.Count;
            if (count == 0) {
                return;
            }

            _mesh.VAO.Bind();
            EnsureVBO();

            // Multiple batches may share the same mesh VAO but have different instance VBOs.
            // Re-bind this batch's VBO and reconfigure attribute pointers before drawing.
            GlContext.GL.BindBuffer(BufferTargetARB.ArrayBuffer, _instanceBufferHandle);
            _mesh.VAO.SetInstancedMatrixAttribute(8);

            UpdateNegativeScaleFlag();

            GlContext.FrontFace(_hasNegativeScale ? FrontFaceDirection.CW : FrontFaceDirection.Ccw);

            GlContext.GL.DrawElementsInstanced(
                PrimitiveType.Triangles,
                (uint)_mesh.Indices.Length,
                DrawElementsType.UnsignedInt,
                null,
                (uint)count
            );

            GlContext.GL.BindVertexArray(0);
        }

        public void Dispose() {
            if (_disposed) {
                return;
            }
            if (_instanceBufferHandle != 0) {
                GlContext.GL.DeleteBuffer(_instanceBufferHandle);
                _instanceBufferHandle = 0;
            }
            _disposed = true;
        }
    }
}
