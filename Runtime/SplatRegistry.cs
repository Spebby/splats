using System.Collections.Generic;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;


namespace Splats {
    public static class SplatsRegistry {
        static Dictionary<uint, ISplatSettings> _confById;
        static bool _isLoading;
        static AsyncOperationHandle<IList<ISplatSettings>> _loadOp;

        public static bool IsReady => _confById != null;

        public static ISplatSettings Get(uint id) {
            EnsureLoaded();
            return _confById.GetValueOrDefault(id);
        }

        static void EnsureLoaded() {
            if (_confById != null || _isLoading) return;

            _isLoading = true;

            // "Defs" is an Addressables label pointing to all your Def ScriptableObjects
            _loadOp           =  Addressables.LoadAssetsAsync<ISplatSettings>("SplatSettings");
            _loadOp.Completed += OnLoaded;
        }

        static void OnLoaded(AsyncOperationHandle<IList<ISplatSettings>> obj) {
            _confById = new Dictionary<uint, ISplatSettings>();
            foreach (ISplatSettings def in obj.Result) _confById[def.ID] = def;
            _isLoading = false;
        }
    }
}