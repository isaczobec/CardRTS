using UnityEngine;

// World-space marker shown on the entity a point-and-click action (an ability today, a
// similarly-targeted card kind later) would target — continuously faces the camera so it
// reads correctly regardless of view angle. EntityTargetIndicator owns
// showing/positioning/destroying instances of this; this only owns facing the camera.
// Billboard convention matches HealthBarPrefab's own LateUpdate.
public class EntityTargetIndicatorPrefab : MonoBehaviour
{
    void LateUpdate()
    {
        Camera cam = Camera.main;
        if (cam == null) return;
        transform.forward = transform.position - cam.transform.position;
    }
}
