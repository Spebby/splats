using UnityEngine;


namespace Splats.Test.Runtime {
    public class ScaleToCameraSize : MonoBehaviour {
        void Update() {
            float size = Camera.main!.orthographicSize;
            transform.localScale = new Vector3(size * 2, size * 2, 1);
        }
    }
}
