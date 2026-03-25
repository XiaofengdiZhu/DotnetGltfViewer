using System;
using System.Numerics;
using DotnetGltfRenderer;
using Hexa.NET.ImGui;
using Hexa.NET.ImGuizmo;

namespace DotnetGltfViewer.Windows {
    public enum GizmoMode {
        Select,
        Translate,
        Rotate,
        Scale
    }

    public static class GizmoManager {
        static GizmoMode _currentMode = GizmoMode.Select;
        static Matrix4x4 _modelMatrix = Matrix4x4.Identity;
        static Matrix4x4 _userTransform = Matrix4x4.Identity;
        static bool _useSnap = false;
        static Vector3 _snapTranslate = new(0.5f, 0.5f, 0.5f);
        static float _snapRotate = 5f;
        static float _snapScale = 0.1f;
        static Camera _camera;
        static Scene _scene;
        static bool _initialized = false;
        // 缓存原始局部包围盒（相对于模型中心）
        static Vector3 _cachedLocalMin;
        static Vector3 _cachedLocalMax;

        public static GizmoMode CurrentMode => _currentMode;
        public static Matrix4x4 ModelMatrix => _modelMatrix;
        public static bool UseSnap { get => _useSnap; set => _useSnap = value; }

        public static void Initialize(Camera camera, Scene scene) {
            _camera = camera;
            _scene = scene;
            _initialized = false;
            _userTransform = Matrix4x4.Identity;

            // 监听选择变化
            SelectionManager.OnSelectionChanged += OnSelectionChanged;

            // 如果场景只有一个模型，自动选中
            if (_scene != null && _scene.Models.Count == 1) {
                _scene.SelectModel(_scene.Models[0]);
            }
        }

        /// <summary>
        /// 选择变化时重置 Gizmo 状态
        /// </summary>
        static void OnSelectionChanged(SceneModel model, MeshInstance instance) {
            // 重置 Gizmo 状态
            _initialized = false;
            _userTransform = Matrix4x4.Identity;
            _modelMatrix = Matrix4x4.Identity;
        }

        static void InitializeModelMatrix() {
            if (_initialized) {
                return;
            }

            SceneModel selectedModel = _scene?.SelectedModel;
            if (selectedModel?.Model == null || selectedModel.Model.MeshInstances.Count == 0) {
                return;
            }

            // 从第一个 MeshInstance 恢复变换状态（如果有的话）
            MeshInstance firstInstance = selectedModel.Model.MeshInstances[0];
            _userTransform = firstInstance.GetGizmoTransform();

            // 基于原始世界矩阵计算包围盒（不包含用户变换）
            Vector3 min = new(float.MaxValue);
            Vector3 max = new(float.MinValue);

            foreach (MeshInstance instance in selectedModel.Model.MeshInstances) {
                if (!instance.IsVisible) continue;

                BoundingBox localBounds = instance.Mesh.LocalBounds;
                if (!localBounds.IsValid) continue;

                // 使用原始世界矩阵变换包围盒
                BoundingBox worldBounds = BoundingBox.Transform(localBounds, instance.OriginalWorldMatrix);
                min = Vector3.Min(min, worldBounds.Min);
                max = Vector3.Max(max, worldBounds.Max);
            }

            if (min.X != float.MaxValue) {
                Vector3 center = (min + max) * 0.5f;
                _modelMatrix = Matrix4x4.CreateTranslation(center);

                // 缓存原始局部包围盒（相对于原始中心）
                _cachedLocalMin = min - center;
                _cachedLocalMax = max - center;
            }

            _initialized = true;
        }

        public static void SetMode(GizmoMode mode) {
            // 如果切换到变换模式但没有选中模型，且场景中只有一个模型，自动选中
            if (mode != GizmoMode.Select && _scene?.SelectedModel == null && _scene?.Models.Count == 1) {
                _scene.SelectModel(_scene.Models[0]);
                _initialized = false;
            }
            _currentMode = mode;
        }

        public static void ResetModelMatrix() {
            _userTransform = Matrix4x4.Identity;
            _initialized = false;

            // 重置选中模型的变换
            SceneModel selectedModel = _scene?.SelectedModel;
            if (selectedModel?.Model != null) {
                foreach (MeshInstance instance in selectedModel.Model.MeshInstances) {
                    instance.ResetGizmoTransform();
                }
                // 立即更新包围盒
                selectedModel.UpdateWorldBounds();
            }

            // 立即重新初始化模型矩阵
            InitializeModelMatrix();
        }

        public static void Update() {
            ImGuizmo.BeginFrame();
        }

