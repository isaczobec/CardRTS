using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Enqueue after IslandPlayerBaseFeature and MidIslandFeature (see
/// WorldManager.SetupWorldGen) — reads both via GetPreviousFeature. Builds two sets of
/// connections, each via BridgeConnectionBuilder (see that class for the actual anchor
/// picking / overlap-avoidance / curve-stamping rules):
///
/// - RING: each player base island to the next one in placement order (island i to island
///   (i+1) % count — "the base to its right"), with RingIntermittentCount waypoint islands
///   threaded along each one. For 3+ players this forms one connected ring; for exactly 2,
///   both islands end up pointed at each other, which is why the curve's bow direction is
///   always derived consistently from its own direction of travel (see
///   BridgeConnectionBuilder.TryBuildCandidate) — the two resulting connections bow to
///   OPPOSITE sides of the straight line between them instead of retracing the same path.
///   The ring's bow distance (see ComputeRingBowDistance) is sized from actual map geometry
///   rather than a fixed constant, specifically so it reaches well out toward the map edges
///   even with only 2 bases, instead of hugging the map center.
///
/// - SPOKE: every player base island to the single MidIslandFeature.MidIsland at the map
///   center, with SpokeIntermittentCount waypoint islands threaded along each one.
///
/// Each connection between two ADJACENT islands (which may be a base, the mid island, or a
/// waypoint island placed by this feature — see BuildMultiHopBridge) always builds something,
/// falling back to an overlapping bridge if every anchor pair overlaps an earlier one, since
/// the ring/spoke network is meant to always fully connect every base. WedgeIslandFeature
/// (which runs after this one) uses the same BridgeConnectionBuilder with the opposite
/// policy — a connection that can't be built cleanly just isn't built at all.
/// </summary>
public class IslandBridgeFeature : WorldGenFeature
{
    // Bridge type looked up in WorldManager.instance.BridgeRegistry for every ring/spoke
    // connection this feature builds — see BridgeRegistry.
    public string BridgeTypeName;

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
    // mathematically identical to a straight line, so this needs no special-casing beyond
    // just passing 0 through the same curve math ring connections use.
    public float SpokeBowDistance = 0f;

    // How close a ring connection's bow is allowed to bring its peak to the map's edge —
    // see ComputeRingBowDistance.
    public float RingBowEdgeMargin = 10f;

    // Caps a ring connection's bow at this multiple of the straight-line distance between
    // its two islands, so a short hop between adjacent bases in a large ring doesn't bow out
    // absurdly far relative to its own length just because the map has room for it — see
    // ComputeRingBowDistance.
    public float RingBowChordMultiplier = 1.5f;

    // How far (world/tile units) past each literal anchor point the STAMPED tiles overshoot —
    // see BridgeConnectionBuilder's own doc comment for why an exact endpoint isn't reliable
    // on its own (Unity's round-half-to-even Mathf.RoundToInt).
    public float BridgePaddingTiles = 1f;

    // Every waypoint island this feature placed along a ring or spoke connection (not
    // including the bases/mid island themselves) — read by WedgeIslandFeature so a filler
    // island placed between the mid and side bridges can bridge to the nearest one of these
    // too, not just to a base or the mid island.
    public IReadOnlyList<IslandPlacementHelper.PlacedIsland> WaypointIslands { get; private set; } = new List<IslandPlacementHelper.PlacedIsland>();

    // Resolved once per Generate() call from BridgeTypeName — see ResolveBridgePrefabs.
    private GameObject[] _bridgePrefabs;

    public override void Generate(WorldGenHandler handler)
    {
        var islandsFeature = handler.GetPreviousFeature<IslandPlayerBaseFeature>();
        if (islandsFeature == null || islandsFeature.Islands.Count == 0) return;

        _bridgePrefabs = ResolveBridgePrefabs();
        if (_bridgePrefabs == null || _bridgePrefabs.Length == 0)
        {
            Debug.LogWarning($"[IslandBridgeFeature] No bridge prefabs resolved for BridgeTypeName '{BridgeTypeName}' — skipping.");
            return;
        }

        List<GameObject> intermittentPrefabs = ResolveIntermittentPrefabs();
        var waypoints = new List<IslandPlacementHelper.PlacedIsland>();

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
                BuildMultiHopBridge(handler, from, to, RingIntermittentCount, ringBow, BowDistance, intermittentPrefabs, waypoints, worldSize);
            }
        }

        // Spoke: every base -> the mid island. Both the macro layout and every individual
        // hop use SpokeBowDistance (0 by default) — see that field's own doc comment.
        var midFeature = handler.GetPreviousFeature<MidIslandFeature>();
        if (midFeature != null && midFeature.MidIsland.HasValue)
        {
            IslandPlacementHelper.PlacedIsland mid = midFeature.MidIsland.Value;
            foreach (IslandPlayerBaseFeature.IslandPlacement b in bases)
                BuildMultiHopBridge(handler, b.Island, mid, SpokeIntermittentCount, SpokeBowDistance, SpokeBowDistance, intermittentPrefabs, waypoints, worldSize);
        }

        WaypointIslands = waypoints;
    }

    private GameObject[] ResolveBridgePrefabs()
    {
        BridgeRegistry registry = WorldManager.instance != null ? WorldManager.instance.BridgeRegistry : null;
        if (registry == null)
        {
            Debug.LogWarning("[IslandBridgeFeature] No BridgeRegistry assigned on WorldManager — skipping.");
            return null;
        }
        if (!registry.TryGetPrefabs(BridgeTypeName, out GameObject[] prefabs))
        {
            Debug.LogWarning($"[IslandBridgeFeature] No bridge type registered under name '{BridgeTypeName}'.");
            return null;
        }
        return prefabs;
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
    // chord midpoint is only half its control point's own offset (see
    // BridgeConnectionBuilder.TryBuildCandidate). Capped at RingBowChordMultiplier times the
    // two islands' straight-line distance so a short hop between adjacent bases in a large
    // ring doesn't bow out absurdly far just because the map has room for it.
    //
    // Sign: raw "rotate 90 degrees" alone only happens to point outward (away from the map
    // center) for a chord that passes through the center (the 2-player case) — for adjacent
    // bases in a 3+ ring it can just as easily point inward, which would bow the curve the
    // wrong way. Flipping it to whichever side actually points away from the map center fixes
    // that, EXCEPT within a small dead zone around chord midpoints that are already ~on the
    // center: there, "outward" is ambiguous (either side is equally valid) and flipping based
    // on a near-zero, rounding-noise-dominated offset would collapse the ring's two 2-player
    // connections onto the SAME side instead of opposite sides, breaking the anti-overlap
    // trick the raw (unflipped) rotation already guarantees on its own.
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
    // segmented itself) — the actual bridge geometry comes from BridgeConnectionBuilder,
    // called once per consecutive pair in the resulting chain (from -> waypoint1 -> ... ->
    // to), each hop bowed by hopBowDistance and picking its own anchors/overlap-avoidance
    // independently. This two-level approach (one big curve to lay out islands, several small
    // independently-built hops between them) means the overall path reads as one curved
    // bridge without needing every hop's anchor pick to agree on a single continuous curve,
    // which would be far more brittle once overlap-avoidance can make any individual hop
    // deviate from it.
    private void BuildMultiHopBridge(WorldGenHandler handler, IslandPlacementHelper.PlacedIsland from, IslandPlacementHelper.PlacedIsland to,
        int intermittentCount, float macroBowDistance, float hopBowDistance, List<GameObject> intermittentPrefabs,
        List<IslandPlacementHelper.PlacedIsland> waypointsOut, ushort worldSize)
    {
        var chain = new List<IslandPlacementHelper.PlacedIsland> { from };

        if (intermittentCount > 0 && intermittentPrefabs.Count > 0 &&
            BridgeConnectionBuilder.TryBuildCandidate(from.CenterWorldPosition, to.CenterWorldPosition, macroBowDistance, out BridgeConnectionBuilder.BridgeCandidate layoutCurve))
        {
            for (int k = 1; k <= intermittentCount; k++)
            {
                float t = k / (float)(intermittentCount + 1);
                Vector2 point = BridgeConnectionBuilder.SamplePoint(layoutCurve, t);
                GameObject prefab = intermittentPrefabs[handler.Random.Next(intermittentPrefabs.Count)];

                IslandPlacementHelper.PlacedIsland? placed = IslandPlacementHelper.TryPlaceIsland(
                    handler, prefab, Mathf.RoundToInt(point.x), Mathf.RoundToInt(point.y), worldSize);

                if (placed.HasValue)
                {
                    chain.Add(placed.Value);
                    waypointsOut.Add(placed.Value);
                }
                else
                    Debug.LogWarning($"[IslandBridgeFeature] Prefab '{(prefab != null ? prefab.name : "null")}' has no IslandFootprint component — skipping this waypoint island.");
            }
        }

        chain.Add(to);

        for (int i = 0; i < chain.Count - 1; i++)
            BuildSingleConnection(handler, chain[i], chain[i + 1], hopBowDistance, worldSize);
    }

    // Builds one hop between two adjacent islands in a chain, always committing something
    // (falling back to an overlapping bridge if nothing else is available) — see this
    // feature's own doc comment for why that differs from WedgeIslandFeature's usage of the
    // same BridgeConnectionBuilder.
    private void BuildSingleConnection(WorldGenHandler handler, IslandPlacementHelper.PlacedIsland from, IslandPlacementHelper.PlacedIsland to, float bowDistance, ushort worldSize)
    {
        GameObject prefab = _bridgePrefabs[handler.Random.Next(_bridgePrefabs.Length)];
        BridgeSegmentFootprint segmentInfo = prefab != null ? prefab.GetComponent<BridgeSegmentFootprint>() : null;
        if (segmentInfo == null)
        {
            Debug.LogWarning($"[IslandBridgeFeature] Prefab '{(prefab != null ? prefab.name : "null")}' has no BridgeSegmentFootprint component — skipping this connection.");
            return;
        }

        // allowOverlapFallback: true — every ring/spoke connection must exist regardless, so
        // the worst case is an overlapping bridge, not a missing one. candidates.Count == 0
        // (both islands' anchors collapsed to the same point) is the only way this returns
        // false; nothing sensible to build there either way.
        if (!BridgeConnectionBuilder.TryFindConnection(handler, from, to, bowDistance, segmentInfo.SegmentWidth, BridgePaddingTiles, worldSize,
                allowOverlapFallback: true, out BridgeConnectionBuilder.BridgeCandidate chosen))
            return;

        BridgeConnectionBuilder.Commit(handler, chosen, prefab, segmentInfo, BridgePaddingTiles, worldSize, from.RegionId, to.RegionId);
    }
}
