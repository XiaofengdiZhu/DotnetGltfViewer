using System.Numerics;

namespace DotnetGltfRenderer {
    /// <summary>
    /// 材质纹理封装，包含纹理对象、UV索引和UV变换
    /// </summary>
    public class MaterialTexture {
        public Texture Texture { get; set; }
        public int UVIndex { get; set; }

        /// <summary>
        /// UV 变换矩阵 (3x3)，用于 KHR_texture_transform 扩展
        /// </summary>
        public Matrix3x2 UVTransform { get; set; } = Matrix3x2.Identity;

        /// <summary>
        /// 是否有非默认的 UV 变换
        /// </summary>
        public bool HasUVTransform { get; set; }

        /// <summary>
        /// UV 偏移量（用于动画）
        /// </summary>
        public Vector2 Offset { get; set; } = Vector2.Zero;

        /// <summary>
        /// UV 缩放（用于动画）
        /// </summary>
        public Vector2 Scale { get; set; } = Vector2.One;

        /// <summary>
        /// UV 旋转角度（弧度，用于动画）
        /// </summary>
        public float Rotation { get; set; }

        public MaterialTexture(Texture texture = null, int uvIndex = 0) {
            Texture = texture;
            UVIndex = uvIndex;
        }

        /// <summary>
        /// 从 SharpGLTF TextureTransform 创建 UV 变换矩阵
        /// glTF 规范要求变换顺序：Scale -> Rotation -> Translation
        /// </summary>
        public static Matrix3x2 CreateUVTransform(Vector2 offset, Vector2 scale, float rotation) {
            // glTF UV transform 变换顺序：Scale -> Rotation -> Translation
            // 在 GLSL 中：uv' = T * R * S * uv
            // 这意味着先应用 Scale，再应用 Rotation，最后应用 Translation
            // Matrix3x2 使用行向量约定：result = v * M（从右到左应用）
            // 所以矩阵乘法顺序应该是：translationMatrix * rotationMatrix * scaleMatrix
            // 这样 v * (T * R * S) = ((v * T) * R) * S，即先 S 再 R 最后 T
            // 但实际上行向量乘法 v * M 是从右到左应用的，所以顺序相反
            // 正确顺序是：先 scale，再 rotation，最后 translation
            // 对于行向量：v * scaleMatrix 先应用 scale
            // 然后 * rotationMatrix 应用 rotation
            // 最后 * translationMatrix 应用 translation
            // 所以我们需要：scaleMatrix * rotationMatrix * translationMatrix
            // 但是！Matrix3x2 乘法的语义是：result = left * right
            // 这意味着 left 先被应用，然后 right
            // 所以我们需要反过来的顺序
            Matrix3x2 scaleMatrix = Matrix3x2.CreateScale(scale.X, scale.Y);
            Matrix3x2 rotationMatrix = Matrix3x2.CreateRotation(rotation);
            Matrix3x2 translationMatrix = Matrix3x2.CreateTranslation(offset.X, offset.Y);

            // 官方渲染器：T * R * S（列主序，应用到向量是 S -> R -> T）
            // Matrix3x2 是行主序，应用到向量是 M * v，所以顺序相同
            // 参考：https://github.com/KhronosGroup/glTF/tree/main/extensions/2.0/Khronos/KHR_texture_transform
            // 变换顺序：Scale -> Rotation -> Translation
            // 对于行向量约定，矩阵乘法顺序是：translationMatrix * rotationMatrix * scaleMatrix
            // 但 Matrix3x2 的乘法是 result = left * right，left 先应用
            // 所以正确的顺序是：translationMatrix * rotationMatrix * scaleMatrix
            return translationMatrix * rotationMatrix * scaleMatrix;
        }

        /// <summary>
        /// 重新计算 UV 变换矩阵（动画更新后调用）
        /// </summary>
        public void RecomputeUVTransform() {
            UVTransform = CreateUVTransform(Offset, Scale, Rotation);
            HasUVTransform = Offset != Vector2.Zero || Scale != Vector2.One || Rotation != 0f;
        }

        /// <summary>
        /// 设置 UV 变换参数并重新计算矩阵
        /// </summary>
        public void SetTransform(Vector2 offset, Vector2 scale, float rotation) {
            Offset = offset;
            Scale = scale;
            Rotation = rotation;
            RecomputeUVTransform();
        }

        public static implicit operator Texture(MaterialTexture mt) => mt?.Texture;
    }
}