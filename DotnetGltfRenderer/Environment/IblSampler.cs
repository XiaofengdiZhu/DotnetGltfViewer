using System;
using System.IO;
using Silk.NET.OpenGLES;
using ZLogger;

namespace DotnetGltfRenderer {
    /// <summary>
    /// IBL 预处理器，用于生成预过滤的环境贴图
    /// </summary>
    public class IblSampler : IDisposable {
        // 配置参数
        readonly int _textureSize = 256;
        readonly int _ggxSampleCount = 1024;
        readonly int _lambertianSampleCount = 2048;
        readonly int _sheenSampleCount = 64;
        readonly int _lowestMipLevel = 4;
        readonly int _lutResolution = 1024;

        // 输出纹理
        public uint LambertianTexture { get; private set; }
        public uint GGXTexture { get; private set; }
        public uint SheenTexture { get; private set; }
        public uint GGXLut { get; private set; }
        public uint CharlieLut { get; private set; }
        public int MipCount { get; private set; }

        // 内部资源
        uint _inputTexture;
        uint _cubemapTexture;
        uint _framebuffer;
        Shader _panoramaToCubemapShader;
        Shader _iblFilteringShader;

        // 进度保存目录
        public static string ProgressDirectory { get; set; } = "Progress";

        /// <summary>
        /// 初始化并处理环境贴图
        /// </summary>
        public void Process(EnvironmentMap panorama) {
            // 确保进度目录存在
            if (!Directory.Exists(ProgressDirectory)) {
                Directory.CreateDirectory(ProgressDirectory);
            }

            // 初始化着色器
            InitShaders();

            // 创建输入纹理（从 HDR 数据）
            CreateInputTexture(panorama);

            // 创建 Cubemap 纹理
            CreateCubemapTextures();

            // 创建 Framebuffer
            _framebuffer = GlContext.GL.GenFramebuffer();

            // Step 1: Equirectangular -> Cubemap
            PanoramaToCubeMap();
            //SaveCubemapToBmp("cubemap");

            // Step 2: Lambertian Irradiance
            CubeMapToLambertian();
            //SaveCubemapToBmp("lambertian", LambertianTexture, 1);

            // Step 3: GGX 预过滤
            CubeMapToGGX();
            /*for (int mip = 0; mip <= MipCount; mip++) {
                SaveCubemapToBmp($"ggx_mip{mip}", GGXTexture, 1, mip);
            }*/

            // Step 4: Charlie (Sheen) 预过滤
            CubeMapToSheen();
            /*for (int mip = 0; mip <= MipCount; mip++) {
                SaveCubemapToBmp($"sheen_mip{mip}", SheenTexture, 1, mip);
            }*/

            // Step 5: BRDF LUT
            GenerateGGXLut();
            //SaveLutToBmp("ggx_lut", GGXLut);
            GenerateCharlieLut();
            //SaveLutToBmp("charlie_lut", CharlieLut);
            GlContext.GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            LogManager.Logger.ZLogInformation($"[IBL] Processing complete!");
        }

        void InitShaders() {
            int vertHash = ShaderCache.SelectShader("fullscreen.vert", []);
            int fragHash = ShaderCache.SelectShader("panorama_to_cubemap.frag", []);
            _panoramaToCubemapShader = ShaderCache.GetShaderProgram(vertHash, fragHash);
            int iblFragHash = ShaderCache.SelectShader("ibl_filtering.frag", []);
            _iblFilteringShader = ShaderCache.GetShaderProgram(vertHash, iblFragHash);
        }

