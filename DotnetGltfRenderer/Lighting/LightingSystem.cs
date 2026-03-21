using System;
using System.Collections.Generic;

namespace DotnetGltfRenderer {
    /// <summary>
    /// 光照系统，管理场景中的所有光源和环境光
    /// </summary>
    public class LightingSystem {
        readonly List<Light> _lights = new();

        /// <summary>
        /// 最大光源数量
        /// </summary>
        public const int MaxLightCount = 8;

        /// <summary>
        /// 场景中的光源列表
        /// </summary>
        public IReadOnlyList<Light> Lights => _lights;

        /// <summary>
        /// 曝光度
        /// </summary>
        public float Exposure { get; set; } = 1.0f;

        /// <summary>
        /// 环境贴图强度（通过 SceneData UBO 传入着色器）
        /// </summary>
        public float EnvironmentStrength { get; set; } = 1.0f;

        /// <summary>
        /// 添加光源
        /// </summary>
        public void AddLight(Light light) {
            if (_lights.Count < MaxLightCount) {
                _lights.Add(light);
            }
        }

        /// <summary>
        /// 移除光源
        /// </summary>
        public void RemoveLight(Light light) {
            _lights.Remove(light);
        }

        /// <summary>
        /// 清除所有光源
        /// </summary>
        public void ClearLights() {
            _lights.Clear();
        }

        /// <summary>
        /// 获取光源数据用于 UBO
        /// </summary>
        public LightsData GetLightsData() {
            LightsData data = new() { LightCount = Math.Min(_lights.Count, MaxLightCount), Pad0 = 0, Pad1 = 0, Pad2 = 0 };

            // 填充光源数据
            if (_lights.Count > 0) {
                data.Light0 = ConvertLight(_lights[0]);
            }
            if (_lights.Count > 1) {
                data.Light1 = ConvertLight(_lights[1]);
            }
            if (_lights.Count > 2) {
                data.Light2 = ConvertLight(_lights[2]);
            }
            if (_lights.Count > 3) {
                data.Light3 = ConvertLight(_lights[3]);
            }
            if (_lights.Count > 4) {
                data.Light4 = ConvertLight(_lights[4]);
            }
            if (_lights.Count > 5) {
                data.Light5 = ConvertLight(_lights[5]);
            }
            if (_lights.Count > 6) {
                data.Light6 = ConvertLight(_lights[6]);
            }
            if (_lights.Count > 7) {
                data.Light7 = ConvertLight(_lights[7]);
            }
            return data;
        }

        static LightData ConvertLight(Light light) {
            // 将角度转换为余弦值
            float innerConeCos = MathF.Cos(light.InnerConeAngle * MathF.PI / 180f);
            float outerConeCos = MathF.Cos(light.OuterConeAngle * MathF.PI / 180f);
            return new LightData {
                Direction = light.Direction,
                Range = light.Range,
                Color = light.Color,
                Intensity = light.Intensity,
                Position = light.Position,
                InnerConeCos = innerConeCos,
                OuterConeCos = outerConeCos,
                Type = (int)light.Type,
                Pad0 = 0f,
                Pad1 = 0f
            };
        }
    }
}