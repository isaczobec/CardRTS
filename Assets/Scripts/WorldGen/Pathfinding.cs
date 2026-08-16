
using System.Collections.Generic;
using UnityEngine;

public static class Pathfinding
{
    private static NavMeshHandler NavMeshHandler => NavMeshHandler.instance;
    private static ulong _currentPathfindingIteration = 0;

    public static List<Vector2> PathFind(float worldX, float worldY, float desX, float desY)
    {
        List<NavMeshNode> path = PathFindNavMesh(worldX, worldY, desX, desY);
        if (path == null || path.Count == 0)
            return null;

        return FunnelPathSmoother.Pull(path, new Vector2(worldX, worldY), new Vector2(desX, desY));
    }

    public static List<NavMeshNode> PathFindNavMesh(float worldX, float worldY, float desX, float desY)
    {
        NavMeshHandler navMeshHandler = NavMeshHandler;
        if (navMeshHandler == null)
            return null;

        // convert world coordinates to tile coordinates with floor
        ushort tileX = (ushort)Mathf.FloorToInt(worldX);
        ushort tileY = (ushort)Mathf.FloorToInt(worldY);
        ushort desTileX = (ushort)Mathf.FloorToInt(desX);
        ushort desTileY = (ushort)Mathf.FloorToInt(desY);

        // Start uses the nearest-node fallback (see GetNearestNodeAt) since this is an
        // entity's own live position, which can rarely drift just outside every node — the
        // destination gets no such fallback, since an unreachable/non-walkable target
        // should still correctly fail here rather than silently snapping elsewhere.
        NavMeshNode startNode = navMeshHandler.GetNearestNodeAt(tileX, tileY);
        NavMeshNode endNode = navMeshHandler.GetNodeAt(desTileX, desTileY);

        if (startNode == null || endNode == null)
            return null;

        Vector2 startPos = new Vector2(worldX, worldY);
        Vector2 goalPos = new Vector2(desX, desY);

        // Hierarchical first pass: islands/bridges form a coarse graph (see
        // IslandGraphBuilder) built alongside them at world-gen time. A cheap Dijkstra over
        // that handful of regions (see NavMeshHandler.TryGetRegionCorridor) picks out which
        // islands/bridges the trip actually needs to cross, so the expensive fine-grained
        // NavMeshNode search below only has to consider nodes belonging to one of them
        // instead of every node on the entire map — the more islands a map has, the more
        // this prunes away. Skipped for the overwhelmingly common case of moving around
        // within a single island (same region, or either endpoint has no region at all —
        // e.g. a legacy/non-island map), where it wouldn't narrow anything down anyway.
        //
        // The corridor is only ever a performance hint, never a correctness requirement: if
        // node tagging is imprecise at some region boundary (see NavMeshNode.RegionId) and
        // the restricted search fails to actually reach the goal, this falls back to a fully
        // unrestricted search rather than incorrectly reporting the destination unreachable.
        int startRegion = navMeshHandler.GetTileRegionId(tileX, tileY);
        int endRegion = navMeshHandler.GetTileRegionId(desTileX, desTileY);

        if (startRegion >= 0 && endRegion >= 0 && startRegion != endRegion &&
            navMeshHandler.TryGetRegionCorridor(startRegion, endRegion, out HashSet<int> corridor))
        {
            List<NavMeshNode> restricted = RunSearch(navMeshHandler, startNode, endNode, startPos, goalPos, corridor);
            if (restricted != null)
                return restricted;
        }

        return RunSearch(navMeshHandler, startNode, endNode, startPos, goalPos, null);
    }

