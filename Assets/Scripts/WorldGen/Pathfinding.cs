
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
        _currentPathfindingIteration++;

        // convert world coordinates to tile coordinates with floor
        ushort tileX = (ushort)Mathf.FloorToInt(worldX);
        ushort tileY = (ushort)Mathf.FloorToInt(worldY);
        ushort desTileX = (ushort)Mathf.FloorToInt(desX);
        ushort desTileY = (ushort)Mathf.FloorToInt(desY);

        NavMeshNode startNode = NavMeshHandler.GetNodeAt(tileX, tileY);
        NavMeshNode endNode = NavMeshHandler.GetNodeAt(desTileX, desTileY);

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

        while (nodes.Count > 0)
        {
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
                while (pathNode != null)
                {
                    path.Add(pathNode);
                    pathNode = pathNode.prev;
                }
                path.Reverse();
                return path;
            }

            foreach (NeighborInfo neighborInfo in current.Neighbors)
            {
                NavMeshNode neighborNode = neighborInfo.node;
                neighborNode.EnsurePathFindingIterationCorrectness(_currentPathfindingIteration);

                Vector2 crossingPoint = ClosestPointOnPortal(neighborInfo);
                float g = current.gCost + Vector2.Distance(current.entryPoint, crossingPoint);

                if (g > neighborNode.gCost)
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