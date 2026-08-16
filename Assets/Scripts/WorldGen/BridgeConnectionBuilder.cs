using System.Collections.Generic;
using UnityEngine;

// Shared "connect two islands with a bowed, overlap-aware bridge" logic, used by both
// IslandBridgeFeature (the main ring/spoke network, which always builds SOMETHING for every
// connection — falling back to an overlapping candidate rather than leaving one unbuilt) and
// WedgeIslandFeature (which requires a genuinely non-overlapping connection before it will
// even place the island in the first place — see TryFindConnection's allowOverlapFallback).
public static class BridgeConnectionBuilder
{
    // Tile-stamping resolution along a curve's length — smaller reads as a smoother curve in
    // the underlying tile data (and therefore the navmesh built from it, and the overlap
    // check below) at the cost of more SetTileType/GetTileType calls; the visual curve
    // (segment placement) is independent of this and always exactly follows the math.
    private const float TileStampStep = 0.5f;

    // A fully-formed candidate curve between two anchor points, kept around (rather than
    // just the two endpoints) so the exact curve tested for overlap is the exact curve that
    // ends up stamped/built — recomputing it afterwards from the same inputs would give the
    // same numbers, but there's no reason to take that on faith.
    public readonly struct BridgeCandidate
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

    // Considers every (fromAnchor, toAnchor) pair drawn from the two islands'
    // IslandFootprint.BridgeAnchors (falling back to each footprint's default/center anchor,
    // with a warning, if it has none marked), shortest bridge first, and returns the first
    // one whose curve doesn't overlap a tile already stamped TileType.Bridge. If every
    // candidate overlaps something: with allowOverlapFallback true, returns the shortest one
    // anyway (the "first best slot" — what IslandBridgeFeature's ring/spoke network wants,
    // since every one of its connections must exist); with it false, returns false and
    // builds/commits nothing (what WedgeIslandFeature wants — a filler island with no clean
    // connection available simply isn't placed).
    public static bool TryFindConnection(WorldGenHandler handler, IslandPlacementHelper.PlacedIsland from, IslandPlacementHelper.PlacedIsland to,
        float bowDistance, float segmentWidth, float paddingTiles, ushort worldSize, bool allowOverlapFallback, out BridgeCandidate chosen)
    {
        List<Vector2> fromAnchors = GatherAnchorWorldPositions(from);
        List<Vector2> toAnchors = GatherAnchorWorldPositions(to);

        List<BridgeCandidate> candidates = new List<BridgeCandidate>();
        foreach (Vector2 start in fromAnchors)
            foreach (Vector2 end in toAnchors)
                if (TryBuildCandidate(start, end, bowDistance, out BridgeCandidate candidate))
                    candidates.Add(candidate);

        if (candidates.Count == 0)
        {
            chosen = default;
            return false;
        }

        // Shortest bridge first: the natural "best slot" ordering when there's nothing else
        // to prefer between candidates.
        candidates.Sort((a, b) => a.Length.CompareTo(b.Length));

        foreach (BridgeCandidate candidate in candidates)
        {
            if (CurveOverlapsExistingBridge(handler, candidate, segmentWidth, paddingTiles, worldSize))
                continue;
            chosen = candidate;
            return true;
        }

        if (allowOverlapFallback)
        {
            chosen = candidates[0];
            return true;
        }

        chosen = default;
        return false;
    }

    // Builds the bowed curve between two raw points (not necessarily anchor points — e.g.
    // IslandBridgeFeature uses this directly, with island CENTERS, to lay out where a
    // connection's waypoint islands should go, well before any anchor has been picked).
    // Exposed publicly for exactly that "just the curve math" use; TryFindConnection above is
    // the anchor-aware, overlap-checked entry point everything else should prefer.
    public static bool TryBuildCandidate(Vector2 start, Vector2 end, float bowDistance, out BridgeCandidate candidate)
    {
        Vector2 direction = end - start;
        if (direction.sqrMagnitude < 0.0001f)
        {
            candidate = default;
            return false;
        }

        // Rotating the direction 90 degrees the same way every time is what makes a reverse
        // connection (the same two islands, built the other way around) bow to the opposite
        // side instead of retracing exactly the same curve: reversing start/end negates
        // direction, which negates this perpendicular too.
        Vector2 perpendicular = new Vector2(-direction.y, direction.x).normalized;
        Vector2 control = (start + end) * 0.5f + perpendicular * bowDistance;
        float length = EstimateBezierLength(start, control, end);

        candidate = new BridgeCandidate(start, end, control, length);
        return true;
    }

