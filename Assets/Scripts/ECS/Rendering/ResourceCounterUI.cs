using TMPro;
using UnityEngine;

/// <summary>
/// Reads the local player's PlayerResourcesComponent every frame and reflects it in five
/// TMP_Text counters. Polled directly off the live ECS rather than event-driven — resource
/// totals just change like any other component value, there's no per-entity spawn/despawn
/// lifecycle to react to (unlike HealthBarManager/CardHandRenderer).
/// Call Initialize() once (e.g. from RenderingSetup.SetupRendering) before use.
/// </summary>
public class ResourceCounterUI : Singleton<ResourceCounterUI>
{
    [SerializeField] private TMP_Text _woodText;
    [SerializeField] private TMP_Text _stoneText;
    [SerializeField] private TMP_Text _metalText;
    [SerializeField] private TMP_Text _gemsText;
    [SerializeField] private TMP_Text _soulstonesText;
    [SerializeField] private TMP_Text _goldText;

    private ECS _ecs;
    private ulong _playerEntityId;

    public void Initialize()
    {
        _ecs = TickManager.instance.ActiveECS;
        _playerEntityId = 0;
    }

    void Update()
    {
        if (TickManager.instance == null || !TickManager.instance.IsGameStarted) return;
        if (_ecs == null) return;

        ComponentStore<PlayerResourcesComponent> resourceStore = _ecs.GetComponentStore<PlayerResourcesComponent>();
        if (resourceStore == null) return;

        if (_playerEntityId == 0 || !resourceStore.HasComponent(_playerEntityId))
            _playerEntityId = FindLocalPlayerEntity();

        if (_playerEntityId == 0) return;

        PlayerResourcesComponent resources = resourceStore.GetComponent(_playerEntityId);
        SetText(_woodText, resources.WoodFloor);
        SetText(_stoneText, resources.StoneFloor);
        SetText(_metalText, resources.MetalFloor);
        SetText(_gemsText, resources.GemsFloor);
        SetText(_soulstonesText, resources.SoulstonesFloor);
        SetText(_goldText, resources.GoldFloor);
    }

    private ulong FindLocalPlayerEntity()
    {
        ushort localPlayerId = NetworkManager.instance != null ? NetworkManager.instance.LocalPlayerId : (ushort)0;

        ComponentStore<PlayerComponent> playerStore = _ecs.GetComponentStore<PlayerComponent>();
        ComponentStore<PlayerResourcesComponent> resourceStore = _ecs.GetComponentStore<PlayerResourcesComponent>();
        if (playerStore == null || resourceStore == null) return 0;

        ulong found = 0;
        playerStore.ForEach((ulong id) =>
        {
            if (found != 0) return;
            if (!resourceStore.HasComponent(id)) return;
            if (playerStore.GetComponent(id).PlayerId == localPlayerId) found = id;
        });
        return found;
    }

    private static void SetText(TMP_Text text, int value)
    {
        if (text != null) text.text = value.ToString();
    }
}
