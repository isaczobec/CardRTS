using System.Collections.Generic;

/// <summary>
/// Enqueue at the end of world generation, after everything that might have placed
/// entity-spawn actions (trees, etc.) near a base's eventual position. Removes every
/// pending EntitySpawnAction within Radius of any player base (see
/// SpawnPlayerBasesFeature), except the bases' own spawn actions, so nothing overlaps a
/// freshly-placed base.
/// </summary>
public class ClearActionsNearBasesFeature : WorldGenFeature
{
    public float Radius = 8f;

    public override void Generate(WorldGenHandler handler)
    {
        var basesFeature = handler.GetPreviousFeature<SpawnPlayerBasesFeature>();
        if (basesFeature == null || basesFeature.Bases.Count == 0) return;

        var baseActions = new HashSet<EntitySpawnAction>();
        foreach (var b in basesFeature.Bases)
            baseActions.Add(b.Action);

        foreach (var b in basesFeature.Bases)
        {
            List<EntitySpawnAction> nearby = handler.GetActions<EntitySpawnAction>(action =>
                !baseActions.Contains(action) && WithinRadius(action, b.X, b.Y, Radius));

            foreach (EntitySpawnAction action in nearby)
                handler.RemoveAction(action);
        }
    }

    private static bool WithinRadius(EntitySpawnAction action, float x, float y, float radius)
    {
        float dx = action.X - x, dy = action.Y - y;
        return dx * dx + dy * dy <= radius * radius;
    }
}
