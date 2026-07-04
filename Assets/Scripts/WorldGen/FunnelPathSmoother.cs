using System.Collections.Generic;
using UnityEngine;
using System;
public static class FunnelPathSmoother
{
    public static List<Vector2> Pull(List<NavMeshNode> path, Vector2 start, Vector2 end)
    {
        if (path.Count < 2)
            return new List<Vector2> { start, end };

        List<(Vector2 a, Vector2 b)> portals = new List<(Vector2 a, Vector2 b)>();
        for (int i = 0; i < path.Count - 1; i++)
        {
            var portal = NavMeshHandler.ComputePortalFloat(path[i], path[i + 1]);
            portals.Add(portal);
        }
        return Pull(portals, start, end);
    }
    public static List<Vector2> Pull(List<(Vector2 a, Vector2 b)> portals, Vector2 start, Vector2 end)
    {
        int n = portals?.Count ?? 0;
        var result = new List<Vector2>(n + 2) { start };

        if (n == 0) { result.Add(end); return result; }

        // P_i = Lerp(a_i, b_i, t_i). Minimize |P_1-start| + Σ|P_{i+1}-P_i| + |end-P_n|,
        // which is convex in (t_1..t_n) -> the relaxation below reaches the global optimum.
        var t = new double[n];

        // Warm start: project the midpoint of start->end onto each portal.
        for (int i = 0; i < n; i++)
            t[i] = InitParam(portals[i].a, portals[i].b, (start + end) * 0.5f);

        const int maxSweeps = 2000;
        const double tol = 1e-9;

        for (int sweep = 0; sweep < maxSweeps; sweep++)
        {
            double maxDelta = 0.0;

            // Alternate direction each sweep -> faster convergence along the chain.
            bool forward = (sweep & 1) == 0;
            int begin = forward ? 0 : n - 1;
            int step  = forward ? 1 : -1;

            for (int k = 0; k < n; k++)
            {
                int i = begin + step * k;

                Vector2 prev = (i == 0)     ? start : Point(portals[i - 1], t[i - 1]);
                Vector2 next = (i == n - 1) ? end   : Point(portals[i + 1], t[i + 1]);

                double newT = OptimalParam(portals[i].a, portals[i].b, prev, next);
                maxDelta = Math.Max(maxDelta, Math.Abs(newT - t[i]));
                t[i] = newT;
            }

            if (maxDelta < tol) break;
        }

        for (int i = 0; i < n; i++) result.Add(Point(portals[i], t[i]));
        result.Add(end);
        return result;
    }

    private static Vector2 Point((Vector2 a, Vector2 b) portal, double t)
        => portal.a + (portal.b - portal.a) * (float)t;

    // argmin over t in [0,1] of |P(t)-q| + |P(t)-r|, P(t)=Lerp(a,b,t).
    // Mirror principle: reflect r across the line if q,r are on the same side,
    // then the segment q->r' crosses the line at the optimum. Clamp to [0,1].
    private static double OptimalParam(Vector2 a, Vector2 b, Vector2 q, Vector2 r)
    {
        Vector2 d = b - a;
        double dd = Dot(d, d);
        if (dd < 1e-20) return 0.0; // zero-length portal

        double sq = Cross(d, q - a);   // ~signed distance of q from the line
        double sr = Cross(d, r - a);

        Vector2 target = r;
        double sTarget = sr;

        if (sq * sr > 0.0)             // same side -> reflect r to force a crossing
        {
            target = Reflect(r, a, d);
            sTarget = -sr;
        }

        double denom = sq - sTarget;
        if (Math.Abs(denom) < 1e-20)   // q and target collinear with line: q is optimal
            return Clamp01(Dot(q - a, d) / dd);

        double s = sq / denom;                     // crossing param along q->target
        Vector2 c = q + (target - q) * (float)s;   // crossing point on the line
        return Clamp01(Dot(c - a, d) / dd);        // its parameter along the portal
    }

    private static Vector2 Reflect(Vector2 p, Vector2 a, Vector2 d)
    {
        double proj = Dot(p - a, d) / Dot(d, d);
        Vector2 foot = a + d * (float)proj;
        return foot + (foot - p);
    }

    private static double InitParam(Vector2 a, Vector2 b, Vector2 p)
    {
        Vector2 d = b - a;
        double dd = Dot(d, d);
        return dd < 1e-20 ? 0.0 : Clamp01(Dot(p - a, d) / dd);
    }

    private static double Dot(Vector2 u, Vector2 v) => (double)u.x * v.x + (double)u.y * v.y;
    private static double Cross(Vector2 u, Vector2 v) => (double)u.x * v.y - (double)u.y * v.x;
    private static double Clamp01(double v) => v < 0 ? 0 : (v > 1 ? 1 : v);
}
