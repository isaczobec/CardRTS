// Which style of projectile TurretAISystem.ResolveAttack fires with — see
// TurretAIComponent.ProjectileMode.
public enum TurretProjectileMode : byte
{
    // ProjectilePool.Fire — a real pooled SeekingProjectileComponent entity that homes in on
    // TargetEntityId and collides with it (see CannonCard).
    Pooled = 0,

    // A cosmetic-only straight-line/arced entity (BallisticProjectileComponent) with no
    // hitbox at all — travels for a fixed flight duration (its own LifetimeComponent) and,
    // when that runs out, a ScheduledCallSystem call (scheduled at launch time, for exactly
    // that same duration) deals AOE damage in a radius around wherever the target was at the
    // moment of firing (see MissileSiloCard).
    Ballistic = 1,
}

// AI for a stationary defensive building (see CannonCard/MissileSiloCard/TurretAISystem) —
// fires at whichever enemy troop is currently closest and within Range, staying locked onto
// it across multiple attack cycles until it leaves Range, dies, or is otherwise invalidated,
// at which point the turret locks onto the next-closest qualifying enemy. Unlike
// BasicMeleeAIComponent/BasicRangedAIComponent, TargetEntityId is NEVER settable by player
// input — TurretAISystem never reads SetTargetsInput/TargetingSystem at all, so a turret
// can't be manually targeted the way a troop can.
public struct TurretAIComponent : IComponent
{
    public ulong TargetEntityId;

    // 0 = not currently winding up. Otherwise counts down AttackSpeed ticks before firing a
    // projectile at TargetEntityId.
    public int AttackTicksRemaining;

    // Post-shot wind-down — counted down before the turret will fire again. Same shape as
    // BasicRangedAIComponent.WindDownTicksRemaining/WindDownMultiplier.
    public int WindDownTicksRemaining;
    public float WindDownMultiplier;

    // Leeway (as a multiple of Range) allowed when re-checking TargetEntityId is still close
    // enough once the windup finishes, before actually firing — mirrors
    // BasicRangedAIComponent.AttackRangeMultiplier.
    public float AttackRangeMultiplier;

    // A target closer than this (world units, NOT a multiple of Range) is never engageable —
    // a hard floor, unlike AttackRangeMultiplier's leeway on the far edge of Range. 0 (the
    // struct default) means no minimum, i.e. every existing turret keeps engaging anything
    // out to Range with no dead zone. See MissileSiloCard for the one turret that sets this
    // — a long-range siege piece that shouldn't be able to blast something standing right
    // next to it.
    public float MinRange;

    // Whether this turret may also engage an enemy-owned building — see TurretAISystem's own
    // targeting-priority comment. Outranks a neutral building (see CanTargetNeutralBuildings
    // below) but NOT an enemy troop: an enemy troop always takes over a currently-locked
    // enemy/neutral building the instant one comes into range. Defaults to false (a turret
    // like CannonCard only ever engages enemy troops).
    public bool CanTargetEnemyBuildings;

    // Whether this turret may also engage a neutral-owned building (a world-gen resource
    // node/tree — see TroopComponent.NEUTRAL_OWNER_PLAYER_ID) as a last-resort fallback, only
    // when no enemy troop AND no enemy building (if CanTargetEnemyBuildings) is currently in
    // range — see TurretAISystem's own targeting-priority comment. Defaults to false (a
    // turret like CannonCard only ever engages enemy troops).
    public bool CanTargetNeutralBuildings;

    public TurretProjectileMode ProjectileMode;

    // Ballistic-only fields (ignored entirely when ProjectileMode == Pooled):
    public RenderableType BallisticRenderableType;
    public float BallisticSpeedTilesPerSecond;
    // Multiple of this turret's own Range stat — the AOE damage radius applied once a
    // ballistic projectile's flight ends. Captured once per shot onto the projectile's own
    // BallisticProjectileComponent.ImpactRadius at launch time (see TurretAISystem.
    // FireBallistic), purely so the renderer can size its target-ground indicator correctly
    // without needing to know this card's own constants.
    public float BallisticImpactRadiusMultiplier;
}
