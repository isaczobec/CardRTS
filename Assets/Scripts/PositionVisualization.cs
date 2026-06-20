using System.Collections.Generic;
using UnityEngine;

public class PositionVisualization : MonoBehaviour
{
    [SerializeField] private GameObject _prefab;

    private readonly Dictionary<ulong, GameObject> _entityObjects = new();

    void Update()
    {
        var ecs = TickManager.instance.ActiveECS;
        if (ecs == null) return;
        var store = ecs.GetComponentStore<PositionComponent>();
        if (store == null) return;

        // Discover any entities that don't have a visual yet.
        store.ForEach(id =>
        {
            if (_entityObjects.ContainsKey(id)) return;
            _entityObjects[id] = Instantiate(_prefab);
        });

        foreach (var kvp in _entityObjects)
        {
            if (!store.HasComponent(kvp.Key)) continue;
            ref var pos = ref store.GetComponent(kvp.Key);
            kvp.Value.transform.position = new Vector3(pos.X, 0f, pos.Y);
        }
    }
}
