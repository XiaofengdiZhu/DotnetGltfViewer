using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json.Nodes;
using SharpGLTF.IO;
using SharpGLTF.Schema2;

namespace DotnetGltfRenderer {
    /// <summary>
    /// glTF 变体加载器
    /// </summary>
    public static class GltfVariantLoader {
        /// <summary>
        /// 从 ModelRoot 加载变体列表
        /// </summary>
        public static List<string> LoadVariants(ModelRoot modelRoot) {
            List<string> variants = new();

            object variantsExt = GetUnknownExtension(modelRoot, "KHR_materials_variants");
            if (variantsExt == null) {
                return variants;
            }

            JsonArray variantsArray = GetExtensionProperty(variantsExt, "variants") as JsonArray;
            if (variantsArray != null) {
                foreach (JsonNode variant in variantsArray) {
                    string name = variant?["name"]?.GetValue<string>();
                    if (!string.IsNullOrEmpty(name)) {
                        variants.Add(name);
                    }
                }
            }

            return variants;
        }

        /// <summary>
        /// 从 ModelRoot 加载变体材质映射
        /// </summary>
        public static Dictionary<(int MeshIndex, int PrimitiveIndex), Dictionary<int, int>> LoadVariantMappings(ModelRoot modelRoot) {
            Dictionary<(int MeshIndex, int PrimitiveIndex), Dictionary<int, int>> mappings = new();

            int meshIndex = 0;
            foreach (SharpGLTF.Schema2.Mesh mesh in modelRoot.LogicalMeshes) {
                int primitiveIndex = 0;
                foreach (MeshPrimitive primitive in mesh.Primitives) {
                    object primExt = GetUnknownExtension(primitive, "KHR_materials_variants");
                    if (primExt != null) {
                        JsonArray mappingsArray = GetExtensionProperty(primExt, "mappings") as JsonArray;
                        if (mappingsArray != null) {
                            Dictionary<int, int> mappingDict = new();
                            foreach (JsonNode mapping in mappingsArray) {
                                int? materialIdx = mapping?["material"]?.GetValue<int>();
                                JsonArray variantIndices = mapping?["variants"] as JsonArray;
                                if (materialIdx.HasValue && variantIndices != null) {
                                    foreach (JsonNode variantIdx in variantIndices) {
                                        int? vIdx = variantIdx?.GetValue<int>();
                                        if (vIdx.HasValue) {
                                            mappingDict[vIdx.Value] = materialIdx.Value;
                                        }
                                    }
                                }
                            }
                            if (mappingDict.Count > 0) {
                                mappings[(meshIndex, primitiveIndex)] = mappingDict;
                            }
                        }
                    }
                    primitiveIndex++;
                }
                meshIndex++;
            }

            return mappings;
        }

        /// <summary>
        /// 获取未知扩展（使用反射）
        /// </summary>
        public static object GetUnknownExtension(IExtraProperties target, string extensionName) {
            foreach (JsonSerializable ext in target.Extensions) {
                // Check if it's UnknownNode by type name (since it's internal)
                if (ext.GetType().FullName == "SharpGLTF.IO.UnknownNode") {
                    PropertyInfo nameProp = ext.GetType().GetProperty("Name", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (nameProp?.GetValue(ext)?.ToString() == extensionName) {
                        return ext;
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// 获取扩展属性
        /// </summary>
        public static object GetExtensionProperty(object unknownExtension, string propertyName) {
            Type extType = unknownExtension.GetType();
            PropertyInfo propsProp = extType.GetProperty("Properties", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            IReadOnlyDictionary<string, JsonNode> properties = propsProp?.GetValue(unknownExtension) as IReadOnlyDictionary<string, JsonNode>;
            if (properties != null && properties.TryGetValue(propertyName, out JsonNode value)) {
                return value;
            }
            return null;
        }
    }
}
