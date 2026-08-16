using System;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

public class NavMeshNode : IComparable<NavMeshNode>
{
    public readonly ushort x1, y1, x2, y2;
    public List<NeighborInfo> Neighbors { get; private set; } = new List<NeighborInfo>();
    public NavMeshNode prev = null;
    public ulong pathFinidngIteration = 0;
    public float gCost = float.MaxValue;
    public float fCost;
    public Vector2 entryPoint;
    public Vector2 Center => new Vector2((x1 + x2 + 1) / 2f, (y1 + y2 + 1) / 2f);

    // Coarse island-graph region this node's origin tile (x1, y1) belongs to (see
    // IslandGraphBuilder) — -1 if no island graph was built for this world (a legacy/non-
    // island map), or the node's origin tile was never stamped as part of any island/bridge.
    // An approximation, not necessarily exact for a node whose rectangle happens to straddle
    // a region boundary — Pathfinding.PathFindNavMesh only ever uses this to PRUNE its
    // hierarchical search, with a fully unrestricted fallback if that pruning fails to find
    // a path, so a mistagged node can cost some performance but never correctness.
    public int RegionId = -1;

    public NavMeshNode(ushort x1, ushort y1, ushort x2, ushort y2)
    {
        this.x1 = x1;
        this.y1 = y1;
        this.x2 = x2;
        this.y2 = y2;
    }

    public void EnsurePathFindingIterationCorrectness(ulong currentIteration)
    {
        if (pathFinidngIteration != currentIteration)
        {
            pathFinidngIteration = currentIteration;
            gCost = float.MaxValue;
            fCost = float.MaxValue;
            prev = null;
        }
    }

    public int CompareTo(NavMeshNode other)
    {
        if (other == null) return 1;
        return fCost.CompareTo(other.fCost);
    }
}

public class NeighborInfo
{
    /// <summary>
    /// Portal coordinates crossing this and the next node, in world space (same
    /// convention as the outer node corners: index+1 for the far edge), not tile
    /// indices — so they can be used directly in distance calculations.
    /// </summary>
    public readonly ushort pX1, pY1, pX2, pY2;
    public NavMeshNode node;

    public NeighborInfo(ushort pX1, ushort pY1, ushort pX2, ushort pY2, NavMeshNode node)
    {
        this.pX1 = pX1;
        this.pY1 = pY1;
        this.pX2 = pX2;
        this.pY2 = pY2;
        this.node = node;
    }
}

public class NavMeshHandler : Singleton<NavMeshHandler>
{
    private WorldManager _worldManager;
    private Dictionary<ushort, List<NavMeshNode>> _xEdges = new Dictionary<ushort, List<NavMeshNode>>();
    private Dictionary<ushort, List<NavMeshNode>> _yEdges = new Dictionary<ushort, List<NavMeshNode>>();
    private bool[,] _isCoveredByNode;
    private NavMeshNode[,] _nodeGrid;
    private ushort _worldSize;

    // Coarse island/bridge region graph built alongside this same generation's tiles (see
    // IslandGraphBuilder) — null for a world generated without ever calling the 2-arg
    // CreateNavMesh overload (e.g. a legacy/non-island map, or a test harness building a
    // navmesh directly), in which case every query below just reports "no region graph"
    // and Pathfinding.PathFindNavMesh falls back to its ordinary unrestricted search.
    private IslandGraphBuilder _islandGraph;

    public List<NavMeshNode> Nodes { get; private set; } = new List<NavMeshNode>();

    private delegate bool IsNeighborPredicate(NavMeshNode node1, NavMeshNode node2);

    // Computes the shared boundary segment between two adjacent nodes, in world space.
    // Same segment is used for both directions of an adjacency so the portal a path
    // crosses doesn't depend on which way it's travelling.
    private static (ushort pX1, ushort pY1, ushort pX2, ushort pY2) ComputePortal(NavMeshNode a, NavMeshNode b)
    {
        if (a.x2 + 1 == b.x1 || b.x2 + 1 == a.x1)
        {
            // vertical boundary: nodes are adjacent left/right
            ushort boundaryX = (ushort)(a.x2 + 1 == b.x1 ? a.x2 + 1 : a.x1);
            ushort yStart = a.y1 > b.y1 ? a.y1 : b.y1;
            ushort yEnd = (ushort)((a.y2 < b.y2 ? a.y2 : b.y2) + 1);
            return (boundaryX, yStart, boundaryX, yEnd);
        }
        else
        {
            // horizontal boundary: nodes are adjacent above/below
            ushort boundaryY = (ushort)(a.y2 + 1 == b.y1 ? a.y2 + 1 : a.y1);
            ushort xStart = a.x1 > b.x1 ? a.x1 : b.x1;
            ushort xEnd = (ushort)((a.x2 < b.x2 ? a.x2 : b.x2) + 1);
            return (xStart, boundaryY, xEnd, boundaryY);
        }
    }

