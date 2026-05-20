using System;
using System.Runtime.InteropServices;
using DotnetGltfRenderer;
using Silk.NET.OpenGLES;
using Silk.NET.OpenXR;
using Silk.NET.OpenXR.Extensions.KHR;
using ZLogger;

namespace DotnetGltfViewer.Windows.VR;

/// <summary>
/// OpenXR 生命周期与帧循环管理
/// </summary>
public unsafe class XrManager : IDisposable {
    XR _xr;
    Instance _instance;
    ulong _systemId;
    Session _session;
    Space _playSpace;
    KhrOpenglEnable _glExt;
    SessionState _sessionState;

    // Swapchains
    readonly Swapchain[] _swapchains = new Swapchain[2];
    uint[][] _swapchainImages = [[], []];
    uint[] _swapchainFbos = [0, 0];
    uint[] _swapchainDepthRbs = [0, 0];
    uint _acquiredImageIndex;

    // Frame state
    FrameState _frameState;
    View[] _views;
    CompositionLayerProjectionView[] _layerViews;

    // Config
    uint _swapchainWidth;
    uint _swapchainHeight;

    GL _gl;

    public bool IsRunning { get; private set; }
    public bool ShouldRender => _frameState.ShouldRender != 0;
    public uint SwapchainWidth => _swapchainWidth;
    public uint SwapchainHeight => _swapchainHeight;

    static void WriteFixedString(byte* dest, string src, int maxLen) {
        int len = Math.Min(src.Length, maxLen - 1);
        for (int i = 0; i < len; i++) {
            dest[i] = (byte)src[i];
        }
        dest[len] = 0;
    }

    static ulong MakeVersion(ushort major, ushort minor, ushort patch) =>
        ((ulong)major << 48) | ((ulong)minor << 32) | ((ulong)patch << 16);

