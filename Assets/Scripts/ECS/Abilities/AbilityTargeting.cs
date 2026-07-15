using UnityEngine;

// Shared "where does this TargetLocation ability actually land" resolution — used by both
// AbilityInputManager (to decide what X/Y to actually send in AbilityUsedAtLocationInput)
// and AbilityIndicatorManager (to draw indicators that match where the cast will land), so
// the two can never disagree about the clamped point.
public static class AbilityTargeting
{
    // Clamps (rawX, rawY) to the closest point within ability.Range of the caster's own
    // position when ability.ClampCastLocationToRange is true and the raw point is further
    // away than that; otherwise x/y are just the raw point. Returns false (x/y left at the
    // raw point) if the caster has no PositionComponent to clamp against.
    public static bool ResolveCastPoint(ECS ecs, ulong casterId, Ability ability, float rawX, float rawY, out float x, out float y)
    {
        x = rawX;
        y = rawY;

        ComponentStore<PositionComponent> posStore = ecs.GetComponentStore<PositionComponent>();
        if (posStore == null || !posStore.HasComponent(casterId)) return false;

        if (!ability.ClampCastLocationToRange) return true;

        PositionComponent casterPos = posStore.GetComponent(casterId);
        float dx = rawX - casterPos.X, dy = rawY - casterPos.Y;
        float distSq = dx * dx + dy * dy;
        if (distSq <= ability.Range * ability.Range) return true;

        float dist = Mathf.Sqrt(distSq);
        if (dist <= 0.0001f) return true; // caster and point coincide — nothing to clamp toward

        float scale = ability.Range / dist;
        x = casterPos.X + dx * scale;
        y = casterPos.Y + dy * scale;
        return true;
    }
}
