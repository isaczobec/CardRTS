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
/// Each connection: picks whichever IslandFootprint.BridgeAnchors point on each island sits
/// closest to the other island (falling back to the footprint's BaseAnchor, with a warning,
/// if none are marked), bends a quadratic Bezier between them, stamps every tile under its
/// width as TileType.Bridge, and enqueues one BridgeSegmentSpawnAction per
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
    // curve in the underlying tile data (and therefore the navmesh built from it) at the
    // cost of more SetTileType calls; the visual curve (segment placement) is independent
    // of this and always exactly follows the math.
    private const float TileStampStep = 0.5f;

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
        Vector2 start = PickAnchor(from, to.CenterWorldPosition);
        Vector2 end = PickAnchor(to, from.CenterWorldPosition);

        Vector2 direction = end - start;
        if (direction.sqrMagnitude < 0.0001f) return;

        // Rotating the direction 90 degrees the same way every time is what makes the
        // reverse connection (the other island's own bridge back to this one, for a
        // 2-player ring) bow to the opposite side instead of retracing exactly the same
        // curve: reversing start/end negates direction, which negates this perpendicular
        // too — see this feature's own doc comment.
        Vector2 perpendicular = new Vector2(-direction.y, direction.x).normalized;
        Vector2 control = (start + end) * 0.5f + perpendicular * BowDistance;

        GameObject prefab = BridgeSegmentPrefabs[handler.Random.Next(BridgeSegmentPrefabs.Length)];
        BridgeSegmentFootprint segmentInfo = prefab != null ? prefab.GetComponent<BridgeSegmentFootprint>() : null;
        if (segmentInfo == null)
        {
            Debug.LogWarning($"[IslandBridgeFeature] Prefab '{(prefab != null ? prefab.name : "null")}' has no BridgeSegmentFootprint component — skipping this bridge.");
            return;
        }

        float curveLength = EstimateBezierLength(start, control, end);
        StampTiles(handler, start, control, end, curveLength, segmentInfo.SegmentWidth, worldSize);
        PlaceSegments(handler, start, control, end, curveLength, prefab, segmentInfo, worldSize);
    }

    // Whichever of the island's BridgeAnchors world-positions sits closest to towardWorldPos
    // — i.e. the anchor that best faces the island being connected to.
    private static Vector2 PickAnchor(IslandPlayerBaseFeature.IslandPlacement island, Vector2 towardWorldPos)
    {
        IslandFootprint footprint = island.Footprint;
        Vector2Int bestCell = footprint.BaseAnchorOrDefault();
        float bestDist2 = float.MaxValue;
        bool found = false;

        foreach (Vector2Int cell in footprint.BridgeAnchors)
        {
            Vector2 world = footprint.AnchorWorldPosition(cell, island.OriginX, island.OriginY);
            float d2 = (world - towardWorldPos).sqrMagnitude;
            if (d2 < bestDist2)
            {
                bestDist2 = d2;
                bestCell = cell;
                found = true;
            }
        }

        if (!found)
            Debug.LogWarning($"[IslandBridgeFeature] Island for client {island.ClientId} has no IslandFootprint.BridgeAnchors marked — falling back to its base anchor cell.");

        return footprint.AnchorWorldPosition(bestCell, island.OriginX, island.OriginY);
    }

    private void PlaceSegments(WorldGenHandler handler, Vector2 start, Vector2 control, Vector2 end, float curveLength, GameObject prefab, BridgeSegmentFootprint segmentInfo, ushort worldSize)
    {
        int segmentCount = Mathf.Max(1, Mathf.RoundToInt(curveLength / segmentInfo.SegmentLength));

        for (int s = 0; s < segmentCount; s++)
        {
            float t0 = s / (float)segmentCount;
            float t1 = (s + 1) / (float)segmentCount;
            Vector2 p0 = Bezier(start, control, end, t0);
            Vector2 p1 = Bezier(start, control, end, t1);
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
    private static void StampTiles(WorldGenHandler handler, Vector2 start, Vector2 control, Vector2 end, float curveLength, float width, ushort worldSize)
    {
        int steps = Mathf.Max(1, Mathf.CeilToInt(curveLength / TileStampStep));
        float halfWidth = Mathf.Max(0.5f, width * 0.5f);

        for (int i = 0; i <= steps; i++)
        {
            float t = i / (float)steps;
            Vector2 p = Bezier(start, control, end, t);
            Vector2 tangent = BezierTangent(start, control, end, t);
            Vector2 normal = tangent.sqrMagnitude > 0.0001f ? new Vector2(-tangent.y, tangent.x).normalized : Vector2.zero;

            for (float o = -halfWidth; o <= halfWidth; o += TileStampStep)
            {
                Vector2 sample = p + normal * o;
                int tx = Mathf.RoundToInt(sample.x);
                int ty = Mathf.RoundToInt(sample.y);
                if (tx < 0 || ty < 0 || tx >= worldSize || ty >= worldSize) continue;
                handler.SetTileType((ushort)tx, (ushort)ty, TileType.Bridge);
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
