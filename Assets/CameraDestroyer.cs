using System.Collections;
using System.Collections.Generic;
using FishNet.Object;
using UnityEngine;

public class CameraDestroyer : NetworkBehaviour
{
    [SerializeField]
    private Camera cam;
    void Start()
    {
        cam.enabled = false;
    }
    // Start is called before the first frame update
    public override void OnStartClient()
    {
        base.OnStartClient();

        Debug.Log("Start Client");
        if (base.IsOwner)
        {
            Debug.Log("Is owner");
            cam.enabled = true;
        }
    }
}
