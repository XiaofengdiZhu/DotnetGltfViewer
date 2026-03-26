using System;
using System.Collections.Generic;
using System.Numerics;

namespace DotnetGltfRenderer {
    /// <summary>
    /// glTF 几何处理器
    /// </summary>
    public static class GltfGeometryProcessor {
        /// <summary>
        /// 填充顶点缓冲区
        /// </summary>
        public static void FillVertexBuffers(Mesh mesh,
            IReadOnlyList<Vector3> positions,
            IReadOnlyList<Vector3> normals,
            IReadOnlyList<Vector2> uv0,
            IReadOnlyList<Vector2> uv1,
            IReadOnlyList<Vector4> colors,
            IReadOnlyList<Vector4> tangents,
            IReadOnlyList<Vector4> joints,
            IReadOnlyList<Vector4> weights,
            Vector4[] generatedTangents) {
            int count = positions.Count;
            bool hasTangents = (tangents != null && tangents.Count > 0) || generatedTangents != null;
            bool hasSurface = normals != null || colors != null || hasTangents;
            bool hasSkin = joints != null && weights != null;
            bool hasUV1 = uv1 != null;
            float[] baseBuf = new float[count * Mesh.BaseVertexStride];
            float[] uv1Buf = hasUV1 ? new float[count * Mesh.UV1VertexStride] : null;
            float[] surfaceBuf = hasSurface ? new float[count * Mesh.SurfaceVertexStride] : null;
            float[] skinBuf = hasSkin ? new float[count * Mesh.SkinVertexStride] : null;
            int bPtr = 0, uPtr = 0, sPtr = 0, kPtr = 0;
            for (int i = 0; i < count; i++) {
                // Base Buffer: [Pos.x, Pos.y, Pos.z, UV0.x, UV0.y]
                Vector3 p = positions[i];
                Vector2 uv0Val = uv0 != null && i < uv0.Count ? uv0[i] : Vector2.Zero;
                baseBuf[bPtr++] = p.X;
                baseBuf[bPtr++] = p.Y;
                baseBuf[bPtr++] = p.Z;
                baseBuf[bPtr++] = uv0Val.X;
                baseBuf[bPtr++] = uv0Val.Y;

                // UV1 Buffer: [UV1.x, UV1.y]
                if (hasUV1) {
                    Vector2 uv1Val = i < uv1.Count ? uv1[i] : Vector2.Zero;
                    uv1Buf[uPtr++] = uv1Val.X;
                    uv1Buf[uPtr++] = uv1Val.Y;
                }

                // Surface Buffer: [Normal, Color, Tangent]
                if (hasSurface) {
                    Vector3 n = normals != null && i < normals.Count ? normals[i] : InferFallbackNormal(p);
                    Vector4 c = colors != null && i < colors.Count ? colors[i] : Vector4.One;
                    Vector4 t = tangents != null && i < tangents.Count ? tangents[i] :
                        generatedTangents != null && i < generatedTangents.Length ? generatedTangents[i] : new Vector4(1, 0, 0, 1);
                    surfaceBuf[sPtr++] = n.X;
                    surfaceBuf[sPtr++] = n.Y;
                    surfaceBuf[sPtr++] = n.Z;
                    surfaceBuf[sPtr++] = c.X;
                    surfaceBuf[sPtr++] = c.Y;
                    surfaceBuf[sPtr++] = c.Z;
                    surfaceBuf[sPtr++] = c.W;
                    surfaceBuf[sPtr++] = t.X;
                    surfaceBuf[sPtr++] = t.Y;
                    surfaceBuf[sPtr++] = t.Z;
                    surfaceBuf[sPtr++] = t.W;
                }

                // Skin Buffer: [Joints, Weights]
                if (hasSkin) {
                    Vector4 j = i < joints.Count ? joints[i] : Vector4.Zero;
                    Vector4 w = i < weights.Count ? weights[i] : new Vector4(1, 0, 0, 0);
                    skinBuf[kPtr++] = j.X;
                    skinBuf[kPtr++] = j.Y;
                    skinBuf[kPtr++] = j.Z;
                    skinBuf[kPtr++] = j.W;
                    skinBuf[kPtr++] = w.X;
                    skinBuf[kPtr++] = w.Y;
                    skinBuf[kPtr++] = w.Z;
                    skinBuf[kPtr++] = w.W;
                }
            }
            mesh.BaseVertices = baseBuf;
            mesh.UV1Vertices = uv1Buf;
            mesh.SurfaceVertices = surfaceBuf;
            mesh.SkinVertices = skinBuf;
        }