        unsafe void CreateInputTexture(EnvironmentMap panorama) {
            _inputTexture = GlContext.GL.GenTexture();
            GlContext.GL.ActiveTexture(TextureUnit.Texture0);
            GlContext.GL.BindTexture(TextureTarget.Texture2D, _inputTexture);

            // 转换为 RGBA float 数据（OpenGL ES 不支持 RGB32F 作为 render target）
            int numPixels = panorama.Width * panorama.Height;
            float[] rgbaData = new float[numPixels * 4];
            for (int i = 0; i < numPixels; i++) {
                rgbaData[i * 4] = panorama.DataFloat[i * 3];
                rgbaData[i * 4 + 1] = panorama.DataFloat[i * 3 + 1];
                rgbaData[i * 4 + 2] = panorama.DataFloat[i * 3 + 2];
                rgbaData[i * 4 + 3] = 1.0f;
            }
            fixed (float* d = rgbaData) {
                GlContext.GL.TexImage2D(
                    TextureTarget.Texture2D,
                    0,
                    InternalFormat.Rgba32f,
                    (uint)panorama.Width,
                    (uint)panorama.Height,
                    0,
                    PixelFormat.Rgba,
                    PixelType.Float,
                    d
                );
            }
            GlContext.GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.MirroredRepeat);
            GlContext.GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.MirroredRepeat);
            GlContext.GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GlContext.GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        }

        void CreateCubemapTextures() {
            // 创建 Cubemap（带 mipmap）
            _cubemapTexture = CreateCubemapTexture(true);

            // Lambertian Irradiance（不需要 mipmap）
            LambertianTexture = CreateCubemapTexture(false);

            // GGX 预过滤（需要 mipmap）
            GGXTexture = CreateCubemapTexture(true);

            // Sheen 预过滤（需要 mipmap）
            SheenTexture = CreateCubemapTexture(true);

            // 预生成 mipmap
            GlContext.GL.BindTexture(TextureTarget.TextureCubeMap, GGXTexture);
            GlContext.GL.GenerateMipmap(TextureTarget.TextureCubeMap);
            GlContext.GL.BindTexture(TextureTarget.TextureCubeMap, SheenTexture);
            GlContext.GL.GenerateMipmap(TextureTarget.TextureCubeMap);

            // 计算 mipmap 级别数
            MipCount = (int)Math.Floor(Math.Log2(_textureSize)) + 1 - _lowestMipLevel;
        }

        unsafe uint CreateCubemapTexture(bool withMipmaps) {
            uint texture = GlContext.GL.GenTexture();
            GlContext.GL.BindTexture(TextureTarget.TextureCubeMap, texture);
            for (int i = 0; i < 6; i++) {
                GlContext.GL.TexImage2D(
                    TextureTarget.TextureCubeMapPositiveX + i,
                    0,
                    InternalFormat.Rgba16f,
                    (uint)_textureSize,
                    (uint)_textureSize,
                    0,
                    PixelFormat.Rgba,
                    PixelType.HalfFloat,
                    null
                );
            }
            if (withMipmaps) {
                GlContext.GL.TexParameter(
                    TextureTarget.TextureCubeMap,
                    TextureParameterName.TextureMinFilter,
                    (int)TextureMinFilter.LinearMipmapLinear
                );
            }
            else {
                GlContext.GL.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            }
            GlContext.GL.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GlContext.GL.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GlContext.GL.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            return texture;
        }

        void PanoramaToCubeMap() {
            for (int i = 0; i < 6; i++) {
                GlContext.GL.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
                GlContext.GL.FramebufferTexture2D(
                    FramebufferTarget.Framebuffer,
                    FramebufferAttachment.ColorAttachment0,
                    TextureTarget.TextureCubeMapPositiveX + i,
                    _cubemapTexture,
                    0
                );
                GlContext.GL.Viewport(0, 0, (uint)_textureSize, (uint)_textureSize);
                GlContext.GL.ClearColor(0.0f, 0.0f, 0.0f, 1.0f);
                GlContext.GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
                _panoramaToCubemapShader.Use();
                GlContext.GL.ActiveTexture(TextureUnit.Texture0);
                GlContext.GL.BindTexture(TextureTarget.Texture2D, _inputTexture);
                _panoramaToCubemapShader.SetUniform("u_panorama", 0);
                _panoramaToCubemapShader.SetUniform("u_currentFace", i);

                // 绘制全屏三角形
                GlContext.GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
            }
            GlContext.GL.BindTexture(TextureTarget.TextureCubeMap, _cubemapTexture);
            GlContext.GL.GenerateMipmap(TextureTarget.TextureCubeMap);
        }

        void CubeMapToLambertian() {
            ApplyFilter(0, 0.0f, 0, LambertianTexture, _lambertianSampleCount);
        }

        void CubeMapToGGX() {
            for (int mipLevel = 0; mipLevel <= MipCount; mipLevel++) {
                // 使用平方映射让低 roughness 区域有更多 mipmap 级别
                // 线性映射: mip1 = 0.25
                // 平方映射: mip1 = 0.0625 (更清晰)
                float roughness = MipCount > 1 ? (float)Math.Pow((double)mipLevel / (MipCount - 1), 2) : 0.0f;
                ApplyFilter(1, roughness, mipLevel, GGXTexture, _ggxSampleCount);
            }
        }

        void CubeMapToSheen() {
            // Charlie 分布在 roughness = 0 时会有数学问题
            // 设置最小 roughness 为 0.05
            const float minSheenRoughness = 0.05f;
            for (int mipLevel = 0; mipLevel <= MipCount; mipLevel++) {
                // 使用平方映射，与 GGX 保持一致
                float roughness = MipCount > 1 ? (float)Math.Pow((double)mipLevel / (MipCount - 1), 2) : minSheenRoughness;
                roughness = Math.Max(roughness, minSheenRoughness);
                ApplyFilter(2, roughness, mipLevel, SheenTexture, _sheenSampleCount);
            }
        }

        void ApplyFilter(int distribution, float roughness, int targetMipLevel, uint targetTexture, int sampleCount) {
            int currentTextureSize = _textureSize >> targetMipLevel;
            currentTextureSize = Math.Max(1, currentTextureSize);
            for (int i = 0; i < 6; i++) {
                GlContext.GL.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
                GlContext.GL.FramebufferTexture2D(
                    FramebufferTarget.Framebuffer,
                    FramebufferAttachment.ColorAttachment0,
                    TextureTarget.TextureCubeMapPositiveX + i,
                    targetTexture,
                    targetMipLevel
                );
                GlContext.GL.Viewport(0, 0, (uint)currentTextureSize, (uint)currentTextureSize);
                GlContext.GL.ClearColor(0.0f, 0.0f, 0.0f, 1.0f);
                GlContext.GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
                _iblFilteringShader.Use();
                GlContext.GL.ActiveTexture(TextureUnit.Texture0);
                GlContext.GL.BindTexture(TextureTarget.TextureCubeMap, _cubemapTexture);
                _iblFilteringShader.SetUniform("u_cubemapTexture", 0);
                _iblFilteringShader.SetUniform("u_roughness", roughness);
                _iblFilteringShader.SetUniform("u_sampleCount", sampleCount);
                _iblFilteringShader.SetUniform("u_width", _textureSize);
                _iblFilteringShader.SetUniform("u_lodBias", 0.0f);
                _iblFilteringShader.SetUniform("u_distribution", distribution);
                _iblFilteringShader.SetUniform("u_currentFace", i);
                _iblFilteringShader.SetUniform("u_isGeneratingLUT", 0);
                _iblFilteringShader.SetUniform("u_floatTexture", 1);
                _iblFilteringShader.SetUniform("u_intensityScale", 1.0f);
                GlContext.GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
            }
        }

        void GenerateGGXLut() {
            GGXLut = CreateLutTexture();
            SampleLut(1, GGXLut);
        }

        void GenerateCharlieLut() {
            CharlieLut = CreateLutTexture();
            SampleLut(2, CharlieLut);
        }

        unsafe uint CreateLutTexture() {
            uint texture = GlContext.GL.GenTexture();
            GlContext.GL.BindTexture(TextureTarget.Texture2D, texture);
            GlContext.GL.TexImage2D(
                TextureTarget.Texture2D,
                0,
                InternalFormat.Rgba16f,
                (uint)_lutResolution,
                (uint)_lutResolution,
                0,
                PixelFormat.Rgba,
                PixelType.HalfFloat,
                null
            );
            GlContext.GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GlContext.GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GlContext.GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GlContext.GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            return texture;
        }

        void SampleLut(int distribution, uint targetTexture) {
            GlContext.GL.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
            GlContext.GL.FramebufferTexture2D(
                FramebufferTarget.Framebuffer,
                FramebufferAttachment.ColorAttachment0,
                TextureTarget.Texture2D,
                targetTexture,
                0
            );
            GlContext.GL.Viewport(0, 0, (uint)_lutResolution, (uint)_lutResolution);
            GlContext.GL.ClearColor(0.0f, 0.0f, 0.0f, 1.0f);
            GlContext.GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
            _iblFilteringShader.Use();
            GlContext.GL.ActiveTexture(TextureUnit.Texture0);
            GlContext.GL.BindTexture(TextureTarget.TextureCubeMap, _cubemapTexture);
            _iblFilteringShader.SetUniform("u_cubemapTexture", 0);
            _iblFilteringShader.SetUniform("u_roughness", 0.0f);
            _iblFilteringShader.SetUniform("u_sampleCount", 512);
            _iblFilteringShader.SetUniform("u_width", 0);
            _iblFilteringShader.SetUniform("u_lodBias", 0.0f);
            _iblFilteringShader.SetUniform("u_distribution", distribution);
            _iblFilteringShader.SetUniform("u_currentFace", 0);
            _iblFilteringShader.SetUniform("u_isGeneratingLUT", 1);
            _iblFilteringShader.SetUniform("u_floatTexture", 1);
            _iblFilteringShader.SetUniform("u_intensityScale", 1.0f);
            GlContext.GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
        }

        #region BMP Saving Methods

        /// <summary>
        /// 保存 Cubemap 的所有面为 BMP
        /// </summary>
        public void SaveCubemapToBmp(string name, uint texture = 0, int sizeMultiplier = 1, int mipLevel = 0) {
            uint tex = texture == 0 ? _cubemapTexture : texture;
            int size = _textureSize >> mipLevel;
            size = Math.Max(1, size);

            // 创建合并图像（2x3 布局）
            int combinedWidth = size * 4;
            int combinedHeight = size * 3;
            byte[] combinedData = new byte[combinedWidth * combinedHeight * 3];
            for (int face = 0; face < 6; face++) {
                // 读取面数据
                byte[] faceData = ReadCubemapFace(tex, face, size, mipLevel);

                // 计算在合并图像中的位置
                int offsetX, offsetY;
                switch (face) {
                    case 0:
                        offsetX = 2;
                        offsetY = 1;
                        break; // +X
                    case 1:
                        offsetX = 0;
                        offsetY = 1;
                        break; // -X
                    case 2:
                        offsetX = 1;
                        offsetY = 0;
                        break; // +Y
                    case 3:
                        offsetX = 1;
                        offsetY = 2;
                        break; // -Y
                    case 4:
                        offsetX = 1;
                        offsetY = 1;
                        break; // +Z
                    case 5:
                        offsetX = 3;
                        offsetY = 1;
                        break; // -Z
                    default:
                        offsetX = 0;
                        offsetY = 0;
                        break;
                }

                // 复制到合并图像
                for (int y = 0; y < size; y++) {
                    for (int x = 0; x < size; x++) {
                        int srcIdx = (y * size + x) * 3;
                        int dstX = offsetX * size + x;
                        int dstY = offsetY * size + y; // 不翻转 Y，SaveBmp 会处理
                        int dstIdx = (dstY * combinedWidth + dstX) * 3;
                        combinedData[dstIdx] = faceData[srcIdx];
                        combinedData[dstIdx + 1] = faceData[srcIdx + 1];
                        combinedData[dstIdx + 2] = faceData[srcIdx + 2];
                    }
                }
            }
            string path = Path.Combine(ProgressDirectory, $"{name}.bmp");
            SaveBmp(path, combinedData, combinedWidth, combinedHeight);
        }

        unsafe byte[] ReadCubemapFace(uint texture, int face, int size, int mipLevel) {
            byte[] data = new byte[size * size * 3];
            float[] floatData = new float[size * size * 4];

            // 创建临时 FBO 读取
            uint fbo = GlContext.GL.GenFramebuffer();
            GlContext.GL.BindFramebuffer(FramebufferTarget.Framebuffer, fbo);
            GlContext.GL.FramebufferTexture2D(
                FramebufferTarget.Framebuffer,
                FramebufferAttachment.ColorAttachment0,
                TextureTarget.TextureCubeMapPositiveX + face,
                texture,
                mipLevel
            );
            fixed (float* d = floatData) {
                GlContext.GL.ReadPixels(
                    0,
                    0,
                    (uint)size,
                    (uint)size,
                    PixelFormat.Rgba,
                    PixelType.Float,
                    d
                );
            }
            GlContext.GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            GlContext.GL.DeleteFramebuffer(fbo);

            // 转换为 RGB 字节（带 HDR tone mapping）
            for (int i = 0; i < size * size; i++) {
                float r = floatData[i * 4];
                float g = floatData[i * 4 + 1];
                float b = floatData[i * 4 + 2];

                // 简单的 tone mapping
                r = ToneMap(r);
                g = ToneMap(g);
                b = ToneMap(b);
                data[i * 3] = (byte)Math.Clamp(r * 255, 0, 255);
                data[i * 3 + 1] = (byte)Math.Clamp(g * 255, 0, 255);
                data[i * 3 + 2] = (byte)Math.Clamp(b * 255, 0, 255);
            }
            return data;
        }

        /// <summary>
        /// 保存 LUT 纹理为 BMP
        /// </summary>
        public unsafe void SaveLutToBmp(string name, uint texture) {
            byte[] data = new byte[_lutResolution * _lutResolution * 3];
            float[] floatData = new float[_lutResolution * _lutResolution * 4];
            uint fbo = GlContext.GL.GenFramebuffer();
            GlContext.GL.BindFramebuffer(FramebufferTarget.Framebuffer, fbo);
            GlContext.GL.FramebufferTexture2D(
                FramebufferTarget.Framebuffer,
                FramebufferAttachment.ColorAttachment0,
                TextureTarget.Texture2D,
                texture,
                0
            );
            fixed (float* d = floatData) {
                GlContext.GL.ReadPixels(
                    0,
                    0,
                    (uint)_lutResolution,
                    (uint)_lutResolution,
                    PixelFormat.Rgba,
                    PixelType.Float,
                    d
                );
            }
            GlContext.GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            GlContext.GL.DeleteFramebuffer(fbo);
            for (int i = 0; i < _lutResolution * _lutResolution; i++) {
                float r = floatData[i * 4];
                float g = floatData[i * 4 + 1];
                float b = floatData[i * 4 + 2];
                data[i * 3] = (byte)Math.Clamp(r * 255, 0, 255);
                data[i * 3 + 1] = (byte)Math.Clamp(g * 255, 0, 255);
                data[i * 3 + 2] = (byte)Math.Clamp(b * 255, 0, 255);
            }
            string path = Path.Combine(ProgressDirectory, $"{name}.bmp");
            SaveBmp(path, data, _lutResolution, _lutResolution);
        }

        float ToneMap(float value) {
            // ACES Filmic Tone Mapping
            const float a = 2.51f;
            const float b = 0.03f;
            const float c = 2.43f;
            const float d = 0.59f;
            const float e = 0.14f;
            return Math.Clamp(value * (a * value + b) / (value * (c * value + d) + e), 0.0f, 1.0f);
        }

        static void SaveBmp(string path, byte[] rgbData, int width, int height) {
            // BMP 文件格式：文件头 + 信息头 + 像素数据
            int rowSize = (width * 3 + 3) / 4 * 4; // 每行对齐到 4 字节
            int padding = rowSize - width * 3;
            int pixelDataSize = rowSize * height;
            int fileSize = 54 + pixelDataSize;
            using (FileStream fs = new(path, FileMode.Create, FileAccess.Write))
            using (BinaryWriter bw = new(fs)) {
                // 文件头 (14 bytes)
                bw.Write((byte)'B');
                bw.Write((byte)'M');
                bw.Write(fileSize);
                bw.Write(0);
                bw.Write(54);

                // 信息头 (40 bytes)
                bw.Write(40);
                bw.Write(width);
                bw.Write(height);
                bw.Write((short)1);
                bw.Write((short)24);
                bw.Write(0);
                bw.Write(pixelDataSize);
                bw.Write(2835);
                bw.Write(2835);
                bw.Write(0);
                bw.Write(0);

                // 像素数据（BGR 顺序，从下往上）
                for (int y = height - 1; y >= 0; y--) {
                    for (int x = 0; x < width; x++) {
                        int srcIdx = (y * width + x) * 3;
                        bw.Write(rgbData[srcIdx + 2]); // B
                        bw.Write(rgbData[srcIdx + 1]); // G
                        bw.Write(rgbData[srcIdx]); // R
                    }
                    // 添加填充
                    for (int p = 0; p < padding; p++) {
                        bw.Write((byte)0);
                    }
                }
            }
        }

        #endregion

        public void Dispose() {
            // 注意：Shader 对象由 static ShaderCache 管理，不需要单独 Dispose
            if (_inputTexture != 0) {
                GlContext.GL.DeleteTexture(_inputTexture);
            }
            if (_cubemapTexture != 0) {
                GlContext.GL.DeleteTexture(_cubemapTexture);
            }
            if (LambertianTexture != 0) {
                GlContext.GL.DeleteTexture(LambertianTexture);
            }
            if (GGXTexture != 0) {
                GlContext.GL.DeleteTexture(GGXTexture);
            }
            if (SheenTexture != 0) {
                GlContext.GL.DeleteTexture(SheenTexture);
            }
            if (GGXLut != 0) {
                GlContext.GL.DeleteTexture(GGXLut);
            }
            if (CharlieLut != 0) {
                GlContext.GL.DeleteTexture(CharlieLut);
            }
            if (_framebuffer != 0) {
                GlContext.GL.DeleteFramebuffer(_framebuffer);
            }
        }
    }
}