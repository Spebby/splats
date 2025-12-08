using Splats;
using UnityEngine;


// I want a "proper" solution but this is ok for testing. Also I don't want users to have to pay upfront
// costs if they're not going to use the system.
public class SplatBootstrap : MonoBehaviour {
    //[SerializeField] MonoBehaviour manager;
    [SerializeField] ScriptableObject splats;
    //ISplatsManager SplatsManager => manager as ISplatsManager;
    ISplatsConfig SplatsConfig => splats as ISplatsConfig;
    
    void Awake() {
        SplatsMan.Init(new GPUSplatManager(), SplatsConfig);
    }

    void OnDestroy() {
        SplatsMan.Denit();
    }
}