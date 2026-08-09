using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public struct WeightedIslandEntry
{
    // Name looked up in WorldManager.instance.IslandRegistry.
    public string Name;

    // Relative likelihood of being picked versus every other entry in the same list — does
    // not need to sum to any particular total. <= 0 is ignored.
    public float Weight;
}

/// <summary>
/// Enqueue after IslandBridgeFeature (see WorldManager.SetupWorldGen) — reads
/// IslandPlayerBaseFeature.Islands, MidIslandFeature.MidIsland, and
/// IslandBridgeFeature.WaypointIslands via GetPreviousFeature, all of which must already be
/// placed/stamped. Scatters extra "filler" islands into each wedge-shaped gap between a pair
/// of adjacent spoke connections (base-to-mid) and the ring connection joining their two
/// bases — purely decorative/optional space-filling, not part of the main base-to-base-to-mid
/// network those other features guarantee.
///
/// For each wedge: rolls a random count in [MinIslandsPerWedge, MaxIslandsPerWedge], and for
/// each one, repeatedly (up to MaxPlacementAttempts) rolls a random island — weighted by
/// IslandWeights, resolved through IslandRegistry — at a random point within the wedge's
/// angular/radial bounds, until it finds a candidate whose footprint doesn't overlap anything
/// already placed (any non-Air tile — island or bridge) AND that can be bridged, without ANY
/// bridge-tile overlap, to whichever already-placed island (base, mid, waypoint, or an
/// earlier filler island) is closest to it. Unlike IslandBridgeFeature's own ring/spoke
/// connections — which always build something, falling back to an overlapping bridge rather
/// than leaving a gap in the required network — a filler island that can't find a clean
/// connection within its attempt budget is simply not placed at all, since nothing else
/// depends on it existing. The footprint check happens before any bridge search, and neither
/// one commits (stamps tiles / enqueues a spawn) until both have already succeeded — so a
/// failed attempt never needs to be undone, just discarded.
///
/// Once an island's required first connection is committed, it has a repeated
/// ExtraConnectionChance chance (up to MaxExtraConnections times) of also bridging to another
/// nearby island — always a DIFFERENT one than it's already connected to, and always subject
/// to the same zero-overlap requirement as the first connection; a roll that can't find a
/// clean connection just stops trying further extras for that island rather than retrying.
///
/// Candidate angle/radius are both drawn with CenterBiasSamples-strength center bias (see
/// BiasTowardCenter) rather than uniformly across the wedge, so islands cluster toward the
/// middle of the "pocket" between the mid and side bridges instead of spreading evenly out
/// to its (angular and radial) edges.
/// </summary>
public class WedgeIslandFeature : WorldGenFeature
{
    public WeightedIslandEntry[] IslandWeights;

    public int MinIslandsPerWedge = 1;
    public int MaxIslandsPerWedge = 2;

    // Bridge type looked up in WorldManager.instance.BridgeRegistry for connecting a filler
    // island to its neighbors — see BridgeRegistry.
    public string BridgeTypeName;

    public float BowDistance = 6f;
    public float BridgePaddingTiles = 1f;

    // Chance (per potential extra connection, rolled independently up to MaxExtraConnections
    // times) that an already-placed wedge island also bridges to another nearby island beyond
    // its required first one. 0 disables extra connections entirely.
    public float ExtraConnectionChance = 0.3f;
    public int MaxExtraConnections = 2;

    // Fraction of a wedge's own angular width left as a no-spawn margin on each of its two
    // sides (the spoke connections bounding it), so a filler island doesn't land right on top
    // of a spoke bridge.
    public float WedgeAngularInset = 0.12f;

    // World/tile-unit margin kept clear around the mid island (inner bound) and the world
    // edge (outer bound) when picking a candidate radius within a wedge. The outer bound is
    // deliberately generous (not clipped to where the ring bridge's own bow happens to sit,
    // which this feature has no direct way to know) — the footprint-overlap check is what
    // actually keeps a candidate off the ring bridge/waypoints wherever they ended up.
    public float WedgeRadialMargin = 8f;

