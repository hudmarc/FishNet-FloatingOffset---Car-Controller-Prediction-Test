using FishNet;
using FishNet.Object;
using UnityEngine;

public class SpawnChildren : MonoBehaviour
{
    void Start()
    {
        var nobs = gameObject.GetComponentsInChildren<NetworkObject>();

        while (transform.childCount > 0)
        {
            transform.GetChild(0).SetParent(null);
        }
        
        foreach (var nob in nobs)
        {
            InstanceFinder.ServerManager.Spawn(nob.gameObject, null, gameObject.scene);
        }
    }
}
