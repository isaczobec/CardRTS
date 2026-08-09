using UnityEngine;

// Cosmetic IWorldGenAction that instantiates a bridge segment prefab at a world position,
// rotation, and (non-uniform) scale. Like IslandSpawnAction, this has no ECS/networked
// representation — the Bridge tiles IslandBridgeFeature stamped during generation are what's
// actually synced — so it isn't gated to the server; every peer instantiates its own local
// copy. Kept separate from IslandSpawnAction (rather than adding rotation/scale there) since
// islands are deliberately never rotated or stretched, while bridge segments always need to
// face along — and now exactly fill — whatever stretch of curve they were placed on.
public class BridgeSegmentSpawnAction : IWorldGenAction
{
    public GameObject Prefab;
    public Vector3 WorldPosition;
    public Quaternion Rotation;

    // Applied as the instance's localScale — see BridgeConnectionBuilder.PlaceSegments, which
    // stretches only the Z (length) axis so every segment exactly fills its own slice of the
    // curve regardless of how evenly BridgeSegmentFootprint.SegmentLength happened to divide
    // into the curve's actual length, without distorting the segment's width/height.
    public Vector3 Scale = Vector3.one;

    public void Execute(ECS ecs)
    {
        if (Prefab == null) return;
        GameObject instance = Object.Instantiate(Prefab, WorldPosition, Rotation, WorldManager.instance.Renderer.transform);
        instance.transform.localScale = Scale;
    }
}
