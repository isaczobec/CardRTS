using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Enqueue after FillWorldFeature (see WorldManager.SetupWorldGen). Places one island per
/// WorldGenHandler.ConnectedClientIds, evenly spaced around a circle inscribed EdgeOffset
/// tiles in from the world's edge — the same equidistant-ring layout SpawnPlayerBasesFeature
/// used, just carrying an island footprint along with each base instead of dropping the base
/// directly onto whatever terrain was already there.
///
/// For each player: resolves IslandNames against WorldManager.instance.IslandRegistry (so
/// which island TYPES are eligible for a player base is data — Inspector-assigned names —
/// rather than a direct prefab list this feature holds itself), picks one at random, stamps
/// its IslandFootprint's occupied cells as TileType.Island (walkable — see WorldManager's
/// Tile Settings), enqueues an IslandSpawnAction to instantiate the visual prefab, and spawns
/// the player's base entity on the footprint's BaseAnchor cell via the same component set
/// SpawnPlayerBasesFeature used (BuildingSpawnHelper, through SpawnPlayerBasesFeature.SpawnerFor).
/// </summary>
public class IslandPlayerBaseFeature : WorldGenFeature
{
    // Names looked up in WorldManager.instance.IslandRegistry — any island registered under
    // one of these is eligible to be picked for a player base. See IslandRegistry.
    public string[] IslandNames;
    public float EdgeOffset = 40f;

    public readonly struct IslandPlacement
    {
        public readonly ushort ClientId;
        public readonly ushort OriginX;
        public readonly ushort OriginY;
        public readonly IslandFootprint Footprint;
        public readonly EntitySpawnAction BaseAction;

        public IslandPlacement(ushort clientId, ushort originX, ushort originY, IslandFootprint footprint, EntitySpawnAction baseAction)
        {
            ClientId = clientId;
            OriginX = originX;
            OriginY = originY;
            Footprint = footprint;
            BaseAction = baseAction;
        }

        // Approximate world-space center of the footprint's bounding box — used by
        // IslandBridgeFeature to pick which of an island's BridgeAnchors faces another
        // island. Not tile-clamped (an island right at the map edge could nominally center
        // slightly outside it), which is fine here since this is only ever used as a
        // direction/distance reference point, never a tile written to.
        public Vector2 CenterWorldPosition => new Vector2(OriginX + Footprint.Width / 2f, OriginY + Footprint.Height / 2f);
    }

    // Populated by Generate — a future bridge-building feature can read where every island
    // ended up via handler.GetPreviousFeature<IslandPlayerBaseFeature>(), same pattern
    // SpawnPlayerBasesFeature.Bases already provides for other features.
    public IReadOnlyList<IslandPlacement> Islands { get; private set; } = Array.Empty<IslandPlacement>();

    public override void Generate(WorldGenHandler handler)
    {
        List<ushort> clientIds = handler.ConnectedClientIds;
        if (clientIds == null || clientIds.Count == 0) return;

        IslandRegistry registry = WorldManager.instance != null ? WorldManager.instance.IslandRegistry : null;
        if (registry == null)
        {
            Debug.LogWarning("[IslandPlayerBaseFeature] No IslandRegistry assigned on WorldManager — skipping.");
            return;
        }

        List<GameObject> candidatePrefabs = registry.GetPrefabs(IslandNames);
        if (candidatePrefabs.Count == 0)
        {
            Debug.LogWarning("[IslandPlayerBaseFeature] None of IslandNames resolved to a registered island — skipping.");
            return;
        }

        ushort worldSize = (ushort)(WorldGenHandler.CHUNK_SIZE_TILES * WorldGenHandler.WorldSizeChunks);
        float center = worldSize * 0.5f;
        float radius = Mathf.Max(0f, center - EdgeOffset);

        // Rotates the whole ring of islands by a random amount each game — same reasoning as
        // SpawnPlayerBasesFeature.phase.
        float phase = (float)(handler.Random.NextDouble() * 2.0 * Math.PI);

        var islands = new List<IslandPlacement>(clientIds.Count);

        for (int i = 0; i < clientIds.Count; i++)
        {
            float angle = phase + 2f * Mathf.PI * i / clientIds.Count;
            int centerTileX = Mathf.RoundToInt(center + Mathf.Cos(angle) * radius);
            int centerTileY = Mathf.RoundToInt(center + Mathf.Sin(angle) * radius);

            GameObject prefab = candidatePrefabs[handler.Random.Next(candidatePrefabs.Count)];
            IslandFootprint footprint = prefab != null ? prefab.GetComponent<IslandFootprint>() : null;
            if (footprint == null)
            {
                Debug.LogWarning($"[IslandPlayerBaseFeature] Prefab '{(prefab != null ? prefab.name : "null")}' has no IslandFootprint component — skipping island for client {clientIds[i]}.");
                continue;
            }

            int originX = centerTileX - footprint.Width / 2;
            int originY = centerTileY - footprint.Height / 2;

            StampFootprint(handler, footprint, originX, originY, worldSize);

            ushort clampedCenterX = (ushort)Mathf.Clamp(centerTileX, 0, worldSize - 1);
            ushort clampedCenterY = (ushort)Mathf.Clamp(centerTileY, 0, worldSize - 1);
            Vector3 worldPosition = WorldManager.instance.TileToWorldPosition(clampedCenterX, clampedCenterY, center: true);
            worldPosition.y += footprint.HeightOffset;
            handler.EnqueueAction(new IslandSpawnAction { Prefab = prefab, WorldPosition = worldPosition });

            Vector2Int anchor = footprint.BaseAnchorOrDefault();
            float baseX = originX + anchor.x + 0.5f;
            float baseY = originY + anchor.y + 0.5f;

            ushort clientId = clientIds[i];
            var baseAction = new EntitySpawnAction { X = baseX, Y = baseY, Spawner = SpawnPlayerBasesFeature.SpawnerFor(clientId) };
            handler.EnqueueAction(baseAction);

            ushort clampedOriginX = (ushort)Mathf.Clamp(originX, 0, worldSize - 1);
            ushort clampedOriginY = (ushort)Mathf.Clamp(originY, 0, worldSize - 1);
            islands.Add(new IslandPlacement(clientId, clampedOriginX, clampedOriginY, footprint, baseAction));
        }

        Islands = islands;
    }

    private static void StampFootprint(WorldGenHandler handler, IslandFootprint footprint, int originX, int originY, ushort worldSize)
    {
        foreach (Vector2Int cell in footprint.OccupiedCells())
        {
            int tx = originX + cell.x;
            int ty = originY + cell.y;
            if (tx < 0 || ty < 0 || tx >= worldSize || ty >= worldSize) continue;
            handler.SetTileType((ushort)tx, (ushort)ty, TileType.Island);
        }
    }
}
