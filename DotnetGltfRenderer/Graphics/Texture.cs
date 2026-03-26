using System;
using System.IO;
using SharpGLTF.Memory;
using Silk.NET.OpenGLES;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using GltfTextureSampler = SharpGLTF.Schema2.TextureSampler;
using GltfTextureWrapMode = SharpGLTF.Schema2.TextureWrapMode;
using GltfTextureMipMapFilter = SharpGLTF.Schema2.TextureMipMapFilter;
using GltfTextureInterpolationFilter = SharpGLTF.Schema2.TextureInterpolationFilter;
using Image = SixLabors.ImageSharp.Image;

namespace DotnetGltfRenderer {
    public enum ModelTextureType {
        None,
        Diffuse,
        Specular,
        Normal,
        Height
    }

    public class Texture : IDisposable {

        public string Path { get; set; }
        public ModelTextureType Type { get; }
        public bool IsHDR { get; private set; }

        /// <summary>
        /// 获取 OpenGL 纹理句柄
        /// </summary>
        public uint Handle { get; }

        // ========== 延迟上传支持 ==========
        bool _initialized;
        MemoryImage _pendingImage;
        GltfTextureSampler _pendingSampler;
        readonly bool _pendingIsSrgb;

        public Texture(string path, ModelTextureType type = ModelTextureType.None, GltfTextureSampler sampler = null, bool isSrgb = true) {
            Path = path;
            Type = type;
            Handle = GlContext.GL.GenTexture();
            // 文件路径纹理立即上传（通常用于环境贴图等外部资源）
            Bind();
            using (Image<Rgba32> img = Image.Load<Rgba32>(path)) {
                UploadImage(img, sampler, isSrgb);
            }
            _initialized = true;
        }

        /// <summary>
        /// 从 glTF MemoryImage 创建纹理（延迟上传模式）
        /// </summary>
        public Texture(
            MemoryImage image,
            ModelTextureType type = ModelTextureType.None,
            GltfTextureSampler sampler = null,
            bool isSrgb = true) {
            Path = image.SourcePath;
            Type = type;
            Handle = GlContext.GL.GenTexture();

            // 延迟上传：只保存参数，不立即上传纹理数据
            _pendingImage = image;
            _pendingSampler = sampler;
            _pendingIsSrgb = isSrgb;
        }

        /// <summary>
        /// 从原始数据创建纹理（立即上传，用于运行时生成的纹理）
        /// </summary>
        public unsafe Texture(Span<byte> data, uint width, uint height) {
            Handle = GlContext.GL.GenTexture();
            Bind();
            fixed (void* d = data) {
                GlContext.GL.TexImage2D(
                    TextureTarget.Texture2D,
                    0,
                    (int)InternalFormat.Rgba,
                    width,
                    height,
                    0,
                    PixelFormat.Rgba,
                    PixelType.UnsignedByte,
                    d
                );
                SetParameters();
            }
            _initialized = true;
        }

        /// <summary>
        /// 从 HDR float 数据创建纹理（立即上传）
        /// </summary>
        public unsafe Texture(Span<float> data, uint width, uint height, bool halfFloat = false) {
            Handle = GlContext.GL.GenTexture();
            IsHDR = true;
            Bind();
            fixed (float* d = data) {
                GlContext.GL.TexImage2D(
                    TextureTarget.Texture2D,
                    0,
                    halfFloat ? InternalFormat.Rgb16f : InternalFormat.Rgb32f,
                    width,
                    height,
                    0,
                    PixelFormat.Rgb,
                    halfFloat ? PixelType.HalfFloat : PixelType.Float,
                    d
                );
                SetParameters(null, false);
            }
            _initialized = true;
        }

        /// <summary>
        /// 从 EnvironmentMap 创建纹理
        /// </summary>
        public static Texture FromEnvironmentMap(EnvironmentMap envMap, bool halfFloat = false) => new(
            envMap.DataFloat,
            (uint)envMap.Width,
            (uint)envMap.Height,
            halfFloat
        );

        /// <summary>
        /// 确保纹理数据已上传到 GPU（首次绑定时调用）
        /// </summary>
        public void EnsureInitialized() {
            if (_initialized) {
                return;
            }
            if (!_pendingImage.Content.IsEmpty) {
                // 直接绑定，不走 Bind() 避免循环调用
                GlContext.GL.BindTexture(TextureTarget.Texture2D, Handle);
                using (Stream stream = _pendingImage.Open()) {
                    if (stream != null) {
                        using (Image<Rgba32> img = Image.Load<Rgba32>(stream)) {
                            UploadImage(img, _pendingSampler, _pendingIsSrgb);
                        }
                    }
                }
                // 上传完成后释放引用
                _pendingImage = default;
                _pendingSampler = null;
            }
            _initialized = true;
        }

