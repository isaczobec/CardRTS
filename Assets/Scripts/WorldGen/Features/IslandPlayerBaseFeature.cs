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
/// rather than a direct prefab list this feature holds itself), picks one at random, places
/// it via IslandPlacementHelper (stamping its walkable cells as TileType.Island and
/// enqueuing its visual spawn), and spawns the player's base entity on the footprint's
/// BaseAnchor cell via the same component set SpawnPlayerBasesFeature used
/// (BuildingSpawnHelper, through SpawnPlayerBasesFeature.SpawnerFor).
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
        public readonly IslandPlacementHelper.PlacedIsland Island;
        public readonly EntitySpawnAction BaseAction;

        public IslandPlacement(ushort clientId, IslandPlacementHelper.PlacedIsland island, EntitySpawnAction baseAction)
        {
            ClientId = clientId;
            Island = island;
            BaseAction = baseAction;
        }
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
            IslandPlacementHelper.PlacedIsland? placed = IslandPlacementHelper.TryPlaceIsland(handler, prefab, centerTileX, centerTileY, worldSize);
            if (placed == null)
            {
                Debug.LogWarning($"[IslandPlayerBaseFeature] Prefab '{(prefab != null ? prefab.name : "null")}' has no IslandFootprint component — skipping island for client {clientIds[i]}.");
                continue;
            }

            IslandFootprint footprint = placed.Value.Footprint;
            Vector2Int anchor = footprint.BaseAnchorOrDefault();
            float baseX = placed.Value.OriginX + anchor.x + 0.5f;
            float baseY = placed.Value.OriginY + anchor.y + 0.5f;

            ushort clientId = clientIds[i];
            var baseAction = new EntitySpawnAction { X = baseX, Y = baseY, Spawner = SpawnPlayerBasesFeature.SpawnerFor(clientId) };
            handler.EnqueueAction(baseAction);

            islands.Add(new IslandPlacement(clientId, placed.Value, baseAction));
        }

        Islands = islands;
    }
}
