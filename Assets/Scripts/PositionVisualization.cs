using System.Collections.Generic;
using UnityEngine;

public class PositionVisualization : MonoBehaviour
{
    [SerializeField] private GameObject _prefab;

    private readonly Dictionary<ulong, GameObject> _entityObjects = new();

    void Start()
    {
        var flagEvents = TickManager.instance.FlagEvents;
        flagEvents.Subscribe<ComponentAddedEvent<PositionComponent>>(OnPositionComponentAdded);
        flagEvents.Subscribe<PositionUpdatedEvent>(OnPositionUpdated);
        TickManager.instance.ServerFlagEvents.Subscribe<ComponentAddedEvent<PositionComponent>>(OnPositionComponentAdded);
        TickManager.instance.ServerFlagEvents.Subscribe<PositionUpdatedEvent>(OnPositionUpdated);
    }

    void OnDestroy()
    {
        if (TickManager.instance == null) return;
        var flagEvents = TickManager.instance.FlagEvents;
        flagEvents.Unsubscribe<ComponentAddedEvent<PositionComponent>>(OnPositionComponentAdded);
        flagEvents.Unsubscribe<PositionUpdatedEvent>(OnPositionUpdated);
        TickManager.instance.ServerFlagEvents.Unsubscribe<ComponentAddedEvent<PositionComponent>>(OnPositionComponentAdded);
        TickManager.instance.ServerFlagEvents.Unsubscribe<PositionUpdatedEvent>(OnPositionUpdated);
    }

    void Update()
    {
        if (_entityObjects.Count == 0) return;

        var ecs = TickManager.instance.ActiveECS;
        if (ecs == null) return;
        var store = ecs.GetComponentStore<PositionComponent>();
        float t = TickManager.instance.TimeSinceLastTick / TickManager.TickInterval;

        foreach (var kvp in _entityObjects)
        {
            if (!store.HasComponent(kvp.Key)) continue;
            ref var pos = ref store.GetComponent(kvp.Key);
            kvp.Value.transform.position = Vector3.Lerp(
                new Vector3(pos.PrevX, 0f, pos.PrevY),
                new Vector3(pos.X,     0f, pos.Y),
                t
            );
        }
    }

    private void OnPositionComponentAdded()
    {
        var ecs = TickManager.instance.ActiveECS;
        if (ecs == null) return;
        var store = ecs.GetComponentStore<PositionComponent>();
        store.ForEach(id =>
        {
            if (_entityObjects.ContainsKey(id)) return;
            var go = Instantiate(_prefab);
            _entityObjects[id] = go;
        });
    }

    private void OnPositionUpdated() { }
}
