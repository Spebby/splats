using UnityEngine;


namespace Splats.TextureChunks {
    [CreateAssetMenu(fileName = "ChunkManagerSettings", menuName = "Splats/Chunks/ChunkManagerSettings")]
    public class ChunkManagerSettings : ScriptableObject {
        [Min(1)]      public int ChunkSize = 64;
        [Range(1, 5)] public int Layers    = 3;
    }
}
