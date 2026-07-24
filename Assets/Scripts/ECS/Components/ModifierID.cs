// Identifies which modifier "kind" a ModifierComponent represents, purely for
// ModifierIconManager's UI purposes (icon/name/description) — the actual gameplay effect is
// entirely driven by whatever payload component (StatModifierComponent, ...) sits alongside
// ModifierComponent, not by this. None (0, the default) means "don't render an icon for this
// modifier at all" — every modifier kind gets this unless explicitly given its own value.
public enum ModifierID
{
    None = 0,
    StatChange = 1,
    Chilled = 2,
    Frozen = 3,
    Scorched = 4,
    ShadowCloak = 5,
}
