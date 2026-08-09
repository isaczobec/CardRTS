using UnityEngine;

// Cosmetic IWorldGenAction that instantiates an island prefab at a world position/rotation. Like
// SpawnMeshPatchAction, this has no ECS/networked representation — the tiles
// IslandPlacementHelper stamped during generation (via a RotatedIslandFootprint built from this
// same Rotation — see IslandPlacementHelper.TryPlaceIsland) are what's actually synced — so it
// isn't gated to the server; every peer instantiates its own local copy.
public class IslandSpawnAction : IWorldGenAction
{
    public GameObject Prefab;
    public Vector3 WorldPosition;
    public Quaternion Rotation = Quaternion.identity;

    public void Execute(ECS ecs)
    {
        if (Prefab == null) return;
        Object.Instantiate(Prefab, WorldPosition, Rotation, WorldManager.instance.Renderer.transform);
    }
}