        unsafe void UploadImage(Image<Rgba32> image, GltfTextureSampler sampler, bool isSrgb = true) {
            InternalFormat internalFormat = isSrgb ? InternalFormat.Srgb8Alpha8 : InternalFormat.Rgba8;
            GlContext.GL.TexImage2D(
                TextureTarget.Texture2D,
                0,
                internalFormat,
                (uint)image.Width,
                (uint)image.Height,
                0,
                PixelFormat.Rgba,
                PixelType.UnsignedByte,
                null
            );
            image.ProcessPixelRows(accessor => {
                    for (int y = 0; y < accessor.Height; y++) {
                        fixed (void* data = accessor.GetRowSpan(y)) {
                            GlContext.GL.TexSubImage2D(
                                TextureTarget.Texture2D,
                                0,
                                0,
                                y,
                                (uint)accessor.Width,
                                1,
                                PixelFormat.Rgba,
                                PixelType.UnsignedByte,
                                data
                            );
                        }
                    }
                }
            );
            SetParameters(sampler);
        }

        void SetParameters(GltfTextureSampler sampler = null, bool allowMipmaps = true) {
            GltfTextureWrapMode wrapS = sampler?.WrapS ?? GltfTextureWrapMode.REPEAT;
            GltfTextureWrapMode wrapT = sampler?.WrapT ?? GltfTextureWrapMode.REPEAT;
            GltfTextureMipMapFilter minFilter = sampler?.MinFilter ?? GltfTextureMipMapFilter.DEFAULT;
            GltfTextureInterpolationFilter magFilter = sampler?.MagFilter ?? GltfTextureInterpolationFilter.DEFAULT;
            TextureMinFilter resolvedMinFilter = MapMinFilter(minFilter);
            if (!allowMipmaps
                && NeedsMipmaps(minFilter)) {
                resolvedMinFilter = TextureMinFilter.Linear;
            }
            GlContext.GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)MapWrapMode(wrapS));
            GlContext.GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)MapWrapMode(wrapT));
            GlContext.GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)resolvedMinFilter);
            GlContext.GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)MapMagFilter(magFilter));
            if (allowMipmaps && NeedsMipmaps(minFilter)) {
                GlContext.GL.GenerateMipmap(TextureTarget.Texture2D);
            }
        }

        static TextureWrapMode MapWrapMode(GltfTextureWrapMode wrapMode) {
            return wrapMode switch {
                GltfTextureWrapMode.CLAMP_TO_EDGE => TextureWrapMode.ClampToEdge,
                GltfTextureWrapMode.MIRRORED_REPEAT => TextureWrapMode.MirroredRepeat,
                _ => TextureWrapMode.Repeat
            };
        }

        static TextureMinFilter MapMinFilter(GltfTextureMipMapFilter minFilter) {
            return minFilter switch {
                GltfTextureMipMapFilter.NEAREST => TextureMinFilter.Nearest,
                GltfTextureMipMapFilter.LINEAR => TextureMinFilter.Linear,
                GltfTextureMipMapFilter.NEAREST_MIPMAP_NEAREST => TextureMinFilter.NearestMipmapNearest,
                GltfTextureMipMapFilter.LINEAR_MIPMAP_NEAREST => TextureMinFilter.LinearMipmapNearest,
                GltfTextureMipMapFilter.NEAREST_MIPMAP_LINEAR => TextureMinFilter.NearestMipmapLinear,
                _ => TextureMinFilter.LinearMipmapLinear
            };
        }

        static TextureMagFilter MapMagFilter(GltfTextureInterpolationFilter magFilter) {
            return magFilter switch {
                GltfTextureInterpolationFilter.NEAREST => TextureMagFilter.Nearest,
                _ => TextureMagFilter.Linear
            };
        }

        static bool NeedsMipmaps(GltfTextureMipMapFilter minFilter) => minFilter == GltfTextureMipMapFilter.DEFAULT
            || minFilter == GltfTextureMipMapFilter.NEAREST_MIPMAP_NEAREST
            || minFilter == GltfTextureMipMapFilter.LINEAR_MIPMAP_NEAREST
            || minFilter == GltfTextureMipMapFilter.NEAREST_MIPMAP_LINEAR
            || minFilter == GltfTextureMipMapFilter.LINEAR_MIPMAP_LINEAR;

        public void Bind(TextureUnit textureSlot = TextureUnit.Texture0) {
            // 延迟上传：首次绑定时确保纹理数据已上传
            EnsureInitialized();
            GlContext.GL.ActiveTexture(textureSlot);
            GlContext.GL.BindTexture(TextureTarget.Texture2D, Handle);
        }

        public void Dispose() {
            GlContext.GL.DeleteTexture(Handle);
        }
    }
}