using System.Collections.Generic;
using UnityEngine;

// Spawns a one-shot VFX prefab at a damaged entity's position whenever it takes damage,
// chosen by the DEALING entity's own RenderableType (see _prefabEntries) — e.g. a distinct
// impact effect per troop kind's attack. Subscribes to DamageDealtEvent via
// TickManager.ServerFlagEvents (server-confirmed only, like FloatingTextManager's own damage
// numbers) rather than DamageRequest's SubscribeExecuted (predicted) — unlike HealthBarManager's
// SetHealth, a one-shot spawn isn't idempotent, so replaying a mispredicted DamageRequest
// during reconciliation would otherwise spawn extra, never-cleaned-up instances.
//
// Purely event-driven and, like FloatingTextManager/SelectionManager, sits outside the ECS
// system list entirely — never registered as an ISystem.
public class DamageImpactEffectManager : Singleton<DamageImpactEffectManager>
{
    [System.Serializable]
    public class DamagerPrefabEntry
    {
        [SerializeField] public RenderableType renderableType;
        [SerializeField] public GameObject prefab;

        // Seconds after spawning before THIS entry's instance is destroyed. <= 0 means
        // indefinite — the spawned instance is left alone entirely (e.g. a prefab that
        // manages its own lifetime, like a one-shot particle system set to auto-destroy on
        // finish).
        [SerializeField] public float destroyAfterSeconds = 3f;
    }

    [SerializeField] private List<DamagerPrefabEntry> _prefabEntries;

    private readonly Dictionary<RenderableType, DamagerPrefabEntry> _entriesByDealerType = new Dictionary<RenderableType, DamagerPrefabEntry>();

    private ComponentStore<PositionComponent> _positionStore;
    private ComponentStore<RenderableComponent> _renderableStore;

    public void Initialize()
    {
        _entriesByDealerType.Clear();
        foreach (DamagerPrefabEntry entry in _prefabEntries)
        {
            if (entry.prefab == null) continue;
            _entriesByDealerType[entry.renderableType] = entry;
        }

        TickManager.instance.ServerFlagEvents.Subscribe<DamageDealtEvent>(OnDamageDealt);

        ECS ecs = TickManager.instance.ActiveECS;
        _positionStore = ecs.GetComponentStore<PositionComponent>();
        _renderableStore = ecs.GetComponentStore<RenderableComponent>();
    }

    private void OnDamageDealt(DamageDealtEvent e)
    {
        if (_positionStore == null || _renderableStore == null) return;
        if (!_positionStore.HasComponent(e.EntityId)) return;

        ulong dealerId = e.DealerEntityId;
        if (dealerId == 0 || dealerId == DamageRequest.NO_DEALER_ENTITYID) return;
        if (!_renderableStore.HasComponent(dealerId)) return;

        RenderableType dealerType = _renderableStore.GetComponent(dealerId).Type;
        if (!_entriesByDealerType.TryGetValue(dealerType, out DamagerPrefabEntry entry)) return;

        PositionComponent pos = _positionStore.GetComponent(e.EntityId);
        GameObject instance = Instantiate(entry.prefab, WorldPositionFor(pos.X, pos.Y), Quaternion.identity);

        if (entry.destroyAfterSeconds > 0f)
            Destroy(instance, entry.destroyAfterSeconds);
    }

    private static Vector3 WorldPositionFor(float x, float y)
    {
        float height = WorldManager.instance.Handler.GetHeight((ushort)x, (ushort)y);
        return new Vector3(x, height, y);
    }
}
