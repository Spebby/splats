using Splats.TextureChunks;
using UnityEngine;


namespace Splats {
    [CreateAssetMenu(menuName = "Splats/Splats Config")]
    public class SplatsConfig : ScriptableObject, ISplatsConfig {
        [SerializeField, Min(64)] int bufferSize;
        [SerializeField, Min(1)] int pixelsPerUnit = 16;
        [SerializeField] ChunkManagerSettings chunkManagerSettings;
        
        public int BufferSize => bufferSize;
        public int PixelsPerUnit => pixelsPerUnit;
        public ChunkManagerSettings cm_Settings => chunkManagerSettings;
    }
}