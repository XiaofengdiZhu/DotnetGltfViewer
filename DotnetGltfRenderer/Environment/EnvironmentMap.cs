using System;
using System.IO;
using System.Text;
using Silk.NET.OpenGLES;

namespace DotnetGltfRenderer {
    /// <summary>
    /// HDR 环境贴图加载器，支持 Radiance .hdr 格式
    /// </summary>
    public class EnvironmentMap : IDisposable {
        public int Width { get; private set; }
        public int Height { get; private set; }
        public float[] DataFloat { get; private set; }
        public float Exposure { get; private set; } = 1.0f;

        // HDR 数据格式
        public enum HdrFormat {
            RgbE // Radiance RGBE format
        }

        /// <summary>
        /// 从 HDR 文件加载环境贴图
        /// </summary>
        public static EnvironmentMap LoadHDR(string path) {
            using FileStream stream = File.OpenRead(path);
            return LoadHDR(stream);
        }

        /// <summary>
        /// 从流加载 HDR 文件
        /// </summary>
        public static EnvironmentMap LoadHDR(Stream stream) {
            using BinaryReader reader = new(stream, Encoding.ASCII, true);

            // 解析文件头
            HdrHeader header = ParseHeader(reader);

            // 读取像素数据
            float[] data = ReadRleRgbe(reader, header.Width, header.Height);
            return new EnvironmentMap { Width = header.Width, Height = header.Height, DataFloat = data, Exposure = header.Exposure };
        }

        #region HDR 文件解析

        struct HdrHeader {
            public int Width;
            public int Height;
            public float Exposure;
            public string Format;
        }

        static HdrHeader ParseHeader(BinaryReader reader) {
            HdrHeader header = new() { Exposure = 1.0f, Format = "32-bit_rle_rgbe" };

            // 读取文件头行
            string line;
            bool foundFormat = false;
            while ((line = ReadLine(reader)) != null) {
                // 空行表示头部结束
                if (string.IsNullOrEmpty(line)) {
                    break;
                }

                // 解析格式
                if (line.StartsWith("FORMAT=", StringComparison.OrdinalIgnoreCase)) {
                    header.Format = line.Substring(7).Trim();
                    foundFormat = true;
                }

                // 解析曝光
                if (line.StartsWith("EXPOSURE=", StringComparison.OrdinalIgnoreCase)) {
                    if (float.TryParse(line.Substring(9).Trim(), out float exp)) {
                        header.Exposure = exp;
                    }
                }
            }

            // 验证格式
            if (!foundFormat
                || !header.Format.Contains("32-bit_rle_rgbe", StringComparison.OrdinalIgnoreCase)) {
                throw new NotSupportedException("Only 32-bit RLE RGBE format is supported");
            }

            // 读取分辨率
            line = ReadLine(reader);
            if (string.IsNullOrEmpty(line)) {
                throw new InvalidDataException("Missing resolution specifier");
            }

            // 解析分辨率 (格式: -Y height +X width 或类似)
            string[] parts = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 4) {
                throw new InvalidDataException($"Invalid resolution specifier: {line}");
            }

            // 解析宽度和高度
            for (int i = 0; i < parts.Length - 1; i++) {
                if (parts[i].Equals("-Y", StringComparison.OrdinalIgnoreCase)
                    || parts[i].Equals("Y", StringComparison.OrdinalIgnoreCase)) {
                    header.Height = int.Parse(parts[i + 1]);
                }
                if (parts[i].Equals("+X", StringComparison.OrdinalIgnoreCase)
                    || parts[i].Equals("-X", StringComparison.OrdinalIgnoreCase)
                    || parts[i].Equals("X", StringComparison.OrdinalIgnoreCase)) {
                    header.Width = int.Parse(parts[i + 1]);
                }
            }
            if (header.Width <= 0
                || header.Height <= 0) {
                throw new InvalidDataException($"Invalid dimensions: {header.Width}x{header.Height}");
            }
            return header;
        }

        static string ReadLine(BinaryReader reader) {
            StringBuilder sb = new();
            int b;
            while ((b = reader.ReadByte()) != -1
                && b != '\n') {
                if (b != '\r') {
                    sb.Append((char)b);
                }
            }
            return b == -1 && sb.Length == 0 ? null : sb.ToString();
        }