        /// <summary>
        /// 生成切线（Lengyel 方法）
        /// </summary>
        public static Vector4[] GenerateTangents(IReadOnlyList<Vector3> positions,
            IReadOnlyList<Vector3> normals,
            IReadOnlyList<Vector2> uvs,
            IReadOnlyList<uint> indices) {
            if (positions == null
                || normals == null
                || uvs == null
                || indices == null) {
                return null;
            }
            int vertexCount = positions.Count;
            if (vertexCount == 0
                || normals.Count < vertexCount
                || uvs.Count < vertexCount) {
                return null;
            }
            Vector3[] tan1 = new Vector3[vertexCount];
            Vector3[] tan2 = new Vector3[vertexCount];
            for (int i = 0; i + 2 < indices.Count; i += 3) {
                int i0 = (int)indices[i];
                int i1 = (int)indices[i + 1];
                int i2 = (int)indices[i + 2];
                if (i0 < 0
                    || i0 >= vertexCount
                    || i1 < 0
                    || i1 >= vertexCount
                    || i2 < 0
                    || i2 >= vertexCount) {
                    continue;
                }
                Vector3 p0 = positions[i0];
                Vector3 p1 = positions[i1];
                Vector3 p2 = positions[i2];
                Vector2 uv0 = uvs[i0];
                Vector2 uv1 = uvs[i1];
                Vector2 uv2 = uvs[i2];
                Vector3 edge1 = p1 - p0;
                Vector3 edge2 = p2 - p0;
                Vector2 duv1 = uv1 - uv0;
                Vector2 duv2 = uv2 - uv0;
                float denominator = duv1.X * duv2.Y - duv2.X * duv1.Y;
                if (MathF.Abs(denominator) < 1e-8f) {
                    continue;
                }
                float inverse = 1f / denominator;
                Vector3 sdir = (edge1 * duv2.Y - edge2 * duv1.Y) * inverse;
                Vector3 tdir = (edge2 * duv1.X - edge1 * duv2.X) * inverse;
                tan1[i0] += sdir;
                tan1[i1] += sdir;
                tan1[i2] += sdir;
                tan2[i0] += tdir;
                tan2[i1] += tdir;
                tan2[i2] += tdir;
            }
            Vector4[] tangents = new Vector4[vertexCount];
            for (int i = 0; i < vertexCount; i++) {
                Vector3 n = normals[i];
                if (n.LengthSquared() < float.Epsilon) {
                    n = InferFallbackNormal(positions[i]);
                }
                else {
                    n = Vector3.Normalize(n);
                }
                Vector3 t = tan1[i];
                if (t.LengthSquared() < 1e-12f) {
                    Vector3 axis = MathF.Abs(n.Y) < 0.999f ? Vector3.UnitY : Vector3.UnitX;
                    t = Vector3.Cross(axis, n);
                    if (t.LengthSquared() < 1e-12f) {
                        t = Vector3.UnitX;
                    }
                    tangents[i] = new Vector4(Vector3.Normalize(t), 1f);
                    continue;
                }
                t = Vector3.Normalize(t - n * Vector3.Dot(n, t));
                Vector3 b = Vector3.Cross(n, t);
                float w = Vector3.Dot(b, tan2[i]) < 0f ? -1f : 1f;
                tangents[i] = new Vector4(t, w);
            }
            return tangents;
        }

        /// <summary>
        /// 推断后备法线（从位置推断）
        /// </summary>
        public static Vector3 InferFallbackNormal(Vector3 position) {
            if (position.LengthSquared() < float.Epsilon) {
                return Vector3.UnitY;
            }
            return Vector3.Normalize(position);
        }
    }
}