    // Point at parameter t (0 = Start, 1 = End) along a candidate's curve.
    public static Vector2 SamplePoint(BridgeCandidate curve, float t) => Bezier(curve.Start, curve.Control, curve.End, t);

    // Stamps the chosen curve's tiles as TileType.Bridge and enqueues one
    // BridgeSegmentSpawnAction per BridgeSegmentFootprint.SegmentLength step along it —
    // chaining a straight modular prefab along a sampled curve rather than deforming its
    // mesh. Call only once TryFindConnection has actually returned true for this candidate.
    // islandARegionId/islandBRegionId (see IslandPlacementHelper.PlacedIsland.RegionId) are
    // the two islands this connection joins — registered as a new bridge region in the
    // coarse island graph (see IslandGraphBuilder.BeginBridge) before its tiles are tagged.
    public static void Commit(WorldGenHandler handler, BridgeCandidate chosen, GameObject prefab, BridgeSegmentFootprint segmentInfo, float paddingTiles, ushort worldSize,
        int islandARegionId, int islandBRegionId)
    {
        int bridgeRegionId = handler.IslandGraph.BeginBridge(islandARegionId, islandBRegionId, (chosen.Start + chosen.End) * 0.5f, chosen.Length);
        StampTiles(handler, chosen, segmentInfo.SegmentWidth, paddingTiles, worldSize, bridgeRegionId);
        PlaceSegments(handler, chosen, prefab, segmentInfo, paddingTiles, worldSize);
    }

    // Every one of the island's BridgeAnchors, converted to world positions — or, if it has
    // none marked, a single-element fallback list containing its default (center) anchor
    // cell (with a warning), so a connection can still be attempted rather than silently
    // failing. Reads through RotatedIslandFootprint (not IslandFootprint directly) so a
    // rotated island's anchors reflect wherever they actually ended up facing, not their
    // as-authored positions.
    private static List<Vector2> GatherAnchorWorldPositions(IslandPlacementHelper.PlacedIsland island)
    {
        RotatedIslandFootprint footprint = island.RotatedFootprint;
        var result = new List<Vector2>();

        foreach (Vector2Int cell in footprint.BridgeAnchors)
            result.Add(footprint.AnchorWorldPosition(cell, island.OriginX, island.OriginY));

        if (result.Count == 0)
        {
            Debug.LogWarning($"[BridgeConnectionBuilder] Island at tile ({island.OriginX}, {island.OriginY}) has no IslandFootprint.BridgeAnchors marked — falling back to its default (center) anchor cell.");
            result.Add(footprint.AnchorWorldPosition(footprint.BaseAnchorOrDefault(), island.OriginX, island.OriginY));
        }

        return result;
    }

