using System;
using System.Numerics;
using SharpGLTF.Schema2;

namespace DotnetGltfRenderer {
    /// <summary>
    /// 光源类型
    /// </summary>
    public enum LightType {
        Directional,
        Point,
        Spot
    }

    /// <summary>
    /// 光源类
    /// </summary>
    public class Light {
        /// <summary>
        /// 光源类型
        /// </summary>
        public LightType Type { get; set; } = LightType.Directional;

        /// <summary>
        /// 关联的 glTF 节点（用于动画更新）
        /// </summary>
        internal Node SourceNode { get; set; }

        /// <summary>
        /// 光源位置（点光源/聚光灯用）
        /// </summary>
        public Vector3 Position { get; set; }

        /// <summary>
        /// 光源方向（方向光/聚光灯用）
        /// </summary>
        public Vector3 Direction { get; set; } = -Vector3.UnitY;

        /// <summary>
        /// 光源颜色
        /// </summary>
        public Vector3 Color { get; set; } = Vector3.One;

        /// <summary>
        /// 光照强度
        /// </summary>
        public float Intensity { get; set; } = 1.0f;

        /// <summary>
        /// 光照范围（点光源用）
        /// </summary>
        public float Range { get; set; } = 10.0f;

        /// <summary>
        /// 内锥角（聚光灯用，度数）
        /// </summary>
        public float InnerConeAngle { get; set; } = 15.0f;

        /// <summary>
        /// 外锥角（聚光灯用，度数）
        /// </summary>
        public float OuterConeAngle { get; set; } = 30.0f;

        /// <summary>
        /// 创建方向光
        /// </summary>
        public static Light CreateDirectional(Vector3 direction, Vector3 color, float intensity = 1.0f) => new() {
            Type = LightType.Directional, Direction = Vector3.Normalize(direction), Color = color, Intensity = intensity
        };

        /// <summary>
        /// 创建点光源
        /// </summary>
        public static Light CreatePoint(Vector3 position, Vector3 color, float intensity = 1.0f, float range = 10.0f) => new() {
            Type = LightType.Point, Position = position, Color = color, Intensity = intensity, Range = range
        };

        /// <summary>
        /// 创建聚光灯
        /// </summary>
        public static Light CreateSpot(Vector3 position,
            Vector3 direction,
            Vector3 color,
            float intensity = 1.0f,
            float innerCone = 15.0f,
            float outerCone = 30.0f) => new() {
            Type = LightType.Spot,
            Position = position,
            Direction = Vector3.Normalize(direction),
            Color = color,
            Intensity = intensity,
            InnerConeAngle = innerCone,
            OuterConeAngle = outerCone
        };

        /// <summary>
        /// 从 glTF PunctualLight 转换为 Light
        /// </summary>
        public static Light ConvertFromGltf(PunctualLight punctualLight, Matrix4x4 worldMatrix, Node sourceNode) {
            // 从世界矩阵提取位置
            Vector3 position = new(worldMatrix.M41, worldMatrix.M42, worldMatrix.M43);

            // 从世界矩阵提取方向（局部空间的前方向是 -Z）
            Vector3 forward = new(-worldMatrix.M31, -worldMatrix.M32, -worldMatrix.M33);
            Vector3 direction = Vector3.Normalize(forward);
            Light light = new() {
                Color = punctualLight.Color,
                Intensity = punctualLight.Intensity,
                Range = punctualLight.Range,
                Position = position,
                Direction = direction,
                SourceNode = sourceNode
            };
            switch (punctualLight.LightType) {
                case PunctualLightType.Directional: light.Type = LightType.Directional; break;
                case PunctualLightType.Point: light.Type = LightType.Point; break;
                case PunctualLightType.Spot:
                    light.Type = LightType.Spot;
                    // 将弧度转换为度数
                    light.InnerConeAngle = punctualLight.InnerConeAngle * 180f / MathF.PI;
                    light.OuterConeAngle = punctualLight.OuterConeAngle * 180f / MathF.PI;
                    break;
            }
            return light;
        }
    }
}