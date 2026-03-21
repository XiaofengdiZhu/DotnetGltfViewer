using System;

namespace DotnetGltfRenderer {
    public static class MathHelper {
        public static float DegreesToRadians(float degrees) => MathF.PI / 180f * degrees;

        public static float RadiansToDegrees(float radians) => radians / MathF.PI * 180f;
    }
}