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
    SantaSnatcherSpawnResolve = 6,
    MissileImpactResolve = 7,
    HealAuraApplyResolve = 8,
    BallisticMissileLaunchResolve = 9,
    BallisticMissileImpactResolve = 10,
    MassiveSleepingDraughtResolve = 11,
    KineticPullResolve = 12,
}
