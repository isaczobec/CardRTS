// Query-style request: use RequestManager.Process to run all subscribers synchronously
// and read back IsSelectable. Subscribers set IsSelectable = false to veto; they should
// not call Cancel() so every subscriber gets a chance to weigh in.
public class IsSelectableRequest : Request
{
    public readonly ulong EntityId;

    // Which player is asking, if known — lets a viewer-relative veto (e.g.
    // ShadowCloakSystem) distinguish "an enemy is trying to select/target this" from "the
    // owner is trying to select/command/self-target their own troop" instead of vetoing
    // both alike. Null (the default, used by every call site that predates this field)
    // means the caller has no particular viewer in mind — a purely state-based veto like
    // RespawnSystem's own (dead/respawning is unselectable to everyone, no exceptions)
    // should ignore this field entirely and veto regardless.
    public readonly ushort? RequestingPlayerId;

    public bool IsSelectable = true;

    public IsSelectableRequest(ulong entityId, ushort? requestingPlayerId = null)
    {
        EntityId = entityId;
        RequestingPlayerId = requestingPlayerId;
    }

    public override void Execute(ECS ecs) { }
}