        /// <summary>
        /// 读取 RLE 压缩的 RGBE 数据并转换为 float RGB
        /// </summary>
        static float[] ReadRleRgbe(BinaryReader reader, int width, int height) {
            float[] rgbData = new float[width * height * 3];
            byte[] scanline = new byte[width * 4];
            for (int y = 0; y < height; y++) {
                // 读取扫描线头部
                int r = reader.ReadByte();
                int g = reader.ReadByte();
                int b = reader.ReadByte();
                int e = reader.ReadByte();

                // 检查是否为新 RLE 格式
                if (r == 2
                    && g == 2
                    && (b & 0x80) == 0) {
                    // 新 RLE 格式
                    int scanWidth = (b << 8) | e;
                    if (scanWidth != width) {
                        throw new InvalidDataException($"Scanline width mismatch: expected {width}, got {scanWidth}");
                    }

                    // 读取每个通道的 RLE 数据
                    for (int channel = 0; channel < 4; channel++) {
                        int pos = 0;
                        while (pos < width) {
                            int code = reader.ReadByte();
                            if (code > 128) {
                                // Run-length encoding
                                int runLength = code - 128;
                                byte value = reader.ReadByte();
                                for (int i = 0; i < runLength && pos < width; i++) {
                                    scanline[pos++ * 4 + channel] = value;
                                }
                            }
                            else {
                                // 未压缩数据
                                for (int i = 0; i < code && pos < width; i++) {
                                    scanline[pos++ * 4 + channel] = reader.ReadByte();
                                }
                            }
                        }
                    }
                }
                else {
                    // 旧格式或未压缩数据
                    scanline[0] = (byte)r;
                    scanline[1] = (byte)g;
                    scanline[2] = (byte)b;
                    scanline[3] = (byte)e;
                    if (width > 1) {
                        reader.Read(scanline, 4, (width - 1) * 4);
                    }
                }

                // 将 RGBE 转换为 float RGB
                for (int x = 0; x < width; x++) {
                    int srcIdx = x * 4;
                    int dstIdx = (y * width + x) * 3;
                    RgbeToFloat(scanline[srcIdx], scanline[srcIdx + 1], scanline[srcIdx + 2], scanline[srcIdx + 3], rgbData, dstIdx);
                }
            }
            return rgbData;
        }

        /// <summary>
        /// 将单个 RGBE 像素转换为 float RGB
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        static void RgbeToFloat(byte r, byte g, byte b, byte e, float[] output, int outputIndex) {
            if (e == 0) {
                // 指数为0表示黑色
                output[outputIndex] = 0;
                output[outputIndex + 1] = 0;
                output[outputIndex + 2] = 0;
            }
            else {
                // 计算缩放因子: (1/256) * 2^(e - 128) = 2^(e - 136)
                float scale = MathF.Pow(2.0f, e - 136);
                output[outputIndex] = r * scale;
                output[outputIndex + 1] = g * scale;
                output[outputIndex + 2] = b * scale;
            }
        }

        #endregion

        #region OpenGL 纹理创建

        /// <summary>
        /// 创建 OpenGL 纹理 (HDR float32 格式)
        /// </summary>
        public unsafe uint CreateGLTexture(GL gl, bool halfFloat = false) {
            uint texture = gl.GenTexture();
            gl.ActiveTexture(TextureUnit.Texture0);
            gl.BindTexture(TextureTarget.Texture2D, texture);

            // 设置纹理参数
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);

