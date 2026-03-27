using System;
using System.Collections.Generic;

namespace DotnetGltfRenderer {
    public class InstancingBatchManager : IDisposable {
        readonly Dictionary<(Mesh, Material), DynamicInstancingBatch> _batchesByKey = new();
        readonly List<DynamicInstancingBatch> _allBatches = new();
        bool _disposed;

        public IReadOnlyList<DynamicInstancingBatch> AllBatches => _allBatches;

        public void Clear() {
            for (int i = _allBatches.Count - 1; i >= 0; i--) {
                _allBatches[i].Clear();
            }
        }

        public void Dispose() {
            if (_disposed) {
                return;
            }
            foreach (DynamicInstancingBatch batch in _allBatches) {
                batch.Dispose();
            }
            _batchesByKey.Clear();
            _allBatches.Clear();
            _disposed = true;
        }

        public DynamicInstancingBatch FindOrCreateBatch(Mesh mesh, Material material) {
            (Mesh, Material) key = (mesh, material);

            if (_batchesByKey.TryGetValue(key, out DynamicInstancingBatch existing)) {
                if (existing.IsEmpty || existing.CanAddInstance) {
                    return existing;
                }
            }

            DynamicInstancingBatch newBatch = new(mesh);
            _batchesByKey[key] = newBatch;
            _allBatches.Add(newBatch);
            return newBatch;
        }

        public static bool CanBatch(MeshInstance instance) {
            if (instance.HasSkinning) {
                return false;
            }
            if (instance.Mesh.HasMorphTargets) {
                return false;
            }
            if (instance.Mesh.UseInstancing) {
                return false;
            }
            return true;
        }
    }
}
