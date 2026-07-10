using UnityEngine;

// Added to a player's own entity (alongside PlayerComponent/PlayerDeckComponent). Tracks
// that player's current stock of each resource type as a float so fractional passive
// generation (see the *PerSecond fields, applied by ResourceGenerationSystem) accumulates
// smoothly instead of being lost to integer truncation each tick. Anything that needs to
// check whether the player can afford a cost should read the *Floor properties, not the
// raw fields.
public struct PlayerResourcesComponent : IComponent
{
    public float Wood;
    public float Stone;
    public float Metal;
    public float Gems;
    public float Soulstones;
    public float Gold;

    public int WoodFloor => Mathf.FloorToInt(Wood);
    public int StoneFloor => Mathf.FloorToInt(Stone);
    public int MetalFloor => Mathf.FloorToInt(Metal);
    public int GemsFloor => Mathf.FloorToInt(Gems);
    public int SoulstonesFloor => Mathf.FloorToInt(Soulstones);
    public int GoldFloor => Mathf.FloorToInt(Gold);

    // Passive generation rate per resource, in units/second — applied every tick by
    // ResourceGenerationSystem via a ResourcesAdded request. 0 = no passive generation.
    public float WoodPerSecond;
    public float StonePerSecond;
    public float MetalPerSecond;
    public float GemsPerSecond;
    public float SoulstonesPerSecond;
    public float GoldPerSecond;
}
