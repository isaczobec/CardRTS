using UnityEngine;

// IWorldGenAction that spawns a single decorative ground-patch mesh (see
// TerrainPatchRegistry) at (X, Y) — a flat, edge-faded plane laid over the world mesh, used
// to visually mark an area (e.g. the ground under a resource cluster) without needing a
// dedicated terrain-shader tile type. Purely cosmetic: unlike EntitySpawnAction, this has no
// ECS/networked representation, so it isn't gated to the server — see IWorldGenAction's own
// doc comment for why that's safe here.
public class SpawnMeshPatchAction : IWorldGenAction
{
    public float X;
    public float Y;
    public string PatchId;
    public float Size = 10f;

    // Added on top of the terrain height sampled at (X, Y) — lets patches that are meant to
    // sit on top of others (e.g. a small accent patch over a wider base patch) avoid
    // z-fighting via a small vertical separation instead of render-queue tricks.
    public float YOffset = 0f;

    public void Execute(ECS ecs)
    {
        TerrainPatchRegistry registry = WorldManager.instance.Renderer.TerrainPatchRegistry;
        if (registry == null)
        {
            Debug.LogWarning("[SpawnMeshPatchAction] WorldRenderer has no TerrainPatchRegistry assigned — skipping patch spawn.");
            return;
        }

        Vector3 worldPosition = WorldManager.instance.TileToWorldPosition((ushort)X, (ushort)Y, center: true);
        worldPosition.y += YOffset;
        registry.Spawn(PatchId, worldPosition, Size);
    }
}
