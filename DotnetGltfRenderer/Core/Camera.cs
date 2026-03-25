using System;
using System.Numerics;

namespace DotnetGltfRenderer {
    /// <summary>
    /// 轨道相机类，支持以固定点为中心的旋转、缩放和平移
    /// </summary>
    public class Camera {
        const float MinDistance = 0.1f;
        const float DefaultDistanceMargin = 1.5f;

        Vector3 _front = -Vector3.UnitZ;
        readonly Vector3 _worldUp = Vector3.UnitY;

        /// <summary>
        /// 焦点位置（相机观察的中心点）
        /// </summary>
        public Vector3 FocalPoint { get; set; } = Vector3.Zero;

        /// <summary>
        /// 相机到焦点的距离
        /// </summary>
        public float Distance { get; private set; } = 5f;

        /// <summary>
        /// 相机位置（自动根据焦点、距离和角度计算）
        /// </summary>
        public Vector3 Position { get; private set; } = new(0f, 0f, 5f);

        /// <summary>
        /// 相机前方向（归一化）
        /// </summary>
        public Vector3 Front => _front;

        /// <summary>
        /// 相机上方向
        /// </summary>
        public Vector3 Up { get; private set; } = Vector3.UnitY;

        /// <summary>
        /// 相机右方向
        /// </summary>
        public Vector3 Right { get; private set; } = Vector3.UnitX;

        /// <summary>
        /// 水平旋转角度（度，绕Y轴）
        /// </summary>
        public float Yaw { get; set; }

        /// <summary>
        /// 垂直旋转角度（度，绕X轴）
        /// </summary>
        public float Pitch { get; set; }

        /// <summary>
        /// 视野角度（度）
        /// </summary>
        public float Zoom { get; set; } = 45f;

        /// <summary>
        /// 移动速度
        /// </summary>
        public float MoveSpeed { get; set; } = 2.5f;

        /// <summary>
        /// 近裁剪面距离
        /// </summary>
        public float NearPlane { get; set; } = 0.1f;

        /// <summary>
        /// 远裁剪面距离
        /// </summary>
        public float FarPlane { get; set; } = 100f;

        /// <summary>
        /// 获取视图矩阵
        /// </summary>
        public Matrix4x4 ViewMatrix => Matrix4x4.CreateLookAt(Position, FocalPoint, Up);

        /// <summary>
        /// 获取投影矩阵
        /// </summary>
        public Matrix4x4 GetProjectionMatrix(float aspect) => Matrix4x4.CreatePerspectiveFieldOfView(
            MathHelper.DegreesToRadians(Zoom),
            aspect,
            NearPlane,
            FarPlane
        );

        /// <summary>
        /// 获取朝向焦点的方向（相机前方）
        /// </summary>
        public Vector3 GetForwardDirection() => Vector3.Normalize(FocalPoint - Position);

        /// <summary>
        /// 获取相机右方向
        /// </summary>
        public Vector3 GetRightDirection() => Right;

        /// <summary>
        /// 轨道旋转：以焦点为中心旋转相机
        /// </summary>
        /// <param name="yawDelta">水平旋转角度（度）</param>
        /// <param name="pitchDelta">垂直旋转角度（度）</param>
        public void Orbit(float yawDelta, float pitchDelta) {
            Yaw += yawDelta;
            Pitch += pitchDelta;
            // 限制垂直角度，避免万向节死锁
            Pitch = Math.Clamp(Pitch, -89.0f, 89.0f);
            UpdatePosition();
        }

        /// <summary>
        /// 缩放：改变相机到焦点的距离
        /// </summary>
        /// <param name="delta">缩放因子（正值靠近，负值远离）</param>
        public void ZoomDistance(float delta) {
            Distance *= MathF.Pow(1.1f, -delta);
            Distance = Math.Max(MinDistance, Distance);
            UpdatePosition();
        }

        /// <summary>
        /// 平移：同时移动相机和焦点
        /// </summary>
        /// <param name="xOffset">水平偏移量（屏幕空间）</param>
        /// <param name="yOffset">垂直偏移量（屏幕空间）</param>
        public void Pan(float xOffset, float yOffset) {
            // 根据当前距离调整平移速度
            float panSpeed = Distance * 0.002f;
            Vector3 right = Right * xOffset * panSpeed;
            Vector3 up = Up * yOffset * panSpeed;
            Vector3 translation = right + up;

            FocalPoint += translation;
            Position += translation;
        }

