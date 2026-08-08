using UnityEngine;

// Cosmetic IWorldGenAction that instantiates an island prefab at a world position. Like
// SpawnMeshPatchAction, this has no ECS/networked representation — the tiles
// IslandPlayerBaseFeature stamped during generation are what's actually synced — so it isn't
// gated to the server; every peer instantiates its own local copy.
public class IslandSpawnAction : IWorldGenAction
{
    public GameObject Prefab;
    public Vector3 WorldPosition;

    public void Execute(ECS ecs)
    {
        if (Prefab == null) return;
        Object.Instantiate(Prefab, WorldPosition, Quaternion.identity, WorldManager.instance.Renderer.transform);
    }
}
