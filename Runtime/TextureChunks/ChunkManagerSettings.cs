using UnityEngine;


namespace Splats.TextureChunks {
    [CreateAssetMenu(fileName = "New CMSettings", menuName = "Splats/Chunks/Chunk Manager Settings")]
    public class ChunkManagerSettings : ScriptableObject {
        [Min(1)]      public int ChunkSize = 64;
        [Range(1, 5)] public int Layers    = 3;
    }
}
