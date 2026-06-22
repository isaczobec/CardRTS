using System.Collections.Generic;

/// <summary>
/// Handles the visual representation of entities that carry a specific RenderableType.
/// Register implementations with RenderableManager.
/// </summary>
public interface IComponentRenderer
{
    /// <summary>Called once when an entity with this renderer's type first appears.</summary>
    void OnEntityAdded(ulong entityId);

    /// <summary>Called once when such an entity is removed or its RenderableComponent is stripped.</summary>
    void OnEntityRemoved(ulong entityId);

    /// <summary>
    /// Called every Unity Update with the full current list of entities belonging to this renderer.
    /// </summary>
    void Update(List<ulong> entityIds);
}
