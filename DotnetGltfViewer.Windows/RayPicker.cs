using System.Numerics;
using DotnetGltfRenderer;

namespace DotnetGltfViewer.Windows {
    /// <summary>
    /// 射线拾取结果
    /// </summary>
    public struct PickingResult {
        /// <summary>
        /// 是否命中
        /// </summary>
        public bool Hit;

        /// <summary>
        /// 命中的场景模型
        /// </summary>
        public SceneModel Model;

        /// <summary>
        /// 命中的 MeshInstance（可能为 null）
        /// </summary>
        public MeshInstance MeshInstance;

        /// <summary>
        /// 命中距离
        /// </summary>
        public float Distance;

        /// <summary>
        /// 命中点
        /// </summary>
        public Vector3 HitPoint;

        /// <summary>
        /// 空结果
        /// </summary>
        public static PickingResult Empty => new() { Hit = false };
    }

    /// <summary>
    /// 射线拾取器，支持 AABB 相交检测
    /// </summary>
    public static class RayPicker {
        /// <summary>
        /// 从屏幕坐标生成世界空间射线
        /// </summary>
        /// <param name="screenPos">屏幕坐标</param>
        /// <param name="screenSize">屏幕尺寸</param>
        /// <param name="camera">相机</param>
        /// <returns>世界空间射线</returns>
        public static Ray CreateRayFromScreen(Vector2 screenPos, Vector2 screenSize, Camera camera) {
            // 转换到 NDC [-1, 1]
            float ndcX = (2.0f * screenPos.X) / screenSize.X - 1.0f;
            float ndcY = 1.0f - (2.0f * screenPos.Y) / screenSize.Y; // Y 翻转

            // 裁剪空间坐标
            Vector4 clipCoords = new(ndcX, ndcY, -1.0f, 1.0f);

            // 逆投影
            Matrix4x4 projection = camera.GetProjectionMatrix(screenSize.X / screenSize.Y);
            Matrix4x4.Invert(projection, out Matrix4x4 invProjection);
            Vector4 eyeCoords = Vector4.Transform(clipCoords, invProjection);

            // 只保留方向，z = -1（朝向屏幕内）
            eyeCoords = new Vector4(eyeCoords.X, eyeCoords.Y, -1.0f, 0.0f);

            // 逆视图
            Matrix4x4 view = camera.ViewMatrix;
            Matrix4x4.Invert(view, out Matrix4x4 invView);
            Vector4 worldCoords = Vector4.Transform(eyeCoords, invView);

            // 归一化方向
            Vector3 direction = Vector3.Normalize(new Vector3(worldCoords.X, worldCoords.Y, worldCoords.Z));

            return new Ray(camera.Position, direction);
        }

        /// <summary>
        /// 拾取场景中最近的模型
        /// </summary>
        /// <param name="scene">场景</param>
        /// <param name="ray">射线</param>
        /// <returns>拾取结果</returns>
        public static PickingResult Pick(Scene scene, Ray ray) {
            if (scene == null || scene.Models.Count == 0) {
                return PickingResult.Empty;
            }

            PickingResult closestResult = PickingResult.Empty;
            closestResult.Distance = float.MaxValue;

            foreach (SceneModel model in scene.Models) {
                if (!model.IsVisible) continue;

                PickingResult result = PickModel(model, ray);
                if (result.Hit && result.Distance < closestResult.Distance) {
                    closestResult = result;
                }
            }

            if (closestResult.Hit) {
                closestResult.HitPoint = ray.GetPoint(closestResult.Distance);
            }

            return closestResult;
        }

        /// <summary>
        /// 拾取单个模型
        /// </summary>
        static PickingResult PickModel(SceneModel sceneModel, Ray ray) {
            PickingResult result = PickingResult.Empty;
            result.Model = sceneModel;
            result.Distance = float.MaxValue;

            // 首先检测模型整体包围盒
            BoundingBox worldBounds = sceneModel.WorldBounds;
            if (!worldBounds.IsValid) {
                return PickingResult.Empty;
            }

            if (!worldBounds.Intersects(ray, out float modelDistance)) {
                return PickingResult.Empty;
            }

            // 然后检测每个 MeshInstance
            foreach (MeshInstance instance in sceneModel.Model.MeshInstances) {
                if (!instance.IsVisible) continue;

                // 获取 MeshInstance 的世界包围盒
                BoundingBox instanceBounds = GetMeshInstanceWorldBounds(instance);
                if (!instanceBounds.IsValid) continue;

                if (instanceBounds.Intersects(ray, out float instanceDistance)) {
                    if (instanceDistance < result.Distance) {
                        result.Hit = true;
                        result.MeshInstance = instance;
                        result.Distance = instanceDistance;
                    }
                }
            }

            // 如果没有命中任何 MeshInstance，但命中了模型包围盒，返回模型级别的命中
            if (!result.Hit && modelDistance < float.MaxValue) {
                result.Hit = true;
                result.MeshInstance = null;
                result.Distance = modelDistance;
            }

            return result;
        }

        /// <summary>
        /// 获取 MeshInstance 的世界空间包围盒
        /// </summary>
        static BoundingBox GetMeshInstanceWorldBounds(MeshInstance instance) {
            BoundingBox localBounds = instance.Mesh.LocalBounds;
            if (!localBounds.IsValid) {
                return BoundingBox.Empty;
            }

            return BoundingBox.Transform(localBounds, instance.WorldMatrix);
        }
    }
}
