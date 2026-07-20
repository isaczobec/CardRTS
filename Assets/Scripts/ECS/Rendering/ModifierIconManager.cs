using System;
using System.Collections.Generic;
using UnityEngine;

// Renders one small icon (with remaining-duration overlay, if not infinite) per active
// modifier targeting an entity, in a row above it — mirrors HealthBarManager's per-frame
// position-follow, but for however many modifiers (0 to N) currently target each entity
// instead of a single fixed bar. A modifier only gets an icon at all if its
// ModifierComponent.ModifierID isn't ModifierID.None (the default) — see ModifierID's own
// doc comment and _resolvers below for how a modifier's icon/name/description get resolved
// from that id.
//
// Polls the ModifierComponent store directly every frame (grouping by TargetEntityId)
// rather than tracking additions/removals via FlagEvents — a target can have any number of
// modifiers coming and going independently and simultaneously, and polling is simpler and
// more robust than keeping a live per-target diff in sync with individual add/remove events
// (mirrors EntityTimerTextRenderer's own reasoning for polling instead of eventing).
public class ModifierIconManager : Singleton<ModifierIconManager>
{
    [SerializeField] private ModifierIconContainerPrefab _containerPrefab;
    [SerializeField] private ModifierIconPrefab _iconPrefab;
    [SerializeField] private Vector3 _worldOffset = new Vector3(0f, 3f, 0f);

    // ModifierID -> (name, imageName, description) resolver, given the ECS and the
    // MODIFIER entity's own id (not the target) — e.g. ResolveStatChange reads
    // StatModifierComponent off it to know which stat(s) it changes.
    private static readonly Dictionary<ModifierID, Func<ECS, ulong, (string name, string imageName, string description)>> _resolvers
        = new Dictionary<ModifierID, Func<ECS, ulong, (string name, string imageName, string description)>>
    {
        { ModifierID.StatChange, ResolveStatChange },
    };

    private ECS _ecs;
    private ComponentStore<ModifierComponent> _modifierStore;
    private ComponentStore<PositionComponent> _positionStore;
    private ComponentStore<MovableComponent> _movableStore;
    private readonly TickPositionInterpolator _interpolator = new();

    // Per target entity: its row container, and which modifier entity each currently-shown
    // icon belongs to.
    private readonly Dictionary<ulong, GameObject> _containers = new();
    private readonly Dictionary<ulong, Dictionary<ulong, ModifierIconPrefab>> _iconsByTarget = new();

    // Scratch, rebuilt every frame: target entity -> active icon-worthy modifier entity ids.
    // Persists across frames (values Cleared, not the dictionary itself) so a target that's
    // ever had a modifier keeps a reusable list instead of reallocating one every time.
    private readonly Dictionary<ulong, List<ulong>> _activeModifiersByTarget = new();
    private readonly List<ulong> _staleModifiersScratch = new();

    public void Initialize()
    {
        _ecs = TickManager.instance.ActiveECS;
        _modifierStore = _ecs.GetComponentStore<ModifierComponent>();
        _positionStore = _ecs.GetComponentStore<PositionComponent>();
        _movableStore = _ecs.GetComponentStore<MovableComponent>();
    }

    void Update()
    {
        if (TickManager.instance == null || !TickManager.instance.IsGameStarted) return;
        if (_modifierStore == null || _positionStore == null) return;
        if (_containerPrefab == null || _iconPrefab == null) return;

        GroupActiveModifiersByTarget();

        foreach (KeyValuePair<ulong, List<ulong>> kvp in _activeModifiersByTarget)
            SyncTargetIcons(kvp.Key, kvp.Value);

        PositionContainers();
    }

    private void GroupActiveModifiersByTarget()
    {
        foreach (List<ulong> list in _activeModifiersByTarget.Values)
            list.Clear();

        _modifierStore.ForEach((ulong modifierId) =>
        {
            ModifierComponent mod = _modifierStore.GetComponent(modifierId);
            if (mod.ModifierID == ModifierID.None) return;
            if (!ModifierQuery.IsActive(_ecs, modifierId)) return;
            if (!_positionStore.HasComponent(mod.TargetEntityId)) return;

            if (!_activeModifiersByTarget.TryGetValue(mod.TargetEntityId, out List<ulong> list))
                _activeModifiersByTarget[mod.TargetEntityId] = list = new List<ulong>();
            list.Add(modifierId);
        });
    }

