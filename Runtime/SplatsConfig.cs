using UnityEngine;


namespace Gamba.Splats {
    [CreateAssetMenu(menuName = "Splats/Splats Config")]
    public class SplatsConfig : ScriptableObject, ISplatsConfig {
        [SerializeField, Min(64)] int bufferSize;
        
        public int BufferSize => bufferSize;
    }
}