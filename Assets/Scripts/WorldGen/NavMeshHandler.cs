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
    /// indices — so portalMidpoint can be used directly in distance calculations.
    /// </summary>
    public readonly ushort pX1, pY1, pX2, pY2;
    public Vector2 portalMidpoint {get; private set;}
    public NavMeshNode node;

    public NeighborInfo(ushort pX1, ushort pY1, ushort pX2, ushort pY2, NavMeshNode node)
    {
        this.pX1 = pX1;
        this.pY1 = pY1;
        this.pX2 = pX2;
        this.pY2 = pY2;
        this.node = node;

        portalMidpoint = new Vector2((pX1 + pX2) * 0.5f, (pY1 + pY2) * 0.5f);
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

    public void CreateNavMesh(WorldManager worldManager)
    {
        _worldManager = worldManager;
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