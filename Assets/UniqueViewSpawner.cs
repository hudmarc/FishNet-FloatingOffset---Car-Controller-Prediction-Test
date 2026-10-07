using System.Collections.Generic;
using UnityEngine;
using FloatingOffset.Runtime;
using Unity.VisualScripting;
using FishNet;

public class UniqueViewSpawner : OffsetBehaviour
{
    private static HashSet<Vector3d> quantized_spawns = new HashSet<Vector3d>();
    [SerializeField]
    private GameObject spawn;
    [SerializeField]
    private Vector3 relativeSpawnOffset;
    // Start is called before the first frame update
    void Start()
    {
        manager.OnOffsetServerInitialized += OnOffsetServerInitialized;
    }

    void OnDestroy()
    {
        manager.OnOffsetServerInitialized -= OnOffsetServerInitialized;
    }
    void OnOffsetServerInitialized()
    {
        Vector3d quantized = HashGrid.Quantize(manager.GetLocalOffset(gameObject.scene) + OffsetUtils.ToVector3d(transform.position), 1);

        if (!quantized_spawns.Contains(quantized))
        {
            quantized_spawns.Add(quantized);
            Object spawned = Instantiate(spawn, gameObject.scene);
            InstanceFinder.ServerManager.Spawn(spawned.GameObject(), null, gameObject.scene);
            spawned.GetComponent<Transform>().position = transform.position + relativeSpawnOffset;
        }
        else
        {
            if (manager.IsLogging())
                Debug.LogWarning($"Ignored duplicate spawn {gameObject.name} in {gameObject.scene.handle.GetHashCode()} @ {quantized}");
        }
    }
}