    // Runs one full A* search from startNode to endNode. When allowedRegions is non-null,
    // expansion never crosses into a node tagged with a region outside that set (except
    // endNode itself, always allowed regardless of its own tag — see the region-corridor
    // comment above) — this is the ONLY difference between the hierarchical first attempt
    // and the unrestricted fallback in PathFindNavMesh, so both go through exactly the same
    // well-tested search/tie-breaking/funnel-input logic.
    private static List<NavMeshNode> RunSearch(NavMeshHandler navMeshHandler, NavMeshNode startNode, NavMeshNode endNode,
        Vector2 startPos, Vector2 goalPos, HashSet<int> allowedRegions)
    {
        _currentPathfindingIteration++;

        // Closest point on the portal segment to the goal, rather than always the
        // portal's midpoint. A wide doorway crossed dead-center forces a detour when
        // the true shortest route hugs one side of it; this stays just as valid for
        // A* as the midpoint was — it's still a fixed point per (portal, goal), so the
        // same triangle-inequality consistency argument applies — while tracking the
        // true taut path much more closely, especially through large rooms.
        Vector2 ClosestPointOnPortal(NeighborInfo portal)
        {
            Vector2 a = new Vector2(portal.pX1, portal.pY1);
            Vector2 b = new Vector2(portal.pX2, portal.pY2);
            Vector2 ab = b - a;
            float lengthSq = ab.sqrMagnitude;
            if (lengthSq < 1e-6f)
                return a;
            float t = Mathf.Clamp01(Vector2.Dot(goalPos - a, ab) / lengthSq);
            return a + ab * t;
        }

        // Entries carry a snapshot of fCost taken at enqueue time rather than ordering by
        // NavMeshNode's own (mutable) fCost live. A node is commonly enqueued more than
        // once as cheaper paths to it are found; without a snapshot, an older heap entry
        // sifted into position for its *old*, worse fCost would silently start reporting
        // its new, better fCost without ever being re-sifted, breaking the min-heap
        // invariant and letting Dequeue return nodes out of true priority order.
        var nodes = new PriorityQueue<(float fCost, NavMeshNode node)>(
            Comparer<(float fCost, NavMeshNode node)>.Create((a, b) => a.fCost.CompareTo(b.fCost)));

        startNode.EnsurePathFindingIterationCorrectness(_currentPathfindingIteration);
        startNode.gCost = 0f;
        startNode.fCost = 0f;
        startNode.entryPoint = startPos;
        nodes.Enqueue((startNode.fCost, startNode));

        // Safety valve: with well-behaved (non-negative, strictly-relaxed) edges this loop
        // expands each node a small, bounded number of times, but a degenerate/near-zero
        // length portal (two nodes touching at a corner) can make two routes compare as
        // exactly equal cost. Re-relaxing on ties used to let A and B keep re-enqueueing
        // each other forever, growing the heap without bound and hanging the tick that
        // called this. The strict "<" relax check below already rules that out, but this
        // cap is a hard backstop against any other latent graph bug (duplicate/self edges,
        // corrupted prev chains) doing the same thing.
        int maxExpansions = Mathf.Max(2048, navMeshHandler.Nodes.Count * 8);
        int expansions = 0;

        while (nodes.Count > 0)
        {
            if (++expansions > maxExpansions)
            {
                DebugLogger.LogWarning(
                    $"Pathfinding aborted after {maxExpansions} node expansions ({navMeshHandler.Nodes.Count} nav nodes total) — likely a cyclic/degenerate graph state.",
                    "NavMesh");
                return null;
            }

            (float snapshotFCost, NavMeshNode current) = nodes.Dequeue();
            current.EnsurePathFindingIterationCorrectness(_currentPathfindingIteration);

            // A cheaper path to this node was found after this entry was queued — stale.
            if (snapshotFCost > current.fCost)
                continue;

            if (current == endNode)
            {
                // reconstruct path, later apply funnel algorithm
                List<NavMeshNode> path = new List<NavMeshNode>();
                NavMeshNode pathNode = endNode;
                int maxPathNodes = navMeshHandler.Nodes.Count + 1;
                while (pathNode != null)
                {
                    if (path.Count > maxPathNodes)
                    {
                        DebugLogger.LogError(
                            "Pathfinding path reconstruction exceeded the total nav node count — prev chain is cyclic; aborting.",
                            "NavMesh");
                        return null;
                    }
                    path.Add(pathNode);
                    pathNode = pathNode.prev;
                }
                path.Reverse();

                if (NavMeshVisualizer.instance != null)
                    NavMeshVisualizer.instance.HighlightPath(path, 10f);

                return path;
            }

            foreach (NeighborInfo neighborInfo in current.Neighbors)
            {
                NavMeshNode neighborNode = neighborInfo.node;

                // Outside the hierarchical corridor for this search — never expanded into,
                // regardless of cost. endNode itself is always allowed even if its own
                // (approximate, origin-tile-based — see NavMeshNode.RegionId) region tag
                // doesn't match, since PathFindNavMesh already resolved the corridor from
                // the exact destination TILE's region, not this node's.
                if (allowedRegions != null && neighborNode != endNode && !allowedRegions.Contains(neighborNode.RegionId))
                    continue;

                neighborNode.EnsurePathFindingIterationCorrectness(_currentPathfindingIteration);

                Vector2 crossingPoint = ClosestPointOnPortal(neighborInfo);
                float g = current.gCost + Vector2.Distance(current.entryPoint, crossingPoint);

                // Strict improvement only — relaxing on ties is what let equal-cost
                // corner-adjacent nodes re-enqueue each other indefinitely (see comment above).
                if (g >= neighborNode.gCost)
                    continue;

                float h = Vector2.Distance(crossingPoint, goalPos);

                neighborNode.gCost = g;
                neighborNode.fCost = g + h;
                neighborNode.prev = current;
                neighborNode.entryPoint = crossingPoint;

                nodes.Enqueue((neighborNode.fCost, neighborNode));
            }
        }
        return null;
    }
}
