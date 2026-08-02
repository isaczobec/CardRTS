using System;

// Grants a permanent modifier entity (ModifierComponent.TicksRemaining = int.MaxValue,
// targeting whatever entity the upgraded SpawnAtPointCard just spawned) carrying a
// VengefulSpiritsSourceComponent — mirrors GiantsbaneUpgrade/CleaveUpgrade's own
// always-visible permanent modifier shape. See VengefulSpiritsSystem for the actual
// proc-a-seeking-projectile-on-direct-hit effect this grants the wielder.
//
// Also attaches (or links, via ProjectilePool.AttachNewPoolOwner — see its own doc comment)
// a brand-new pooled seeking-projectile source to the spawned entity itself, since this can
// run against ANY SpawnAtPointCard's result (melee troop, ranged troop, or building) and
// most of those don't already have one of their own.
public class VengefulSpiritsUpgrade : CardUpgrade
{
    private const int Damage = 12;
    private const int PoolSize = 16;
    private const int ProjectileSpeedMilliTilesPerSecond = 20000; // 20 tiles/sec

    public override UpgradeType Type => UpgradeType.VengefulSpirits;
    public override string Title => "Vengeful Spirits";
    public override string Description => $"Every direct hit this troop lands also summons a spirit that seeks out the target, dealing {Damage} damage.";
    public override string ImageName => "VengefulSpirits";
    public override int ShopGoldCost => 170;
    // Explicit design ask — doesn't stack; only one copy of this upgrade may be equipped on
    // the same card at once (one pool, one proc per hit).
    public override int MaxStackCount => 1;

    public override Action<ulong, ECS> OnSpawnAtPointCardPlayed => (entityId, ecs) =>
    {
        ulong poolOwnerId = ProjectilePool.AttachNewPoolOwner(ecs, entityId);
        if (poolOwnerId == 0) return;

        // Projectiles are created with entityId (the real troop/building) as their own
        // owner — see ProjectilePool.AttachNewPoolOwner's own doc comment on why, and
        // SkillshotRangedTroopCard for the precedent this mirrors.
        ulong firstProjectileId = ProjectilePool.CreatePool(
            ecs, entityId, PoolSize, ProjectileSpeedMilliTilesPerSecond,
            RenderableType.VengefulSpirit,
            fixedDamageOverride: Damage,
            procType: DamageProcType.Secondary);

        ComponentStore<ProjectileOwnerComponent> ownerStore = ecs.GetComponentStore<ProjectileOwnerComponent>();
        ref ProjectileOwnerComponent poolOwner = ref ownerStore.GetComponent(poolOwnerId);
        poolOwner.MaxProjectiles = PoolSize;
        poolOwner.NextProjectileId = firstProjectileId;
        ecs.Delta.MarkComponentDirty(poolOwnerId, typeof(ProjectileOwnerComponent));

        EntityHandle modifier = ecs.CreateEntity();
        ecs.AddComponent(modifier.Id, new ModifierComponent
        {
            TargetEntityId = entityId,
            TicksRemaining = int.MaxValue,
            ModifierID     = ModifierID.VengefulSpirits,
        });
        ecs.AddComponent(modifier.Id, new VengefulSpiritsSourceComponent
        {
            ProjectilePoolOwnerId = poolOwnerId,
        });
        ecs.AddComponent(modifier.Id, new RenderableModifierComponent
        {
            Type = RenderableModifierType.VengefulSpirits,
        });
    };
}