            // 上传纹理数据
            InternalFormat internalFormat = halfFloat ? InternalFormat.Rgb16f : InternalFormat.Rgb32f;
            PixelType pixelType = halfFloat ? PixelType.HalfFloat : PixelType.Float;
            fixed (float* data = DataFloat) {
                gl.TexImage2D(
                    TextureTarget.Texture2D,
                    0,
                    internalFormat,
                    (uint)Width,
                    (uint)Height,
                    0,
                    PixelFormat.Rgb,
                    pixelType,
                    data
                );
            }
            gl.BindTexture(TextureTarget.Texture2D, 0);
            return texture;
        }

        /// <summary>
        /// 获取 RGBE 编码的原始数据（用于兼容不支持 float 纹理的平台）
        /// </summary>
        public byte[] GetRgbEData() {
            byte[] rgbeData = new byte[Width * Height * 4];
            for (int i = 0; i < Width * Height; i++) {
                int srcIdx = i * 3;
                int dstIdx = i * 4;
                FloatToRgbe(DataFloat[srcIdx], DataFloat[srcIdx + 1], DataFloat[srcIdx + 2], rgbeData, dstIdx);
            }
            return rgbeData;
        }

        /// <summary>
        /// 将 float RGB 转换为 RGBE 编码
        /// </summary>
        static void FloatToRgbe(float r, float g, float b, byte[] output, int outputIndex) {
            float maxComponent = MathF.Max(r, MathF.Max(g, b));
            if (maxComponent < 1e-6f) {
                // 太暗，直接设为黑色
                output[outputIndex] = 0;
                output[outputIndex + 1] = 0;
                output[outputIndex + 2] = 0;
                output[outputIndex + 3] = 0;
                return;
            }

            // 计算指数：使用 log2 来近似 Frexp
            // Frexp 返回 m * 2^e，其中 0.5 <= |m| < 1
            // 所以 e = floor(log2(x)) + 1，m = x / 2^e
            float logVal = MathF.Log2(maxComponent);
            int e = (int)MathF.Floor(logVal) + 1;
            float mantissa = maxComponent / MathF.Pow(2, e);

            // scale 使得 mantissa * 256 / maxComponent = 256 / 2^e
            float scale = mantissa * 256.0f / maxComponent;

            // 指数偏移
            e += 128;

            // 溢出保护
            if (e > 255) {
                output[outputIndex] = 255;
                output[outputIndex + 1] = 255;
                output[outputIndex + 2] = 255;
                output[outputIndex + 3] = 255;
            }
            else if (e < 1) {
                output[outputIndex] = 0;
                output[outputIndex + 1] = 0;
                output[outputIndex + 2] = 0;
                output[outputIndex + 3] = 0;
            }
            else {
                output[outputIndex] = (byte)Math.Clamp(r * scale, 0, 255);
                output[outputIndex + 1] = (byte)Math.Clamp(g * scale, 0, 255);
                output[outputIndex + 2] = (byte)Math.Clamp(b * scale, 0, 255);
                output[outputIndex + 3] = (byte)e;
            }
        }

        #endregion

        #region 采样方法

        /// <summary>
        /// 采样环境贴图 (equirectangular 坐标)
        /// </summary>
        public (float R, float G, float B) Sample(float u, float v) {
            // 将 UV 转换为像素坐标
            float x = u * Width;
            float y = v * Height;
            int x0 = (int)MathF.Floor(x);
            int y0 = (int)MathF.Floor(y);

            // 双线性插值
            float fx = x - x0;
            float fy = y - y0;
            int x1 = (x0 + 1) % Width;
            int y1 = Math.Clamp(y0 + 1, 0, Height - 1);
            x0 = Math.Clamp(x0, 0, Width - 1);
            y0 = Math.Clamp(y0, 0, Height - 1);
            int idx00 = (y0 * Width + x0) * 3;
            int idx10 = (y0 * Width + x1) * 3;
            int idx01 = (y1 * Width + x0) * 3;
            int idx11 = (y1 * Width + x1) * 3;
            float r = Bilerp(DataFloat[idx00], DataFloat[idx10], DataFloat[idx01], DataFloat[idx11], fx, fy);
            float g = Bilerp(DataFloat[idx00 + 1], DataFloat[idx10 + 1], DataFloat[idx01 + 1], DataFloat[idx11 + 1], fx, fy);
            float b = Bilerp(DataFloat[idx00 + 2], DataFloat[idx10 + 2], DataFloat[idx01 + 2], DataFloat[idx11 + 2], fx, fy);
            return (r, g, b);
        }

        /// <summary>
        /// 根据方向向量采样环境贴图
        /// </summary>
        public (float R, float G, float B) SampleDirection(System.Numerics.Vector3 direction) {
            // 将方向转换为 equirectangular UV 坐标
            float u = MathF.Atan2(direction.Z, direction.X) / (2 * MathF.PI) + 0.5f;
            float v = MathF.Asin(Math.Clamp(direction.Y, -1, 1)) / MathF.PI + 0.5f;
            return Sample(u, v);
        }

        static float Bilerp(float v00, float v10, float v01, float v11, float fx, float fy) {
            float v0 = v00 * (1 - fx) + v10 * fx;
            float v1 = v01 * (1 - fx) + v11 * fx;
            return v0 * (1 - fy) + v1 * fy;
        }

        #endregion

        public void Dispose() {
            DataFloat = null;
        }
    }
}