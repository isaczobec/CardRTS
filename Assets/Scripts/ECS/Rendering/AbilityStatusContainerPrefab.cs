using UnityEngine;

// Thin wrapper for the per-troop ability-status icon row container — AbilityStatusIconManager
// owns positioning it (offset to the right of the troop) and spawns one
// AbilityStatusIconPrefab child per equipped ability slot into this (add a Horizontal Layout
// Group to the prefab for automatic spacing between them, same as ModifierIconContainer's own
// layout); this just keeps the whole row facing the camera — mirrors
// ModifierIconContainerPrefab exactly, children inherit this rotation so individual icons
// don't need their own.
public class AbilityStatusContainerPrefab : MonoBehaviour
{
    void LateUpdate()
    {
        Camera cam = Camera.main;
        if (cam == null) return;
        transform.forward = transform.position - cam.transform.position;
    }
}
