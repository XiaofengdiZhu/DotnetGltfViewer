using System;
using System.Collections.Generic;
using System.Numerics;
using SharpGLTF.Animations;
using SharpGLTF.Schema2;
using Silk.NET.OpenGLES;

namespace DotnetGltfRenderer {
    /// <summary>
    /// 场景中的网格实例
    /// </summary>
    public sealed class MeshInstance {
        const int MaxSkinJoints = 64;

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
        /// 是否使用 GPU 实例化（EXT_mesh_gpu_instancing）
        /// </summary>
        public bool UseGpuInstancing { get; set; }

        internal MeshInstance(Node node, Mesh mesh, Skin skin, GL gl) {
            Node = node;
            Mesh = mesh;
            Skin = skin;
            WorldMatrix = node.WorldMatrix;
            OriginalWorldMatrix = node.WorldMatrix;
            IsVisible = true;
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

        /// <summary>
        /// 设置 Gizmo 变换矩阵
        /// </summary>
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

        internal void UpdateSkinning(Animation animation, float time, Dictionary<Node, Matrix4x4> cache) {
            if (!HasSkinning) {
                return;
            }

            for (int i = 0; i < JointMatrices.Length; i++) {
                Matrix4x4 jointWorld;
                if (animation != null && cache != null && cache.TryGetValue(_joints[i], out Matrix4x4 cached)) {
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
}
