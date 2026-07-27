// Executed on every peer (server and clients alike) as the final step of world generation,
// after all tiles are filled, all ECS systems are set up, and the world has been rendered
// (see WorldManager.GenerateAndRender). Implementations that touch shared/networked ECS
// state (e.g. EntitySpawnAction) must gate themselves to the server internally — the
// entity they create is included in the initial ECS snapshot sent to clients, so it
// propagates over the network instead of being created independently everywhere, which
// would desync ids/ordering from the server's own ECS. Purely cosmetic actions with no
// networked representation (e.g. SpawnMeshPatchAction) need no such gate and can just run
// locally on every machine.
public interface IWorldGenAction
{
    void Execute(ECS ecs);
}