    // Every container's target is guaranteed to already be a key in _activeModifiersByTarget
    // (it's only ever created below, when that target has at least one active modifier), so
    // an empty list here is enough on its own to know a container should be torn down — no
    // separate "stale target" cleanup pass needed.
    private void SyncTargetIcons(ulong targetEntityId, List<ulong> activeModifierIds)
    {
        if (activeModifierIds.Count == 0)
        {
            DestroyContainer(targetEntityId);
            return;
        }

        if (!_containers.TryGetValue(targetEntityId, out GameObject container))
        {
            ModifierIconContainerPrefab instance = Instantiate(_containerPrefab, transform);
            instance.name = $"ModifierIcons_{targetEntityId}";
            container = instance.gameObject;
            _containers[targetEntityId] = container;
            _iconsByTarget[targetEntityId] = new Dictionary<ulong, ModifierIconPrefab>();
        }

        Dictionary<ulong, ModifierIconPrefab> icons = _iconsByTarget[targetEntityId];

        // Remove icons for modifiers that are no longer active on this target.
        _staleModifiersScratch.Clear();
        foreach (ulong existingModifierId in icons.Keys)
            if (!activeModifierIds.Contains(existingModifierId))
                _staleModifiersScratch.Add(existingModifierId);

        foreach (ulong staleModifierId in _staleModifiersScratch)
        {
            if (icons.TryGetValue(staleModifierId, out ModifierIconPrefab staleIcon) && staleIcon != null)
                Destroy(staleIcon.gameObject);
            icons.Remove(staleModifierId);
        }

        // Add/refresh icons for every currently active modifier.
        foreach (ulong modifierId in activeModifierIds)
        {
            if (!icons.TryGetValue(modifierId, out ModifierIconPrefab icon) || icon == null)
            {
                icon = Instantiate(_iconPrefab, container.transform);
                icon.name = $"Modifier_{modifierId}";
                icons[modifierId] = icon;
            }

            ApplyIcon(icon, modifierId);
        }
    }

    private void ApplyIcon(ModifierIconPrefab icon, ulong modifierId)
    {
        ModifierComponent mod = _modifierStore.GetComponent(modifierId);

        if (_resolvers.TryGetValue(mod.ModifierID, out var resolve))
        {
            (string name, string imageName, string description) = resolve(_ecs, modifierId);
            icon.SetIcon(ResolveSprite(imageName));
        }

        icon.SetDuration(mod.TicksRemaining == int.MaxValue
            ? null
            : Mathf.CeilToInt(TickManager.TicksToSeconds(mod.TicksRemaining)).ToString());
    }

    private void DestroyContainer(ulong targetEntityId)
    {
        if (_containers.TryGetValue(targetEntityId, out GameObject container))
        {
            if (container != null) Destroy(container);
            _containers.Remove(targetEntityId);
        }
        _iconsByTarget.Remove(targetEntityId);
        _interpolator.Remove(targetEntityId);
    }

    // Follows the same interpolated position troop renderers/HealthBarManager show, rather
    // than the raw per-tick PositionComponent, so icons don't visibly snap/lag behind a
    // moving troop's smoothed-out rendered position.
    private void PositionContainers()
    {
        foreach (KeyValuePair<ulong, GameObject> kvp in _containers)
        {
            ulong entityId = kvp.Key;
            if (!_positionStore.HasComponent(entityId)) continue;

            PositionComponent pos = _positionStore.GetComponent(entityId);
            float height = WorldManager.instance.Handler.GetHeight(pos.TileX, pos.TileY);
            Vector3 worldPos = new Vector3(pos.X, height, pos.Y);

            bool isMoving = _movableStore != null && _movableStore.HasComponent(entityId)
                && _movableStore.GetComponent(entityId).IsMoving;
            bool teleported = _movableStore != null && _movableStore.HasComponent(entityId)
                && _movableStore.GetComponent(entityId).TeleportedTick == _ecs.CurrentSimulationTick;

            kvp.Value.transform.position = _interpolator.Update(entityId, worldPos, isMoving, teleported) + _worldOffset;
        }
    }

