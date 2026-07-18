using System.Collections.Generic;
using UnityEngine;

// Card kind for "click N ground points, then resolve" cards (e.g. Blink) — sits alongside
// SpawnAtPointCard/TargetEntityCard as another Card-kind extension (see Card.cs), not a
// subclass of either. Unlike SpawnAtPointCard's single indicator, CardPlacementIndicatorManager
// spawns one indicator per point — frozen in place for each point already clicked, plus one
// live indicator following the cursor for the next point not yet placed — and, if this card
// opts in (RenderBetweenPoints), one additional prefab per consecutive pair of points.
public abstract class MultiPointCard : Card
{
    // How many ground points this card needs before OnPlayed can run.
    public abstract int PointCount { get; }

    // Key into IndicatorPrefabRegistry for the prefab shown at each point — see
    // CardPlacementIndicatorManager. Null/empty means no per-point indicator is shown.
    public virtual string IndicatorPrefabName => null;

    // Called once per point, right after CardPlacementIndicatorManager instantiates that
    // point's IndicatorPrefabName instance — so a subtype can customize it (e.g. scale it to
    // reflect a range stat, the way AoeSpellCard does for SpawnAtPointCard's single
    // indicator). pointIndex is which point (0-based) this instance represents.
    public virtual void OnIndicatorSpawned(GameObject indicator, int pointIndex) { }

    // Whether CardPlacementIndicatorManager should also spawn a prefab between every pair of
    // consecutive points — positioned at their midpoint and rotated to face from the first
    // to the second, recomputed every frame as the live (not-yet-placed) point follows the
    // cursor. Off by default.
    public virtual bool RenderBetweenPoints => false;

    // Key into IndicatorPrefabRegistry for the "between two points" prefab — only consulted
    // when RenderBetweenPoints is true.
    public virtual string BetweenIndicatorPrefabName => null;

    // Called once per segment, right after its between-indicator is instantiated.
    // segmentIndex is which segment (0-based — between point segmentIndex and segmentIndex+1).
    public virtual void OnBetweenIndicatorSpawned(GameObject indicator, int segmentIndex) { }

    // Called by MultiPointCardPlaySystem once a player has clicked all PointCount points for
    // this card. cardEntityId is the card entity that was played (recycled back into the
    // deck afterwards, not deleted).
    public abstract void OnPlayed(ECS ecs, ulong cardEntityId, ushort ownerPlayerId, IReadOnlyList<Vector2> points);
}
