using FishNet.Object;
using UnityEngine;

public class CameraDestroyer : NetworkBehaviour
{
    [SerializeField]
    private Camera cam;
    void Awake()
    {
        DestroyImmediate(cam.GetComponent<AudioListener>());
        cam.enabled = false;
    }
    // Start is called before the first frame update
    public override void OnStartClient()
    {
        base.OnStartClient();

        if (base.IsOwner)
        {
            cam.enabled = true;
            cam.gameObject.AddComponent<AudioListener>();
        }
    }
}
