using System;

// Which AbilityUsed*Input kind an ability responds to — determines which of Ability's
// Execute* lambdas AbilitySystem expects to be non-null for it.
public enum AbilityType
{
    // Cast via AbilityUsedInput — fires the instant the input is processed, no location.
    Instant,

    // Cast via AbilityUsedAtLocationInput — needs a world point within Range of the
    // casting entity.
    TargetLocation,

    // Cast via AbilityUsedOnEntityInput — needs an existing selectable entity within Range
    // of the casting entity, matching CanTargetFriendly/CanTargetEnemyOrNeutral.
    TargetEntity,
}

// Data + behavior for one ability, looked up by ID via AbilityManager. Not subclassed —
// every ability is a plain instance of this class built with an object initializer (see
// AbilityManager); Type says which input kind it responds to, and exactly one of the
// Execute* lambdas below should be non-null to match. AbilitySystem rejects an input whose
// corresponding lambda is null, so an ability only ever needs to fill in the one it uses.
public class Ability
{
    public AbilityType Type;

    // Shown by AbilityBarUI's hover tooltip (see DescriptionTooltip) — display name and a
    // short explanation of what the ability does.
    public string Name;
    public string Description;

    // World/tile units. Only consulted for AbilityType.TargetLocation — AbilitySystem
    // rejects an AbilityUsedAtLocationInput whose point is further than this from the
    // casting entity's own position before ExecuteAtLocation runs (a server-side safety
    // net; the client is never trusted). In the common case the client itself already
    // clamps the point to within Range before sending — see ClampCastLocationToRange —
    // so that rejection only ever fires against a client that isn't clamping or is lying.
    // Unused (leave 0) for AbilityType.Instant.
    public float Range;

    // Key into ImageRegistry for this ability's icon — see AbilityBarUI.
    public string ImageName;

    // The following Show*/Clamp* fields are purely client-side presentation, read by
    // AbilityIndicatorManager while this ability's hotkey is held (see
    // AbilityInputManager.HeldAbilityId) — AbilitySystem itself never looks at them.

    // Ground circle of radius Range, centered on the caster.
    public bool ShowRangeCircle;

    // Ground circle of radius CursorCircleRadius, centered on the cast point (the cursor's
    // world position, range-clamped per ClampCastLocationToRange — see
    // AbilityTargeting.ResolveCastPoint). Only meaningful for AbilityType.TargetLocation.
    public bool ShowCursorCircle;
    public float CursorCircleRadius;

    // Direction arrow from the caster toward the cast point — for skillshot-style
    // abilities. Only meaningful for AbilityType.TargetLocation.
    public bool ShowDirectionArrow;

    // When true, the direction arrow is always drawn Range long (the same radius as
    // ShowRangeCircle) instead of the actual distance to the cast point — so it always
    // reads as "this is exactly how far it'll travel" rather than shrinking as the cursor
    // gets closer to the caster. The arrow's direction still follows the cursor either
    // way; only its length is pinned. Only meaningful alongside ShowDirectionArrow.
    public bool DirectionArrowAlwaysMaxRange;

    // Whether the actual cast location — and every indicator above that depends on the
    // cursor (ShowCursorCircle/ShowDirectionArrow) — clamps to the closest point within
    // Range of the caster when the raw cursor position is further away, instead of using
    // the raw point as-is. AbilityInputManager applies this to what it actually sends;
    // AbilityIndicatorManager applies the identical resolution so the preview always
    // matches where the cast will really land. Only meaningful for
    // AbilityType.TargetLocation.
    public bool ClampCastLocationToRange = true;

    // The following are only meaningful for AbilityType.TargetEntity.

    // Whether this ability may be cast on an entity owned by the casting player.
    public bool CanTargetFriendly;
    // Whether this ability may be cast on an entity NOT owned by the casting player
    // (covers both enemy and neutral).
    public bool CanTargetEnemyOrNeutral;

    // How close to the cursor (world units) a candidate entity must be to be considered
    // "the one the cursor is pointing at" — see EntityTargeting.FindClosestSelectable.
    // Independent of Range (the max distance from the CASTER a target may be); this is
    // about resolving which entity near the cursor the player means, the same role
    // SelectionManager.SingleSelectRadius plays for click-selection.
    public float TargetSelectionRadius = 2.5f;

    // World-space marker on the entity that would be targeted right now, shown while this
    // ability's hotkey is held — see AbilityIndicatorManager/EntityTargetIndicator.
    public bool ShowTargetIndicator;

    // Which of the casting entity's projectile pools (see ProjectileOwnerComponent's
    // linked-list doc comment) an ability that fires a pooled projectile should draw from —
    // 0 (default) is the caster's own primary pool, 1 is NextProjectileOwnerId, 2 is the
    // one after that, and so on. Only meaningful for an ability whose Execute* actually
    // fires a pooled projectile (see ProjectilePool.ResolveOwnerAtIndex); ignored otherwise.
    public int ProjectileOwnerIndex;

    // Exactly one of these should be non-null, matching Type. Left null for whichever
    // input kind this ability doesn't apply to; AbilitySystem checks for that and rejects
    // an input whose matching lambda is missing instead of throwing.
    public Action<ECS, AbilityUsedInput> ExecuteInstant;
    public Action<ECS, AbilityUsedAtLocationInput> ExecuteAtLocation;
    public Action<ECS, AbilityUsedOnEntityInput> ExecuteOnEntity;

    // The following fields drive AbilityCasterTargeting.FindCasters — deciding WHICH of the
    // player's own troops actually cast this ability when its ability-bar hotkey (see
    // AbilityBarComponent/AbilityInputManager) is pressed and released. Entirely client-side
    // targeting/UX (like the Show*/Clamp* fields above) — AbilitySystem itself only ever
    // validates one (casterId, abilityId) pair at a time and doesn't care how many casters a
    // single key-release resolved to.

    // Distance (world/tile units) from the cursor within which an off-cooldown troop that has
    // this ability equipped is a candidate caster — the same role Ability.TargetSelectionRadius
    // plays for resolving a TargetEntity ability's CAST TARGET near the cursor, just for
    // resolving the CASTER instead.
    public float CasterSelectionRadius = 15f;

    // Max number of troops that may simultaneously cast this ability from one hotkey
    // press/release — the closest MaxSimultaneousCasters eligible troops (within
    // CasterSelectionRadius, or among the selection — see PrioritizeSelectedTroops) are chosen.
    // <= 0 means unlimited: every eligible troop found casts.
    public int MaxSimultaneousCasters = 1;

    // When true: if one or more of the player's currently-selected troops (see
    // SelectionManager) have this ability equipped at all (regardless of cooldown), casting is
    // restricted to just the selected troops that are actually off-cooldown right now —
    // CasterSelectionRadius/cursor distance is ignored entirely in that case (only
    // MaxSimultaneousCasters still caps how many of them cast). Falls back to ordinary
    // cursor-proximity selection (CasterSelectionRadius, above) when no selected troop has this
    // ability equipped at all.
    public bool PrioritizeSelectedTroops;
}
