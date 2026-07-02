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
        _renderableManager.Register(RenderableType.Capsule, new CapsuleRenderer(ecs, _capsuleMaterial));
    }
}
