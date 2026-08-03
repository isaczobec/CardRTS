using System.Collections.Generic;
using UnityEngine;

// Shows preview indicators for whichever troop(s) currently resolve as the caster(s) of a
// held ability-bar hotkey — a range circle around each such troop, a circle at each caster's
// own cast point / a direction arrow from each caster toward it (skillshot-style abilities),
// and/or a marker on whichever entity would be targeted — driven by AbilityInputManager's
// currently-held ability (HeldAbilityId) and AbilityCasterTargeting's own resolution of who
// would actually cast it right now (mirrors exactly what AbilityInputManager itself will do
// on key release), plus the equipped Ability's own Show*/ClampCastLocationToRange fields. All
// indicators are optional per ability and can be combined freely; each is only shown while
// its own flag is set on the held ability.
//
// Since MORE THAN ONE troop can resolve as a caster at once (see Ability.
// MaxSimultaneousCasters/PrioritizeSelectedTroops), the range circle/cursor circle/direction
// arrow are each a small per-caster POOL (keyed by caster entity id) rather than a single lazy
// instance — every resolved caster gets its own positioned indicator, and each caster clamps
// its own cast point to its own Range independently (see AbilityTargeting.ResolveCastPoint),
// so two casters at different distances can legitimately show different cursor
// circles/arrows. The entity-target marker stays a SINGLE instance — the resolved
// target-under-cursor is the same regardless of which caster is asking (only the per-caster
// range check differs), so there's only ever one entity being pointed at, shown as long as at
// least one resolved caster is actually in range of it.
//
// Separate from the ECS architecture, like CardRangeIndicatorManager — never registered as
// an ISystem, just polls AbilityInputManager/the live cursor each frame.
public class AbilityIndicatorManager : Singleton<AbilityIndicatorManager>
{
    [SerializeField] private RangeIndicatorPrefab _rangeCirclePrefab;
    [SerializeField] private RangeIndicatorPrefab _cursorCirclePrefab;
    [SerializeField] private AbilityDirectionIndicatorPrefab _directionArrowPrefab;
    [SerializeField] private EntityTargetIndicatorPrefab _targetIndicatorPrefab;

    [SerializeField] private Color _rangeCircleColor = new Color(0f, 1f, 0f, 0.25f);
    [SerializeField] private Color _cursorCircleColor = new Color(1f, 0f, 0f, 0.25f);

    private ECS _ecs;

    // Pooled per-caster instances, keyed by caster entity id — entries are never removed,
    // just deactivated once that caster stops being a currently-resolved caster (see
    // PruneStale), and reused again if/when it becomes one again.
    private readonly Dictionary<ulong, RangeIndicatorPrefab> _rangeCircleInstances = new Dictionary<ulong, RangeIndicatorPrefab>();
    private readonly Dictionary<ulong, RangeIndicatorPrefab> _cursorCircleInstances = new Dictionary<ulong, RangeIndicatorPrefab>();
    private readonly Dictionary<ulong, AbilityDirectionIndicatorPrefab> _directionArrowInstances = new Dictionary<ulong, AbilityDirectionIndicatorPrefab>();
    private EntityTargetIndicator _targetIndicator;

    private readonly List<ulong> _casterBuffer = new List<ulong>();
    private readonly HashSet<ulong> _activeCasterSet = new HashSet<ulong>();
    private readonly List<ulong> _targetQueryBuffer = new List<ulong>();

    public void Initialize()
    {
        _ecs = TickManager.instance.ActiveECS;
        _targetIndicator = new EntityTargetIndicator(_targetIndicatorPrefab, transform);
    }

