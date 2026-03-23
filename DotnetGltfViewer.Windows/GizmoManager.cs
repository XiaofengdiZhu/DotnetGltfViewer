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
        static Model _model;
        static bool _initialized = false;

        public static GizmoMode CurrentMode => _currentMode;
        public static Matrix4x4 ModelMatrix => _modelMatrix;
        public static bool UseSnap { get => _useSnap; set => _useSnap = value; }

        public static void Initialize(Camera camera, Model model) {
            _camera = camera;
            _model = model;
            _initialized = false;
            _userTransform = Matrix4x4.Identity;
        }

        static void InitializeModelMatrix() {
            if (_initialized || _model == null || _model.MeshInstances.Count == 0) {
                return;
            }
            
            Vector3 min = new(float.MaxValue);
            Vector3 max = new(float.MinValue);
            
            foreach (Model.MeshInstance instance in _model.MeshInstances) {
                Vector3 pos = new(instance.OriginalWorldMatrix.M41, 
                                  instance.OriginalWorldMatrix.M42, 
                                  instance.OriginalWorldMatrix.M43);
                min = Vector3.Min(min, pos);
                max = Vector3.Max(max, pos);
            }
            
            if (min.X != float.MaxValue) {
                Vector3 center = (min + max) * 0.5f;
                _modelMatrix = Matrix4x4.CreateTranslation(center);
            }
            
            _initialized = true;
        }

        public static void SetMode(GizmoMode mode) {
            _currentMode = mode;
        }

        public static void ResetModelMatrix() {
            _modelMatrix = Matrix4x4.Identity;
            _userTransform = Matrix4x4.Identity;
            _initialized = false;
            
            if (_model != null) {
                foreach (Model.MeshInstance instance in _model.MeshInstances) {
                    instance.ResetGizmoTransform();
                }
            }
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
                GizmoMode.Select => "Q",
                GizmoMode.Translate => "W",
                GizmoMode.Rotate => "E",
                GizmoMode.Scale => "R",
                _ => ""
            };
        }

        public static void HandleKeyboardShortcuts() {
            if (ImGuiManager.IO.WantCaptureKeyboard) {
                return;
            }

            if (ImGui.IsKeyPressed(ImGuiKey.Q)) _currentMode = GizmoMode.Select;
            else if (ImGui.IsKeyPressed(ImGuiKey.W)) _currentMode = GizmoMode.Translate;
            else if (ImGui.IsKeyPressed(ImGuiKey.E)) _currentMode = GizmoMode.Rotate;
            else if (ImGui.IsKeyPressed(ImGuiKey.R)) _currentMode = GizmoMode.Scale;
        }

        public static unsafe bool Manipulate(Matrix4x4 viewMatrix, Matrix4x4 projectionMatrix) {
            if (_currentMode == GizmoMode.Select || _model == null) {
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

            bool manipulated;
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

            manipulated = ImGuizmo.Manipulate(
                ref viewMatrix.M11,
                ref projectionMatrix.M11,
                operation,
                ImGuizmoMode.World,
                ref transform.M11,
                null,
                snapPtr
            );

            if (manipulated) {
                Matrix4x4.Invert(_modelMatrix, out Matrix4x4 invModelMatrix);
                _userTransform = transform * invModelMatrix;
                
                ApplyTransformToModel();
            }

            return manipulated;
        }

        static void ApplyTransformToModel() {
            if (_model == null) return;

            foreach (Model.MeshInstance instance in _model.MeshInstances) {
                instance.SetGizmoTransform(_userTransform);
            }
        }

        public static void Render(Matrix4x4 viewMatrix, Matrix4x4 projectionMatrix) {
            HandleKeyboardShortcuts();
            Manipulate(viewMatrix, projectionMatrix);
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