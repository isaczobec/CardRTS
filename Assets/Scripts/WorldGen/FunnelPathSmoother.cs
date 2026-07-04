using System.Collections.Generic;
using UnityEngine;

public static class FunnelPathSmoother
{
    public static List<Vector2> Pull(List<NavMeshNode> path, Vector2 start, Vector2 end)
    {
        List<Vector2> points = new();

        if (path == null || path.Count == 0)
        {
            points.Add(start);
            points.Add(end);
            return points;
        }

        int portalCount = path.Count - 1;

        // Left/right chains of the corridor, bookended by the degenerate start/end
        // "portals". Each real portal's raw endpoints are unordered (the same segment
        // is shared by both directions of travel), so each portal is oriented against
        // the previous one: a portal and its predecessor bound the same convex node,
        // and of the two ways to pair up their endpoints, exactly one keeps the
        // left-to-left and right-to-right connector segments from crossing inside
        // that node. Picking that pairing at every step keeps both chains simple.
        Vector2[] portalLeft = new Vector2[portalCount + 2];
        Vector2[] portalRight = new Vector2[portalCount + 2];

        portalLeft[0] = start;
        portalRight[0] = start;

        for (int i = 0; i < portalCount; i++)
        {
            NeighborInfo portal = FindPortal(path[i], path[i + 1]);

            Vector2 a = new(portal.pX1, portal.pY1);
            Vector2 b = new(portal.pX2, portal.pY2);

            Vector2 portalMid = (a + b) * 0.5f;
            Vector2 dir = path[i + 1].Center - path[i].Center;

            if (TriArea2(portalMid, portalMid + dir, a) > 0f)
            {
                portalLeft[i + 1] = a;
                portalRight[i + 1] = b;
            }
            else
            {
                portalLeft[i + 1] = b;
                portalRight[i + 1] = a;
            }
        }

        portalLeft[portalCount + 1] = end;
        portalRight[portalCount + 1] = end;

        // Simple Stupid Funnel Algorithm.
        points.Add(start);

        Vector2 apex = start;
        Vector2 left = portalLeft[0];
        Vector2 right = portalRight[0];
        int apexIndex, leftIndex = 0, rightIndex = 0;

        for (int i = 1; i < portalLeft.Length; i++)
        {
            Vector2 newLeft = portalLeft[i];
            Vector2 newRight = portalRight[i];

            if (TriArea2(apex, right, newRight) <= 0f)
            {
                if (apex == right || TriArea2(apex, left, newRight) > 0f)
                {
                    right = newRight;
                    rightIndex = i;
                }
                else
                {
                    points.Add(left);
                    apex = left;
                    apexIndex = leftIndex;
                    left = apex;
                    right = apex;
                    leftIndex = apexIndex;
                    rightIndex = apexIndex;
                    i = apexIndex;
                    continue;
                }
            }

            if (TriArea2(apex, left, newLeft) >= 0f)
            {
                if (apex == left || TriArea2(apex, right, newLeft) < 0f)
                {
                    left = newLeft;
                    leftIndex = i;
                }
                else
                {
                    points.Add(right);
                    apex = right;
                    apexIndex = rightIndex;
                    left = apex;
                    right = apex;
                    leftIndex = apexIndex;
                    rightIndex = apexIndex;
                    i = apexIndex;
                    continue;
                }
            }
        }

        points.Add(end);

        return points;
    }

    private static NeighborInfo FindPortal(NavMeshNode from, NavMeshNode to)
    {
        foreach (NeighborInfo neighbor in from.Neighbors)
        {
            if (neighbor.node == to)
                return neighbor;
        }
        return null;
    }

    private static float TriArea2(Vector2 a, Vector2 b, Vector2 c)
    {
        return (b.x - a.x) * (c.y - a.y) - (c.x - a.x) * (b.y - a.y);
    }

    // Proper segment-crossing test (shared endpoints, e.g. the degenerate start
    // "portal", never count as crossing, which is what allows the first real
    // portal to be paired arbitrarily).
    private static bool SegmentsCross(Vector2 p1, Vector2 p2, Vector2 p3, Vector2 p4)
    {
        float d1 = TriArea2(p3, p4, p1);
        float d2 = TriArea2(p3, p4, p2);
        float d3 = TriArea2(p1, p2, p3);
        float d4 = TriArea2(p1, p2, p4);

        return ((d1 > 0f && d2 < 0f) || (d1 < 0f && d2 > 0f)) &&
               ((d3 > 0f && d4 < 0f) || (d3 < 0f && d4 > 0f));
    }
}