    public void Initialize(nint hdc, nint hglrc, GL gl) {
        _gl = gl;
        _xr = XR.GetApi();

        // 1. Check extension
        if (!_xr.IsInstanceExtensionPresent(null, "XR_KHR_opengl_enable")) {
            LogManager.Logger.ZLogError($"XR_KHR_opengl_enable extension not available");
            return;
        }

        // 2. Create Instance
        byte* extName = (byte*)Marshal.StringToHGlobalAnsi("XR_KHR_opengl_enable");
        try {
            ApplicationInfo appInfo = new() {
                ApplicationVersion = 1,
                EngineVersion = 1,
                ApiVersion = MakeVersion(1, 0, 0)
            };
            WriteFixedString(appInfo.ApplicationName, "DotnetGltfViewer", 128);
            WriteFixedString(appInfo.EngineName, "DotnetGltfViewer", 128);

            InstanceCreateInfo createInfo = new() {
                Type = StructureType.InstanceCreateInfo,
                ApplicationInfo = appInfo,
                EnabledExtensionCount = 1,
                EnabledExtensionNames = &extName
            };

            Result result = _xr.CreateInstance(ref createInfo, ref _instance);
            if (result != Result.Success) {
                LogManager.Logger.ZLogError($"xrCreateInstance failed: {result}");
                return;
            }
        }
        finally {
            Marshal.FreeHGlobal((nint)extName);
        }

        // 3. Get System
        SystemGetInfo systemInfo = new() {
            Type = StructureType.SystemGetInfo,
            FormFactor = FormFactor.HeadMountedDisplay
        };
        {
            Result result = _xr.GetSystem(_instance, ref systemInfo, ref _systemId);
            if (result != Result.Success) {
                LogManager.Logger.ZLogError($"xrGetSystem failed: {result}");
                return;
            }
        }

        // 4. Load OpenGL extension + check requirements
        if (!_xr.TryGetInstanceExtension<KhrOpenglEnable>(null, _instance, out _glExt)) {
            LogManager.Logger.ZLogError($"Failed to load XR_KHR_opengl_enable extension");
            return;
        }

        GraphicsRequirementsOpenGLKHR glReqs = new() {
            Type = StructureType.GraphicsRequirementsOpenglKhr
        };
        {
            Result result = _glExt.GetOpenGlgraphicsRequirements(_instance, _systemId, ref glReqs);
            if (result != Result.Success) {
                LogManager.Logger.ZLogError($"GetOpenGLGraphicsRequirements failed: {result}");
                return;
            }
        }

        // 5. Enumerate view configurations
        ViewConfigurationView[] configViews = new ViewConfigurationView[2];
        for (int i = 0; i < 2; i++) {
            configViews[i] = new() { Type = StructureType.ViewConfigurationView };
        }
        {
            uint viewCount = 2;
            _xr.EnumerateViewConfigurationView(
                _instance, _systemId,
                ViewConfigurationType.PrimaryStereo,
                viewCount, ref viewCount,
                ref configViews[0]);
            _swapchainWidth = configViews[0].RecommendedImageRectWidth;
            _swapchainHeight = configViews[0].RecommendedImageRectHeight;
        }

        LogManager.Logger.ZLogInformation($"OpenXR swapchain size: {_swapchainWidth}x{_swapchainHeight}");

        // 6. Create Session
        {
            GraphicsBindingOpenGLWin32KHR graphicsBinding = new() {
                Type = StructureType.GraphicsBindingOpenglWin32Khr,
                HDC = hdc,
                HGlrc = hglrc
            };

            SessionCreateInfo sessionCreateInfo = new() {
                Type = StructureType.SessionCreateInfo,
                Next = &graphicsBinding,
                SystemId = _systemId
            };

            Result result = _xr.CreateSession(_instance, ref sessionCreateInfo, ref _session);
            if (result != Result.Success) {
                LogManager.Logger.ZLogError($"xrCreateSession failed: {result}");
                return;
            }
        }

        // 7. Create reference space
        ReferenceSpaceCreateInfo spaceInfo = new() {
            Type = StructureType.ReferenceSpaceCreateInfo,
            ReferenceSpaceType = ReferenceSpaceType.Local,
            PoseInReferenceSpace = new() {
                Orientation = new() { X = 0, Y = 0, Z = 0, W = 1 },
                Position = new() { X = 0, Y = 0, Z = 0 }
            }
        };
        {
            Result result = _xr.CreateReferenceSpace(_session, ref spaceInfo, ref _playSpace);
            if (result != Result.Success) {
                LogManager.Logger.ZLogError($"xrCreateReferenceSpace failed: {result}");
                return;
            }
        }

        // 8. Create swapchains
        for (int eye = 0; eye < 2; eye++) {
            CreateSwapchain(eye);
        }

        // Init view arrays
        _views = new View[2];
        _layerViews = new CompositionLayerProjectionView[2];
        for (int i = 0; i < 2; i++) {
            _views[i] = new() { Type = StructureType.View };
            _layerViews[i] = new() { Type = StructureType.CompositionLayerProjectionView };
        }

        _sessionState = SessionState.Idle;
        IsRunning = true;
        LogManager.Logger.ZLogInformation($"OpenXR initialized successfully");
    }

    void CreateSwapchain(int eye) {
        SwapchainCreateInfo swapchainInfo = new() {
            Type = StructureType.SwapchainCreateInfo,
            UsageFlags = SwapchainUsageFlags.ColorAttachmentBit | SwapchainUsageFlags.SampledBit,
            Format = 0x8058, // GL_RGBA8
            SampleCount = 1,
            Width = _swapchainWidth,
            Height = _swapchainHeight,
            FaceCount = 1,
            ArraySize = 1,
            MipCount = 1
        };

        Result result = _xr.CreateSwapchain(_session, ref swapchainInfo, ref _swapchains[eye]);
        if (result != Result.Success) {
            LogManager.Logger.ZLogError($"xrCreateSwapchain failed for eye {eye}: {result}");
            return;
        }

        // Enumerate swapchain images
        uint imageCount = 0;
        _xr.EnumerateSwapchainImages(_swapchains[eye], 0, ref imageCount, ref *(SwapchainImageBaseHeader*)null);

        SwapchainImageOpenGLKHR[] images = new SwapchainImageOpenGLKHR[imageCount];
        for (int i = 0; i < imageCount; i++) {
            images[i] = new() { Type = StructureType.SwapchainImageOpenglKhr };
        }
        fixed (SwapchainImageOpenGLKHR* pImages = images) {
            _xr.EnumerateSwapchainImages(_swapchains[eye], imageCount, ref imageCount, ref *(SwapchainImageBaseHeader*)pImages);
        }

        _swapchainImages[eye] = new uint[imageCount];
        for (int i = 0; i < imageCount; i++) {
            _swapchainImages[eye][i] = images[i].Image;
        }

        // Create FBO for this eye
        (_swapchainFbos[eye], _swapchainDepthRbs[eye]) = CreateFBO(_swapchainImages[eye], _swapchainWidth, _swapchainHeight);
    }

