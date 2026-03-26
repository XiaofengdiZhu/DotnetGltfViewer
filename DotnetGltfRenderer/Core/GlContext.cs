using Silk.NET.OpenGLES;

namespace DotnetGltfRenderer {
    /// <summary>
    /// OpenGL ES 上下文，提供全局 GL 访问和状态缓存
    /// </summary>
    public static class GlContext {
        static GL _gl;

        // Enable caps 状态缓存
        static bool _blendEnabled;
        static bool _cullFaceEnabled;
        static bool _depthTestEnabled;
        static bool _scissorTestEnabled;

        // 混合函数状态
        static (BlendingFactor Src, BlendingFactor Dst, BlendingFactor SrcAlpha, BlendingFactor DstAlpha) _blendFunc;

        // 正面朝向
        static FrontFaceDirection _frontFace;

        // 深度写入掩码
        static bool _depthMask;

        // 深度比较函数
        static DepthFunction _depthFunc;

        /// <summary>
        /// 获取或设置 OpenGL ES 接口
        /// 必须在使用前设置
        /// </summary>
        public static GL GL {
            get => _gl;
            set {
                _gl = value;
                Reset();
            }
        }

        /// <summary>
        /// 是否已初始化
        /// </summary>
        public static bool IsInitialized => _gl != null;

        /// <summary>
        /// 重置所有缓存状态（例如切换上下文或窗口大小变化时调用）
        /// </summary>
        public static void Reset() {
            _blendEnabled = false;
            _cullFaceEnabled = false;
            _depthTestEnabled = false;
            _scissorTestEnabled = false;
            _blendFunc = ((BlendingFactor)(-1), (BlendingFactor)(-1), (BlendingFactor)(-1), (BlendingFactor)(-1));
            _frontFace = (FrontFaceDirection)(-1);
            _depthFunc = (DepthFunction)(-1);
            _depthMask = true;
        }

        // ========== Enable/Disable ==========

        public static void EnableBlend() {
            if (!_blendEnabled) {
                _gl.Enable(EnableCap.Blend);
                _blendEnabled = true;
            }
        }

        public static void DisableBlend() {
            if (_blendEnabled) {
                _gl.Disable(EnableCap.Blend);
                _blendEnabled = false;
            }
        }

        public static void EnableCullFace() {
            if (!_cullFaceEnabled) {
                _gl.Enable(EnableCap.CullFace);
                _cullFaceEnabled = true;
            }
        }

        public static void DisableCullFace() {
            if (_cullFaceEnabled) {
                _gl.Disable(EnableCap.CullFace);
                _cullFaceEnabled = false;
            }
        }

        public static void EnableDepthTest() {
            if (!_depthTestEnabled) {
                _gl.Enable(EnableCap.DepthTest);
                _depthTestEnabled = true;
            }
        }

        public static void DisableDepthTest() {
            if (_depthTestEnabled) {
                _gl.Disable(EnableCap.DepthTest);
                _depthTestEnabled = false;
            }
        }

        public static void EnableScissorTest() {
            if (!_scissorTestEnabled) {
                _gl.Enable(EnableCap.ScissorTest);
                _scissorTestEnabled = true;
            }
        }

        public static void DisableScissorTest() {
            if (_scissorTestEnabled) {
                _gl.Disable(EnableCap.ScissorTest);
                _scissorTestEnabled = false;
            }
        }

        // ========== Blend Function ==========

        public static void BlendFunc(BlendingFactor srcFactor, BlendingFactor dstFactor) {
            BlendFuncSeparate(srcFactor, dstFactor, srcFactor, dstFactor);
        }

        public static void BlendFuncSeparate(BlendingFactor srcRgb, BlendingFactor dstRgb, BlendingFactor srcAlpha, BlendingFactor dstAlpha) {
            if (_blendFunc.Src != srcRgb
                || _blendFunc.Dst != dstRgb
                || _blendFunc.SrcAlpha != srcAlpha
                || _blendFunc.DstAlpha != dstAlpha) {
                _gl.BlendFuncSeparate(srcRgb, dstRgb, srcAlpha, dstAlpha);
                _blendFunc = (srcRgb, dstRgb, srcAlpha, dstAlpha);
            }
        }

        /// <summary>
        /// 设置标准 Alpha 混合（src_alpha, one_minus_src_alpha）
        /// </summary>
        public static void SetAlphaBlend() {
            BlendFuncSeparate(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha, BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha);
        }

        // ========== Front Face ==========

        public static void FrontFace(FrontFaceDirection direction) {
            if (_frontFace != direction) {
                _gl.FrontFace(direction);
                _frontFace = direction;
            }
        }

        public static void FrontFaceCCW() => FrontFace(FrontFaceDirection.Ccw);

        public static void FrontFaceCW() => FrontFace(FrontFaceDirection.CW);

        // ========== Depth ==========

        public static void DepthMask(bool enabled) {
            if (_depthMask != enabled) {
                _gl.DepthMask(enabled);
                _depthMask = enabled;
            }
        }

        public static void DepthFunc(DepthFunction func) {
            if (_depthFunc != func) {
                _gl.DepthFunc(func);
                _depthFunc = func;
            }
        }

        // ========== 便捷组合方法 ==========

        /// <summary>
        /// 设置背面剔除状态
        /// </summary>
        public static void SetCullFace(bool enabled, bool isNegativeScale = false) {
            if (enabled) {
                EnableCullFace();
                FrontFace(isNegativeScale ? FrontFaceDirection.CW : FrontFaceDirection.Ccw);
            }
            else {
                DisableCullFace();
            }
        }

        /// <summary>
        /// 设置透明材质混合模式
        /// </summary>
        public static void SetAlphaModeBlend() {
            EnableBlend();
            SetAlphaBlend();
        }

        /// <summary>
        /// 设置不透明渲染状态
        /// </summary>
        public static void SetOpaqueState() {
            DisableBlend();
            EnableDepthTest();
            DepthMask(true);
        }
    }
}