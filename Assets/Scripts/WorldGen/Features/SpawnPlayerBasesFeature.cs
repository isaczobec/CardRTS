using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Enqueue this feature FIRST in world generation (see WorldManager.SetupWorldGen) so base
/// positions are established before anything else that needs them (e.g. clearing nearby
/// obstacles/actions) runs.
///
/// Spawns one base per WorldGenHandler.ConnectedClientIds, evenly spaced around a circle
/// inscribed EdgeOffset tiles in from the world's edge — a circle rather than literally
/// walking the square perimeter, since it's the only way to make every base equidistant
/// from its neighbours regardless of player count.
///
/// Each base is a building near-identical to the one BuildingCard spawns (same helper —
/// see BuildingSpawnHelper), except RenderableType.PlayerBaseCore and more health.
/// </summary>
public class SpawnPlayerBasesFeature : WorldGenFeature
{
    public const int BaseMaxHealth = 1000; // BuildingCard's building has 300 — a base should outlast it by a wide margin.

    public float EdgeOffset = 20f;

    public readonly struct PlayerBase
    {
        public readonly ushort ClientId;
        public readonly float X;
        public readonly float Y;
        public readonly EntitySpawnAction Action;

        public PlayerBase(ushort clientId, float x, float y, EntitySpawnAction action)
        {
            ClientId = clientId;
            X = x;
            Y = y;
            Action = action;
        }
    }

    // Populated by Generate — later features (e.g. via
    // handler.GetPreviousFeature<SpawnPlayerBasesFeature>()) can read where every base
    // ended up, and which player it belongs to.
    public IReadOnlyList<PlayerBase> Bases { get; private set; } = Array.Empty<PlayerBase>();

    public override void Generate(WorldGenHandler handler)
    {
        List<ushort> clientIds = handler.ConnectedClientIds;
        if (clientIds == null || clientIds.Count == 0) return;

        float worldSize = WorldGenHandler.CHUNK_SIZE_TILES * WorldGenHandler.WorldSizeChunks;
        float center = worldSize * 0.5f;
        float radius = Mathf.Max(0f, center - EdgeOffset);

        // Rotates the whole ring of bases by a random amount each game — bases stay evenly
        // spaced relative to each other, but which direction the first one lands in isn't
        // fixed to any particular (e.g. cardinal) angle.
        float phase = (float)(handler.Random.NextDouble() * 2.0 * Math.PI);

        var bases = new List<PlayerBase>(clientIds.Count);

        for (int i = 0; i < clientIds.Count; i++)
        {
            float angle = phase + 2f * Mathf.PI * i / clientIds.Count;
            float x = center + Mathf.Cos(angle) * radius;
            float y = center + Mathf.Sin(angle) * radius;

            ushort clientId = clientIds[i];
            var action = new EntitySpawnAction { X = x, Y = y, Spawner = SpawnerFor(clientId) };
            handler.EnqueueAction(action);
            bases.Add(new PlayerBase(clientId, x, y, action));
        }

        Bases = bases;
    }

    private static Action<ulong, ECS> SpawnerFor(ushort ownerPlayerId) => (id, ecs) =>
        BuildingSpawnHelper.AddBuildingComponents(ecs, id, ownerPlayerId, RenderableType.PlayerBaseCore, BaseMaxHealth,
            ticksUntilActive: 1); // already in the initial snapshot — no card-play-style delay needed, see EntitySpawnAction.SpawnTree
}
