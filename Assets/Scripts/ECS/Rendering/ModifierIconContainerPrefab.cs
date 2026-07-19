using UnityEngine;

// Thin wrapper for the per-entity modifier-icon row container — ModifierIconManager owns
// positioning (above the entity) and spawns one ModifierIconPrefab child per active
// modifier into this (add a Horizontal Layout Group to the prefab for automatic spacing
// between them); this just keeps the whole row facing the camera, mirroring
// HealthBarPrefab/FloatingTextSpawner's own billboarding — children inherit this rotation,
// so individual icons don't need their own.
public class ModifierIconContainerPrefab : MonoBehaviour
{
    void LateUpdate()
    {
        Camera cam = Camera.main;
        if (cam == null) return;
        transform.forward = transform.position - cam.transform.position;
    }
}