    private static Sprite ResolveSprite(string imageName)
    {
        if (ImageRegistry.instance == null) return null;
        return ImageRegistry.instance.TryGet(imageName, out Sprite sprite) ? sprite : null;
    }

    // ── Resolvers ────────────────────────────────────────────────────────────

    // Every current modifier that carries a StatModifierComponent (SpeedBoostCard,
    // AbilityManager's RingOfProjectiles buff, DamageBoostUpgrade) uses this — a single
    // ratio/additive bonus gets a specific name/image/description based on which stat and
    // whether it's a buff (net positive) or debuff (net negative); more than one changed
    // stat falls back to a generic "Stat Change" name/image with every change listed in the
    // description.
    private static (string name, string imageName, string description) ResolveStatChange(ECS ecs, ulong modifierEntityId)
    {
        const string genericName = "Stat Change";
        const string genericImageName = "StatChange";

        ComponentStore<StatModifierComponent> statStore = ecs.GetComponentStore<StatModifierComponent>();
        if (statStore == null || !statStore.HasComponent(modifierEntityId))
            return (genericName, genericImageName, "This troop has stat changes.");

        StatModifierComponent mod = statStore.GetComponent(modifierEntityId);

        List<(string stat, float ratio, float additive)> changes = new List<(string, float, float)>();
        AddIfChanged(changes, "Max Health", mod.MaxHealthRatioBonus, mod.MaxHealthAdditiveBonus);
        AddIfChanged(changes, "Speed", mod.SpeedRatioBonus, mod.SpeedAdditiveBonus);
        AddIfChanged(changes, "Range", mod.RangeRatioBonus, mod.RangeAdditiveBonus);
        AddIfChanged(changes, "Armor", mod.ArmorRatioBonus, mod.ArmorAdditiveBonus);
        AddIfChanged(changes, "Damage", mod.DamageRatioBonus, mod.DamageAdditiveBonus);
        AddIfChanged(changes, "Attack Speed", mod.AttackSpeedRatioBonus, mod.AttackSpeedAdditiveBonus);

        if (changes.Count == 0)
            return (genericName, genericImageName, "This troop has stat changes.");

        if (changes.Count > 1)
        {
            List<string> parts = new List<string>();
            foreach ((string stat, float ratio, float additive) in changes)
                parts.Add($"{FormatBonus(ratio, additive)} {stat}");

            return (genericName, genericImageName,
                "This troop has the following stat changes: " + string.Join(", ", parts) + ".");
        }

        (string singleStat, float singleRatio, float singleAdditive) = changes[0];
        bool isBuff = (singleRatio + singleAdditive) >= 0f;
        string compactStat = singleStat.Replace(" ", "");

        string name = $"{singleStat} {(isBuff ? "Boost" : "Reduction")}";
        string imageName = $"{compactStat}{(isBuff ? "Boost" : "Reduction")}";
        string description = $"{FormatBonus(singleRatio, singleAdditive)} {singleStat}.";
        return (name, imageName, description);
    }

    private static void AddIfChanged(List<(string, float, float)> changes, string stat, float ratio, float additive)
    {
        if (ratio != 0f || additive != 0f)
            changes.Add((stat, ratio, additive));
    }

    private static string FormatBonus(float ratio, float additive)
    {
        List<string> parts = new List<string>();
        if (ratio != 0f) parts.Add($"{(ratio >= 0f ? "+" : "")}{ratio * 100f:0}%");
        if (additive != 0f) parts.Add($"{(additive >= 0f ? "+" : "")}{additive:0.#}");
        return string.Join(" ", parts);
    }
}
