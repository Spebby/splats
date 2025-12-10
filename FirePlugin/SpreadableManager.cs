using System.Runtime.CompilerServices;
using Gilzoide.UpdateManager;
using Splats;
using Unity.Collections;
using UnityEngine;


public class SpreadableManager : IFixedUpdatable {
    NativeList<Vector2> _frontier; // current fire edge
    
    
    public SpreadableManager() {
        _frontier = new NativeList<Vector2>(1 << 11, Allocator.Persistent);
        this.RegisterInManager();
        SplatsMan.OnSplat += ProcessNewSplat;
    }

    ~SpreadableManager() {
        _frontier.Dispose();
        this.UnregisterInManager();
    }

    public void StartFire(Vector2 position, float radius = SAMPLE_SIZE) {
        /*
        SplatsMan.RequestQuery(position, id => {
            if (!IsBurnable(id)) return;
            SplatsMan.Edit(position, new SplatEditData(type: SplatEditData.Type.Replace,
                                                    sourceID: id,
                                                    targetID: GetFlameable(id),
                                                    radius: radius));
            
            // TODO: make sure this actually runs *after* the edit is applied
            SplatsMan.RequestQuerySplatEdge(position, (_, array) => {
                _frontier.AddRange(array);
            });
        });
        */
    }

    bool IsBurnable(uint id) {
        return id switch {
            1 => true,
            _ => false
        };
    }

    void ProcessNewSplat(uint ID, Vector2 Position) {
        // worry about this shit later
        return;
        
        // issue: consider case where oil is spawned far away from fire. should not be on fire
        // consider case where oil is in fire, should be lit on fire.
        // how do we efficiently do this? Probably process along the edges.
        /*
        SplatsMan.RequestQuerySplatEdge(Position, (id, array) => {
            
            
            
        });
        */
    }

    static readonly Vector2[] SPREAD_OFFSETS = {
        new(0, 1),                      // N
        new(1, 0),                      // E
        new(0, -1),                     // S
        new(-1, 0),                     // W
        new Vector2(1, 1).normalized,   // NE
        new Vector2(1, -1).normalized,  // SE
        new Vector2(-1, -1).normalized, // SW
        new Vector2(-1, 1).normalized,  // NW
    };

    const float SAMPLE_SIZE = 0.5f;

    static uint GetFlameable(uint id) {
        return id;
    }
    
    public void ManagedFixedUpdate() {
        // Process this frames' frontier.
        
        NativeArray<float> radii = new(_frontier.Length, Allocator.Temp);
        for (int i = 0; i < _frontier.Length; i++) {
            radii[i] = SAMPLE_SIZE;
        }
       
        // GPU Readback
        NativeArray<SplatHit> hits = SplatsMan.Query(
            _frontier.AsArray(),
            radii, 
            Allocator.Temp
        );
        
        
        // Process each hit result from the query
        for (int i = 0; i < hits.Length; i++) {
            SplatHit hit = hits[i];
            if (!IsBurnable(hit.ID)) {
                _frontier.RemoveAtSwapBack(i);
                continue;
            }

            
            SplatsMan.Edit(_frontier[i],
                        new SplatEditData(type: SplatEditData.Type.Replace,
                                          sourceID: hit.ID,
                                          targetID: GetFlameable(hit.ID)));
        }
        
        
        radii.Dispose();
        hits.Dispose();
        
        BuildNextFrontier(ref _frontier);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static void BuildNextFrontier(ref NativeList<Vector2> frontier) {
        NativeList<Vector2> allPosQuery = new(Allocator.Temp);
        NativeArray<Vector2> temp = new (SPREAD_OFFSETS.Length, Allocator.Temp);
        
        foreach (Vector2 position in frontier) {
            for (int i = 0; i < SPREAD_OFFSETS.Length; i++) {
                temp[i] = position + SPREAD_OFFSETS[i];
            }

            allPosQuery.AddRange(temp);
        }

        frontier.Clear();
        frontier.AddRange(allPosQuery.AsArray());
        
        temp.Dispose();
        allPosQuery.Dispose();
    }
}