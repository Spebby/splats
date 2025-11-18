using Unity.Collections;
using UnityEngine;


namespace Gamba.Splats {
    public static class Splats {
        static ISplatsManager _splatsManager = new BasicSplatsMaker();
        
        internal static void Init(ISplatsManager manager, ISplatsConfig config) {
            _splatsManager = manager;
            manager.Init(config);
        }
        
        public static void Spawn(Vector3 position, Quaternion rotation, SplatParams @params) {
            _splatsManager.Spawn(position, rotation, @params);
        }

        public static void Spawn(Vector3 position, Quaternion? rotation) {
            _splatsManager.Spawn(position, rotation ?? Quaternion.identity, new SplatParams());
        }

        public static SplatHit Query(Vector3 position, float radius) {
            return _splatsManager.Query(position, radius);
        }

        public static NativeArray<SplatHit> Query(NativeArray<Vector3> positions, NativeArray<float> radii, Allocator allocator = Allocator.Temp) {
            return _splatsManager.Query(positions, radii, allocator);
        }
    }

    public interface ISplatsManager {
        // this is prolly bad
        internal void Init(ISplatsConfig conf);
        
        void Spawn(Vector3 position, Quaternion rotation, SplatParams @params);
        SplatHit Query(Vector3 position, float radius);
        NativeArray<SplatHit> Query(NativeArray<Vector3> positions, NativeArray<float> radii, Allocator allocator = Allocator.Temp);
    }
    
    public readonly struct SplatParams {
        public readonly float Size;
        public readonly float Lifetime;
        public readonly Vector2 Sheer;
        // temp
        public readonly GameObject Object;

        public SplatParams(GameObject obj, Vector2 sheer, float size = 1f, float lifetime = 10f) {
            Size = Mathf.Clamp(size, 0f, float.MaxValue);
            Lifetime = Mathf.Clamp(lifetime, 0f, float.MaxValue);
            Sheer = sheer;
            Object = obj;
        }

        public SplatParams(GameObject obj, float size = 1f, float lifetime = 10f) {
            Size     = Mathf.Clamp(size, 0, float.MaxValue);
            Lifetime = Mathf.Clamp(lifetime, 0f, float.MaxValue);
            Sheer    = new Vector2(1, 1);
            Object = obj;
        }
    }

    public readonly struct SplatHit {
        public readonly int ID;

        public SplatHit(int id) {
            ID = id;
        }
    }
   
    public interface ISplatsConfig {
        int BufferSize { get; }
    }
}