using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Tracks each entity's two most recent simulation-tick world positions and produces a
/// frame-smoothed position between them, driven by how far we are into the current
/// tick interval. While an entity is reported as not moving, positions are not
/// interpolated (and the sample is reset to the resting point) — otherwise the last
/// pre-stop step keeps getting re-played every tick interval, which reads as the
/// object lagging back and forth in place.
/// </summary>
public class TickPositionInterpolator
{
    private struct Sample
    {
        public Vector3 Previous;
        public Vector3 Current;
    }

    private readonly Dictionary<ulong, Sample> _samples = new();

    public void Remove(ulong entityId) => _samples.Remove(entityId);

    public Vector3 Update(ulong entityId, Vector3 worldPos, bool isMoving, bool teleported = false)
    {
        // A teleport is an instantaneous, potentially huge jump — lerping across it (which
        // would otherwise happen whenever isMoving is true, e.g. a troop teleported mid-path)
        // reads as a slide/jitter. Snap immediately instead, same as the not-moving case.
        if (!isMoving || teleported)
        {
            _samples[entityId] = new Sample { Previous = worldPos, Current = worldPos };
            return worldPos;
        }

        if (!_samples.TryGetValue(entityId, out Sample sample))
            sample = new Sample { Previous = worldPos, Current = worldPos };
        else if (sample.Current != worldPos)
            sample = new Sample { Previous = sample.Current, Current = worldPos };

        _samples[entityId] = sample;

        float alpha = Mathf.Clamp01(TickManager.instance.TimeSinceLastTick / TickManager.TickInterval);
        return Vector3.Lerp(sample.Previous, sample.Current, alpha);
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
