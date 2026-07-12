// Executed on the server as the final step of world generation, after all tiles are
// filled and all ECS systems are set up. Use this to spawn entities (resources, etc.)
// that should exist from the first tick; the server's ECS snapshot sent to clients
// will include them automatically.
public interface IWorldGenAction
{
    void Execute(ECS ecs);
}