        /// <summary>
        /// 平移：同时移动相机和焦点（世界空间向量）
        /// </summary>
        public void Pan(Vector3 translation) {
            FocalPoint += translation;
            Position += translation;
        }

        /// <summary>
        /// 设置相机位置和焦点，自动计算角度和距离
        /// </summary>
        public void SetPositionAndTarget(Vector3 position, Vector3 target) {
            Position = position;
            FocalPoint = target;
            Distance = Vector3.Distance(position, target);

            // 计算角度
            Vector3 direction = Vector3.Normalize(target - position);
            Yaw = MathHelper.RadiansToDegrees(MathF.Atan2(direction.Z, direction.X));
            Pitch = MathHelper.RadiansToDegrees(MathF.Asin(direction.Y));

            UpdateCameraVectors();
        }

        /// <summary>
        /// 让相机正视指定的包围盒，自动计算合适的距离
        /// </summary>
        public void LookAtBoundingBox(Vector3 min, Vector3 max, float aspect, float distanceMargin = DefaultDistanceMargin) {
            Vector3 modelCenter = (min + max) * 0.5f;
            Vector3 extents = (max - min) * 0.5f;
            float radius = MathF.Max(extents.Length(), 0.5f);

            // 计算合适的距离
            float verticalHalfFov = MathHelper.DegreesToRadians(Zoom) * 0.5f;
            float horizontalHalfFov = MathF.Atan(MathF.Tan(verticalHalfFov) * aspect);
            float limitingHalfFov = MathF.Min(verticalHalfFov, horizontalHalfFov);
            float distance = radius / MathF.Max(0.01f, MathF.Tan(limitingHalfFov)) * distanceMargin;

            // 正视模型：从正前方观察
            FocalPoint = modelCenter;
            Distance = distance;
            Yaw = -90f;      // 正对模型前方
            Pitch = 0f;    // 水平视角

            UpdateDerivedProperties(radius, distance);
            UpdatePosition();
        }

        /// <summary>
        /// 获取从相机指向焦点的方向（用于光照计算）
        /// </summary>
        public Vector3 GetLightDirectionFromCamera() => Vector3.Normalize(FocalPoint - Position);

        /// <summary>
        /// 更新相机位置（根据焦点、距离和角度）
        /// </summary>
        void UpdatePosition() {
            UpdateCameraVectors();

            // 球坐标系转笛卡尔坐标
            float yawRad = MathHelper.DegreesToRadians(Yaw);
            float pitchRad = MathHelper.DegreesToRadians(Pitch);

            float x = MathF.Cos(pitchRad) * MathF.Cos(yawRad);
            float y = MathF.Sin(pitchRad);
            float z = MathF.Cos(pitchRad) * MathF.Sin(yawRad);

            // 相机位置 = 焦点 - 方向向量 * 距离
            _front = new Vector3(x, y, z);
            Position = FocalPoint - _front * Distance;
        }

        /// <summary>
        /// 更新相机基向量
        /// </summary>
        void UpdateCameraVectors() {
            float yawRad = MathHelper.DegreesToRadians(Yaw);
            float pitchRad = MathHelper.DegreesToRadians(Pitch);

            // 计算前方向（从相机指向焦点）
            float x = MathF.Cos(pitchRad) * MathF.Cos(yawRad);
            float y = MathF.Sin(pitchRad);
            float z = MathF.Cos(pitchRad) * MathF.Sin(yawRad);
            _front = Vector3.Normalize(new Vector3(x, y, z));

            // 计算右方向和上方向
            Right = Vector3.Normalize(Vector3.Cross(_front, _worldUp));
            Up = Vector3.Normalize(Vector3.Cross(Right, _front));
        }

        /// <summary>
        /// 更新派生属性（移动速度、近/远裁剪面）
        /// </summary>
        void UpdateDerivedProperties(float radius, float distance) {
            MoveSpeed = Math.Clamp(radius * 0.5f, 0.5f, 20f);
            NearPlane = MathF.Max(0.01f, radius / 200f);
            FarPlane = MathF.Max(200f, distance + radius * 10f);
        }
    }
}
