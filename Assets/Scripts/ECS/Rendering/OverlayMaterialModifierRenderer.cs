using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Generic IModifierRenderer implementation: applies _material as an overlay (via
/// OverlayMaterialManager) on the target troop's MeshRenderer once a modifier of this
/// renderer's type activates on it, and removes it again once the modifier is gone. One
/// instance handles one RenderableModifierType — register it against that type via
/// RenderableModifierManager's Inspector list, the same way e.g. PrefabModifierRenderer is.
/// RenderableModifierManager now supports more than one renderer per type, so a status
/// effect that needs both a spawned prefab and a material overlay just needs one instance
/// of each registered against the same type — no combined script required.
/// </summary>
public class OverlayMaterialModifierRenderer : MonoBehaviour, IModifierRenderer
{
    [SerializeField] private Material _material;

    // Key OverlayMaterialManager uses to identify this overlay, so it can be removed again
    // without disturbing any other overlay active on the same troop at the same time.
    // Defaults to this component's own GameObject name if left blank — set it explicitly
    // if you have more than one OverlayMaterialModifierRenderer whose default names could
    // collide.
    [SerializeField] private string _key;

    public void Initialize(ECS ecs) { }

    // Deliberately a no-op — the overlay is applied on OnEntityActivated instead (a
    // modifier entity can exist for a tick or more before it's actually active, e.g. mid
    // deploy delay; the overlay shouldn't show until the effect it represents is really in
    // force).
    public void OnEntityAdded(ulong targetEntityId) { }

    public void OnEntityRemoved(ulong targetEntityId)
    {
        if (OverlayMaterialManager.instance != null)
            OverlayMaterialManager.instance.RemoveOverlay(targetEntityId, ResolveKey());
    }

    // Can fire more than once for the same target (see IModifierRenderer) — AddOverlay
    // already no-ops for a duplicate key, so no extra guard is needed here.
    public void OnEntityActivated(ulong targetEntityId)
    {
        if (OverlayMaterialManager.instance != null)
            OverlayMaterialManager.instance.AddOverlay(targetEntityId, ResolveKey(), _material);
    }

    public void UpdateRenderable(List<ulong> targetEntityIds) { }

    private string ResolveKey() => string.IsNullOrEmpty(_key) ? name : _key;
}
