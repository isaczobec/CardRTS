// Subscribes to IsSelectableRequest and vetoes it for any entity currently targeted by an
// active ShadowCloakComponent modifier (see AbilityManager's Shadow Cloak ability,
// StalkerCard) — but ONLY from an identifiable enemy viewpoint (RequestingPlayerId set and
// different from the cloaked troop's own owner), unlike RespawnSystem's own
// IsSelectableRequest veto, which applies unconditionally regardless of who's asking. That
// distinction matters here specifically because a respawning entity genuinely can't be
// interacted with by anyone (it's dead), whereas a cloaked troop is still fully alive and
// player-controlled — vetoing it unconditionally would also stop its own owner from
// selecting, commanding, or self/ally-targeting it. See IsSelectableRequest.
// RequestingPlayerId's own doc comment for why that field exists.
//
// BasicMeleeAISystem/BasicRangedAISystem don't consult IsSelectableRequest for their own
// auto-attack target acquisition at all (their IsEnemy/IsValidTarget only ever look at
// TroopComponent/HealthComponent directly) — their own veto against a cloaked target lives
// in IsValidTarget instead, reusing IsCloaked below. That's safe without any owner check
// there: every candidate that ever reaches IsValidTarget is already guaranteed non-owned
// (added via IsEnemy's own owner check, or via a player's right-click order, which
// SelectionManager.IsTargetable already restricts to non-owned entities), so it's always
// being asked "is this cloaked from an enemy's perspective" by construction.
//
// Deliberately does NOT touch already-fired projectiles: SeekingProjectileSystem tracks a
// captured TargetEntityId and SkillshotProjectileSystem does a live hitbox-radius scan —
// neither consults IsSelectableRequest or IsValidTarget, so a shot fired before the cloak
// went up keeps flying and can still land, exactly like RespawnableInPlaceComponent's own
// respawning-but-still-hittable behavior.
public static class ShadowCloakSystem
{
    public static readonly GlobalSystem Instance = new GlobalSystem(Execute, Setup);

    private static void Execute(ECS ecs, FlagEventManager flagEvents) { }

    private static void Setup(ECS ecs)
    {
        ecs.Requests.Subscribe<IsSelectableRequest>((req, innerEcs) =>
        {
            if (!req.RequestingPlayerId.HasValue) return;
            if (!IsCloaked(innerEcs, req.EntityId, out ushort ownerId)) return;
            if (req.RequestingPlayerId.Value == ownerId) return;

            req.IsSelectable = false;
        });
    }

    // True (with the cloaked troop's own OwnerPlayerId out) if an active
    // ShadowCloakComponent modifier currently targets entityId — mirrors
    // ProjectileOnHitSystem.FindActiveModifierId's "walk every ModifierComponent targeting
    // this entity" shape.
    public static bool IsCloaked(ECS ecs, ulong entityId, out ushort ownerId)
    {
        ownerId = 0;

        ComponentStore<TroopComponent> troopStore = ecs.GetComponentStore<TroopComponent>();
        if (troopStore == null || !troopStore.HasComponent(entityId)) return false;

        ComponentStore<ModifierComponent> modifierStore = ecs.GetComponentStore<ModifierComponent>();
        ComponentStore<ShadowCloakComponent> cloakStore = ecs.GetComponentStore<ShadowCloakComponent>();
        if (modifierStore == null || cloakStore == null) return false;

        bool cloaked = false;
        cloakStore.ForEach((ulong modifierId) =>
        {
            if (cloaked) return;
            if (!modifierStore.HasComponent(modifierId)) return;
            if (modifierStore.GetComponent(modifierId).TargetEntityId != entityId) return;
            if (!ModifierQuery.IsActive(ecs, modifierId)) return;
            cloaked = true;
        });

        if (cloaked) ownerId = troopStore.GetComponent(entityId).OwnerPlayerId;
        return cloaked;
    }
}
