using System.Collections.Generic;
using UnityEngine;

// String-pulling ("simple stupid funnel algorithm") over the portals connecting a
// NavMeshNode path from Pathfinding.PathFind. Turns the coarse node sequence into
// a taut list of waypoints that hugs corners instead of routing through node/portal
// centers.
public static class FunnelPathSmoother
{
    private struct Portal
    {
        public Vector2 left;
        public Vector2 right;
    }

    public static List<Vector2> Pull(List<NavMeshNode> path, Vector2 start, Vector2 end)
    {
        if (path == null || path.Count == 0)
            return null;

        if (path.Count == 1)
            return new List<Vector2> { start, end };

        List<Portal> portals = BuildPortals(path, start, end);
        return Funnel(portals);
    }

    private static List<Portal> BuildPortals(List<NavMeshNode> path, Vector2 start, Vector2 end)
    {
        var portals = new List<Portal>(path.Count + 1)
        {
            new Portal { left = start, right = start }
        };

        for (int i = 0; i < path.Count - 1; i++)
        {
            NavMeshNode a = path[i];
            NavMeshNode b = path[i + 1];

            NeighborInfo link = a.Neighbors.Find(n => n.node == b);
            GetPortalLeftRight(a, b, link, out Vector2 left, out Vector2 right);
            portals.Add(new Portal { left = left, right = right });
        }

        portals.Add(new Portal { left = end, right = end });
        return portals;
    }

    // Nodes are unordered axis-aligned rectangles, so the portal's two stored
    // endpoints (NeighborInfo.pX1/pY1/pX2/pY2) aren't inherently "left" or "right" —
    // that depends on which way you're walking through the doorway. Which side of A
    // node B sits on fully determines that, with no ambiguity, since portals here are
    // always axis-aligned (purely vertical or purely horizontal).
    private static void GetPortalLeftRight(NavMeshNode a, NavMeshNode b, NeighborInfo portal, out Vector2 left, out Vector2 right)
    {
        Vector2 p0 = new Vector2(portal.pX1, portal.pY1);
        Vector2 p1 = new Vector2(portal.pX2, portal.pY2);

        if (b.x1 == a.x2 + 1)
        {
            // b is to the right of a: larger Y is left, smaller Y is right.
            if (p0.y >= p1.y) { left = p0; right = p1; } else { left = p1; right = p0; }
        }
        else if (a.x1 == b.x2 + 1)
        {
            // b is to the left of a: smaller Y is left, larger Y is right.
            if (p0.y <= p1.y) { left = p0; right = p1; } else { left = p1; right = p0; }
        }
        else if (b.y1 == a.y2 + 1)
        {
            // b is above a: smaller X is left, larger X is right.
            if (p0.x <= p1.x) { left = p0; right = p1; } else { left = p1; right = p0; }
        }
        else
        {
            // b is below a: larger X is left, smaller X is right.
            if (p0.x >= p1.x) { left = p0; right = p1; } else { left = p1; right = p0; }
        }
    }

    // Twice the signed area of triangle abc. Positive when c is left of the
    // directed line a->b, negative when c is right of it, zero when collinear.
    private static float TriArea2(Vector2 a, Vector2 b, Vector2 c)
    {
        return (b.x - a.x) * (c.y - a.y) - (c.x - a.x) * (b.y - a.y);
    }

    private static List<Vector2> Funnel(List<Portal> portals)
    {
        var result = new List<Vector2>();

        Vector2 portalApex = portals[0].left;
        Vector2 portalLeft = portals[0].left;
        Vector2 portalRight = portals[0].right;

        int apexIndex = 0;
        int leftIndex = 0;
        int rightIndex = 0;

        result.Add(portalApex);

        for (int i = 1; i < portals.Count; i++)
        {
            Vector2 left = portals[i].left;
            Vector2 right = portals[i].right;

            // Try to tighten the funnel from the right.
            if (TriArea2(portalApex, portalRight, right) <= 0f)
            {
                if (portalApex == portalRight || TriArea2(portalApex, portalLeft, right) > 0f)
                {
                    portalRight = right;
                    rightIndex = i;
                }
                else
                {
                    // Right crossed over left: the funnel has collapsed. portalLeft
                    // becomes a waypoint and the new apex; restart scanning from there.
                    result.Add(portalLeft);

                    portalApex = portalLeft;
                    apexIndex = leftIndex;
                    portalLeft = portalApex;
                    portalRight = portalApex;
                    leftIndex = apexIndex;
                    rightIndex = apexIndex;

                    i = apexIndex;
                    continue;
                }
            }

            // Try to tighten the funnel from the left.
            if (TriArea2(portalApex, portalLeft, left) >= 0f)
            {
                if (portalApex == portalLeft || TriArea2(portalApex, portalRight, left) < 0f)
                {
                    portalLeft = left;
                    leftIndex = i;
                }
                else
                {
                    // Left crossed over right: same collapse, mirrored.
                    result.Add(portalRight);

                    portalApex = portalRight;
                    apexIndex = rightIndex;
                    portalLeft = portalApex;
                    portalRight = portalApex;
                    leftIndex = apexIndex;
                    rightIndex = apexIndex;

                    i = apexIndex;
                    continue;
                }
            }
        }

        result.Add(portals[portals.Count - 1].left);
        return result;
    }
}
