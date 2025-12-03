using System;
using Gilzoide.UpdateManager;
using Unity.Collections;
using UnityEngine;
using Object = UnityEngine.Object;


namespace Splats {
    public class BasicSplatsMaker : ISplatsManager, IFixedUpdatable {
        ISplatsConfig config;

        // most re
        int back = -1;
        int count;
        GameObject[] splats;
        float[] lifetimes;
        Bounds[] bounds;

        void ISplatsManager.Init(ISplatsConfig conf) {
            config    = conf;
            count     = config.BufferSize;
            splats    = new GameObject[count];
            lifetimes = new float[count];
            bounds    = new Bounds[count];
            this.RegisterInManager();
        }

        public void Spawn(Vector2 position, Quaternion rotation, SplatParams @params) {
            back++;
            if (back == count) {
                count <<= 1;
                GameObject[] tempA = new GameObject[count];
                float[]      tempB = new float[count];
                Bounds[]     tempC = new Bounds[count];

                Array.Copy(splats, tempA, back);
                Array.Copy(lifetimes, tempB, back);
                Array.Copy(bounds, tempC, back);
                
                splats    = tempA;
                lifetimes = tempB;
                bounds    = tempC;
            }

            splats[back]    = Object.Instantiate(@params.Object, position, rotation);
            lifetimes[back] = @params.Lifetime;
            bounds[back] = splats[back].GetComponent<Renderer>().bounds;
        }

        /// <param name="position"></param>
        /// <param name="radius"></param>
        /// <returns>A value of default is a standin for null</returns>
        public SplatHit Query(Vector2 position, float radius) {
            Bounds bound = new(position, Vector3.one * (radius * 2f));
            foreach (Bounds b in bounds) {
                if (!b.Intersects(bound)) continue;
                return new SplatHit(0);
            }
            
            return default;
        }

        public NativeArray<SplatHit> Query(NativeArray<Vector2> positions, NativeArray<float> radii, Allocator allocator = Allocator.Temp) {
            if (radii.Length != positions.Length) throw new ArgumentException("positions and radii must be same length.");

            NativeArray<SplatHit> results = new(positions.Length, allocator);
            for (int i = 0; i < positions.Length; i++) {
                Vector2 pos = positions[i];
                float   r   = radii[i];

                Bounds queryBound = new(pos, Vector3.one * (r * 2f));
                SplatHit hit = default;

                // Check against all splat bounds
                foreach (Bounds splatBound in bounds) {
                    if (!splatBound.Intersects(queryBound)) continue;
                    hit = new SplatHit(0);
                    break;
                }

                results[i] = hit;
            }

            return results;
        }

        public void ManagedFixedUpdate() {
            // consider batching events like spawn splats if necessary.
            for (int i = 0; i < back; i++) {
                lifetimes[i] = lifetimes[i] -= Time.fixedDeltaTime;
                if (0 < lifetimes[i]) continue;
                
                lifetimes[i] = lifetimes[back];
                bounds[i]    = bounds[back];
                Object.Destroy(splats[i]);
                splats[i] = splats[back];
                back--;
            }
        }
    }
}