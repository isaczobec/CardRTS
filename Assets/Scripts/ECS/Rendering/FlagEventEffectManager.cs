using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

// Data-driven "spawn a prefab and/or play sounds wherever a FlagEvent happened" table — one
// Inspector-configured list instead of a dedicated renderer class per event type (contrast
// DamageImpactEffectManager, which does the same thing but only for DamageDealtEvent,
// keyed off the dealer's RenderableType instead of a flat type list). Subscribes to
// TickManager.ServerFlagEvents (server-confirmed only, same reasoning as
// DamageImpactEffectManager's own doc comment: a one-shot spawn isn't idempotent, so
// reacting to a predicted-but-later-cancelled request would leave orphaned instances behind)
// for whichever FlagEvent types are configured below, and reads each one's own Position (see
// FlagEvent.Position) to know where to spawn/play — entries for an event with no meaningful
// position (Position == null) are silently skipped when that event fires.
//
// Purely event-driven and, like DamageImpactEffectManager/FloatingTextManager/
// SelectionManager, sits outside the ECS system list entirely — never registered as an
// ISystem.
public class FlagEventEffectManager : Singleton<FlagEventEffectManager>
{
    [Serializable]
    public class FlagEventEffectEntry
    {
        [Tooltip("Simple class name of the FlagEvent subclass to react to (e.g. \"TroopDiedEvent\", \"DamageDealtEvent\").")]
        [SerializeField] public string flagEventTypeName;

        [Tooltip("Spawned at the event's Position. Left empty to only play sounds for this entry.")]
        [SerializeField] public GameObject prefab;

        [Tooltip("Seconds after spawning before the prefab instance is destroyed. 0 or negative means it's never destroyed automatically (e.g. a VFX that manages its own lifetime).")]
        [SerializeField] public float destroyAfterSeconds;

        [Tooltip("Sound names (AudioManager) played at the event's Position.")]
        [SerializeField] public List<string> soundNames = new List<string>();
    }

    [SerializeField] private List<FlagEventEffectEntry> _entries = new List<FlagEventEffectEntry>();

    private static readonly MethodInfo _subscribeEntryMethod =
        typeof(FlagEventEffectManager).GetMethod(nameof(SubscribeEntry), BindingFlags.NonPublic | BindingFlags.Instance);

    public void Initialize()
    {
        foreach (FlagEventEffectEntry entry in _entries)
        {
            Type flagEventType = FindFlagEventType(entry.flagEventTypeName);
            if (flagEventType == null)
            {
                DebugLogger.LogError($"FlagEventEffectManager: no FlagEvent type named \"{entry.flagEventTypeName}\" found — entry skipped.");
                continue;
            }

            _subscribeEntryMethod.MakeGenericMethod(flagEventType).Invoke(this, new object[] { entry });
        }
    }

    // Bound reflectively once per configured entry, against its own resolved FlagEvent type
    // (see Initialize) — FlagEventManager.Subscribe<T> needs a compile-time T, but this
    // entry's type is only known at runtime from the Inspector-typed name, so a generic
    // method invoked via MakeGenericMethod is what bridges the two.
    private void SubscribeEntry<T>(FlagEventEffectEntry entry) where T : FlagEvent
    {
        TickManager.instance.ServerFlagEvents.Subscribe<T>(evt => HandleEvent(entry, evt));
    }

    private void HandleEvent(FlagEventEffectEntry entry, FlagEvent evt)
    {
        (float X, float Y)? position = evt.Position;
        if (position == null) return;

        Vector3 worldPos = WorldPositionFor(position.Value.X, position.Value.Y);

        if (entry.prefab != null)
        {
            GameObject instance = Instantiate(entry.prefab, worldPos, Quaternion.identity);
            if (entry.destroyAfterSeconds > 0f)
                Destroy(instance, entry.destroyAfterSeconds);
        }

        if (AudioManager.instance != null && entry.soundNames != null)
            foreach (string soundName in entry.soundNames)
                if (!string.IsNullOrEmpty(soundName))
                    AudioManager.instance.PlayOneShotAtPosition(soundName, worldPos);
    }

    private static Vector3 WorldPositionFor(float x, float y)
    {
        float height = 0f;
        if (WorldManager.instance?.Handler != null)
        {
            ushort maxTile = (ushort)(WorldGenHandler.CHUNK_SIZE_TILES * WorldGenHandler.WorldSizeChunks - 1);
            ushort tx = (ushort)Mathf.Clamp(x, 0, maxTile);
            ushort ty = (ushort)Mathf.Clamp(y, 0, maxTile);
            height = WorldManager.instance.Handler.GetHeight(tx, ty);
        }

        return new Vector3(x, height, y);
    }

    // All FlagEvent subclasses live in the same assembly as FlagEvent itself (there's no
    // assembly-definition split in this project), so scanning just that one assembly avoids
    // both the cost and the ReflectionTypeLoadException risk of scanning every loaded
    // assembly. Excludes abstract types and open generic ones (e.g. ComponentAddedEvent<T>)
    // — there's no single well-defined "T" to subscribe to for those from a flat type name.
    private static Type FindFlagEventType(string typeName)
    {
        if (string.IsNullOrEmpty(typeName)) return null;

        foreach (Type type in typeof(FlagEvent).Assembly.GetTypes())
        {
            if (type.Name != typeName) continue;
            if (!typeof(FlagEvent).IsAssignableFrom(type)) continue;
            if (type.IsAbstract || type.ContainsGenericParameters) continue;
            return type;
        }

        return null;
    }
}
