using System.Collections.Generic;
using UnityEngine;

// Accumulates a coarse island/bridge adjacency graph while world gen stamps tiles — the only
// two places that ever stamp TileType.Island/TileType.Bridge are IslandPlacementHelper.
// TryPlaceIsland and BridgeConnectionBuilder.Commit, and both call into this (via
// WorldGenHandler.IslandGraph) as they do it, so the finished graph always exactly matches
// whatever this generation actually stamped. One instance per WorldGenHandler (see that
// class), read once by NavMeshHandler.CreateNavMesh (see WorldManager.GenerateAndRender) to
// tag every NavMeshNode with the region it belongs to and to keep the coarse graph itself
// around for Pathfinding's hierarchical search — see NavMeshHandler.TryGetRegionCorridor.
//
// A "region" is either a single placed island or a single committed bridge connection —
// both just get the next sequential id from the same counter (Regions.Count at
// registration time), so the coarse graph reads as one flat island<->bridge<->island chain
// per connection rather than needing two separate id spaces.
public class IslandGraphBuilder
{
    public struct RegionInfo
    {
        public int Id;
        public bool IsBridge;
        public Vector2 Center;
    }

    private readonly ushort _worldSize;
    private readonly int[,] _tileRegionId;

    public List<RegionInfo> Regions { get; } = new List<RegionInfo>();

    // regionId -> list of (neighborRegionId, edgeCost). Always has an entry (possibly empty)
    // for every region in Regions.
    public Dictionary<int, List<(int neighbor, float cost)>> Adjacency { get; } = new Dictionary<int, List<(int neighbor, float cost)>>();

    public IslandGraphBuilder(ushort worldSize)
    {
        _worldSize = worldSize;
        _tileRegionId = new int[worldSize, worldSize];
        for (int y = 0; y < worldSize; y++)
            for (int x = 0; x < worldSize; x++)
                _tileRegionId[y, x] = -1;
    }

    // Registers a brand-new island region and returns its id — call once per placed island,
    // before stamping its footprint (see MarkTile).
    public int BeginIsland(Vector2 center)
    {
        int id = Regions.Count;
        Regions.Add(new RegionInfo { Id = id, IsBridge = false, Center = center });
        Adjacency[id] = new List<(int, float)>();
        return id;
    }

    // Registers a brand-new bridge region connecting two already-registered island regions
    // and wires up the coarse edges both ways (each half-weighted, so island->island through
    // the bridge totals its real length). Returns the new bridge region's id — call once per
    // committed connection, before stamping its tiles (see MarkTile). Either endpoint may be
    // -1 (an island whose own placement predates this graph, or a hypothetical/candidate
    // island never actually stamped — see IslandPlacementHelper.PlacedIsland's default
    // RegionId) — that side of the connection is simply left unwired rather than throwing.
    public int BeginBridge(int islandARegion, int islandBRegion, Vector2 center, float length)
    {
        int id = Regions.Count;
        Regions.Add(new RegionInfo { Id = id, IsBridge = true, Center = center });
        var edges = new List<(int, float)>();
        Adjacency[id] = edges;

        float halfLength = length * 0.5f;
        if (islandARegion >= 0 && Adjacency.TryGetValue(islandARegion, out List<(int neighbor, float cost)> aEdges))
        {
            edges.Add((islandARegion, halfLength));
            aEdges.Add((id, halfLength));
        }
        if (islandBRegion >= 0 && Adjacency.TryGetValue(islandBRegion, out List<(int neighbor, float cost)> bEdges))
        {
            edges.Add((islandBRegion, halfLength));
            bEdges.Add((id, halfLength));
        }

        return id;
    }

    // Tags a single stamped tile with regionId — first-writer-wins (never overwrites an
    // already-tagged tile), mirroring BridgeConnectionBuilder.StampTiles's own rule of never
    // downgrading an already-Island tile's TYPE to Bridge: a bridge's padded overshoot into
    // an island's own footprint should read as that island's region, not the bridge's. Safe
    // to call with an out-of-bounds (x, y) — a footprint/curve can extend past the map edge
    // and both callers already skip those cells for SetTileType, but this checks again
    // independently rather than trusting that.
    public void MarkTile(int regionId, int x, int y)
    {
        if (x < 0 || y < 0 || x >= _worldSize || y >= _worldSize) return;
        if (_tileRegionId[y, x] != -1) return;
        _tileRegionId[y, x] = regionId;
    }

    // -1 if (x, y) was never stamped as part of any island/bridge (open water/void, or a
    // world with no island graph at all — e.g. legacy biome terrain).
    public int GetRegionId(ushort x, ushort y)
    {
        if (x >= _worldSize || y >= _worldSize) return -1;
        return _tileRegionId[y, x];
    }
}
