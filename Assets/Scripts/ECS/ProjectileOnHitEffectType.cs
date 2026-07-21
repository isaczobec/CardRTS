// Which on-hit effect a ProjectileOnHitComponent-carrying projectile applies to whatever it
// hits — see ProjectileOnHitSystem for how each value's actual effect is implemented. None
// (0, the default) means no on-hit effect at all.
public enum ProjectileOnHitEffectType
{
    None = 0,
    Slow = 1,
    Burn = 2,
}
