using UnityEngine;

/// <summary>
/// Utility for converting the screen-space cursor position into tile-space float
/// coordinates by ray-casting against the world ground plane (y = 0).
/// </summary>
public static class TileSpaceMouse
{
    /// <summary>
    /// Returns true and sets (x, y) in tile-space floats when the cursor ray
    /// intersects the y = 0 ground plane. Returns false if the camera is missing
    /// or the ray is parallel to the plane.
    /// </summary>
    public static bool TryGetPosition(out float x, out float y)
    {
        x = y = 0f;

        Camera cam = Camera.main;
        if (cam == null) return false;

        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        if (Mathf.Abs(ray.direction.y) < 1e-5f) return false;

        float t = -ray.origin.y / ray.direction.y;
        if (t < 0f) return false;

        Vector3 hit = ray.origin + ray.direction * t;
        x = hit.x;
        y = hit.z;
        return true;
    }
}