    void Update()
    {
        if (TickManager.instance == null || !TickManager.instance.IsGameStarted) { HideAll(); return; }

        int abilityId = AbilityInputManager.instance != null ? AbilityInputManager.instance.HeldAbilityId : 0;
        if (abilityId == 0 || !AbilityManager.TryGet(abilityId, out Ability ability)) { HideAll(); return; }

        if (!TileSpaceMouse.TryGetPosition(out float cursorX, out float cursorY)) { HideAll(); return; }

        ushort localPlayerId = NetworkManager.instance != null ? NetworkManager.instance.LocalPlayerId : (ushort)0;
        IReadOnlyCollection<ulong> selected = SelectionManager.instance != null ? SelectionManager.instance.SelectedEntityIds : null;
        AbilityCasterTargeting.FindCasters(_ecs, abilityId, ability, cursorX, cursorY, localPlayerId, selected, _casterBuffer);

        ComponentStore<PositionComponent> posStore = _ecs.GetComponentStore<PositionComponent>();
        if (posStore == null || _casterBuffer.Count == 0) { HideAll(); return; }

        _activeCasterSet.Clear();
        foreach (ulong id in _casterBuffer)
            _activeCasterSet.Add(id);

        foreach (ulong casterId in _casterBuffer)
        {
            if (!posStore.HasComponent(casterId)) continue;
            PositionComponent casterPos = posStore.GetComponent(casterId);

            UpdateRangeCircle(ability, casterId, casterPos);
            UpdateCursorIndicators(ability, casterId, casterPos, cursorX, cursorY);
        }

        PruneStale(_rangeCircleInstances, _activeCasterSet);
        PruneStale(_cursorCircleInstances, _activeCasterSet);
        PruneStale(_directionArrowInstances, _activeCasterSet);

        UpdateTargetIndicator(ability, localPlayerId, posStore, cursorX, cursorY);
    }

    private void UpdateRangeCircle(Ability ability, ulong casterId, PositionComponent casterPos)
    {
        if (!ability.ShowRangeCircle || ability.Range <= 0f)
        {
            HideIfExists(_rangeCircleInstances, casterId);
            return;
        }

        RangeIndicatorPrefab instance = GetOrCreate(_rangeCircleInstances, casterId, _rangeCirclePrefab);
        if (instance == null) return;

        instance.SetColor(_rangeCircleColor);
        instance.transform.position = WorldPositionForXY(casterPos.X, casterPos.Y);
        instance.SetScale(ability.Range * 2f); // radius -> diameter
        instance.gameObject.SetActive(true);
    }

    private void UpdateCursorIndicators(Ability ability, ulong casterId, PositionComponent casterPos, float rawX, float rawY)
    {
        bool needsCursor = ability.ShowCursorCircle || ability.ShowDirectionArrow;
        if (!needsCursor)
        {
            HideIfExists(_cursorCircleInstances, casterId);
            HideIfExists(_directionArrowInstances, casterId);
            return;
        }

        AbilityTargeting.ResolveCastPoint(_ecs, casterId, ability, rawX, rawY, out float x, out float y);

        UpdateCursorCircle(ability, casterId, x, y);
        UpdateDirectionArrow(ability, casterId, casterPos, x, y);
    }

    private void UpdateCursorCircle(Ability ability, ulong casterId, float x, float y)
    {
        if (!ability.ShowCursorCircle)
        {
            HideIfExists(_cursorCircleInstances, casterId);
            return;
        }

        RangeIndicatorPrefab instance = GetOrCreate(_cursorCircleInstances, casterId, _cursorCirclePrefab);
        if (instance == null) return;

        instance.SetColor(_cursorCircleColor);
        instance.transform.position = WorldPositionForXY(x, y);
        instance.SetScale(ability.CursorCircleRadius * 2f); // radius -> diameter
        instance.gameObject.SetActive(true);
    }

