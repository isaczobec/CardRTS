using System.Diagnostics;
using UnityEngine;

// Subscribes to PeriodicDamageReductionProcEvent and, whenever the proccing troop's
// RenderableType matches _renderableType, spawns _prefab at its position and plays
// _sfxName as a one-shot via AudioManager. Spawn-and-forget, like FloatingTextManager's own
// Instantiate call — _prefab is expected to clean itself up (e.g. a particle system with
// Stop Action: Destroy) rather than being tracked/destroyed from here.
//
// One instance per RenderableType that should get this feedback — add another instance
// (its own prefab/sound) for any other troop granted its own PeriodicDamageReductionComponent
// modifier, and register it in RenderingSetup's list. See IronKnightCard for the example
// grant this is meant to visualize.
public class PeriodicDamageReductionProcRenderer : MonoBehaviour
{
    [SerializeField] private RenderableType _renderableType;
    [SerializeField] private GameObject _prefab;
    [SerializeField] private string _sfxName;

    private ComponentStore<PositionComponent> _positionStore;
    private ComponentStore<RenderableComponent> _renderableStore;

    public void Initialize(ECS ecs)
    {
        _positionStore = ecs.GetComponentStore<PositionComponent>();
        _renderableStore = ecs.GetComponentStore<RenderableComponent>();
        TickManager.instance.ServerFlagEvents.Subscribe<PeriodicDamageReductionProcEvent>(OnProc);
    }

    private void OnProc(PeriodicDamageReductionProcEvent e)
    {

        UnityEngine.Debug.Log("proc");

        if (_renderableStore == null || !_renderableStore.HasComponent(e.EntityId)) return;
        if (_renderableStore.GetComponent(e.EntityId).Type != _renderableType) return;
        if (_positionStore == null || !_positionStore.HasComponent(e.EntityId)) return;

        Vector3 worldPos = ToWorldPosition(_positionStore.GetComponent(e.EntityId));

        if (_prefab != null)
            Instantiate(_prefab, worldPos, Quaternion.identity);

        if (!string.IsNullOrEmpty(_sfxName) && AudioManager.instance != null)
            AudioManager.instance.PlayOneShotAtPosition(_sfxName, worldPos);
    }

    private Vector3 ToWorldPosition(PositionComponent pos)
    {
        float height = 0f;
        if (WorldManager.instance != null && WorldManager.instance.Handler != null)
            height = WorldManager.instance.Handler.GetHeight(pos.TileX, pos.TileY);

        return new Vector3(pos.X, height, pos.Y);
    }
}
