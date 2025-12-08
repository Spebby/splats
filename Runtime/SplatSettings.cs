using UnityEngine;


namespace Splats {
    public interface ISplatSettings {
        public uint ID { get; }
        
    }
    
    public class SplatSettings : ScriptableObject, ISplatSettings {
        // Interface
        public uint ID => id;
        
        
        // Serialised Data
        [SerializeField] uint id;
    }
}