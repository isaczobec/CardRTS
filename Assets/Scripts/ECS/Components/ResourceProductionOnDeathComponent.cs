// Attach to a neutral resource-node entity (Tree/Rock/Ore) to permanently boost the killing
// player's passive resource production (PlayerResourcesComponent.*PerSecond) every time this
// entity dies — see ResourceProductionOnDeathSystem. MainResourceType is the resource this
// entity "represents" (e.g. Wood for a Tree) and gets the larger boost; the other two of
// Wood/Stone/Metal each get a smaller one. Only Wood/Stone/Metal are ever affected — Gems/
// Soulstones/Gold are untouched regardless of MainResourceType.
public struct ResourceProductionOnDeathComponent : IComponent
{
    public ResourceType MainResourceType;
}
