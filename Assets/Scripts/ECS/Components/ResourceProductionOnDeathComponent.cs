// Attach to a neutral resource-node entity (Tree/Rock/Ore) to permanently boost the killing
// player's passive resource production (PlayerResourcesComponent.*PerSecond) every time this
// entity dies — see ResourceProductionOnDeathSystem. One flat field per resource type,
// authored in units per minute (converted to the *PerSecond rate the system actually mutates
// — see ResourceProductionOnDeathSystem); 0 (the default) means no boost to that resource at
// all, so a spawner only needs to set the fields it actually wants to grant.
public struct ResourceProductionOnDeathComponent : IComponent
{
    public float WoodPerMinute;
    public float StonePerMinute;
    public float MetalPerMinute;
    public float GemsPerMinute;
    public float SoulstonesPerMinute;
    public float GoldPerMinute;
}
