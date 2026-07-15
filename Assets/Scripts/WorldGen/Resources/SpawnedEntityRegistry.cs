using System.Collections.Generic;
using UnityEngine;

// Tracks every EntitySpawnAction currently enqueued during world generation, indexed by
// position in a uniform grid so features can cheaply query what's already been placed
// nearby (e.g. EntityClusterFeature's MinDistanceToOtherEntities) instead of scanning
// every pending action. Kept in sync automatically by WorldGenHandler.EnqueueAction /
// RemoveAction — nothing needs to call Register/Unregister directly.
public class SpawnedEntityRegistry : WorldGenResource
{
    public const string ResourceKey = "spawnedEntities";
    public string Identifier => ResourceKey;

    private readonly float _cellSize;
    private readonly Dictionary<(int cx, int cy), List<EntitySpawnAction>> _cells = new();
    private readonly List<EntitySpawnAction> _all = new();

    public IReadOnlyList<EntitySpawnAction> All => _all;

    public SpawnedEntityRegistry(float cellSize = 8f)
    {
        _cellSize = cellSize;
    }

    public void Register(EntitySpawnAction action)
    {
        _all.Add(action);
        var cell = CellFor(action.X, action.Y);
        if (!_cells.TryGetValue(cell, out var list))
            _cells[cell] = list = new List<EntitySpawnAction>();
        list.Add(action);
    }

    public void Unregister(EntitySpawnAction action)
    {
        _all.Remove(action);
        var cell = CellFor(action.X, action.Y);
        if (_cells.TryGetValue(cell, out var list))
            list.Remove(action);
    }

    // Every registered action within radius of (x, y).
    public List<EntitySpawnAction> GetWithinRadius(float x, float y, float radius)
    {
        var result = new List<EntitySpawnAction>();
        float r2 = radius * radius;
        foreach (var cell in CellsInRadius(x, y, radius))
            foreach (var action in cell)
                if (SqrDistance(action, x, y) <= r2)
                    result.Add(action);
        return result;
    }

    // Cheaper than GetWithinRadius(...).Count > 0 — stops at the first hit.
    public bool AnyWithinRadius(float x, float y, float radius)
    {
        float r2 = radius * radius;
        foreach (var cell in CellsInRadius(x, y, radius))
            foreach (var action in cell)
                if (SqrDistance(action, x, y) <= r2)
                    return true;
        return false;
    }

    // Nearest registered action to (x, y), or null if none are registered.
    public EntitySpawnAction GetClosest(float x, float y)
    {
        var center = CellFor(x, y);
        int maxRing = Mathf.CeilToInt(WorldGenHandler.WorldSizeChunks * WorldGenHandler.CHUNK_SIZE_TILES / _cellSize) + 1;

        EntitySpawnAction best = null;
        float bestDist2 = float.MaxValue;
        int foundAtRing = -1;

        for (int ring = 0; ring <= maxRing; ring++)
        {
            // Once something is found, search one extra ring beyond it — a closer action
            // could still sit just across a cell boundary — then stop.
            if (foundAtRing >= 0 && ring > foundAtRing + 1)
                break;

            for (int dx = -ring; dx <= ring; dx++)
            {
                for (int dy = -ring; dy <= ring; dy++)
                {
                    if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != ring) continue;
                    if (!_cells.TryGetValue((center.cx + dx, center.cy + dy), out var list)) continue;

                    foreach (var action in list)
                    {
                        float d2 = SqrDistance(action, x, y);
                        if (d2 < bestDist2)
                        {
                            bestDist2 = d2;
                            best = action;
                            foundAtRing = ring;
                        }
                    }
                }
            }
        }

        return best;
    }

    private IEnumerable<List<EntitySpawnAction>> CellsInRadius(float x, float y, float radius)
    {
        int cellRadius = Mathf.CeilToInt(radius / _cellSize);
        var center = CellFor(x, y);
        for (int dx = -cellRadius; dx <= cellRadius; dx++)
            for (int dy = -cellRadius; dy <= cellRadius; dy++)
                if (_cells.TryGetValue((center.cx + dx, center.cy + dy), out var list))
                    yield return list;
    }

    private (int cx, int cy) CellFor(float x, float y)
        => (Mathf.FloorToInt(x / _cellSize), Mathf.FloorToInt(y / _cellSize));

    private static float SqrDistance(EntitySpawnAction action, float x, float y)
    {
        float dx = action.X - x, dy = action.Y - y;
        return dx * dx + dy * dy;
    }
}
