// Attach alongside a HealModifierComponent (on the same modifier entity) to identify WHICH
// healing source (e.g. a specific Healer Guardian troop) created it — lets a card query for
// "does MY heal already have an active modifier on this target" (to refresh instead of
// duplicating) while leaving a DIFFERENT source's own heal modifier on the same target
// completely alone, so multiple simultaneous sources healing the same target stack rather
// than overwrite each other. Kept separate from HealModifierComponent so that one stays
// usable in contexts with no source/stacking concept at all.
public struct HealSourceComponent : IComponent
{
    public ulong HealerEntityId;
}
