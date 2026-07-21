using System.Collections.Generic;
using UnityEngine;

// Lets any system layer extra "overlay" materials onto a troop's Renderer(s) (see
// IComponentRenderer.GetRenderers/BasicTroopGameObject.Renderers — a multi-mesh or rigged
// prefab can expose more than one) — e.g. a status effect's tint/outline shader pass drawn
// on top of its normal materials. A Renderer can hold more than one material
// (Renderer.sharedMaterials is an array — extra slots render as additional full-mesh
// passes over the same geometry), so adding an overlay is just appending to that array on
// every one of the entity's renderers, keyed by a caller-chosen string so it can be removed
// again later without disturbing any other overlay active on the same troop at the same
// time (e.g. stunned + slowed simultaneously). Each renderer's own original material set is
// cached the first time an overlay touches it, so removing every overlay always restores
// exactly that — never an accumulation of stale overlay entries.
public class OverlayMaterialManager : Singleton<OverlayMaterialManager>
{
    [SerializeField] private RenderableManager _renderableManager;

    private readonly Dictionary<ulong, Dictionary<Renderer, Material[]>> _baseMaterials = new();
    private readonly Dictionary<ulong, List<(string key, Material material)>> _overlays = new();

    public void Initialize()
    {
        TickManager.instance.ServerFlagEvents.Subscribe<EntityDeletedEvent>(OnEntityDeleted);
    }

    // No-op if entityId already has an overlay under this exact key — call RemoveOverlay
    // first if you want to swap it for a different material.
    public void AddOverlay(ulong entityId, string key, Material material)
    {
        if (material == null || _renderableManager == null) return;

        IReadOnlyList<Renderer> renderers = _renderableManager.GetRenderers(entityId);
        if (renderers == null || renderers.Count == 0) return;

        EnsureBaseCached(entityId, renderers);

        if (!_overlays.TryGetValue(entityId, out List<(string key, Material material)> list))
            _overlays[entityId] = list = new List<(string, Material)>();

        if (list.Exists(o => o.key == key)) return;

        list.Add((key, material));
        Apply(entityId, renderers);
    }

    public void RemoveOverlay(ulong entityId, string key)
    {
        if (!_overlays.TryGetValue(entityId, out List<(string key, Material material)> list)) return;

        int index = list.FindIndex(o => o.key == key);
        if (index < 0) return;

        list.RemoveAt(index);

        IReadOnlyList<Renderer> renderers = _renderableManager?.GetRenderers(entityId);
        if (renderers != null)
            Apply(entityId, renderers);
    }

    private void EnsureBaseCached(ulong entityId, IReadOnlyList<Renderer> renderers)
    {
        if (!_baseMaterials.TryGetValue(entityId, out Dictionary<Renderer, Material[]> baseByRenderer))
            _baseMaterials[entityId] = baseByRenderer = new Dictionary<Renderer, Material[]>();

        foreach (Renderer renderer in renderers)
        {
            if (renderer == null || baseByRenderer.ContainsKey(renderer)) continue;
            baseByRenderer[renderer] = renderer.sharedMaterials;
        }
    }

    private void Apply(ulong entityId, IReadOnlyList<Renderer> renderers)
    {
        if (!_baseMaterials.TryGetValue(entityId, out Dictionary<Renderer, Material[]> baseByRenderer)) return;

        List<(string key, Material material)> overlays = _overlays.TryGetValue(entityId, out var list) ? list : null;

        foreach (Renderer renderer in renderers)
        {
            if (renderer == null || !baseByRenderer.TryGetValue(renderer, out Material[] baseMaterials)) continue;

            if (overlays == null || overlays.Count == 0)
            {
                renderer.sharedMaterials = baseMaterials;
                continue;
            }

            Material[] combined = new Material[baseMaterials.Length + overlays.Count];
            baseMaterials.CopyTo(combined, 0);
            for (int i = 0; i < overlays.Count; i++)
                combined[baseMaterials.Length + i] = overlays[i].material;

            renderer.sharedMaterials = combined;
        }
    }

    private void OnEntityDeleted(EntityDeletedEvent e)
    {
        _baseMaterials.Remove(e.EntityId);
        _overlays.Remove(e.EntityId);
    }
}
