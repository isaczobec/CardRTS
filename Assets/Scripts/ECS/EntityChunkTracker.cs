using System;
using System.Collections.Generic;

public class EntityChunkTracker
{
    public const int ChunkSize = 16;

    private ECS _ecs;
    private readonly Dictionary<ulong, (int x, int y)> _entityToChunk = new();
    private readonly Dictionary<(int, int), HashSet<ulong>> _chunkToEntities = new();

    public void Initialize(ECS ecs)
    {
        _ecs = ecs;
        ecs.FlagEvents.Subscribe<ComponentAddedEvent<PositionComponent>>(OnPositionAdded);
        ecs.FlagEvents.Subscribe<ComponentRemovedEvent<PositionComponent>>(OnPositionRemoved);
        ecs.FlagEvents.Subscribe<EntityDeletedEvent>(OnEntityDeleted);
        ecs.Delta.ComponentChangedEvents.Subscribe<ComponentChangedEvent<PositionComponent>>(OnPositionChanged);
    }

    public bool TryGetChunk(ulong entityId, out int chunkX, out int chunkY)
    {
        if (_entityToChunk.TryGetValue(entityId, out var chunk))
        {
            chunkX = chunk.x;
            chunkY = chunk.y;
            return true;
        }
        chunkX = chunkY = 0;
        return false;
    }

    public IReadOnlyCollection<ulong> GetEntitiesInChunk(int chunkX, int chunkY)
    {
        _chunkToEntities.TryGetValue((chunkX, chunkY), out var set);
        return set;
    }

    // Fills results with entity IDs whose position is within radius of (cx, cy).
    public void GetEntitiesNear(float cx, float cy, float radius, List<ulong> results)
    {
        var store = _ecs.GetComponentStore<PositionComponent>();
        if (store == null) return;

        float r2 = radius * radius;
        int minChunkX = (int)Math.Floor((cx - radius) / ChunkSize);
        int maxChunkX = (int)Math.Floor((cx + radius) / ChunkSize);
        int minChunkY = (int)Math.Floor((cy - radius) / ChunkSize);
        int maxChunkY = (int)Math.Floor((cy + radius) / ChunkSize);

        for (int x = minChunkX; x <= maxChunkX; x++)
        for (int y = minChunkY; y <= maxChunkY; y++)
        {
            var set = GetEntitiesInChunk(x, y);
            if (set == null) continue;
            foreach (ulong entityId in set)
            {
                if (!store.HasComponent(entityId)) continue;
                PositionComponent pos = store.GetComponent(entityId);
                float dx = pos.X - cx, dy = pos.Y - cy;
                if (dx * dx + dy * dy <= r2)
                    results.Add(entityId);
            }
        }
    }

    // Fills results with entity IDs whose position falls within the axis-aligned rect.
    public void GetEntitiesInRect(float minX, float minY, float maxX, float maxY, List<ulong> results)
    {
        var store = _ecs.GetComponentStore<PositionComponent>();
        if (store == null) return;

        int minChunkX = (int)Math.Floor(minX / ChunkSize);
        int maxChunkX = (int)Math.Floor(maxX / ChunkSize);
        int minChunkY = (int)Math.Floor(minY / ChunkSize);
        int maxChunkY = (int)Math.Floor(maxY / ChunkSize);

        for (int x = minChunkX; x <= maxChunkX; x++)
        for (int y = minChunkY; y <= maxChunkY; y++)
        {
            var set = GetEntitiesInChunk(x, y);
            if (set == null) continue;
            foreach (ulong entityId in set)
            {
                if (!store.HasComponent(entityId)) continue;
                PositionComponent pos = store.GetComponent(entityId);
                if (pos.X >= minX && pos.X <= maxX && pos.Y >= minY && pos.Y <= maxY)
                    results.Add(entityId);
            }
        }
    }

    // Call after ECS.CopyStateFrom — that path bypasses the event system.
    public void Rebuild()
    {
        _entityToChunk.Clear();
        _chunkToEntities.Clear();

        var store = _ecs.GetComponentStore<PositionComponent>();
        if (store == null) return;

        store.ForEach((ulong entityId) =>
        {
            PositionComponent pos = store.GetComponent(entityId);
            UpdateChunk(entityId, pos.X, pos.Y);
        });
    }

    private void OnPositionAdded(ComponentAddedEvent<PositionComponent> evt)
    {
        var store = _ecs.GetComponentStore<PositionComponent>();
        if (store == null || !store.HasComponent(evt.EntityId)) return;
        PositionComponent pos = store.GetComponent(evt.EntityId);
        UpdateChunk(evt.EntityId, pos.X, pos.Y);
    }

    private void OnPositionRemoved(ComponentRemovedEvent<PositionComponent> evt)
        => RemoveFromChunk(evt.EntityId);

    private void OnEntityDeleted(EntityDeletedEvent evt)
        => RemoveFromChunk(evt.EntityId);

    private void OnPositionChanged(ComponentChangedEvent<PositionComponent> evt)
    {
        var store = _ecs.GetComponentStore<PositionComponent>();
        if (store == null) return;
        foreach (ulong entityId in evt.EntityIds)
        {
            if (!store.HasComponent(entityId)) continue;
            PositionComponent pos = store.GetComponent(entityId);
            UpdateChunk(entityId, pos.X, pos.Y);
        }
    }

    private void UpdateChunk(ulong entityId, float x, float y)
    {
        int chunkX = (int)System.Math.Floor(x / ChunkSize);
        int chunkY = (int)System.Math.Floor(y / ChunkSize);

        if (_entityToChunk.TryGetValue(entityId, out var old))
        {
            if (old.x == chunkX && old.y == chunkY) return;
            RemoveFromChunkSet(entityId, old);
        }

        _entityToChunk[entityId] = (chunkX, chunkY);
        var key = (chunkX, chunkY);
        if (!_chunkToEntities.TryGetValue(key, out var set))
            _chunkToEntities[key] = set = new HashSet<ulong>();
        set.Add(entityId);
    }

    private void RemoveFromChunk(ulong entityId)
    {
        if (!_entityToChunk.TryGetValue(entityId, out var chunk)) return;
        RemoveFromChunkSet(entityId, chunk);
        _entityToChunk.Remove(entityId);
    }

    private void RemoveFromChunkSet(ulong entityId, (int x, int y) chunk)
    {
        if (!_chunkToEntities.TryGetValue(chunk, out var set)) return;
        set.Remove(entityId);
        if (set.Count == 0)
            _chunkToEntities.Remove(chunk);
    }
}
