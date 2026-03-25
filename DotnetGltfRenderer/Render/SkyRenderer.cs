using System;
using System.Numerics;
using Silk.NET.OpenGLES;

namespace DotnetGltfRenderer {
    /// <summary>
    /// 天空盒渲染器
    /// </summary>
    public class SkyRenderer {
        readonly Skybox _skybox;
        readonly Shader _skyShader;

        public SkyRenderer(Skybox skybox, Shader skyShader) {
            _skybox = skybox;
            _skyShader = skyShader;
        }

        /// <summary>
        /// 渲染天空盒
        /// </summary>
        public void Render(Matrix4x4 view, Matrix4x4 projection,
            float envIntensity, float envBlur, float envRotationDegrees,
            float exposure, IblSampler iblSampler) {
            if (_skybox == null || iblSampler == null) {
                return;
            }

            // 计算 ViewProjection 矩阵（移除平移分量）
            Matrix4x4 viewRotation = view;
            viewRotation.M41 = 0f;
            viewRotation.M42 = 0f;
            viewRotation.M43 = 0f;
            Matrix4x4 viewProjection = viewRotation * projection;

            // 计算环境旋转矩阵（绕 Y 轴旋转）
            float rotRad = envRotationDegrees * MathF.PI / 180.0f;
            float cosR = MathF.Cos(rotRad);
            float sinR = MathF.Sin(rotRad);
            Vector3 envRotCol0 = new(cosR, 0, -sinR);
            Vector3 envRotCol1 = new(0, 1, 0);
            Vector3 envRotCol2 = new(sinR, 0, cosR);

            // 禁用深度测试（官方做法）
            GlContext.DisableDepthTest();
            GlContext.DisableBlend();
            GlContext.FrontFaceCCW();
            GlContext.EnableCullFace();

            _skyShader.Use();

            // 设置 uniforms
            _skyShader.SetUniform("u_ViewProjectionMatrix", viewProjection);
            _skyShader.SetUniformMatrix3("u_EnvRotation", envRotCol0, envRotCol1, envRotCol2);
            _skyShader.SetUniform("u_MipCount", iblSampler.MipCount);
            _skyShader.SetUniform("u_EnvBlurNormalized", envBlur);
            _skyShader.SetUniform("u_EnvIntensity", envIntensity);
            _skyShader.SetUniform("u_Exposure", exposure);

            // 绑定 GGX 预过滤环境贴图
            GlContext.GL.ActiveTexture(TextureUnit.Texture0);
            GlContext.GL.BindTexture(TextureTarget.TextureCubeMap, iblSampler.GGXTexture);
            _skyShader.SetUniform("u_GGXEnvSampler", 0);

            _skybox.Draw();

            // 恢复深度测试
            GlContext.EnableDepthTest();
        }

        /// <summary>
        /// 渲染天空盒（线性输出模式，用于离屏缓冲区）
        /// </summary>
        public void RenderLinear(Matrix4x4 view, Matrix4x4 projection,
            float envIntensity, float exposure, IblSampler iblSampler) {
            if (_skybox == null || iblSampler == null) {
                return;
            }

            // 计算 ViewProjection 矩阵（移除平移分量）
            Matrix4x4 viewRotation = view;
            viewRotation.M41 = 0f;
            viewRotation.M42 = 0f;
            viewRotation.M43 = 0f;
            Matrix4x4 viewProjection = viewRotation * projection;

            // 计算环境旋转矩阵（绕 Y 轴旋转）
            float rotRad = 0f;
            float cosR = MathF.Cos(rotRad);
            float sinR = MathF.Sin(rotRad);
            Vector3 envRotCol0 = new(cosR, 0, -sinR);
            Vector3 envRotCol1 = new(0, 1, 0);
            Vector3 envRotCol2 = new(sinR, 0, cosR);

            // 禁用深度测试
            GlContext.DisableDepthTest();
            GlContext.DisableBlend();
            GlContext.FrontFaceCCW();
            GlContext.EnableCullFace();

            // 使用 LINEAR_OUTPUT 版本的天空盒着色器
            ShaderDefines skyFragDefines = new();
            skyFragDefines.Add("LINEAR_OUTPUT");
            int skyVertHash = ShaderCache.SelectShader("cubemap.vert", new System.Collections.Generic.List<string>());
            int skyFragHash = ShaderCache.SelectShader("cubemap.frag", skyFragDefines.GetDefinesList());
            Shader linearSkyShader = ShaderCache.GetShaderProgram(skyVertHash, skyFragHash);
            linearSkyShader.Use();

            // 设置 uniforms
            linearSkyShader.SetUniform("u_ViewProjectionMatrix", viewProjection);
            linearSkyShader.SetUniformMatrix3("u_EnvRotation", envRotCol0, envRotCol1, envRotCol2);
            linearSkyShader.SetUniform("u_MipCount", iblSampler.MipCount);
            linearSkyShader.SetUniform("u_EnvBlurNormalized", 0.0f);
            linearSkyShader.SetUniform("u_EnvIntensity", envIntensity);
            linearSkyShader.SetUniform("u_Exposure", exposure);

            // 绑定 GGX 预过滤环境贴图
            GlContext.GL.ActiveTexture(TextureUnit.Texture0);
            GlContext.GL.BindTexture(TextureTarget.TextureCubeMap, iblSampler.GGXTexture);
            linearSkyShader.SetUniform("u_GGXEnvSampler", 0);

            _skybox.Draw();

            // 恢复深度测试
            GlContext.EnableDepthTest();
        }
    }
}
