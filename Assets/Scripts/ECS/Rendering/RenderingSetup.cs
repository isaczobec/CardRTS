using UnityEngine;

public class RenderingSetup : Singleton<RenderingSetup>
{
    [SerializeField] RenderableManager _renderableManager;
    [SerializeField] Material _capsuleMaterial;

    public void SetupRendering()
    {
        // ActiveECS is ClientLocalECS on clients/host, ECS on standalone
        ECS ecs = TickManager.instance.ActiveECS;
        _renderableManager.Initialize(ecs);


        SelectionManager.instance.Initialize();
        HealthBarManager.instance.Initialize();
        CardHandRenderer.instance.Initialize();
        ResourceCounterUI.instance.Initialize();
        MinimapManager.instance.Initialize();
    }
}
