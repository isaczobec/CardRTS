using UnityEngine;

// Cosmetic IWorldGenAction that instantiates a bridge segment prefab at a world position,
// rotation, and (non-uniform) scale. Like IslandSpawnAction, this has no ECS/networked
// representation — the Bridge tiles IslandBridgeFeature stamped during generation are what's
// actually synced — so it isn't gated to the server; every peer instantiates its own local
// copy. Kept separate from IslandSpawnAction (rather than reusing this one for both) since a
// bridge segment's Scale is stretched per-instance to exactly fill its own slice of curve (see
// BridgeConnectionBuilder.PlaceSegments) — islands can now rotate too (see
// IslandSpawnAction.Rotation / IslandPlacementHelper.TryPlaceIsland), but never stretch.
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
