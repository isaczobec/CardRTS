using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Enqueue after IslandPlayerBaseFeature and MidIslandFeature (see
/// WorldManager.SetupWorldGen) — reads both via GetPreviousFeature. Builds two sets of
/// connections:
///
/// - RING: each player base island to the next one in placement order (island i to island
///   (i+1) % count — "the base to its right"), with RingIntermittentCount waypoint islands
///   threaded along each one. For 3+ players this forms one connected ring; for exactly 2,
///   both islands end up pointed at each other, which is why the curve's bow direction is
///   always derived consistently from its own direction of travel (see TryBuildCandidate) — the two
///   resulting connections bow to OPPOSITE sides of the straight line between them instead of
///   retracing the same path. The ring's bow distance (see ComputeRingBowDistance) is sized
///   from actual map geometry rather than a fixed constant, specifically so it reaches well
///   out toward the map edges even with only 2 bases, instead of hugging the map center.
///
/// - SPOKE: every player base island to the single MidIslandFeature.MidIsland at the map
///   center, with SpokeIntermittentCount waypoint islands threaded along each one.
///
/// Each connection between two ADJACENT islands (which may be a base, the mid island, or a
/// waypoint island placed by this feature — see BuildMultiHopBridge) considers every
/// (fromAnchor, toAnchor) pair drawn from their IslandFootprint.BridgeAnchors (falling back
/// to each footprint's default/center anchor, with a warning, if it has none marked), shortest
/// bridge first, and picks the first one whose curve doesn't overlap a tile already stamped
/// TileType.Bridge by an earlier connection this same generation pass — so bridges route
/// around each other where a free anchor allows it. If every candidate overlaps something, it
/// just uses the shortest one anyway (the "first best slot") rather than leaving the
/// connection unbuilt. Whichever pair wins, the curve is a bowed quadratic Bezier: stamp every
/// tile under its width as TileType.Bridge, then enqueue one BridgeSegmentSpawnAction per
/// BridgeSegmentFootprint.SegmentLength step along it — chaining a straight modular prefab
/// along a sampled curve rather than deforming its mesh.
/// </summary>
public class IslandBridgeFeature : WorldGenFeature
{
    public GameObject[] BridgeSegmentPrefabs;

    // Names looked up in WorldManager.instance.IslandRegistry for waypoint islands placed
    // along a bridge — see RingIntermittentCount/SpokeIntermittentCount.
    public string[] IntermittentIslandNames;

    // Waypoint islands threaded along each ring (base-to-base) / spoke (base-to-mid)
    // connection, evenly spaced along that connection's own curve. 0 disables waypoint
    // islands for that kind of connection (a direct bridge is still built).
    public int RingIntermittentCount = 3;
    public int SpokeIntermittentCount = 1;

    // Perpendicular distance (world/tile units) a single ring hop's own curve is bowed by —
    // used for every individual hop within a ring connection's chain (between its own
    // waypoint islands), once the chain's overall shape has already been laid out by
    // ComputeRingBowDistance. NOT used for spoke connections — see SpokeBowDistance.
    public float BowDistance = 6f;

    // Bow used for spoke (base-to-mid-island) connections — both for laying out their
    // waypoint island and for each individual hop. 0 (the default) makes a spoke completely
    // straight: a quadratic Bezier whose control point sits exactly on the chord midpoint is
    // mathematically identical to a straight line (see TryBuildCandidate), so this needs no
    // special-casing beyond just passing 0 through the same curve math ring connections use.
    public float SpokeBowDistance = 0f;

    // How close a ring connection's bow is allowed to bring its peak to the map's edge —
    // see ComputeRingBowDistance.
    public float RingBowEdgeMargin = 10f;

    // Caps a ring connection's bow at this multiple of the straight-line distance between
    // its two islands, so a short hop between adjacent bases in a large ring doesn't bow out
    // absurdly far relative to its own length just because the map has room for it — see
    // ComputeRingBowDistance.
    public float RingBowChordMultiplier = 1.5f;

    // How far (world/tile units) past each literal anchor point the STAMPED tiles (not the
    // visual segments — see IterateCurveTiles) extend, along the curve's own local direction.
    // Without this, the tile nearest an anchor can occasionally fail to get stamped at all:
    // an anchor point always sits exactly on a tile's .5 center, and Unity's Mathf.RoundToInt
    // uses round-half-to-even, so which of the two neighboring tiles a sample landing exactly
    // on that boundary rounds to depends on that tile's parity — sometimes the "wrong" one,
    // leaving a 1-tile gap between the bridge and the island it's meant to connect to. A
    // small overshoot at both ends makes that gap impossible regardless of which way any
    // individual sample happens to round.
    public float BridgePaddingTiles = 1f;

    // Tile-stamping resolution along a curve's length — smaller reads as a smoother curve in
    // the underlying tile data (and therefore the navmesh built from it, and the overlap
    // check below) at the cost of more SetTileType/GetTileType calls; the visual curve
    // (segment placement) is independent of this and always exactly follows the math.
    private const float TileStampStep = 0.5f;

    // A fully-formed candidate curve between two anchor points, kept around (rather than
    // just the two endpoints) so the exact curve tested for overlap is the exact curve that
    // ends up stamped/built — recomputing it afterwards from the same inputs would give the
    // same numbers, but there's no reason to take that on faith.
    private readonly struct BridgeCandidate
    {
        public readonly Vector2 Start;
        public readonly Vector2 End;
        public readonly Vector2 Control;
        public readonly float Length;

        public BridgeCandidate(Vector2 start, Vector2 end, Vector2 control, float length)
        {
            Start = start;
            End = end;
            Control = control;
            Length = length;
        }
    }

    public override void Generate(WorldGenHandler handler)
    {
        var islandsFeature = handler.GetPreviousFeature<IslandPlayerBaseFeature>();
        if (islandsFeature == null || islandsFeature.Islands.Count == 0) return;

        if (BridgeSegmentPrefabs == null || BridgeSegmentPrefabs.Length == 0)
        {
            Debug.LogWarning("[IslandBridgeFeature] No BridgeSegmentPrefabs assigned — skipping.");
            return;
        }

        List<GameObject> intermittentPrefabs = ResolveIntermittentPrefabs();

        IReadOnlyList<IslandPlayerBaseFeature.IslandPlacement> bases = islandsFeature.Islands;
        ushort worldSize = (ushort)(WorldGenHandler.CHUNK_SIZE_TILES * WorldGenHandler.WorldSizeChunks);

        // Ring: base i -> base (i+1) % count. Macro layout (where the waypoint islands go)
        // uses ComputeRingBowDistance's map-edge-aware bow; each individual hop between
        // consecutive islands in the resulting chain uses the plain, modest BowDistance.
        if (bases.Count >= 2)
        {
            for (int i = 0; i < bases.Count; i++)
            {
                IslandPlacementHelper.PlacedIsland from = bases[i].Island;
                IslandPlacementHelper.PlacedIsland to = bases[(i + 1) % bases.Count].Island;
                float ringBow = ComputeRingBowDistance(from, to, worldSize);
                BuildMultiHopBridge(handler, from, to, RingIntermittentCount, ringBow, BowDistance, intermittentPrefabs, worldSize);
            }
        }

        // Spoke: every base -> the mid island. Both the macro layout and every individual
        // hop use SpokeBowDistance (0 by default) — see that field's own doc comment.
        var midFeature = handler.GetPreviousFeature<MidIslandFeature>();
        if (midFeature != null && midFeature.MidIsland.HasValue)
        {
            IslandPlacementHelper.PlacedIsland mid = midFeature.MidIsland.Value;
            foreach (IslandPlayerBaseFeature.IslandPlacement b in bases)
                BuildMultiHopBridge(handler, b.Island, mid, SpokeIntermittentCount, SpokeBowDistance, SpokeBowDistance, intermittentPrefabs, worldSize);
        }
    }

    private List<GameObject> ResolveIntermittentPrefabs()
    {
        if (RingIntermittentCount <= 0 && SpokeIntermittentCount <= 0) return new List<GameObject>();

        IslandRegistry registry = WorldManager.instance != null ? WorldManager.instance.IslandRegistry : null;
        if (registry == null)
        {
            Debug.LogWarning("[IslandBridgeFeature] No IslandRegistry assigned on WorldManager — bridges will have no intermittent islands.");
            return new List<GameObject>();
        }

        List<GameObject> prefabs = registry.GetPrefabs(IntermittentIslandNames);
        if (prefabs.Count == 0)
            Debug.LogWarning("[IslandBridgeFeature] None of IntermittentIslandNames resolved to a registered island — bridges will have no intermittent islands.");
        return prefabs;
    }

    // Sizes a ring connection's bow (SIGNED — see the sign/dead-zone handling below) from
    // actual map geometry instead of a fixed constant, so it reaches meaningfully toward the
    // map's edges rather than hugging the center — most noticeable (and most requested) for
    // exactly 2 players, where a fixed small bow would otherwise route both of the ring's
    // connections through a tight loop near the middle of the map instead of using its width.
    // The target is a uniform circular arc (an inscribed circle, not the square world's actual
    // corners) — a direction-dependent square-boundary target was tried and reverted: reaching
    // further into corners along diagonal chords read as inconsistent/uneven rather than like
    // a clean top/bot-lane arc, and IslandPlacementHelper.TryPlaceIsland now clamps any
    // resulting position to the world's actual bounds regardless, so nothing can end up
    // off-map even at the edge of this circle.
    //
    // desiredPeakOffset is "how much further the curve's peak needs to travel, from where the
    // CHORD MIDPOINT already sits, to reach that circle" — critically, measured from the chord
    // midpoint's own distance from the map center, NOT from ringRadius: for 2 players the two
    // bases are diametrically opposite, so their chord midpoint IS the map center (distance
    // ~0), while for adjacent bases in a bigger ring the midpoint already sits much closer to
    // the ring than to the center. Using ringRadius here (an earlier version of this method
    // did) systematically undersized the 2-player case, since it subtracted a "bases are
    // already this far out" allowance that doesn't apply when the midpoint is actually still
    // at the center. Doubled into reachBow since a quadratic Bezier's actual peak sag from the
    // chord midpoint is only half its control point's own offset (see TryBuildCandidate).
    // Capped at RingBowChordMultiplier times the two islands' straight-line distance so a
    // short hop between adjacent bases in a large ring doesn't bow out absurdly far just
    // because the map has room for it.
    //
    // Sign: raw "rotate 90 degrees" alone only happens to point outward (away from the map
    // center) for a chord that passes through the center (the 2-player case) — for adjacent
    // bases in a 3+ ring it can just as easily point inward, which would bow the curve the
    // wrong way. Flipping it to whichever side actually points away from the map center fixes
    // that, EXCEPT within a small dead zone around chord midpoints that are already ~on the
    // center: there, "outward" is ambiguous (either side is equally valid) and flipping based
    // on a near-zero, rounding-noise-dominated offset would collapse the ring's two 2-player
    // connections onto the SAME side instead of opposite sides, breaking the anti-overlap
    // trick TryBuildCandidate's own raw (unflipped) rotation already guarantees on its own.
    private float ComputeRingBowDistance(IslandPlacementHelper.PlacedIsland from, IslandPlacementHelper.PlacedIsland to, ushort worldSize)
    {
        Vector2 fromCenter = from.CenterWorldPosition;
        Vector2 toCenter = to.CenterWorldPosition;
        Vector2 direction = toCenter - fromCenter;
        if (direction.sqrMagnitude < 0.0001f) return 0f;

        Vector2 perpendicular = new Vector2(-direction.y, direction.x).normalized;
        float chordLength = direction.magnitude;

        Vector2 mapCenter = new Vector2(worldSize * 0.5f, worldSize * 0.5f);
        Vector2 chordMid = (fromCenter + toCenter) * 0.5f;
        float outwardOffset = Vector2.Dot(chordMid - mapCenter, perpendicular);

        const float outwardDeadZone = 2f;
        float sign = outwardOffset < -outwardDeadZone ? -1f : 1f;
        float chordMidOffsetAlongOutward = Mathf.Abs(outwardOffset) < outwardDeadZone ? 0f : Mathf.Abs(outwardOffset);

        float maxUsableRadius = worldSize * 0.5f - RingBowEdgeMargin;
        float desiredPeakOffset = Mathf.Max(0f, maxUsableRadius - chordMidOffsetAlongOutward);
        float reachBow = desiredPeakOffset * 2f;
        float magnitude = Mathf.Min(reachBow, chordLength * RingBowChordMultiplier);
        return magnitude * sign;
    }

    // Builds one connection between two islands, threading intermittentCount waypoint islands
    // along the way. The curve from->to (bowed by macroBowDistance) is used ONLY to decide
    // where those waypoint islands go (evenly spaced by arc parameter, not stamped or
    // segmented itself) — the actual bridge geometry comes from BuildSingleConnection, called
    // once per consecutive pair in the resulting chain (from -> waypoint1 -> ... -> to), each
    // hop bowed by hopBowDistance and picking its own anchors/overlap-avoidance independently.
    // This two-level approach (one big curve to lay out islands, several small independently-
    // built hops between them) means the overall path reads as one curved bridge without
    // needing every hop's anchor pick to agree on a single continuous curve, which would be
    // far more brittle once overlap-avoidance can make any individual hop deviate from it.
    private void BuildMultiHopBridge(WorldGenHandler handler, IslandPlacementHelper.PlacedIsland from, IslandPlacementHelper.PlacedIsland to,
        int intermittentCount, float macroBowDistance, float hopBowDistance, List<GameObject> intermittentPrefabs, ushort worldSize)
    {
        var chain = new List<IslandPlacementHelper.PlacedIsland> { from };

        if (intermittentCount > 0 && intermittentPrefabs.Count > 0 &&
            TryBuildCandidate(from.CenterWorldPosition, to.CenterWorldPosition, macroBowDistance, out BridgeCandidate layoutCurve))
        {
            for (int k = 1; k <= intermittentCount; k++)
            {
                float t = k / (float)(intermittentCount + 1);
                Vector2 point = Bezier(layoutCurve.Start, layoutCurve.Control, layoutCurve.End, t);
                GameObject prefab = intermittentPrefabs[handler.Random.Next(intermittentPrefabs.Count)];

                IslandPlacementHelper.PlacedIsland? placed = IslandPlacementHelper.TryPlaceIsland(
                    handler, prefab, Mathf.RoundToInt(point.x), Mathf.RoundToInt(point.y), worldSize);

                if (placed.HasValue)
                    chain.Add(placed.Value);
                else
                    Debug.LogWarning($"[IslandBridgeFeature] Prefab '{(prefab != null ? prefab.name : "null")}' has no IslandFootprint component — skipping this waypoint island.");
            }
        }

        chain.Add(to);

        for (int i = 0; i < chain.Count - 1; i++)
            BuildSingleConnection(handler, chain[i], chain[i + 1], hopBowDistance, worldSize);
    }

    // Builds one hop between two adjacent islands in a chain: picks the best (shortest,
    // preferring no overlap with an already-placed bridge) anchor pair bowed by bowDistance,
    // then stamps/segments exactly that curve. See this feature's own doc comment for the
    // full selection rules.
    private void BuildSingleConnection(WorldGenHandler handler, IslandPlacementHelper.PlacedIsland from, IslandPlacementHelper.PlacedIsland to, float bowDistance, ushort worldSize)
    {
        GameObject prefab = BridgeSegmentPrefabs[handler.Random.Next(BridgeSegmentPrefabs.Length)];
        BridgeSegmentFootprint segmentInfo = prefab != null ? prefab.GetComponent<BridgeSegmentFootprint>() : null;
        if (segmentInfo == null)
        {
            Debug.LogWarning($"[IslandBridgeFeature] Prefab '{(prefab != null ? prefab.name : "null")}' has no BridgeSegmentFootprint component — skipping this connection.");
            return;
        }

        List<Vector2> fromAnchors = GatherAnchorWorldPositions(from);
        List<Vector2> toAnchors = GatherAnchorWorldPositions(to);

        List<BridgeCandidate> candidates = new List<BridgeCandidate>();
        foreach (Vector2 start in fromAnchors)
            foreach (Vector2 end in toAnchors)
                if (TryBuildCandidate(start, end, bowDistance, out BridgeCandidate candidate))
                    candidates.Add(candidate);

        // Every anchor pair produced the same (degenerate) point on both islands — nothing
        // sensible to build.
        if (candidates.Count == 0) return;

        // Shortest bridge first: the natural "best slot" ordering when there's nothing else
        // to prefer between candidates, and what the fallback below reduces to once overlap
        // is factored in.
        candidates.Sort((a, b) => a.Length.CompareTo(b.Length));

        BridgeCandidate chosen = candidates[0];
        foreach (BridgeCandidate candidate in candidates)
        {
            if (CurveOverlapsExistingBridge(handler, candidate, segmentInfo.SegmentWidth, BridgePaddingTiles, worldSize))
                continue;
            chosen = candidate;
            break;
        }
        // If every candidate overlapped an already-placed bridge, chosen is still
        // candidates[0] from the initial assignment above — the shortest pair regardless of
        // overlap, i.e. placing it at the first best slot as a last resort.

        StampTiles(handler, chosen, segmentInfo.SegmentWidth, BridgePaddingTiles, worldSize);
        PlaceSegments(handler, chosen, prefab, segmentInfo, worldSize);
    }

    // Every one of the island's BridgeAnchors, converted to world positions — or, if it has
    // none marked, a single-element fallback list containing its default (center) anchor
    // cell (with a warning), so a connection can still be built rather than silently failing.
    private static List<Vector2> GatherAnchorWorldPositions(IslandPlacementHelper.PlacedIsland island)
    {
        IslandFootprint footprint = island.Footprint;
        var result = new List<Vector2>();

        foreach (Vector2Int cell in footprint.BridgeAnchors)
            result.Add(footprint.AnchorWorldPosition(cell, island.OriginX, island.OriginY));

        if (result.Count == 0)
        {
            Debug.LogWarning($"[IslandBridgeFeature] Island at tile ({island.OriginX}, {island.OriginY}) has no IslandFootprint.BridgeAnchors marked — falling back to its default (center) anchor cell.");
            result.Add(footprint.AnchorWorldPosition(footprint.BaseAnchorOrDefault(), island.OriginX, island.OriginY));
        }

        return result;
    }

    private static bool TryBuildCandidate(Vector2 start, Vector2 end, float bowDistance, out BridgeCandidate candidate)
    {
        Vector2 direction = end - start;
        if (direction.sqrMagnitude < 0.0001f)
        {
            candidate = default;
            return false;
        }

        // Rotating the direction 90 degrees the same way every time is what makes the
        // reverse connection (the other island's own bridge back to this one, for a
        // 2-player ring) bow to the opposite side instead of retracing exactly the same
        // curve: reversing start/end negates direction, which negates this perpendicular
        // too — see this feature's own doc comment.
        Vector2 perpendicular = new Vector2(-direction.y, direction.x).normalized;
        Vector2 control = (start + end) * 0.5f + perpendicular * bowDistance;
        float length = EstimateBezierLength(start, control, end);

        candidate = new BridgeCandidate(start, end, control, length);
        return true;
    }

    private void PlaceSegments(WorldGenHandler handler, BridgeCandidate curve, GameObject prefab, BridgeSegmentFootprint segmentInfo, ushort worldSize)
    {
        int segmentCount = Mathf.Max(1, Mathf.RoundToInt(curve.Length / segmentInfo.SegmentLength));

        // segmentCount is the closest whole number of SegmentLength-sized pieces, but the
        // curve's actual length is essentially never an exact multiple of it — stretching
        // each instance's length (Z) axis by however much its own actual slice differs from
        // the prefab's authored SegmentLength closes that gap exactly, rather than leaving a
        // small gap or overlap at the end of the chain. Width/height (X/Y) are left alone.
        float actualSegmentLength = curve.Length / segmentCount;
        float lengthScale = segmentInfo.SegmentLength > 0f ? actualSegmentLength / segmentInfo.SegmentLength : 1f;
        Vector3 segmentScale = new Vector3(1f, 1f, lengthScale);

        for (int s = 0; s < segmentCount; s++)
        {
            float t0 = s / (float)segmentCount;
            float t1 = (s + 1) / (float)segmentCount;
            Vector2 p0 = Bezier(curve.Start, curve.Control, curve.End, t0);
            Vector2 p1 = Bezier(curve.Start, curve.Control, curve.End, t1);
            Vector2 mid = (p0 + p1) * 0.5f;
            Vector2 tangent = (p1 - p0).normalized;

            ushort heightTileX = (ushort)Mathf.Clamp(Mathf.RoundToInt(mid.x), 0, worldSize - 1);
            ushort heightTileY = (ushort)Mathf.Clamp(Mathf.RoundToInt(mid.y), 0, worldSize - 1);
            float height = handler.GetHeight(heightTileX, heightTileY);

            Vector3 worldPosition = new Vector3(mid.x, height + segmentInfo.HeightOffset, mid.y);
            Quaternion rotation = tangent.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(new Vector3(tangent.x, 0f, tangent.y))
                : Quaternion.identity;

            handler.EnqueueAction(new BridgeSegmentSpawnAction { Prefab = prefab, WorldPosition = worldPosition, Rotation = rotation, Scale = segmentScale });
        }
    }

    // Marks every tile within halfWidth of the curve (measured perpendicular to its local
    // tangent, not just the sampled centerline points themselves) as TileType.Bridge, so the
    // navmesh built from these tiles is exactly as wide as the bridge actually reads
    // visually — a 1-tile-wide navmesh strip on a visually 3-tile-wide bridge would let
    // troops path (and look like they're walking) off the edge.
    private static void StampTiles(WorldGenHandler handler, BridgeCandidate curve, float width, float paddingTiles, ushort worldSize)
    {
        foreach ((int tx, int ty) in IterateCurveTiles(curve, width, paddingTiles, worldSize))
            handler.SetTileType((ushort)tx, (ushort)ty, TileType.Bridge);
    }

    // True if any tile this candidate curve would stamp (at the given width) is already
    // TileType.Bridge — i.e. claimed by a connection BuildSingleConnection already finished
    // earlier in this same Generate() pass. Only ever sees earlier bridges, never this one
    // (nothing is written for a candidate until BuildSingleConnection has already picked it),
    // and never flags an island's own Island tiles, since those are a different TileType.
    private static bool CurveOverlapsExistingBridge(WorldGenHandler handler, BridgeCandidate curve, float width, float paddingTiles, ushort worldSize)
    {
        foreach ((int tx, int ty) in IterateCurveTiles(curve, width, paddingTiles, worldSize))
            if (handler.GetTileType((ushort)tx, (ushort)ty) == TileType.Bridge)
                return true;
        return false;
    }

    // Shared sampling walk along a candidate curve's full width, in-bounds tiles only —
    // StampTiles writes every one of these, CurveOverlapsExistingBridge just reads them (and
    // stops at the first hit), so both go through the exact same set of tiles for a given
    // curve/width rather than two independently-written loops that could quietly drift apart.
    //
    // Sampled t range is extended by paddingTiles (converted to a t delta via the curve's own
    // estimated length) past both literal endpoints — see BridgePaddingTiles's own doc
    // comment for why an exact [0,1] range isn't reliable on its own. Capped at 0.5 either
    // way so a very short curve's padding can't dominate/wildly extrapolate the Bezier well
    // past where it's actually a sensible approximation of "a bit further in this direction".
    private static IEnumerable<(int x, int y)> IterateCurveTiles(BridgeCandidate curve, float width, float paddingTiles, ushort worldSize)
    {
        float deltaT = curve.Length > 0.0001f ? Mathf.Min(0.5f, paddingTiles / curve.Length) : 0f;
        float tStart = -deltaT;
        float tRange = 1f + deltaT * 2f;

        int steps = Mathf.Max(1, Mathf.CeilToInt(curve.Length * tRange / TileStampStep));
        float halfWidth = Mathf.Max(0.5f, width * 0.5f);

        for (int i = 0; i <= steps; i++)
        {
            float t = tStart + tRange * i / (float)steps;
            Vector2 p = Bezier(curve.Start, curve.Control, curve.End, t);
            Vector2 tangent = BezierTangent(curve.Start, curve.Control, curve.End, t);
            Vector2 normal = tangent.sqrMagnitude > 0.0001f ? new Vector2(-tangent.y, tangent.x).normalized : Vector2.zero;

            for (float o = -halfWidth; o <= halfWidth; o += TileStampStep)
            {
                Vector2 sample = p + normal * o;
                int tx = Mathf.RoundToInt(sample.x);
                int ty = Mathf.RoundToInt(sample.y);
                if (tx < 0 || ty < 0 || tx >= worldSize || ty >= worldSize) continue;
                yield return (tx, ty);
            }
        }
    }

    private static float EstimateBezierLength(Vector2 start, Vector2 control, Vector2 end, int samples = 16)
    {
        float length = 0f;
        Vector2 prev = start;
        for (int i = 1; i <= samples; i++)
        {
            Vector2 p = Bezier(start, control, end, i / (float)samples);
            length += Vector2.Distance(prev, p);
            prev = p;
        }
        return length;
    }

    private static Vector2 Bezier(Vector2 a, Vector2 b, Vector2 c, float t)
    {
        float u = 1f - t;
        return u * u * a + 2f * u * t * b + t * t * c;
    }

    // Derivative of the quadratic Bezier above — direction of travel at parameter t, not
    // yet normalized (Bezier(a,b,c,t)'s tangent).
    private static Vector2 BezierTangent(Vector2 a, Vector2 b, Vector2 c, float t)
        => 2f * (1f - t) * (b - a) + 2f * t * (c - b);
}
