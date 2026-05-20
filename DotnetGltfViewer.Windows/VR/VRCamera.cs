using System;
using System.Numerics;
using Silk.NET.OpenXR;

namespace DotnetGltfViewer.Windows.VR;

/// <summary>
/// VR 相机工具，从 OpenXR HMD pose 和 FOV 构建渲染矩阵
/// </summary>
static class VRCamera {
    /// <summary>
    /// 从 HMD pose 构建视图矩阵
    /// </summary>
    public static Matrix4x4 CreateViewMatrix(Posef pose) {
        // OpenXR pose: position + orientation (quaternion)
        // View matrix = inverse of camera world transform
        Quaternion q = new(pose.Orientation.X, pose.Orientation.Y, pose.Orientation.Z, pose.Orientation.W);
        Matrix4x4 rotation = Matrix4x4.CreateFromQuaternion(q);
        Matrix4x4 translation = Matrix4x4.CreateTranslation(pose.Position.X, pose.Position.Y, pose.Position.Z);
        Matrix4x4 world = rotation * translation;
        Matrix4x4.Invert(world, out Matrix4x4 view);
        return view;
    }

    /// <summary>
    /// 从 OpenXR FOV 构建非对称投影矩阵（OpenGL convention，clip Z [-1,1]）
    /// </summary>
    public static Matrix4x4 CreateProjectionMatrix(Fovf fov, float nearZ, float farZ) {
        float tanLeft = MathF.Tan(fov.AngleLeft);
        float tanRight = MathF.Tan(fov.AngleRight);
        float tanUp = MathF.Tan(fov.AngleUp);
        float tanDown = MathF.Tan(fov.AngleDown);
        float tanWidth = tanRight - tanLeft;
        float tanHeight = tanUp - tanDown;

        // OpenGL convention: clip Z = [-1, 1]
        float m00 = 2.0f / tanWidth;
        float m11 = 2.0f / tanHeight;
        float m02 = (tanRight + tanLeft) / tanWidth;
        float m12 = (tanUp + tanDown) / tanHeight;
        float m22 = -(farZ + nearZ) / (farZ - nearZ);
        float m23 = -(2.0f * farZ * nearZ) / (farZ - nearZ);

        return new Matrix4x4(
            m00, 0, 0, 0,
            0, m11, 0, 0,
            m02, m12, m22, -1,
            0, 0, m23, 0
        );
    }
}
