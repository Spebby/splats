using System.Collections.Generic;
using UnityEngine;


namespace Splats {
    public interface ISplatSettings {
        public uint ID { get; }
        public Sprite RandomTexture { get; }
    }
    
    [CreateAssetMenu(menuName = "Splats/Splats Settings", fileName = "SplatSettings")]
    public class SplatSettings : ScriptableObject, ISplatSettings {
        // Interface
        public uint ID => id;
        public Sprite RandomTexture => textures[Random.Range(0, textures.Count)];
        
        // Serialised Data
        [SerializeField, Min(1)] uint id;
        [SerializeField] List<Sprite> textures;
    }
}