    // How strongly a candidate's angle/radius within its wedge is pulled toward the middle of
    // its own [start, end] range, rather than sampled uniformly across it — see
    // BiasTowardCenter. 1 = uniform (no bias); higher values concentrate placements more
    // tightly around the wedge's center in both the angular and radial directions.
    public int CenterBiasSamples = 3;

    private const int MaxPlacementAttempts = 30;

    // How many of the nearest already-placed islands a candidate will try bridging to before
    // giving up on that candidate position — capped rather than exhaustive since this list
    // only grows over the course of generation, and testing every one of them per attempt
    // would get expensive for a busy map.
    private const int MaxNearbyIslandSearch = 6;

    // Resolved once per Generate() call from BridgeTypeName — see ResolveBridgePrefabs.
    private GameObject[] _bridgePrefabs;

    // Every filler island this feature successfully placed — read by IslandResourceClusterFeature
    // (via GetPreviousFeature) so it knows which islands are eligible for scattered tree/stone/ore
    // clusters, alongside IslandBridgeFeature.WaypointIslands.
    public IReadOnlyList<IslandPlacementHelper.PlacedIsland> PlacedIslands { get; private set; } = new List<IslandPlacementHelper.PlacedIsland>();

    private List<IslandPlacementHelper.PlacedIsland> _placedIslands;

    public override void Generate(WorldGenHandler handler)
    {
        _placedIslands = new List<IslandPlacementHelper.PlacedIsland>();
        var islandsFeature = handler.GetPreviousFeature<IslandPlayerBaseFeature>();
        if (islandsFeature == null || islandsFeature.Islands.Count < 2) return;

        var midFeature = handler.GetPreviousFeature<MidIslandFeature>();
        if (midFeature == null || !midFeature.MidIsland.HasValue) return;

        _bridgePrefabs = ResolveBridgePrefabs();
        if (_bridgePrefabs == null || _bridgePrefabs.Length == 0)
        {
            Debug.LogWarning($"[WedgeIslandFeature] No bridge prefabs resolved for BridgeTypeName '{BridgeTypeName}' — skipping.");
            return;
        }

        IslandRegistry registry = WorldManager.instance != null ? WorldManager.instance.IslandRegistry : null;
        if (registry == null)
        {
            Debug.LogWarning("[WedgeIslandFeature] No IslandRegistry assigned on WorldManager — skipping.");
            return;
        }

        List<(GameObject prefab, float weight)> weightedPrefabs = ResolveWeightedPrefabs(registry);
        if (weightedPrefabs.Count == 0)
        {
            Debug.LogWarning("[WedgeIslandFeature] No usable entries in IslandWeights — skipping.");
            return;
        }

        IReadOnlyList<IslandPlayerBaseFeature.IslandPlacement> bases = islandsFeature.Islands;
        IslandPlacementHelper.PlacedIsland mid = midFeature.MidIsland.Value;
        ushort worldSize = (ushort)(WorldGenHandler.CHUNK_SIZE_TILES * WorldGenHandler.WorldSizeChunks);
        Vector2 mapCenter = new Vector2(worldSize * 0.5f, worldSize * 0.5f);

        var bridgeFeature = handler.GetPreviousFeature<IslandBridgeFeature>();
        List<IslandPlacementHelper.PlacedIsland> allIslands = CollectAllIslands(bases, mid, bridgeFeature);

        float innerRadius = Mathf.Max(mid.Footprint.Width, mid.Footprint.Height) * 0.5f + WedgeRadialMargin;
        float outerRadius = Mathf.Max(innerRadius, worldSize * 0.5f - WedgeRadialMargin);

        for (int i = 0; i < bases.Count; i++)
        {
            Vector2 centerA = bases[i].Island.CenterWorldPosition;
            Vector2 centerB = bases[(i + 1) % bases.Count].Island.CenterWorldPosition;

            float angleA = Mathf.Atan2(centerA.y - mapCenter.y, centerA.x - mapCenter.x);
            float angleB = Mathf.Atan2(centerB.y - mapCenter.y, centerB.x - mapCenter.x);
            float angleDelta = angleB - angleA;
            while (angleDelta < 0f) angleDelta += Mathf.PI * 2f;

            float inset = angleDelta * WedgeAngularInset;
            float angleStart = angleA + inset;
            float angleEnd = angleA + angleDelta - inset;
            if (angleEnd <= angleStart) continue; // wedge too narrow for its own inset margins

            int count = handler.Random.Next(MinIslandsPerWedge, MaxIslandsPerWedge + 1);
            for (int k = 0; k < count; k++)
                TryPlaceWedgeIsland(handler, angleStart, angleEnd, innerRadius, outerRadius, mapCenter, allIslands, weightedPrefabs, worldSize);
        }

        PlacedIslands = _placedIslands;
    }