    public static (Vector2 p1, Vector2 p2) ComputePortalFloat(NavMeshNode a, NavMeshNode b)
    {
        if (a.x2 + 1 == b.x1 || b.x2 + 1 == a.x1)
        {
            // vertical boundary: nodes are adjacent left/right
            float boundaryX = (float)(a.x2 + 1 == b.x1 ? a.x2 + 1 : a.x1);
            float yStart = a.y1 > b.y1 ? a.y1 : b.y1;
            float yEnd = (float)((a.y2 < b.y2 ? a.y2 : b.y2) + 1);
            return (new Vector2(boundaryX, yStart), new Vector2(boundaryX, yEnd));
        }
        else
        {
            // horizontal boundary: nodes are adjacent above/below
            float boundaryY = (float)(a.y2 + 1 == b.y1 ? a.y2 + 1 : a.y1);
            float xStart = a.x1 > b.x1 ? a.x1 : b.x1;
            float xEnd = (float)((a.x2 < b.x2 ? a.x2 : b.x2) + 1);
            return (new Vector2(xStart, boundaryY), new Vector2(xEnd, boundaryY));
        }
    }

    private bool IsOccupied(ushort x, ushort y)
    {
        return x >= _worldSize || y >= _worldSize || x < 0 || y < 0 || _worldManager.HasCollision(x, y) || _isCoveredByNode[y, x];
    }

    public NavMeshNode GetNodeAt(ushort x, ushort y)
    {
        if (x >= _worldSize || y >= _worldSize || x < 0 || y < 0)
            return null;
        return _nodeGrid[y, x];
    }

    public NavMeshNode GetNodeAtWorldCoords(float x, float y)
    {
        ushort tileX = (ushort)Mathf.FloorToInt(x);
        ushort tileY = (ushort)Mathf.FloorToInt(y);
        return GetNodeAt(tileX, tileY);
    }

