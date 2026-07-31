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
/// strict two-sample lerp. The extra overload below — Update(..., speedWorldUnitsPerSecond,
/// ...) — is more lenient: instead of being strictly locked to a lerp between the last two
/// tick samples, the returned position keeps extrapolating along the most recently observed
/// tick-to-tick direction every frame, independent of exactly when the next tick sample
/// lands. See that overload's own doc comment for why (irregular tick cadence — e.g.
/// TickManager's own catch-up loop running several ticks in one rendered frame after a stall
/// — makes the strict lerp jump/snap almost every frame instead of smoothly gliding, since a
/// whole burst of ticks can land between two rendered frames with no intermediate alpha left
/// to lerp across).
/// </summary>
public class TickPositionInterpolator
{
    private struct Sample
    {
        public Vector3 Previous;
        public Vector3 Current;

        // Extra state for the lenient overload only — the actual visual position (which can
        // differ from a strict Previous/Current lerp) and how long it's currently been
        // drifting too far from Current (the true, authoritative tick-sampled position).
        public Vector3 Visual;
        public float DriftSeconds;
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
        _samples[entityId] = sample;

        float alpha = Mathf.Clamp01(TickManager.instance.TimeSinceLastTick / TickManager.TickInterval);
        return Vector3.Lerp(sample.Previous, sample.Current, alpha);
    }

    // Lenient counterpart to the plain Update above — same not-moving/teleported snap
    // behavior, but while genuinely moving under its own power (isMoving true), the returned
    // position keeps extrapolating along the direction of the most recent actual tick-to-tick
    // step, at up to speedWorldUnitsPerSecond, instead of being strictly confined to a lerp
    // between the two most recent tick samples.
    //
    // Deliberately extrapolates the last observed STEP direction rather than aiming straight
    // at the entity's final MovableComponent destination — PathfindingSystem walks a computed
    // path that can bend around obstacles, so a beeline to a far-off destination is shorter
    // than the true path and consistently cuts the corner, running the visual ahead of the
    // real position until a hard correction snaps it back (this was tried and produced
    // exactly that "runs fast, periodically teleports back" artifact). Extrapolating only the
    // most recent local step tracks the true path closely as long as it stays straight, and
    // lets the correction below handle it whenever a turn (or a stop, stun, teleport, etc.)
    // invalidates that guess. Height (Y) is snapped straight to the true current tick's
    // height every call rather than extrapolated — terrain height isn't a straight-line
    // function of horizontal distance, so guessing it would just reintroduce the same kind of
    // error this whole overload exists to avoid.
    //
    // Trade-off: the visual position is no longer a pure function of the last two tick
    // samples, so it CAN still drift from the true, authoritative one — e.g. the path turns a
    // corner, the entity gets stunned/rooted mid-step, or a reconciliation replay nudges it.
    // maxDriftDistance/maxDriftSeconds bound that: once the visual position has been further
    // than maxDriftDistance from the true tick-sampled position for longer than
    // maxDriftSeconds, it's snapped straight back to truth rather than left to keep drifting.
    // A single frame (or even a few tenths of a second) of minor drift is invisible/
    // unimportant; a large or sustained one reads as the visual having "lost track" of the
    // entity and needs correcting.
    //
    // Callers MUST NOT use this while the entity is displaced (MovableComponent.IsDisplaced)
    // — a knockback's direction can differ tick-to-tick from ordinary path-following motion
    // in ways this overload has no way to anticipate; fall back to the plain Update overload
    // for that case (the strict tick-to-tick lerp already tracks a knockback's constant-
    // velocity step correctly on its own, so there's nothing for the lenient path to improve
    // there anyway).
    public Vector3 Update(ulong entityId, Vector3 worldPos, bool isMoving, bool teleported,
        float speedWorldUnitsPerSecond,
        float maxDriftDistance = 1.5f, float maxDriftSeconds = 0.5f)
    {
        if (!isMoving || teleported || speedWorldUnitsPerSecond <= 0f)
        {
            _samples[entityId] = new Sample { Previous = worldPos, Current = worldPos, Visual = worldPos };
            return worldPos;
        }

        Sample sample = AdvanceSample(entityId, worldPos);

        Vector3 direction = sample.Current - sample.Previous;
        direction.y = 0f;
        if (direction.sqrMagnitude > 0.0001f)
            sample.Visual += direction.normalized * speedWorldUnitsPerSecond * Time.deltaTime;
        sample.Visual.y = sample.Current.y;

        float error = Vector3.Distance(sample.Visual, sample.Current);
        sample.DriftSeconds = error > maxDriftDistance ? sample.DriftSeconds + Time.deltaTime : 0f;

        if (sample.DriftSeconds > maxDriftSeconds)
        {
            sample.Visual = sample.Current;
            sample.DriftSeconds = 0f;
        }

        _samples[entityId] = sample;
        return sample.Visual;
    }

    // Shared "roll Previous/Current forward if the tick sample actually changed" bookkeeping
    // both Update overloads need — Visual/DriftSeconds carry over unchanged (a brand new
    // entity gets Visual = worldPos, same as Previous/Current).
    private Sample AdvanceSample(ulong entityId, Vector3 worldPos)
    {
        if (!_samples.TryGetValue(entityId, out Sample sample))
            return new Sample { Previous = worldPos, Current = worldPos, Visual = worldPos };

        if (sample.Current != worldPos)
            sample = new Sample { Previous = sample.Current, Current = worldPos, Visual = sample.Visual, DriftSeconds = sample.DriftSeconds };

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