    // Same paddingTiles-into-a-t-range overshoot as IterateCurveTiles, applied to the VISUAL
    // segment chain too — a bridge's tiles used to overshoot into the island (for
    // walkability) while its segments stopped exactly at the literal anchor point, which
    // still left a visible gap whenever an anchor was marked even slightly shy of a small
    // island's actual mesh silhouette. Both now overshoot together, by the same amount.
    private static void PlaceSegments(WorldGenHandler handler, BridgeCandidate curve, GameObject prefab, BridgeSegmentFootprint segmentInfo, float paddingTiles, ushort worldSize)
    {
        (float tStart, float tRange) = PaddedTRange(curve, paddingTiles);
        float paddedLength = curve.Length * tRange;

        int segmentCount = Mathf.Max(1, Mathf.RoundToInt(paddedLength / segmentInfo.SegmentLength));

        // segmentCount is the closest whole number of SegmentLength-sized pieces, but the
        // padded curve's actual length is essentially never an exact multiple of it —
        // stretching each instance's length (Z) axis by however much its own actual slice
        // differs from the prefab's authored SegmentLength closes that gap exactly, rather
        // than leaving a small gap or overlap at the end of the chain. Width/height (X/Y)
        // are left alone.
        float actualSegmentLength = paddedLength / segmentCount;
        float lengthScale = segmentInfo.SegmentLength > 0f ? actualSegmentLength / segmentInfo.SegmentLength : 1f;
        Vector3 segmentScale = new Vector3(1f, 1f, lengthScale);

        for (int s = 0; s < segmentCount; s++)
        {
            float t0 = tStart + tRange * s / (float)segmentCount;
            float t1 = tStart + tRange * (s + 1) / (float)segmentCount;
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
    // troops path (and look like they're walking) off the edge. Never downgrades an existing
    // TileType.Island tile to Bridge — both are equally walkable, so this has no gameplay
    // effect, but the padded overshoot into each island's own footprint (see PaddedTRange)
    // would otherwise visibly bite a strip of Bridge color into what should read as solid
    // island on a minimap that colors tiles by type.
    private static void StampTiles(WorldGenHandler handler, BridgeCandidate curve, float width, float paddingTiles, ushort worldSize, int regionId)
    {
        foreach ((int tx, int ty) in IterateCurveTiles(curve, width, paddingTiles, worldSize))
        {
            if (handler.GetTileType((ushort)tx, (ushort)ty) == TileType.Island) continue;
            handler.SetTileType((ushort)tx, (ushort)ty, TileType.Bridge);
            handler.IslandGraph.MarkTile(regionId, tx, ty);
        }
    }

    // True if any tile this candidate curve would stamp (at the given width) is already
    // TileType.Bridge — i.e. claimed by a connection already committed earlier in this same
    // generation pass. Never flags an island's own Island tiles, since those are a different
    // TileType.
    private static bool CurveOverlapsExistingBridge(WorldGenHandler handler, BridgeCandidate curve, float width, float paddingTiles, ushort worldSize)
    {
        foreach ((int tx, int ty) in IterateCurveTiles(curve, width, paddingTiles, worldSize))
            if (handler.GetTileType((ushort)tx, (ushort)ty) == TileType.Bridge)
                return true;
        return false;
    }

    // How far past both literal endpoints (as a t-parameter range, not just for tile
    // stamping — see PlaceSegments and IterateCurveTiles, both of which now use this same
    // range) a curve should be sampled, converted from a world/tile-unit padding distance via
    // the curve's own estimated length. An anchor point always sits exactly on a tile's .5
    // center, and Unity's Mathf.RoundToInt uses round-half-to-even, so which of the two
    // neighboring tiles a tile-stamping sample landing exactly on that boundary rounds to
    // depends on that tile's parity — sometimes the "wrong" one, leaving a 1-tile gap between
    // the bridge and the island it's meant to connect to. Applying the same overshoot to the
    // visual segment chain closes the matching visual gap that could otherwise show up
    // whenever an anchor was marked even slightly shy of a (typically small) island's actual
    // mesh silhouette. Capped at 0.5 either way so a very short curve's padding can't
    // dominate/wildly extrapolate the Bezier well past where it's actually a sensible
    // approximation of "a bit further in this direction".
    private static (float tStart, float tRange) PaddedTRange(BridgeCandidate curve, float paddingTiles)
    {
        float deltaT = curve.Length > 0.0001f ? Mathf.Min(0.5f, paddingTiles / curve.Length) : 0f;
        return (-deltaT, 1f + deltaT * 2f);
    }

    // Shared sampling walk along a candidate curve's full width, in-bounds tiles only —
    // StampTiles writes every one of these, CurveOverlapsExistingBridge just reads them (and
    // stops at the first hit), so both go through the exact same set of tiles for a given
    // curve/width rather than two independently-written loops that could quietly drift apart.
    private static IEnumerable<(int x, int y)> IterateCurveTiles(BridgeCandidate curve, float width, float paddingTiles, ushort worldSize)
    {
        (float tStart, float tRange) = PaddedTRange(curve, paddingTiles);

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

    // Derivative of the quadratic Bezier above — direction of travel at parameter t, not yet
    // normalized (Bezier(a,b,c,t)'s tangent).
    private static Vector2 BezierTangent(Vector2 a, Vector2 b, Vector2 c, float t)
        => 2f * (1f - t) * (b - a) + 2f * t * (c - b);
}
