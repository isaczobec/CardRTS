using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Tracks each entity's two most recent simulation-tick world positions and produces a
/// frame-smoothed position between them, driven by how far we are into the current
/// tick interval. While an entity is reported as not moving, positions are not
/// interpolated (and the sample is reset to the resting point) — otherwise the last
/// pre-stop step keeps getting re-played every tick interval, which reads as the
/// object lagging back and forth in place.
///
/// The plain Update(entityId, worldPos, isMoving, teleported) overload does exactly that
/// strict two-sample lerp. The extra overload below — Update(..., speedWorldUnitsPerSecond)
/// — is more lenient: instead of being strictly locked to a lerp between the last two tick
/// samples, the returned position continuously chases the true, authoritative current tick
/// position at up to speedWorldUnitsPerSecond every frame, independent of exactly when the
/// next tick sample lands. See that overload's own doc comment for why (irregular tick
/// cadence — e.g. TickManager's own catch-up loop running several ticks in one rendered
/// frame after a stall — makes the strict lerp jump/snap almost every frame instead of
/// smoothly gliding, since a whole burst of ticks can land between two rendered frames with
/// no intermediate alpha left to lerp across).
/// </summary>
public class TickPositionInterpolator
{
    private struct Sample
    {
        public Vector3 Previous;
        public Vector3 Current;

        // Extra state for the lenient overload only — the actual visual position, which can
        // lag behind Current (never ahead of it — see that overload's own doc comment) while
        // catching up.
        public Vector3 Visual;
    }

    private readonly Dictionary<ulong, Sample> _samples = new();

    public void Remove(ulong entityId) => _samples.Remove(entityId);

    public Vector3 Update(ulong entityId, Vector3 worldPos, bool isMoving, bool teleported = false)
    {
        if (!isMoving || teleported)
        {
            _samples[entityId] = new Sample { Previous = worldPos, Current = worldPos, Visual = worldPos };
            return worldPos;
        }

        Sample sample = AdvanceSample(entityId, worldPos);

        float alpha = Mathf.Clamp01(TickManager.instance.TimeSinceLastTick / TickManager.TickInterval);
        Vector3 lerped = Vector3.Lerp(sample.Previous, sample.Current, alpha);

        // Kept in sync with whatever this overload actually returns (e.g. while displaced —
        // see the lenient overload's own doc comment on why callers must use THIS overload,
        // not that one, during a knockback) so that if a caller switches back to the lenient
        // overload afterward (displacement ending), its MoveTowards chase resumes from
        // wherever this was actually last rendered, not from a stale pre-displacement point
        // frozen the last time the lenient overload ran.
        sample.Visual = lerped;
        _samples[entityId] = sample;

        return lerped;
    }

    // Lenient counterpart to the plain Update above — same not-moving/teleported snap
    // behavior, but while genuinely moving under its own power (isMoving true), the returned
    // position continuously CHASES the true, authoritative current tick position (via
    // Vector3.MoveTowards) at up to speedWorldUnitsPerSecond, instead of being strictly
    // confined to a lerp between the two most recent tick samples.
    //
    // An earlier version of this extrapolated forward along a remembered direction/speed
    // instead of chasing Current directly — that's an OPEN-LOOP prediction, and it turned out
    // to be fragile to anything that makes the true tick-to-tick step even slightly irregular:
    // reconciliation replaying predicted ticks after a server correction, PathfindingSystem's
    // waypoint-to-waypoint steps (Vector2.MoveTowards clamps the last step into each
    // waypoint, so even a visually straight path isn't perfectly uniform tick to tick), and
    // the tick catch-up loop compressing several ticks into what looks like one step to this
    // class. Any of those made the open-loop guess run ahead of the truth, which needed a
    // periodic hard "snap back" correction once it drifted too far — that snap was itself the
    // visible teleport.
    //
    // Chasing Current directly (this version) is CLOSED-LOOP and self-correcting by
    // construction: Vector3.MoveTowards can never overshoot its target, so the visual
    // literally cannot get ahead of the true position, and there's nothing left to
    // periodically snap back from. If a burst of ticks lands in one rendered frame, the
    // visual just visibly (and smoothly) catches up over the next few frames instead of
    // teleporting.
    //
    // Callers MUST NOT use this while the entity is displaced (MovableComponent.IsDisplaced)
    // — a knockback's velocity is independent of (and can exceed) the troop's own Speed stat,
    // so chasing at Speed could lag behind a fast knockback; fall back to the plain Update
    // overload for that case (the strict tick-to-tick lerp already tracks a knockback's own
    // step correctly regardless of how fast it is).
    public Vector3 Update(ulong entityId, Vector3 worldPos, bool isMoving, bool teleported,
        float speedWorldUnitsPerSecond)
    {
        if (!isMoving || teleported || speedWorldUnitsPerSecond <= 0f)
        {
            _samples[entityId] = new Sample { Previous = worldPos, Current = worldPos, Visual = worldPos };
            return worldPos;
        }

        Sample sample = AdvanceSample(entityId, worldPos);
        sample.Visual = Vector3.MoveTowards(sample.Visual, sample.Current, speedWorldUnitsPerSecond * Time.deltaTime);

        _samples[entityId] = sample;
        return sample.Visual;
    }

    // Shared "roll Previous/Current forward if the tick sample actually changed" bookkeeping
    // both Update overloads need — Visual carries over unchanged (a brand new entity gets
    // Visual = worldPos, same as Previous/Current).
    private Sample AdvanceSample(ulong entityId, Vector3 worldPos)
    {
        if (!_samples.TryGetValue(entityId, out Sample sample))
            return new Sample { Previous = worldPos, Current = worldPos, Visual = worldPos };

        if (sample.Current != worldPos)
            sample = new Sample { Previous = sample.Current, Current = worldPos, Visual = sample.Visual };

        return sample;
    }

    // Horizontal direction of the most recent tick-to-tick movement step, or
    // Vector3.zero if there isn't one (no sample yet, or not currently moving).
    public Vector3 GetLastMoveDirection(ulong entityId)
    {
        if (!_samples.TryGetValue(entityId, out Sample sample)) return Vector3.zero;
        Vector3 dir = sample.Current - sample.Previous;
        dir.y = 0f;
        return dir;
    }
}
