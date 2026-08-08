
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

        _currentPathfindingIteration++;

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

        Vector2 goalPos = new Vector2(desX, desY);

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
        startNode.entryPoint = new Vector2(worldX, worldY);
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