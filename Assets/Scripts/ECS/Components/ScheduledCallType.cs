// Identifies which registered function a ScheduledCallComponent should invoke once its
// tick arrives — see ScheduledCallSystem.RegisterCall. None (0) never fires (a
// ScheduledCallComponent left at its default value is inert).
public enum ScheduledCallType : byte
{
    None = 0,
    IceNovaResolve = 1,
    GroundSlamResolve = 2,
    SkeletonSummonResolve = 3,
    StalkerAmbushResolve = 4,
    AoeRootResolve = 5,
}