        public static void RenderToolbar() {
            float buttonSize = 36f;
            float padding = 8f;
            float windowHeight = buttonSize * 4 + padding * 5;

            ImGui.SetNextWindowPos(new Vector2(padding, padding + 24f));
            ImGui.SetNextWindowSize(new Vector2(buttonSize + padding * 2, windowHeight));
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(padding, padding));
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0, padding));

            ImGuiWindowFlags flags = ImGuiWindowFlags.NoTitleBar |
                                     ImGuiWindowFlags.NoResize |
                                     ImGuiWindowFlags.NoMove |
                                     ImGuiWindowFlags.NoScrollbar |
                                     ImGuiWindowFlags.NoScrollWithMouse |
                                     ImGuiWindowFlags.NoCollapse |
                                     ImGuiWindowFlags.NoBringToFrontOnFocus;

            if (ImGui.Begin("GizmoToolbar", flags)) {
                RenderToolbarButton("Select", GizmoMode.Select, buttonSize);
                RenderToolbarButton("Move", GizmoMode.Translate, buttonSize);
                RenderToolbarButton("Rotate", GizmoMode.Rotate, buttonSize);
                RenderToolbarButton("Scale", GizmoMode.Scale, buttonSize);
            }
            ImGui.End();

            ImGui.PopStyleVar(2);
        }

        static void RenderToolbarButton(string label, GizmoMode mode, float size) {
            bool isActive = _currentMode == mode;
            if (isActive) {
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.28f, 0.56f, 0.88f, 1.0f));
            }

            string icon = mode switch {
                GizmoMode.Select => "V",
                GizmoMode.Translate => "M",
                GizmoMode.Rotate => "R",
                GizmoMode.Scale => "S",
                _ => "?"
            };

            if (ImGui.Button(icon, new Vector2(size, size))) {
                _currentMode = mode;
            }

            if (ImGui.IsItemHovered()) {
                ImGui.SetTooltip($"{label} ({GetShortcut(mode)})");
            }

            if (isActive) {
                ImGui.PopStyleColor();
            }
        }

        static string GetShortcut(GizmoMode mode) {
            return mode switch {
                GizmoMode.Select => "Num 1",
                GizmoMode.Translate => "Num 2",
                GizmoMode.Rotate => "Num 3",
                GizmoMode.Scale => "Num 4",
                _ => ""
            };
        }

        public static void HandleKeyboardShortcuts() {
            if (ImGuiManager.IO.WantCaptureKeyboard) {
                return;
            }

            if (ImGui.IsKeyPressed(ImGuiKey.Keypad1)) _currentMode = GizmoMode.Select;
            else if (ImGui.IsKeyPressed(ImGuiKey.Keypad2)) _currentMode = GizmoMode.Translate;
            else if (ImGui.IsKeyPressed(ImGuiKey.Keypad3)) _currentMode = GizmoMode.Rotate;
            else if (ImGui.IsKeyPressed(ImGuiKey.Keypad4)) _currentMode = GizmoMode.Scale;
        }

        public static unsafe bool Manipulate(Matrix4x4 viewMatrix, Matrix4x4 projectionMatrix) {
            // Select 模式不显示变换 Gizmo
            if (_currentMode == GizmoMode.Select) {
                return false;
            }

            // 检查是否有选中的模型
            SceneModel selectedModel = _scene?.SelectedModel;
            if (selectedModel?.Model == null) {
                return false;
            }

            InitializeModelMatrix();

            ImGuizmo.SetDrawlist(ImGui.GetBackgroundDrawList());
            ImGuizmo.Enable(true);
            ImGuizmo.SetOrthographic(false);
            ImGuizmo.SetRect(0, 0, MainWindow.Size.X, MainWindow.Size.Y);

            ImGuizmoOperation operation = _currentMode switch {
                GizmoMode.Translate => ImGuizmoOperation.Translate,
                GizmoMode.Rotate => ImGuizmoOperation.Rotate,
                GizmoMode.Scale => ImGuizmoOperation.Scale,
                _ => ImGuizmoOperation.Translate
            };

            Matrix4x4 transform = _userTransform * _modelMatrix;

            // 使用缓存的原始局部包围盒
            // localBounds: [min.x, min.y, min.z, max.x, max.y, max.z]
            float* localBounds = stackalloc float[6];
            localBounds[0] = _cachedLocalMin.X;
            localBounds[1] = _cachedLocalMin.Y;
            localBounds[2] = _cachedLocalMin.Z;
            localBounds[3] = _cachedLocalMax.X;
            localBounds[4] = _cachedLocalMax.Y;
            localBounds[5] = _cachedLocalMax.Z;

            float snapRotate = _snapRotate;
            float snapScale = _snapScale;
            Vector3 snapTranslate = _snapTranslate;

            float* snapPtr = null;
            if (_useSnap) {
                snapPtr = _currentMode switch {
                    GizmoMode.Translate => &snapTranslate.X,
                    GizmoMode.Rotate => &snapRotate,
                    GizmoMode.Scale => &snapScale,
                    _ => null
                };
            }

            bool manipulated = ImGuizmo.Manipulate(
                ref viewMatrix.M11,
                ref projectionMatrix.M11,
                operation,
                ImGuizmoMode.World,
                ref transform.M11,
                null,
                snapPtr,
                localBounds
            );

            if (manipulated) {
                Matrix4x4.Invert(_modelMatrix, out Matrix4x4 invModelMatrix);
                _userTransform = transform * invModelMatrix;

                ApplyTransformToModel();
            }

            return manipulated;
        }

        static void ApplyTransformToModel() {
            SceneModel selectedModel = _scene?.SelectedModel;
            if (selectedModel?.Model == null) return;

            // 只对选中模型的 MeshInstance 应用变换
            foreach (MeshInstance instance in selectedModel.Model.MeshInstances) {
                instance.SetGizmoTransform(_userTransform);
            }
        }

        public static void Render(Matrix4x4 viewMatrix, Matrix4x4 projectionMatrix) {
            HandleKeyboardShortcuts();

            // 绘制选中模型的包围盒高亮
            DrawSelectionBounds(viewMatrix, projectionMatrix);

            // 处理变换操作
            Manipulate(viewMatrix, projectionMatrix);
        }

        /// <summary>
        /// 绘制选中模型的包围盒高亮（使用 ImGuizmoOperation.Bounds）
        /// </summary>
        static unsafe void DrawSelectionBounds(Matrix4x4 viewMatrix, Matrix4x4 projectionMatrix) {
            SceneModel selectedModel = _scene?.SelectedModel;
            if (selectedModel?.Model == null) {
                return;
            }

            InitializeModelMatrix();

            // 检查缓存的包围盒是否有效
            if (_cachedLocalMin.X > _cachedLocalMax.X) {
                return;
            }

            ImGuizmo.SetDrawlist(ImGui.GetBackgroundDrawList());
            ImGuizmo.Enable(true);
            ImGuizmo.SetOrthographic(false);
            ImGuizmo.SetRect(0, 0, MainWindow.Size.X, MainWindow.Size.Y);

            // 使用缓存的原始局部包围盒
            // localBounds: [min.x, min.y, min.z, max.x, max.y, max.z]
            float* localBounds = stackalloc float[6];
            localBounds[0] = _cachedLocalMin.X;
            localBounds[1] = _cachedLocalMin.Y;
            localBounds[2] = _cachedLocalMin.Z;
            localBounds[3] = _cachedLocalMax.X;
            localBounds[4] = _cachedLocalMax.Y;
            localBounds[5] = _cachedLocalMax.Z;

            // 使用 Bounds 操作绘制包围盒
            Matrix4x4 transform = _userTransform * _modelMatrix;
            ImGuizmo.Manipulate(
                ref viewMatrix.M11,
                ref projectionMatrix.M11,
                ImGuizmoOperation.Bounds,
                ImGuizmoMode.World,
                ref transform.M11,
                null,
                null,
                localBounds
            );
        }

        public static bool IsOver() => ImGuizmo.IsOver();
        public static bool IsUsing() => ImGuizmo.IsUsing();

        public static void RenderOptionsPanel() {
            ImGui.SeparatorText("Gizmo Options");

            ImGui.Checkbox("Enable Snap", ref _useSnap);

            if (_useSnap) {
                switch (_currentMode) {
                    case GizmoMode.Translate:
                        ImGui.DragFloat3("Snap (Translate)", ref _snapTranslate, 0.1f, 0.1f, 10f);
                        break;
                    case GizmoMode.Rotate:
                        ImGui.DragFloat("Snap (Rotate)", ref _snapRotate, 1f, 1f, 45f);
                        break;
                    case GizmoMode.Scale:
                        ImGui.DragFloat("Snap (Scale)", ref _snapScale, 0.01f, 0.01f, 1f);
                        break;
                }
            }

            if (ImGui.Button("Reset Transform")) {
                ResetModelMatrix();
            }
        }

        public static void Dispose() {
            ImGuizmo.SetImGuiContext(null);
        }
    }
}