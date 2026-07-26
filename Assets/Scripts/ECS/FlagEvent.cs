public abstract class FlagEvent
{
    public abstract byte[] Serialize();
    public abstract void Deserialize(byte[] data);
    public virtual bool ShouldNetwork() { return true; }

    // World position this event happened at, or null if it doesn't have one — the default,
    // since most flag events (UI/administrative ones like CardDrawnEvent, or ones too generic
    // to have a single well-defined location like ComponentAddedEvent<T>) have no meaningful
    // world location. A subclass that does override this must return data captured onto its
    // own serialized fields at the moment it was raised (see PositionQuery.TryGet, used at
    // each relevant raise site) rather than resolving it lazily against whatever the live ECS
    // says now — this event may be read well after the tick it was raised on (network delay
    // to a client, or the entity it's about being deleted later the very same tick, e.g.
    // TroopDiedEvent right before DeathRequest deletes the entity), by which point a fresh ECS
    // lookup would often come back empty. Effect-spawners (see FlagEventEffectManager) read
    // this to know where to play VFX/SFX for an event without needing to special-case every
    // event type themselves.
    public virtual (float X, float Y)? Position => null;
}
