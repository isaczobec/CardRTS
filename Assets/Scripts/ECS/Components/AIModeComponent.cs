// A troop's stance toward automatic targeting/chasing — see BasicMeleeAISystem/
// BasicRangedAISystem for exactly how each mode behaves. Added to every troop card via
// TroopCardHelper.SpawnTroop; buildings never get one (they have no MovableComponent/AI
// component either) — the AI systems default to Guard for any troop missing this component
// (e.g. one spawned through a path that doesn't go through TroopCardHelper).
public enum AIMode : byte
{
    Passive = 0,
    Guard = 1,
    Aggressive = 2,
}

public struct AIModeComponent : IComponent
{
    public AIMode Mode;
}
