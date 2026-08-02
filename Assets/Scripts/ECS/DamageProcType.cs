// What KIND of damage instance a DamageRequest represents — orthogonal to DamageType (which
// stat mitigates it). Direct is the default, so every existing DamageRequest call site that
// doesn't set ProcType explicitly keeps behaving exactly as before this was added.
//
// Intended for future on-hit-effect systems (Cleave, Giantsbane, ...) to gate themselves on —
// e.g. only proc off Direct hits, so an on-hit effect doesn't recursively trigger itself off
// its own Secondary damage, or off unrelated DamageOverTime ticks. See CleaveSystem for the
// one system that already does this today (skips Secondary).
public enum DamageProcType : byte
{
    // A troop/building's own direct ranged or melee attack landing, an ability's direct
    // damage, a spell/missile's own impact, or a damage aura's periodic pulse — the
    // "originating" hit, not caused by processing some other DamageRequest.
    Direct = 0,

    // Damage from a periodic damage-over-time effect (a burn/poison-style debuff ticking on
    // its own schedule, or a Bruiser-style deferred/banked damage draining out over time) —
    // e.g. FireManCard's burn, or BruiserUpgrade's drain.
    DamageOverTime = 1,

    // Extra damage dealt as a side effect of processing ANOTHER DamageRequest — e.g. Cleave's
    // splash, Giantsbane's bonus-damage proc, or ShadowAngelDamageShareSystem's redistributed
    // share. Never the original/triggering hit itself.
    Secondary = 2,
}
