using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Enqueue after IslandPlayerBaseFeature (see WorldManager.SetupWorldGen) — reads its
/// Islands list via GetPreviousFeature. For now, connects each island to the next one in
/// placement order — island i to island (i+1) % count, i.e. its neighbour going around the
/// same ring IslandPlayerBaseFeature scattered bases on ("the base to its right"). For 3+
/// players this forms one connected ring; for exactly 2, both islands end up pointed at each
/// other, which is why every curve bows to a consistent side of its own direction of travel
/// (see BuildBridge) rather than being a straight line — the two resulting bridges bow to
/// OPPOSITE sides of the straight line between them instead of retracing the same path.
///
/// Each connection considers every (fromAnchor, toAnchor) pair drawn from the two islands'
/// IslandFootprint.BridgeAnchors (falling back to each footprint's BaseAnchor, with a
/// warning, if it has none marked), shortest bridge first, and picks the first one whose
/// curve doesn't overlap a tile already stamped TileType.Bridge by an earlier connection this
/// same generation pass — so bridges route around each other where a free anchor allows it.
/// If every candidate overlaps something, it just uses the shortest one anyway (the "first
/// best slot") rather than leaving the connection unbuilt. Whichever pair wins, the curve
/// itself is the same bowed quadratic Bezier as before: stamp every tile under its width as
/// TileType.Bridge, then enqueue one BridgeSegmentSpawnAction per
/// BridgeSegmentFootprint.SegmentLength step along it — chaining a straight modular prefab
/// along a sampled curve rather than deforming its mesh.
/// </summary>
public class IslandBridgeFeature : WorldGenFeature
{
    public GameObject[] BridgeSegmentPrefabs;

    // Perpendicular distance (world/tile units) the curve's midpoint is pushed away from
    // the straight line connecting the two chosen anchor points.
    public float BowDistance = 6f;

    // Tile-stamping resolution along the curve's length — smaller reads as a smoother
    // curve in the underlying tile data (and therefore the navmesh built from it, and the
    // overlap check below) at the cost of more SetTileType/GetTileType calls; the visual
    // curve (segment placement) is independent of this and always exactly follows the math.
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
        if (islandsFeature == null || islandsFeature.Islands.Count < 2) return;

        if (BridgeSegmentPrefabs == null || BridgeSegmentPrefabs.Length == 0)
        {
            Debug.LogWarning("[IslandBridgeFeature] No BridgeSegmentPrefabs assigned — skipping.");
            return;
        }

        IReadOnlyList<IslandPlayerBaseFeature.IslandPlacement> islands = islandsFeature.Islands;
        ushort worldSize = (ushort)(WorldGenHandler.CHUNK_SIZE_TILES * WorldGenHandler.WorldSizeChunks);

