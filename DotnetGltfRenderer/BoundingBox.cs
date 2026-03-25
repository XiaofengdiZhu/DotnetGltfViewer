using System;
using System.Numerics;

namespace DotnetGltfRenderer {
    /// <summary>
    /// 轴对齐包围盒，用于快速碰撞检测和射线拾取
    /// </summary>
    public struct BoundingBox {
        /// <summary>
        /// 包围盒最小点
        /// </summary>
        public Vector3 Min;

        /// <summary>
        /// 包围盒最大点
        /// </summary>
        public Vector3 Max;

        /// <summary>
        /// 创建包围盒
        /// </summary>
        /// <param name="min">最小点</param>
        /// <param name="max">最大点</param>
        public BoundingBox(Vector3 min, Vector3 max) {
            Min = min;
            Max = max;
        }

        /// <summary>
        /// 包围盒中心点
        /// </summary>
        public readonly Vector3 Center => (Min + Max) * 0.5f;

        /// <summary>
        /// 包围盒半尺寸（从中心到边的距离）
        /// </summary>
        public readonly Vector3 Extents => (Max - Min) * 0.5f;

        /// <summary>
        /// 包围盒全尺寸
        /// </summary>
        public readonly Vector3 Size => Max - Min;

        /// <summary>
        /// 空包围盒（无效状态）
        /// </summary>
        public static BoundingBox Empty => new(Vector3.Zero, Vector3.Zero);

        /// <summary>
        /// 创建从中心点和尺寸生成的包围盒
        /// </summary>
        public static BoundingBox FromCenterAndSize(Vector3 center, Vector3 size) {
            Vector3 halfSize = size * 0.5f;
            return new BoundingBox(center - halfSize, center + halfSize);
        }

        /// <summary>
        /// 合并两个包围盒
        /// </summary>
        public static BoundingBox Merge(BoundingBox a, BoundingBox b) {
            return new BoundingBox(
                Vector3.Min(a.Min, b.Min),
                Vector3.Max(a.Max, b.Max)
            );
        }

        /// <summary>
        /// 变换包围盒（结果为包含变换后所有顶点的 AABB）
        /// </summary>
        public static BoundingBox Transform(BoundingBox box, Matrix4x4 matrix) {
            // 变换所有 8 个顶点，然后计算新的 AABB
            Vector3[] corners = GetCorners(box);
            Vector3 min = new Vector3(float.MaxValue);
            Vector3 max = new Vector3(float.MinValue);

            foreach (Vector3 corner in corners) {
                Vector3 transformed = Vector3.Transform(corner, matrix);
                min = Vector3.Min(min, transformed);
                max = Vector3.Max(max, transformed);
            }

            return new BoundingBox(min, max);
        }

        /// <summary>
        /// 获取包围盒的 8 个角点
        /// </summary>
        public static Vector3[] GetCorners(BoundingBox box) {
            return [
                new Vector3(box.Min.X, box.Min.Y, box.Min.Z),
                new Vector3(box.Max.X, box.Min.Y, box.Min.Z),
                new Vector3(box.Min.X, box.Max.Y, box.Min.Z),
                new Vector3(box.Max.X, box.Max.Y, box.Min.Z),
                new Vector3(box.Min.X, box.Min.Y, box.Max.Z),
                new Vector3(box.Max.X, box.Min.Y, box.Max.Z),
                new Vector3(box.Min.X, box.Max.Y, box.Max.Z),
                new Vector3(box.Max.X, box.Max.Y, box.Max.Z),
            ];
        }

        /// <summary>
        /// 射线与包围盒相交检测（Slab 方法）
        /// </summary>
        /// <param name="ray">射线</param>
        /// <param name="distance">相交距离（如果相交）</param>
        /// <returns>是否相交</returns>
        public readonly bool Intersects(Ray ray, out float distance) {
            return Intersects(ray, this, out distance);
        }

        /// <summary>
        /// 射线与包围盒相交检测（Slab 方法）
        /// </summary>
        public static bool Intersects(Ray ray, BoundingBox box, out float distance) {
            // Slab 方法：检查射线与三对平行平面的相交
            float tmin = float.NegativeInfinity;
            float tmax = float.PositiveInfinity;

            // X 轴
            if (!IntersectAxis(ray.Origin.X, ray.Direction.X, box.Min.X, box.Max.X, ref tmin, ref tmax)) {
                distance = 0;
                return false;
            }

            // Y 轴
            if (!IntersectAxis(ray.Origin.Y, ray.Direction.Y, box.Min.Y, box.Max.Y, ref tmin, ref tmax)) {
                distance = 0;
                return false;
            }

            // Z 轴
            if (!IntersectAxis(ray.Origin.Z, ray.Direction.Z, box.Min.Z, box.Max.Z, ref tmin, ref tmax)) {
                distance = 0;
                return false;
            }

            // 选择最近的正距离
            if (tmin > 0) {
                distance = tmin;
            } else if (tmax > 0) {
                distance = tmax;
            } else {
                distance = 0;
                return false;
            }

            return true;
        }

        static bool IntersectAxis(float origin, float direction, float min, float max, ref float tmin, ref float tmax) {
            if (MathF.Abs(direction) < 1e-8f) {
                // 射线与轴平行，检查原点是否在 slab 内
                return origin >= min && origin <= max;
            }

            float t1 = (min - origin) / direction;
            float t2 = (max - origin) / direction;

            // 确保 t1 <= t2
            if (t1 > t2) {
                (t1, t2) = (t2, t1);
            }

            tmin = MathF.Max(tmin, t1);
            tmax = MathF.Min(tmax, t2);

            return tmin <= tmax;
        }

        /// <summary>
        /// 检查点是否在包围盒内
        /// </summary>
        public readonly bool Contains(Vector3 point) {
            return point.X >= Min.X && point.X <= Max.X &&
                   point.Y >= Min.Y && point.Y <= Max.Y &&
                   point.Z >= Min.Z && point.Z <= Max.Z;
        }

        /// <summary>
        /// 检查包围盒是否有效（Max >= Min）
        /// </summary>
        public readonly bool IsValid => Max.X >= Min.X && Max.Y >= Min.Y && Max.Z >= Min.Z;

        public override readonly string ToString() => $"BoundingBox(Min: {Min}, Max: {Max})";
    }
}
