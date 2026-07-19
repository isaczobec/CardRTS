using UnityEngine;

public class RenderingSetup : Singleton<RenderingSetup>
{
    [SerializeField] RenderableManager _renderableManager;
    [SerializeField] RenderableModifierManager _renderableModifierManager;
    [SerializeField] Material _capsuleMaterial;

    public void SetupRendering()
    {
        // ActiveECS is ClientLocalECS on clients/host, ECS on standalone
        ECS ecs = TickManager.instance.ActiveECS;
        _renderableManager.Initialize(ecs);
        _renderableModifierManager.Initialize(ecs);


        SelectionManager.instance.Initialize();
        HealthBarManager.instance.Initialize();
        EntityTimerTextRenderer.instance.Initialize();
        CardHandRenderer.instance.Initialize();
        ResourceCounterUI.instance.Initialize();
        MinimapManager.instance.Initialize();
        FloatingTextManager.instance.Initialize();
        CardRangeIndicatorManager.instance.Initialize();
        CardPlacementIndicatorManager.instance.Initialize();
        CardTargetIndicatorManager.instance.Initialize();
        DeployProgressIndicatorManager.instance.Initialize();
        AbilityInputManager.instance.Initialize();
        AbilityBarUI.instance.Initialize();
        AbilityIndicatorManager.instance.Initialize();
        AudioManager.instance.Initialize();
        ShopUIManager.instance.Initialize();
        UpgradeShopUIManager.instance.Initialize();
        AIModeUI.instance.Initialize();
    }
}