    (uint fbo, uint depthRb) CreateFBO(uint[] images, uint width, uint height) {
        uint fbo;
        _gl.GenFramebuffers(1, out fbo);
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, fbo);

        uint depthRb;
        _gl.GenRenderbuffers(1, out depthRb);
        _gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, depthRb);
        _gl.RenderbufferStorage(RenderbufferTarget.Renderbuffer, InternalFormat.DepthComponent24, width, height);
        _gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, RenderbufferTarget.Renderbuffer, depthRb);

        _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, images[0], 0);

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        return (fbo, depthRb);
    }

    public void PollEvents() {
        if (!IsRunning) return;

        EventDataBuffer eventData = new() { Type = StructureType.EventDataBuffer };
        while (_xr.PollEvent(_instance, ref eventData) == Result.Success) {
            if (eventData.Type == StructureType.EventDataSessionStateChanged) {
                EventDataSessionStateChanged* sessionEvent = (EventDataSessionStateChanged*)&eventData;
                _sessionState = sessionEvent->State;
                HandleSessionStateChange();
            }
        }
    }

    void HandleSessionStateChange() {
        LogManager.Logger.ZLogDebug($"OpenXR session state: {_sessionState}");

        switch (_sessionState) {
            case SessionState.Ready:
                BeginSession();
                break;
            case SessionState.Stopping:
                EndSession();
                break;
            case SessionState.LossPending:
            case SessionState.Exiting:
                IsRunning = false;
                break;
        }
    }

    void BeginSession() {
        SessionBeginInfo beginInfo = new() {
            Type = StructureType.SessionBeginInfo,
            PrimaryViewConfigurationType = ViewConfigurationType.PrimaryStereo
        };
        _xr.BeginSession(_session, ref beginInfo);
    }

    void EndSession() {
        _xr.EndSession(_session);
    }

    /// <summary>
    /// 开始一帧（WaitFrame → BeginFrame → LocateViews）
    /// </summary>
    public bool BeginFrame() {
        if (!IsRunning) return false;

        FrameWaitInfo waitInfo = new() { Type = StructureType.FrameWaitInfo };
        _frameState = new() { Type = StructureType.FrameState };
        Result result = _xr.WaitFrame(_session, ref waitInfo, ref _frameState);
        if (result != Result.Success) return false;

        FrameBeginInfo beginInfo = new() { Type = StructureType.FrameBeginInfo };
        _xr.BeginFrame(_session, ref beginInfo);

        if (_frameState.ShouldRender == 0) return true;

        // Locate views
        ViewLocateInfo viewLocateInfo = new() {
            Type = StructureType.ViewLocateInfo,
            ViewConfigurationType = ViewConfigurationType.PrimaryStereo,
            DisplayTime = _frameState.PredictedDisplayTime,
            Space = _playSpace
        };

        ViewState viewState = new() { Type = StructureType.ViewState };
        uint viewCount = 2;
        _xr.LocateView(_session, ref viewLocateInfo, ref viewState, viewCount, ref viewCount, ref _views[0]);

        return true;
    }

    /// <summary>
    /// 获取指定眼睛的 FBO 和 View
    /// </summary>
    public (uint fbo, View view) AcquireEye(int eyeIndex) {
        SwapchainImageAcquireInfo acquireInfo = new() { Type = StructureType.SwapchainImageAcquireInfo };
        _xr.AcquireSwapchainImage(_swapchains[eyeIndex], ref acquireInfo, ref _acquiredImageIndex);

        SwapchainImageWaitInfo waitInfo = new() {
            Type = StructureType.SwapchainImageWaitInfo,
            Timeout = 1000000000
        };
        _xr.WaitSwapchainImage(_swapchains[eyeIndex], ref waitInfo);

        // Re-attach the acquired texture to the FBO
        uint texture = _swapchainImages[eyeIndex][_acquiredImageIndex];
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _swapchainFbos[eyeIndex]);
        _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, texture, 0);

        return (_swapchainFbos[eyeIndex], _views[eyeIndex]);
    }

    /// <summary>
    /// 释放指定眼睛的 swapchain image
    /// </summary>
    public void ReleaseEye(int eyeIndex) {
        SwapchainImageReleaseInfo releaseInfo = new() { Type = StructureType.SwapchainImageReleaseInfo };
        _xr.ReleaseSwapchainImage(_swapchains[eyeIndex], ref releaseInfo);
    }

    /// <summary>
    /// 结束一帧，提交投影层
    /// </summary>
    public void EndFrame() {
        if (!IsRunning) return;

        if (_frameState.ShouldRender != 0) {
            for (int i = 0; i < 2; i++) {
                _layerViews[i].Pose = _views[i].Pose;
                _layerViews[i].Fov = _views[i].Fov;
                _layerViews[i].SubImage = new() {
                    Swapchain = _swapchains[i],
                    ImageRect = new() {
                        Offset = new() { X = 0, Y = 0 },
                        Extent = new() { Width = (int)_swapchainWidth, Height = (int)_swapchainHeight }
                    },
                    ImageArrayIndex = 0
                };
            }

            CompositionLayerProjection layer = new() {
                Type = StructureType.CompositionLayerProjection,
                Space = _playSpace,
                ViewCount = 2
            };

            // fixed 块必须覆盖 EndFrame 调用，防止 GC 移动托管数组
            fixed (CompositionLayerProjectionView* pViews = _layerViews) {
                layer.Views = pViews;
                CompositionLayerBaseHeader* pLayer = (CompositionLayerBaseHeader*)&layer;
                FrameEndInfo endInfo = new() {
                    Type = StructureType.FrameEndInfo,
                    DisplayTime = _frameState.PredictedDisplayTime,
                    EnvironmentBlendMode = EnvironmentBlendMode.Opaque,
                    LayerCount = 1,
                    Layers = &pLayer
                };
                _xr.EndFrame(_session, ref endInfo);
            }
        }
        else {
            FrameEndInfo endInfo = new() {
                Type = StructureType.FrameEndInfo,
                DisplayTime = _frameState.PredictedDisplayTime,
                EnvironmentBlendMode = EnvironmentBlendMode.Opaque,
                LayerCount = 0,
                Layers = null
            };
            _xr.EndFrame(_session, ref endInfo);
        }
    }

    public void Dispose() {
        for (int i = 0; i < 2; i++) {
            if (_swapchains[i].Handle != 0) _xr.DestroySwapchain(_swapchains[i]);
            if (_swapchainFbos[i] != 0) _gl.DeleteFramebuffer(_swapchainFbos[i]);
            if (_swapchainDepthRbs[i] != 0) _gl.DeleteRenderbuffer(_swapchainDepthRbs[i]);
        }

        if (_playSpace.Handle != 0) { _xr.DestroySpace(_playSpace); _playSpace = default; }
        if (_session.Handle != 0) { _xr.DestroySession(_session); _session = default; }
        if (_instance.Handle != 0) { _xr.DestroyInstance(_instance); _instance = default; }

        _glExt?.Dispose();
        _xr?.Dispose();
        IsRunning = false;
    }
}
