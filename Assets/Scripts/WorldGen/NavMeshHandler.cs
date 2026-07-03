using System.Collections.Generic;
using Unity.VisualScripting;

public class NavMeshNode
{
    public readonly ushort x1, y1, x2, y2;
    public List<NavMeshNode> Neighbors { get; private set; } = new List<NavMeshNode>();

    public NavMeshNode(ushort x1, ushort y1, ushort x2, ushort y2)
    {
        this.x1 = x1;
        this.y1 = y1;
        this.x2 = x2;
        this.y2 = y2;
    }
}

public class NavMeshHandler : Singleton<NavMeshHandler>
{
    private WorldManager _worldManager;
    private Dictionary<ushort, List<NavMeshNode>> _xEdges = new Dictionary<ushort, List<NavMeshNode>>();
    private Dictionary<ushort, List<NavMeshNode>> _yEdges = new Dictionary<ushort, List<NavMeshNode>>();
    private bool[,] _isCoveredByNode;
    private ushort _worldSize;

    private delegate bool IsNeighborPredicate(NavMeshNode node1, NavMeshNode node2);

    
    private bool IsOccupied(ushort x, ushort y)
    {
        return x >= _worldSize || y >= _worldSize || x < 0 || y < 0 || _worldManager.HasCollision(x, y) || _isCoveredByNode[y, x];
    }

    public void CreateNavMesh(WorldManager worldManager)
    {
        _worldManager = worldManager;
        _worldSize = (ushort)(WorldGenHandler.CHUNK_SIZE_TILES * WorldGenHandler.WorldSizeChunks);
        _isCoveredByNode = new bool[_worldSize, _worldSize];

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

                        // register edges
                        void AddEdgeToDict(ushort x_bl, NavMeshNode node, Dictionary<ushort, List<NavMeshNode>> dict)
                        {
                            if (!dict.ContainsKey(x_bl))
                                dict[x_bl] = new List<NavMeshNode>();
                            dict[x_bl].Add(node);
                        }

                        AddEdgeToDict(x_bl, node, _xEdges);
                        AddEdgeToDict(y_bl, node, _yEdges);
                        AddEdgeToDict(x_tr, node, _xEdges);
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
                                    node.Neighbors.Add(neighbor);
                                    neighbor.Neighbors.Add(node);
                                }
                            }
                        }

                        AddNeighbors(node, potentialTrXNeighbors, (n1, n2) => n1.y1 <= n2.y2 && n1.y2 >= n2.y1);       
                        AddNeighbors(node, potentialTrYNeighbors, (n1, n2) => n1.x1 <= n2.x2 && n1.x2 >= n2.x1);
                        AddNeighbors(node, potentialBlXNeighbors, (n1, n2) => n1.y1 <= n2.y2 && n1.y2 >= n2.y1);
                        AddNeighbors(node, potentialBlYNeighbors, (n1, n2) => n1.x1 <= n2.x2 && n1.x2 >= n2.x1);
                        
                        break;
                    }
                }
            }
        }
    }


}