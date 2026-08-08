using UnityEngine;

// Cosmetic IWorldGenAction that instantiates a bridge segment prefab at a world position and
// rotation. Like IslandSpawnAction, this has no ECS/networked representation — the Bridge
// tiles IslandBridgeFeature stamped during generation are what's actually synced — so it
// isn't gated to the server; every peer instantiates its own local copy. Kept separate from
// IslandSpawnAction (rather than adding rotation there) since islands are deliberately never
// rotated, while bridge segments always need to face along whatever direction the curve
// they're chained around points at that point.
public class BridgeSegmentSpawnAction : IWorldGenAction
{
    public GameObject Prefab;
    public Vector3 WorldPosition;
    public Quaternion Rotation;

    public void Execute(ECS ecs)
    {
        if (Prefab == null) return;
        Object.Instantiate(Prefab, WorldPosition, Rotation, WorldManager.instance.Renderer.transform);
    }
}
