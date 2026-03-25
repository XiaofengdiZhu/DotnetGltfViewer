using System.Numerics;

namespace DotnetGltfRenderer {
    /// <summary>
    /// 射线结构，用于射线拾取检测
    /// </summary>
    public struct Ray {
        /// <summary>
        /// 射线原点
        /// </summary>
        public Vector3 Origin;

        /// <summary>
        /// 射线方向（应为单位向量）
        /// </summary>
        public Vector3 Direction;

        /// <summary>
        /// 创建射线
        /// </summary>
        /// <param name="origin">射线原点</param>
        /// <param name="direction">射线方向（会被归一化）</param>
        public Ray(Vector3 origin, Vector3 direction) {
            Origin = origin;
            Direction = Vector3.Normalize(direction);
        }

        /// <summary>
        /// 获取射线上指定距离的点
        /// </summary>
        /// <param name="distance">距离</param>
        /// <returns>点坐标</returns>
        public readonly Vector3 GetPoint(float distance) => Origin + Direction * distance;

        public override readonly string ToString() => $"Ray(Origin: {Origin}, Direction: {Direction})";
    }
}