        for (int i = 0; i < islands.Count; i++)
            BuildBridge(handler, islands[i], islands[(i + 1) % islands.Count], worldSize);
    }

    private void BuildBridge(WorldGenHandler handler, IslandPlayerBaseFeature.IslandPlacement from, IslandPlayerBaseFeature.IslandPlacement to, ushort worldSize)
    {
        GameObject prefab = BridgeSegmentPrefabs[handler.Random.Next(BridgeSegmentPrefabs.Length)];
        BridgeSegmentFootprint segmentInfo = prefab != null ? prefab.GetComponent<BridgeSegmentFootprint>() : null;
        if (segmentInfo == null)
        {
            Debug.LogWarning($"[IslandBridgeFeature] Prefab '{(prefab != null ? prefab.name : "null")}' has no BridgeSegmentFootprint component — skipping this bridge.");
            return;
        }

        List<Vector2> fromAnchors = GatherAnchorWorldPositions(from);
        List<Vector2> toAnchors = GatherAnchorWorldPositions(to);

        List<BridgeCandidate> candidates = new List<BridgeCandidate>();
        foreach (Vector2 start in fromAnchors)
            foreach (Vector2 end in toAnchors)
                if (TryBuildCandidate(start, end, out BridgeCandidate candidate))
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
            if (CurveOverlapsExistingBridge(handler, candidate, segmentInfo.SegmentWidth, worldSize))
                continue;
            chosen = candidate;
            break;
        }
        // If every candidate overlapped an already-placed bridge, chosen is still
        // candidates[0] from the initial assignment above — the shortest pair regardless of
        // overlap, i.e. placing it at the first best slot as a last resort.

        StampTiles(handler, chosen, segmentInfo.SegmentWidth, worldSize);
        PlaceSegments(handler, chosen, prefab, segmentInfo, worldSize);
    }

    // Every one of the island's BridgeAnchors, converted to world positions — or, if it has
    // none marked, a single-element fallback list containing its BaseAnchor cell (with a
    // warning), so a bridge can still be built rather than silently failing.
    private static List<Vector2> GatherAnchorWorldPositions(IslandPlayerBaseFeature.IslandPlacement island)
    {
        IslandFootprint footprint = island.Footprint;
        var result = new List<Vector2>();

        foreach (Vector2Int cell in footprint.BridgeAnchors)
            result.Add(footprint.AnchorWorldPosition(cell, island.OriginX, island.OriginY));

        if (result.Count == 0)
        {
            Debug.LogWarning($"[IslandBridgeFeature] Island for client {island.ClientId} has no IslandFootprint.BridgeAnchors marked — falling back to its base anchor cell.");
            result.Add(footprint.AnchorWorldPosition(footprint.BaseAnchorOrDefault(), island.OriginX, island.OriginY));
        }

        return result;
    }

    private bool TryBuildCandidate(Vector2 start, Vector2 end, out BridgeCandidate candidate)
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
        Vector2 control = (start + end) * 0.5f + perpendicular * BowDistance;
        float length = EstimateBezierLength(start, control, end);

        candidate = new BridgeCandidate(start, end, control, length);
        return true;
    }

    private void PlaceSegments(WorldGenHandler handler, BridgeCandidate curve, GameObject prefab, BridgeSegmentFootprint segmentInfo, ushort worldSize)
    {
        int segmentCount = Mathf.Max(1, Mathf.RoundToInt(curve.Length / segmentInfo.SegmentLength));

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

            handler.EnqueueAction(new BridgeSegmentSpawnAction { Prefab = prefab, WorldPosition = worldPosition, Rotation = rotation });
        }
    }

    // Marks every tile within halfWidth of the curve (measured perpendicular to its local
    // tangent, not just the sampled centerline points themselves) as TileType.Bridge, so the
    // navmesh built from these tiles is exactly as wide as the bridge actually reads
    // visually — a 1-tile-wide navmesh strip on a visually 3-tile-wide bridge would let
    // troops path (and look like they're walking) off the edge.
    private static void StampTiles(WorldGenHandler handler, BridgeCandidate curve, float width, ushort worldSize)
    {
        foreach ((int tx, int ty) in IterateCurveTiles(curve, width, worldSize))
            handler.SetTileType((ushort)tx, (ushort)ty, TileType.Bridge);
    }

    // True if any tile this candidate curve would stamp (at the given width) is already
    // TileType.Bridge — i.e. claimed by a connection BuildBridge already finished earlier in
    // this same Generate() pass. Only ever sees earlier bridges, never this one (nothing is
    // written for a candidate until BuildBridge has already picked it), and never flags an
    // island's own Island tiles, since those are a different TileType.
    private static bool CurveOverlapsExistingBridge(WorldGenHandler handler, BridgeCandidate curve, float width, ushort worldSize)
    {
        foreach ((int tx, int ty) in IterateCurveTiles(curve, width, worldSize))
            if (handler.GetTileType((ushort)tx, (ushort)ty) == TileType.Bridge)
                return true;
        return false;
    }

    // Shared sampling walk along a candidate curve's full width, in-bounds tiles only —
    // StampTiles writes every one of these, CurveOverlapsExistingBridge just reads them (and
    // stops at the first hit), so both go through the exact same set of tiles for a given
    // curve/width rather than two independently-written loops that could quietly drift apart.
    private static IEnumerable<(int x, int y)> IterateCurveTiles(BridgeCandidate curve, float width, ushort worldSize)
    {
        int steps = Mathf.Max(1, Mathf.CeilToInt(curve.Length / TileStampStep));
        float halfWidth = Mathf.Max(0.5f, width * 0.5f);

        for (int i = 0; i <= steps; i++)
        {
            float t = i / (float)steps;
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
