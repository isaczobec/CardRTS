using UnityEngine;

public class TickManager : Singleton<TickManager>
{
    public ECS ECS { get; private set; }
    public FlagEventManager FlagEvents => ECS.FlagEvents;

    public const float TickInterval = 0.1f;
    public float TimeSinceLastTick => _timer;
    private float _timer;

    protected override void Awake()
    {
        base.Awake();
        ECS = new ECS();
        ECS.AddComponentStore(new ComponentStore<PositionComponent>());
        ECS.AddComponentStore(new ComponentStore<RandomWalkComponent>());

        ECS.RegisterSystem(RandomWalkSystem.Instance);

        EntityHandle e1 = ECS.CreateEntity();
        EntityHandle e2 = ECS.CreateEntity();


        ECS.AddComponent(e1.Id, new PositionComponent());
        ECS.AddComponent(e2.Id, new PositionComponent());
        ECS.AddComponent(e1.Id, new RandomWalkComponent(speed: 5f, arrivalRadius: 1f));
        ECS.AddComponent(e2.Id, new RandomWalkComponent(speed: 5f, arrivalRadius: 1f));
    }

    void Update()
    {
        _timer += Time.deltaTime;
        if (_timer < TickInterval) return;
        _timer -= TickInterval;
        Tick();
    }

    void Tick()
    {
        ECS.ExecuteSystems();
        FlagEvents.Flush();
    }
}