    private GameObject[] ResolveBridgePrefabs()
    {
        BridgeRegistry registry = WorldManager.instance != null ? WorldManager.instance.BridgeRegistry : null;
        if (registry == null)
        {
            Debug.LogWarning("[WedgeIslandFeature] No BridgeRegistry assigned on WorldManager — skipping.");
            return null;
        }
        if (!registry.TryGetPrefabs(BridgeTypeName, out GameObject[] prefabs))
        {
            Debug.LogWarning($"[WedgeIslandFeature] No bridge type registered under name '{BridgeTypeName}'.");
            return null;
        }
        return prefabs;
    }

    private static List<IslandPlacementHelper.PlacedIsland> CollectAllIslands(IReadOnlyList<IslandPlayerBaseFeature.IslandPlacement> bases,
        IslandPlacementHelper.PlacedIsland mid, IslandBridgeFeature bridgeFeature)
    {
        var result = new List<IslandPlacementHelper.PlacedIsland>();
        foreach (IslandPlayerBaseFeature.IslandPlacement b in bases)
            result.Add(b.Island);
        result.Add(mid);
        if (bridgeFeature != null)
            result.AddRange(bridgeFeature.WaypointIslands);
        return result;
    }

    private List<(GameObject prefab, float weight)> ResolveWeightedPrefabs(IslandRegistry registry)
    {
        var result = new List<(GameObject, float)>();
        if (IslandWeights == null) return result;

        foreach (WeightedIslandEntry entry in IslandWeights)
        {
            if (entry.Weight <= 0f) continue;
            if (!registry.TryGetPrefab(entry.Name, out GameObject prefab))
            {
                Debug.LogWarning($"[WedgeIslandFeature] No island registered under name '{entry.Name}' — skipped.");
                continue;
            }
            result.Add((prefab, entry.Weight));
        }
        return result;
    }

    // Same "walk the list, subtract weight until the roll lands" weighted pick GoTileManager
    // already uses for its own per-tile prop chances.
    private static GameObject PickWeighted(List<(GameObject prefab, float weight)> entries, System.Random rng)
    {
        float total = 0f;
        foreach (var e in entries) total += e.weight;

        float roll = (float)(rng.NextDouble() * total);
        foreach (var e in entries)
        {
            if (roll < e.weight) return e.prefab;
            roll -= e.weight;
        }
        return entries[entries.Count - 1].prefab;
    }

    // Averages `samples` independent uniform [0,1) rolls — a simple, well-known way to
    // approximate a bell-shaped distribution centered on 0.5 without needing a real Gaussian
    // sampler (the Irwin-Hall distribution: 1 sample is uniform, 2 is a triangle peaked at
    // the center, more looks increasingly bell-shaped), always still bounded to [0,1) so it
    // can be applied directly to any [start, end) range without extra clamping.
    private static float BiasTowardCenter(System.Random rng, int samples)
    {
        float sum = 0f;
        for (int i = 0; i < samples; i++)
            sum += (float)rng.NextDouble();
        return sum / Mathf.Max(1, samples);
    }

