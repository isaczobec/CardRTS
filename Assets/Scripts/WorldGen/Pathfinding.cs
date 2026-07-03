
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

        float distance(float originX, float originY, NavMeshNode a, NeighborInfo b)
        {
            float distance = Vector2.Distance(new Vector2(originX, originY), b.portalMidpoint);
            return distance;
        }

        float heuristic(NavMeshNode a, NeighborInfo b)
        {
            float distance = Vector2.Distance(b.portalMidpoint, new Vector2(desX, desY));
            return distance;
        }

        PriorityQueue<NavMeshNode> nodes = new PriorityQueue<NavMeshNode>();
        startNode.EnsurePathFindingIterationCorrectness(_currentPathfindingIteration);
        startNode.gCost = 0f;
        startNode.entryPoint = new Vector2(worldX, worldY);
        nodes.Enqueue(startNode);

        while (nodes.Count > 0)
        {
            NavMeshNode current = nodes.Dequeue();
            current.EnsurePathFindingIterationCorrectness(_currentPathfindingIteration);

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
                float g = current.gCost + distance(current.entryPoint.x, current.entryPoint.y, current, neighborInfo);

                if (g > neighborNode.gCost)
                    continue;

                float h = heuristic(current, neighborInfo);

                neighborNode.gCost = g;
                neighborNode.fCost = g + h;
                neighborNode.prev = current;
                neighborNode.entryPoint = neighborInfo.portalMidpoint;

                nodes.Enqueue(neighborNode);
            }
        }
        return null;
    }
}