    private void UpdateDirectionArrow(Ability ability, ulong casterId, PositionComponent casterPos, float x, float y)
    {
        if (!ability.ShowDirectionArrow)
        {
            HideIfExists(_directionArrowInstances, casterId);
            return;
        }

        AbilityDirectionIndicatorPrefab instance = GetOrCreate(_directionArrowInstances, casterId, _directionArrowPrefab);
        if (instance == null) return;

        Vector3 basePos = WorldPositionForXY(casterPos.X, casterPos.Y);
        Vector3 tipPos = WorldPositionForXY(x, y);
        Vector3 delta = tipPos - basePos;
        float length = ability.DirectionArrowAlwaysMaxRange ? ability.Range : delta.magnitude;

        instance.transform.position = basePos;
        if (delta.sqrMagnitude > 0.0001f)
            instance.transform.rotation = Quaternion.FromToRotation(Vector3.right, delta.normalized);
        instance.SetLength(length);
        instance.gameObject.SetActive(true);
    }

    private void UpdateTargetIndicator(Ability ability, ushort localPlayerId, ComponentStore<PositionComponent> posStore, float cursorX, float cursorY)
    {
        if (!ability.ShowTargetIndicator)
        {
            _targetIndicator.Hide();
            return;
        }

        ulong targetId = EntityTargeting.FindClosestSelectable(
            _ecs, cursorX, cursorY, ability.TargetSelectionRadius, localPlayerId,
            ability.CanTargetFriendly, ability.CanTargetEnemyOrNeutral, _targetQueryBuffer);
        if (targetId == 0 || !posStore.HasComponent(targetId))
        {
            _targetIndicator.Hide();
            return;
        }

        PositionComponent targetPos = posStore.GetComponent(targetId);

        // Only worth showing if at least one currently-resolved caster is actually within
        // range of it — same per-caster range precheck AbilityInputManager itself applies
        // before sending a cast, just checked across every resolved caster instead of one.
        bool anyCasterInRange = false;
        foreach (ulong casterId in _casterBuffer)
        {
            if (!posStore.HasComponent(casterId)) continue;
            PositionComponent casterPos = posStore.GetComponent(casterId);
            float dx = targetPos.X - casterPos.X, dy = targetPos.Y - casterPos.Y;
            if (dx * dx + dy * dy <= ability.Range * ability.Range)
            {
                anyCasterInRange = true;
                break;
            }
        }

        if (!anyCasterInRange)
        {
            _targetIndicator.Hide();
            return;
        }

        _targetIndicator.Show(WorldPositionForXY(targetPos.X, targetPos.Y));
    }

    private void HideAll()
    {
        foreach (KeyValuePair<ulong, RangeIndicatorPrefab> kvp in _rangeCircleInstances) SetActive(kvp.Value, false);
        foreach (KeyValuePair<ulong, RangeIndicatorPrefab> kvp in _cursorCircleInstances) SetActive(kvp.Value, false);
        foreach (KeyValuePair<ulong, AbilityDirectionIndicatorPrefab> kvp in _directionArrowInstances) SetActive(kvp.Value, false);
        _targetIndicator?.Hide();
    }

    private T GetOrCreate<T>(Dictionary<ulong, T> pool, ulong casterId, T prefab) where T : Component
    {
        if (prefab == null) return null;
        if (pool.TryGetValue(casterId, out T existing) && existing != null) return existing;

        T instance = Instantiate(prefab, transform);
        pool[casterId] = instance;
        return instance;
    }

    private static void HideIfExists<T>(Dictionary<ulong, T> pool, ulong casterId) where T : Component
    {
        if (pool.TryGetValue(casterId, out T instance))
            SetActive(instance, false);
    }

    private static void PruneStale<T>(Dictionary<ulong, T> pool, HashSet<ulong> active) where T : Component
    {
        foreach (KeyValuePair<ulong, T> kvp in pool)
            if (!active.Contains(kvp.Key))
                SetActive(kvp.Value, false);
    }

    private static void SetActive(Component c, bool active)
    {
        if (c != null)
            c.gameObject.SetActive(active);
    }

    private static Vector3 WorldPositionForXY(float x, float y)
    {
        float height = WorldManager.instance.Handler.GetHeight((ushort)x, (ushort)y);
        return new Vector3(x, height + 0.01f, y);
    }
}