    private void TryPlaceWedgeIsland(WorldGenHandler handler, float angleStart, float angleEnd, float innerRadius, float outerRadius, Vector2 mapCenter,
        List<IslandPlacementHelper.PlacedIsland> allIslands, List<(GameObject prefab, float weight)> weightedPrefabs, ushort worldSize)
    {
        for (int attempt = 0; attempt < MaxPlacementAttempts; attempt++)
        {
            GameObject prefab = PickWeighted(weightedPrefabs, handler.Random);
            IslandFootprint footprint = prefab != null ? prefab.GetComponent<IslandFootprint>() : null;
            if (footprint == null)
            {
                Debug.LogWarning($"[WedgeIslandFeature] Prefab '{(prefab != null ? prefab.name : "null")}' has no IslandFootprint component — skipped.");
                continue;
            }

            float angle = angleStart + BiasTowardCenter(handler.Random, CenterBiasSamples) * (angleEnd - angleStart);
            float radius = innerRadius + BiasTowardCenter(handler.Random, CenterBiasSamples) * (outerRadius - innerRadius);
            Vector2 centerF = mapCenter + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;

            // Clamped up front (not left to IslandPlacementHelper.TryPlaceIsland's own,
            // independent re-clamp below) so every step — the footprint-overlap check, the
            // bridge anchor search, and the final placement — all agree on exactly the same
            // origin. Computing it twice from the unclamped center used to let a large
            // island sampled near the edge of the wedge's radial range end up STAMPED at a
            // different origin than the one its bridge was actually built against — the
            // bridge would reach toward where the island was supposed to be, not where it
            // actually landed, reading as a bridge that simply isn't connected to anything.
            int centerTileX = Mathf.Clamp(Mathf.RoundToInt(centerF.x), 0, worldSize - 1);
            int centerTileY = Mathf.Clamp(Mathf.RoundToInt(centerF.y), 0, worldSize - 1);
            int originX = centerTileX - footprint.Width / 2;
            int originY = centerTileY - footprint.Height / 2;

            if (FootprintOverlapsExisting(handler, footprint, originX, originY, worldSize)) continue;

            // A hypothetical placement — nothing stamped yet — just enough data for
            // BridgeConnectionBuilder to evaluate anchor points against. Origin is clamped
            // again here only to guard a footprint so large its origin still falls outside
            // the map even from an already-clamped center; centerTileX/Y themselves are
            // never reclamped again below, which is what keeps this consistent with the
            // actual placement. Wedge filler islands are never rotated, so this is always the
            // 0-degree (identity) RotatedIslandFootprint — same shape/anchors as footprint
            // itself.
            var candidateIsland = new IslandPlacementHelper.PlacedIsland(
                (ushort)Mathf.Clamp(originX, 0, worldSize - 1),
                (ushort)Mathf.Clamp(originY, 0, worldSize - 1),
                footprint,
                FootprintRotator.Rotate(footprint, 0f),
                0f);

            if (!TryFindConnectableIsland(handler, candidateIsland, allIslands, null, worldSize,
                    out BridgeConnectionBuilder.BridgeCandidate bridgeCandidate, out GameObject bridgePrefab, out BridgeSegmentFootprint bridgeSegmentInfo,
                    out IslandPlacementHelper.PlacedIsland connectedTo))
                continue;

            // Both checks passed — now actually commit: stamp/spawn the island, then build
            // its bridge.
            IslandPlacementHelper.PlacedIsland? placed = IslandPlacementHelper.TryPlaceIsland(handler, prefab, centerTileX, centerTileY, worldSize);
            if (!placed.HasValue) continue; // footprint was already confirmed non-null above, so this shouldn't happen

            BridgeConnectionBuilder.Commit(handler, bridgeCandidate, bridgePrefab, bridgeSegmentInfo, BridgePaddingTiles, worldSize);
            allIslands.Add(placed.Value);
            _placedIslands.Add(placed.Value);

            // Optional extra connections, each to a DIFFERENT island than any already
            // connected to (see connectedTargets) — a roll or search failure just stops
            // trying further extras for this island, rather than retrying.
            var connectedTargets = new List<IslandPlacementHelper.PlacedIsland> { connectedTo };
            for (int extra = 0; extra < MaxExtraConnections; extra++)
            {
                if (handler.Random.NextDouble() >= ExtraConnectionChance) break;

                if (!TryFindConnectableIsland(handler, placed.Value, allIslands, connectedTargets, worldSize,
                        out BridgeConnectionBuilder.BridgeCandidate extraBridge, out GameObject extraPrefab, out BridgeSegmentFootprint extraSegmentInfo,
                        out IslandPlacementHelper.PlacedIsland extraTarget))
                    break;

                BridgeConnectionBuilder.Commit(handler, extraBridge, extraPrefab, extraSegmentInfo, BridgePaddingTiles, worldSize);
                connectedTargets.Add(extraTarget);
            }

            return;
        }

        Debug.LogWarning("[WedgeIslandFeature] Could not find a non-overlapping placement + connection for a wedge island after several attempts — skipped.");
    }

