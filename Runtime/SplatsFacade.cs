using System;
using Splats.TextureChunks;
using Unity.Collections;
using UnityEngine;


namespace Splats {
    public static class SplatsMan {
        static ISplatsManager _splatsManager = new BasicSplatsMaker();
        
        public static Action<uint, Vector2> OnSplat;
        
        // make internal at some point or only accessible through controled bootstrapper
        public static void Init(ISplatsManager manager, ISplatsConfig config) {
            _splatsManager = manager;
            manager.Init(config);
        }

        // this is not ideal but is acceptable for now
        // Doing this b/c classes don't auto destruct between scene loads in Unity. So until I figure out
        // how to properly do this, we'll have to go with this for now.
        public static void Denit() {
            _splatsManager = null;
            OnSplat        = null;
        }
        
        public static void Spawn(Vector2 position, Quaternion rotation, SplatParams @params) {
            _splatsManager.Spawn(position, rotation, @params);
            OnSplat?.Invoke(@params.ID, position);
        }

        public static void Edit(Vector2 pos, SplatEditData edit) {
            
        }

        public static void RequestQuery(Vector2 position, Action<uint> onComplete) {
            throw new System.NotImplementedException();
        }

        public static void RequestQuerySplatEdge(Vector2 position, Action<uint, NativeArray<Vector2>> OnComplete) {
            throw new System.NotImplementedException();
        }
        
        public static SplatHit Query(Vector2 position, float radius) {
            return _splatsManager.Query(position, radius);
        }

        public static NativeArray<SplatHit> Query(NativeArray<Vector2> positions, NativeArray<float> radii, Allocator allocator = Allocator.Temp) {
            return _splatsManager.Query(positions, radii, allocator);
        }
        
        /*
        public static JobHandle Query(NativeArray<Vector3> positions, NativeArray<float> radii, out NativeArray<SplatHit> hits, Allocator allocator = Allocator.Temp) {
            //return _splatsManager.Query(positions, radii, allocator);
            return null;
            //return _splatsManager.Query(positions, radii, allocator);
        }
        */
    }

    public enum SplatQueryType {
        
    }

    public interface ISplatsManager {
        // this is prolly bad
        internal void Init(ISplatsConfig conf);
        
        void Spawn(Vector2 position, Quaternion rotation, SplatParams @params);
        SplatHit Query(Vector2 position, float radius);
        NativeArray<SplatHit> Query(NativeArray<Vector2> positions, NativeArray<float> radii, Allocator allocator = Allocator.Temp);
    }
    
    public readonly struct SplatParams {
        public readonly float Size;
        public readonly float Lifetime;
        public readonly Vector2 Sheer;
        public readonly uint ID;

        public SplatParams(GameObject obj, Vector2 sheer, float size = 1f, float lifetime = 10f) {
            Size = Mathf.Clamp(size, 0f, float.MaxValue);
            Lifetime = Mathf.Clamp(lifetime, 0f, float.MaxValue);
            Sheer = sheer;

            ID = 0;
        }

        public SplatParams(GameObject obj, float size = 1f, float lifetime = 10f) {
            Size     = Mathf.Clamp(size, 0, float.MaxValue);
            Lifetime = Mathf.Clamp(lifetime, 0f, float.MaxValue);
            Sheer    = new Vector2(1, 1);

            ID = 0;
        }
    }

 
    public readonly struct SplatEditData {
        public readonly Type EditType;
        public readonly uint SourceID;
        public readonly uint TargetID;
        public readonly float Radius;
        
        
        public SplatEditData(Type type, uint sourceID, uint targetID, float radius = 0.25f) {
            EditType = type;
            SourceID = sourceID;
            TargetID = targetID;
            Radius = radius;
        }


        public enum Type {
            Remove,
            Replace
        }
    }

    public readonly struct SplatHit {
        public readonly uint ID;
        public readonly float Lifetime;

        public SplatHit(uint id, float lifetime = 0) {
            ID = id;
            Lifetime = lifetime;
        }
    }
   
    public interface ISplatsConfig {
        int BufferSize { get; }
        int PixelsPerUnit { get; }
        
        ChunkManagerSettings cm_Settings { get; }
    }
}