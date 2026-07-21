using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Handles the visual representation of entities that carry a specific RenderableType.
/// Register implementations with RenderableManager.
/// </summary>
public interface IComponentRenderer
{
    /// <summary>Called once, before any other callback, so the renderer can stash the
    /// ECS reference and subscribe to whatever flag events it needs.</summary>
    void Initialize(ECS ecs);

    /// <summary>Called once when an entity with this renderer's type first appears.</summary>
    void OnEntityAdded(ulong entityId);

    /// <summary>Called once when such an entity is removed or its RenderableComponent is stripped.</summary>
    void OnEntityRemoved(ulong entityId);

    /// <summary>Called once when EntityActivatedEvent fires for an entity belonging to this renderer's type.</summary>
    void OnEntityActivated(ulong entityId);

    /// <summary>
    /// Called every Unity Update with the full current list of entities belonging to this renderer.
    /// </summary>
    void UpdateRenderable(List<ulong> entityIds);

    /// <summary>
    /// Optional — every Renderer (MeshRenderer, SkinnedMeshRenderer, ...) that make up
    /// entityId's visual, for anything that wants to layer extra overlay materials onto it
    /// (see OverlayMaterialManager) — e.g. a multi-mesh or rigged prefab can expose more
    /// than one. Null if this renderer's visuals don't expose any (most non-troop
    /// renderers).
    /// </summary>
    IReadOnlyList<Renderer> GetRenderers(ulong entityId);
}