    // True if any of the footprint's occupied cells (at the given origin) is already
    // something other than TileType.Air — an island or a bridge. Out-of-bounds cells are
    // skipped rather than treated as a conflict, matching IslandPlacementHelper.StampFootprint's
    // own convention for cells that fall outside the map.
    private static bool FootprintOverlapsExisting(WorldGenHandler handler, IslandFootprint footprint, int originX, int originY, ushort worldSize)
    {
        foreach (Vector2Int cell in footprint.OccupiedCells())
        {
            int tx = originX + cell.x;
            int ty = originY + cell.y;
            if (tx < 0 || ty < 0 || tx >= worldSize || ty >= worldSize) continue;
            if (handler.GetTileType((ushort)tx, (ushort)ty) != TileType.Air) return true;
        }
        return false;
    }

    // Nearest-first search for an already-placed island this candidate can reach with a
    // bridge that doesn't overlap anything — allowOverlapFallback: false, since a wedge
    // island with no clean connection just doesn't get one at all. exclude (when given) skips
    // islands already connected to this same candidate, so a repeat call for extra
    // connections always looks for a NEW island rather than re-picking the first one. Always
    // skips the candidate itself too: for an EXTRA connection, allIslands already contains
    // the just-placed candidate (added right before the extra-connection loop starts), which
    // makes it its own distance-0 nearest neighbor — left unguarded, this let an island with
    // 2+ BridgeAnchors "connect" one of its own anchor points to another, producing a short
    // bridge stub that starts and ends on the same island and visibly leads nowhere.
    private bool TryFindConnectableIsland(WorldGenHandler handler, IslandPlacementHelper.PlacedIsland candidate, List<IslandPlacementHelper.PlacedIsland> allIslands,
        List<IslandPlacementHelper.PlacedIsland> exclude, ushort worldSize,
        out BridgeConnectionBuilder.BridgeCandidate bridgeCandidate, out GameObject bridgePrefab, out BridgeSegmentFootprint bridgeSegmentInfo,
        out IslandPlacementHelper.PlacedIsland connectedTo)
    {
        bridgeCandidate = default;
        connectedTo = default;
        bridgePrefab = _bridgePrefabs[handler.Random.Next(_bridgePrefabs.Length)];
        bridgeSegmentInfo = bridgePrefab != null ? bridgePrefab.GetComponent<BridgeSegmentFootprint>() : null;
        if (bridgeSegmentInfo == null) return false;

        Vector2 candidateCenter = candidate.CenterWorldPosition;
        var byDistance = new List<IslandPlacementHelper.PlacedIsland>(allIslands);
        byDistance.Sort((a, b) => Vector2.Distance(a.CenterWorldPosition, candidateCenter).CompareTo(Vector2.Distance(b.CenterWorldPosition, candidateCenter)));

        int searched = 0;
        foreach (IslandPlacementHelper.PlacedIsland other in byDistance)
        {
            if (searched >= MaxNearbyIslandSearch) break;
            if (candidate.Equals(other)) continue;
            if (exclude != null && exclude.Contains(other)) continue;
            searched++;

            if (BridgeConnectionBuilder.TryFindConnection(handler, candidate, other, BowDistance, bridgeSegmentInfo.SegmentWidth, BridgePaddingTiles, worldSize,
                    allowOverlapFallback: false, out BridgeConnectionBuilder.BridgeCandidate found))
            {
                bridgeCandidate = found;
                connectedTo = other;
                return true;
            }
        }

        return false;
    }
}
