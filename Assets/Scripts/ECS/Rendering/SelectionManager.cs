using System.Collections.Generic;
using UnityEngine;

public class SelectionManager : Singleton<SelectionManager>
{
    [SerializeField] private GameObject _selectionPrefab;

    ComponentStore<PositionComponent> _positionStore;
    Dictionary<ulong, SelectionPrefab> _selectionObjects = new();

    public void Initialize()
    {
        TickManager.instance.ServerFlagEvents.Subscribe<ComponentAddedEvent<SelectableComponent>>(SetupSelection);
        TickManager.instance.ServerFlagEvents.Subscribe<EntityDeletedEvent>(DeleteSelectionObject);
        _positionStore = TickManager.instance.ActiveECS.GetComponentStore<PositionComponent>();
    }

    public void Update()
    {
        foreach (var kvp in _selectionObjects)
        {
            ulong entityId = kvp.Key;
            SelectionPrefab selection = kvp.Value;

            if (_positionStore.HasComponent(entityId))
            {
                PositionComponent pos = _positionStore.GetComponent(entityId);
                ApplySelectionPosition(ref pos, selection);
            }
        }
    }

    public void SetupSelection(ComponentAddedEvent<SelectableComponent> e)
    {
        if (!_positionStore.HasComponent(e.EntityId)) return;

        PositionComponent pos = _positionStore.GetComponent(e.EntityId);

        GameObject go = Instantiate(_selectionPrefab, this.transform);
        go.name = $"Selection_{e.EntityId}";
        _selectionObjects[e.EntityId] = go.GetComponent<SelectionPrefab>();
        ApplySelectionPosition(ref pos, _selectionObjects[e.EntityId]);
    }

    public void ApplySelectionPosition(ref PositionComponent pos, SelectionPrefab selection)
    {
        float height = WorldManager.instance.Handler.GetHeight(pos.TileX, pos.TileY);
        selection.transform.position = new Vector3(pos.X, height + 1f, pos.Y);
    }

    public void DeleteSelectionObject(EntityDeletedEvent e)
    {
        if (_selectionObjects.ContainsKey(e.EntityId))
        {
            Destroy(_selectionObjects[e.EntityId].gameObject);
            _selectionObjects.Remove(e.EntityId);
        }
    }
}