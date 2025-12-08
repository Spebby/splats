using Splats.TextureChunks;
using UnityEngine;


public class ChunkManagerTest : MonoBehaviour {
    [SerializeField] ChunkManagerSettings settings;
    [SerializeField] Color gizmoColor = Color.red;
    ChunkManager cm;

    void Start() {
        cm = new ChunkManager(Camera.main.transform, settings);
    }

    void OnDrawGizmos() {
        if (cm == null) return;
        
        Gizmos.color = gizmoColor;
        Vector2Int[] chunks = cm.Chunks;

        foreach (Vector2Int chunk in chunks) {
            Gizmos.DrawWireCube(cm.ChunkToWorld(chunk), new Vector3(cm.ChunkSize, cm.ChunkSize, 0));
        }
    }
}