    // Same as GetNodeAt, but if (x, y) itself isn't covered by any node, spirals outward
    // tile-by-tile (up to maxTileRadius) for the nearest one instead of giving up — same
    // expanding-ring idea as EntityClusterFeature.TryFindNearbyFreeTile, just over navmesh
    // nodes instead of raw tile types. Used as Pathfinding.PathFindNavMesh's START node
    // fallback: an entity's own continuous position can, in rare cases (a funnel-smoothed
    // waypoint sitting exactly on a walkable/blocked tile boundary, floating-point drift,
    // etc.), floor into a tile with no node at all — without this, that entity could never
    // path anywhere again, from anywhere, since PathFindNavMesh has no start node to search
    // from. Deliberately NOT used for the destination side of a path — an unreachable
    // destination should still correctly fail rather than snapping to whatever's nearby.
    public NavMeshNode GetNearestNodeAt(ushort x, ushort y, int maxTileRadius = 8)
    {
        NavMeshNode direct = GetNodeAt(x, y);
        if (direct != null) return direct;

        for (int radius = 1; radius <= maxTileRadius; radius++)
        {
            for (int dy = -radius; dy <= radius; dy++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != radius) continue;

                    int nx = x + dx;
                    int ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= _worldSize || ny >= _worldSize) continue;

                    NavMeshNode node = _nodeGrid[ny, nx];
                    if (node != null) return node;
                }
            }
        }

        return null;
    }

    // Exact (tile-precise, not node-approximated) coarse region lookup for a single tile —
    // used by Pathfinding.PathFindNavMesh to resolve the START and DESTINATION region, which
    // needs to be as accurate as possible since it decides whether/how the hierarchical
    // search corridor gets built at all. -1 if no island graph exists for this world, or the
    // tile itself was never stamped as part of any island/bridge.
    public int GetTileRegionId(ushort x, ushort y) => _islandGraph?.GetRegionId(x, y) ?? -1;

    // Cheap Dijkstra over the coarse island/bridge region graph (see IslandGraphBuilder) —
    // typically a few dozen regions even on a large map, several orders of magnitude smaller
    // than the fine NavMeshNode graph it's meant to prune. Returns false (corridor left null)
    // if there's no island graph at all, either region is unknown, or the two regions simply
    // aren't connected in the coarse graph (shouldn't normally happen if the fine graph
    // itself is connected, but the coarse graph is a separate, independently-built
    // approximation of it) — Pathfinding falls back to an unrestricted fine search in every
    // one of those cases, so this is always safe to call speculatively.
    public bool TryGetRegionCorridor(int fromRegion, int toRegion, out HashSet<int> corridor)
    {
        corridor = null;
        if (_islandGraph == null || fromRegion < 0 || toRegion < 0) return false;

        if (fromRegion == toRegion)
        {
            corridor = new HashSet<int> { fromRegion };
            return true;
        }

        Dictionary<int, List<(int neighbor, float cost)>> adjacency = _islandGraph.Adjacency;
        var dist = new Dictionary<int, float> { [fromRegion] = 0f };
        var prev = new Dictionary<int, int>();
        var visited = new HashSet<int>();
        var queue = new PriorityQueue<(float dist, int region)>(Comparer<(float dist, int region)>.Create((a, b) => a.dist.CompareTo(b.dist)));
        queue.Enqueue((0f, fromRegion));

        while (queue.Count > 0)
        {
            (float d, int region) = queue.Dequeue();
            if (!visited.Add(region)) continue;
            if (region == toRegion) break;

            if (!adjacency.TryGetValue(region, out List<(int neighbor, float cost)> edges)) continue;
            foreach ((int neighbor, float cost) in edges)
            {
                float nd = d + cost;
                if (dist.TryGetValue(neighbor, out float existing) && existing <= nd) continue;
                dist[neighbor] = nd;
                prev[neighbor] = region;
                queue.Enqueue((nd, neighbor));
            }
        }

        if (!dist.ContainsKey(toRegion)) return false;

        corridor = new HashSet<int> { toRegion };
        int current = toRegion;
        while (prev.TryGetValue(current, out int previous))
        {
            corridor.Add(previous);
            current = previous;
        }
        return true;
    }

    // islandGraph (see IslandGraphBuilder) tags every node created below with the coarse
    // island/bridge region its origin tile belongs to, so Pathfinding.PathFindNavMesh can
    // run a cheap first-pass search over that handful of regions to narrow down which nodes
    // its real search needs to consider — see TryGetRegionCorridor. Optional/nullable so a
    // caller with no such graph (a legacy/non-island map, or a test harness) still gets an
    // ordinary, fully-connected navmesh with no region pruning at all.
    public void CreateNavMesh(WorldManager worldManager, IslandGraphBuilder islandGraph = null)
    {
        _worldManager = worldManager;
        _islandGraph = islandGraph;
        _worldSize = (ushort)(WorldGenHandler.CHUNK_SIZE_TILES * WorldGenHandler.WorldSizeChunks);
        _isCoveredByNode = new bool[_worldSize, _worldSize];
        _nodeGrid = new NavMeshNode[_worldSize, _worldSize];
        _xEdges.Clear();
        _yEdges.Clear();
        Nodes.Clear();

        // iterate over every world row
        for (ushort i  = 0; i < _worldSize; i++)
        {
            for (ushort j = 0; j < _worldSize; j++)
            {

                if (IsOccupied(j, i))
                    continue;

                _isCoveredByNode[i, j] = true;

                ushort x_bl = j;
                ushort y_bl = i;
                ushort x_tr = j;
                ushort y_tr = i;

                bool x_ok = true;
                bool y_ok = true;
                bool corner_ok = true;

                while (true)
                {
                    // check new y row, new x column, and corner
                    if (x_ok) for (ushort x = x_bl; x <= x_tr; x++)
                    {
                        if (IsOccupied(x, (ushort)(y_tr + 1)))
                        {
                            x_ok = false;
                            break;
                        }
                    }
                    if (y_ok) for (ushort y = y_bl; y <= y_tr; y++)
                    {
                        if (IsOccupied((ushort)(x_tr + 1), y))
                        {
                            y_ok = false;
                            break;
                        }
                    }
                    if (x_ok && y_ok && IsOccupied((ushort)(x_tr + 1), (ushort)(y_tr + 1)))
                    {
                        corner_ok = false;
                    }

                    // expand top right corner if possible, otherwise create node and break
                    if (x_ok && y_ok && corner_ok)
                    {
                        x_tr = (ushort)(x_tr + 1);
                        y_tr = (ushort)(y_tr + 1);

                        for (ushort x = x_bl; x <= x_tr; x++)
                        {
                            _isCoveredByNode[y_tr, x] = true;
                        }
                        for (ushort y = y_bl; y <= y_tr; y++)
                        {
                            _isCoveredByNode[y, x_tr] = true;
                        }

                    } else if (x_ok)
                    {
                        y_tr = (ushort)(y_tr + 1);

                        for (ushort x = x_bl; x <= x_tr; x++)
                        {
                            _isCoveredByNode[y_tr, x] = true;
                        }
                    } else if (y_ok)
                    {
                        x_tr = (ushort)(x_tr + 1);

                        for (ushort y = y_bl; y <= y_tr; y++)
                        {
                            _isCoveredByNode[y, x_tr] = true;
                        }
                    } else
                    {
                        // create node
                        var node = new NavMeshNode(x_bl, y_bl, x_tr, y_tr);
                        node.RegionId = _islandGraph?.GetRegionId(x_bl, y_bl) ?? -1;
                        Nodes.Add(node);

                        // register edges
                        void AddEdgeToDict(ushort coord, NavMeshNode node, Dictionary<ushort, List<NavMeshNode>> dict)
                        {
                            if (!dict.ContainsKey(coord))
                                dict[coord] = new List<NavMeshNode>();
                            dict[coord].Add(node);
                        }

                        // Skip the second call when a node is exactly one tile wide/tall
                        // (x_bl == x_tr or y_bl == y_tr) — otherwise it's registered twice
                        // under the same key, producing duplicate NeighborInfo entries.
                        AddEdgeToDict(x_bl, node, _xEdges);
                        if (x_tr != x_bl)
                            AddEdgeToDict(x_tr, node, _xEdges);

                        AddEdgeToDict(y_bl, node, _yEdges);
                        if (y_tr != y_bl)
                            AddEdgeToDict(y_tr, node, _yEdges);

                        // find all neighbors
                        List<NavMeshNode> potentialTrXNeighbors = _xEdges.ContainsKey((ushort)(x_tr + 1)) ? _xEdges[(ushort)(x_tr + 1)] : new List<NavMeshNode>();
                        List<NavMeshNode> potentialTrYNeighbors = _yEdges.ContainsKey((ushort)(y_tr + 1)) ? _yEdges[(ushort)(y_tr + 1)] : new List<NavMeshNode>();
                        List<NavMeshNode> potentialBlXNeighbors = _xEdges.ContainsKey((ushort)(x_bl - 1)) ? _xEdges[(ushort)(x_bl - 1)] : new List<NavMeshNode>();
                        List<NavMeshNode> potentialBlYNeighbors = _yEdges.ContainsKey((ushort)(y_bl - 1)) ? _yEdges[(ushort)(y_bl - 1)] : new List<NavMeshNode>();
                        
                        void AddNeighbors(NavMeshNode node, List<NavMeshNode> potentialNeighbors, IsNeighborPredicate isNeighbor)
                        {
                            foreach (var neighbor in potentialNeighbors)
                            {
                                if (isNeighbor(node, neighbor))
                                {
                                    var (pX1, pY1, pX2, pY2) = ComputePortal(node, neighbor);
                                    node.Neighbors.Add(new NeighborInfo(pX1, pY1, pX2, pY2, neighbor));
                                    neighbor.Neighbors.Add(new NeighborInfo(pX1, pY1, pX2, pY2, node));
                                }
                            }
                        }

                        AddNeighbors(node, potentialTrXNeighbors, (n1, n2) => n1.y1 <= n2.y2 && n1.y2 >= n2.y1);       
                        AddNeighbors(node, potentialTrYNeighbors, (n1, n2) => n1.x1 <= n2.x2 && n1.x2 >= n2.x1);
                        AddNeighbors(node, potentialBlXNeighbors, (n1, n2) => n1.y1 <= n2.y2 && n1.y2 >= n2.y1);
                        AddNeighbors(node, potentialBlYNeighbors, (n1, n2) => n1.x1 <= n2.x2 && n1.x2 >= n2.x1);

                        // add to nodegrid
                        for (ushort y = y_bl; y <= y_tr; y++)
                        {
                            for (ushort x = x_bl; x <= x_tr; x++)
                            {
                                _nodeGrid[y, x] = node;
                            }
                        }
                        
                        break;
                    }
                }
            }
        }
        DebugLogger.Log($"NavMeshHandler Created {Nodes.Count} nav mesh nodes.", "NavMesh");
    }


}