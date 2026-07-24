using System.Collections.Generic;
using UnityEngine;

// Shared by cards that spawn several entities in a ring around the played point (see
// SkeletonsCard/EphemeralSkeletonsCard) so their placement-preview indicator actually shows
// where each one will land, instead of a single prefab sitting at the cursor.
public static class CircleIndicatorHelper
{
    // CardPlacementIndicatorManager instantiates one copy of the card's IndicatorPrefabName
    // prefab and keeps moving/toggling that exact GameObject every frame to track the
    // cursor (see its own SwapIndicator/UpdateSinglePointIndicator) — indicator here IS that
    // instance. This empties out indicator's own visual content (whatever renderer(s)/child
    // hierarchy the prefab originally carried, regardless of whether they live directly on
    // its root or nested under it) and reparents `count` fresh copies of that same original
    // visual under it, arranged in a ring of `radius` — since they're children of indicator,
    // they keep following the cursor for free with no extra per-frame code needed.
    public static void ArrangeInRing(GameObject indicator, int count, float radius)
    {
        if (indicator == null || count <= 0) return;

        // A pristine copy of indicator's original look, taken before anything below
        // modifies indicator itself — used purely as a stamp for the ring copies, then
        // discarded. Disabled immediately so it never renders at wherever Instantiate
        // happens to place it.
        GameObject template = GameObject.Instantiate(indicator);
        template.SetActive(false);

        // Empty out indicator itself: drop whatever children it already has (snapshot
        // first — mutating the hierarchy while iterating Transform's own enumerator isn't
        // safe) and disable any renderer sitting directly on its root.
        var existingChildren = new List<Transform>(indicator.transform.childCount);
        for (int i = 0; i < indicator.transform.childCount; i++)
            existingChildren.Add(indicator.transform.GetChild(i));
        foreach (Transform child in existingChildren)
            GameObject.Destroy(child.gameObject);

        foreach (Renderer renderer in indicator.GetComponents<Renderer>())
            renderer.enabled = false;

        for (int i = 0; i < count; i++)
        {
            float angle = i * (360f / count) * Mathf.Deg2Rad;
            GameObject ring = GameObject.Instantiate(template, indicator.transform);
            ring.name = $"RingIndicator_{i}";
            ring.transform.localPosition = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
            ring.SetActive(true);
        }

        GameObject.Destroy(template);
    }
}
