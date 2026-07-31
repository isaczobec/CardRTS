// Identifies which registered callback PeriodicAreaEffectSystem invokes against each
// qualifying entity a PeriodicAreaEffectComponent finds each proc — see
// PeriodicAreaEffectSystem.RegisterEffect. None (0) never fires.
public enum AreaEffectType
{
    None        = 0,
    HealPulse   = 1,
    ShadowShield = 2,
    AttackSpeedAura = 3,
}
