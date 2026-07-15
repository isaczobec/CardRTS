using UnityEngine;

// Shows a held ability's preview indicators — a range circle around the caster, a circle at
// the cast point, and/or a direction arrow from the caster toward the cast point — driven
// entirely by AbilityInputManager's currently-held slot (HeldCasterId/HeldSlot, set while a
// Q/W/E/R key is held down but not yet released — see AbilityInputManager) and the equipped
// Ability's own Show*/ClampCastLocationToRange flags. All three indicators are optional per
// ability and can be combined freely; each is only shown while its own flag is set on the
// currently-held ability. The cast point used for the circle/arrow is resolved through
// AbilityTargeting.ResolveCastPoint, the exact same clamping AbilityInputManager applies to
// what it actually sends, so the preview never lies about where the cast will land.
//
// Separate from the ECS architecture, like CardRangeIndicatorManager — never registered as
// an ISystem, just polls AbilityInputManager/the live cursor each frame.
public class AbilityIndicatorManager : Singleton<AbilityIndicatorManager>
{
    [SerializeField] private RangeIndicatorPrefab _rangeCirclePrefab;
    [SerializeField] private RangeIndicatorPrefab _cursorCirclePrefab;
    [SerializeField] private AbilityDirectionIndicatorPrefab _directionArrowPrefab;

    [SerializeField] private Color _rangeCircleColor = new Color(0f, 1f, 0f, 0.25f);
    [SerializeField] private Color _cursorCircleColor = new Color(1f, 0f, 0f, 0.25f);

    private ECS _ecs;
    private RangeIndicatorPrefab _rangeCircleInstance;
    private RangeIndicatorPrefab _cursorCircleInstance;
    private AbilityDirectionIndicatorPrefab _directionArrowInstance;

    public void Initialize()
    {
        _ecs = TickManager.instance.ActiveECS;
    }

    void Update()
    {
        if (TickManager.instance == null || !TickManager.instance.IsGameStarted) { HideAll(); return; }

        ulong casterId = AbilityInputManager.instance != null ? AbilityInputManager.instance.HeldCasterId : 0;
        int slot = AbilityInputManager.instance != null ? AbilityInputManager.instance.HeldSlot : -1;
        if (casterId == 0 || slot < 0) { HideAll(); return; }

        ComponentStore<AbilityComponent> abilityStore = _ecs.GetComponentStore<AbilityComponent>();
        if (abilityStore == null || !abilityStore.HasComponent(casterId)) { HideAll(); return; }

        AbilityComponent abilities = abilityStore.GetComponent(casterId);
        int abilityId = abilities.GetAbilityId(slot);
        if (abilityId == 0 || !AbilityManager.TryGet(abilityId, out Ability ability)) { HideAll(); return; }

        // A cast that AbilitySystem is just going to reject anyway (still on cooldown)
        // shouldn't get a preview implying it's ready — matches AbilityBarUI's own
        // cooldown-overlay gating off the same GetCooldownTicksRemaining.
        if (abilities.GetCooldownTicksRemaining(slot) > 0) { HideAll(); return; }

        ComponentStore<PositionComponent> posStore = _ecs.GetComponentStore<PositionComponent>();
        if (posStore == null || !posStore.HasComponent(casterId)) { HideAll(); return; }
        PositionComponent casterPos = posStore.GetComponent(casterId);

        UpdateRangeCircle(ability, casterPos);
        UpdateCursorIndicators(ability, casterId, casterPos);
    }

    private void UpdateRangeCircle(Ability ability, PositionComponent casterPos)
    {
        if (!ability.ShowRangeCircle || ability.Range <= 0f)
        {
            SetActive(_rangeCircleInstance, false);
            return;
        }

        if (_rangeCircleInstance == null)
        {
            if (_rangeCirclePrefab == null) return;
            _rangeCircleInstance = Instantiate(_rangeCirclePrefab, transform);
            _rangeCircleInstance.SetColor(_rangeCircleColor);
        }

        _rangeCircleInstance.transform.position = WorldPositionForXY(casterPos.X, casterPos.Y);
        _rangeCircleInstance.SetScale(ability.Range * 2f); // radius -> diameter
        _rangeCircleInstance.gameObject.SetActive(true);
    }

    private void UpdateCursorIndicators(Ability ability, ulong casterId, PositionComponent casterPos)
    {
        bool needsCursor = ability.ShowCursorCircle || ability.ShowDirectionArrow;
        if (!needsCursor || !TileSpaceMouse.TryGetPosition(out float rawX, out float rawY))
        {
            SetActive(_cursorCircleInstance, false);
            SetActive(_directionArrowInstance, false);
            return;
        }

        AbilityTargeting.ResolveCastPoint(_ecs, casterId, ability, rawX, rawY, out float x, out float y);

        UpdateCursorCircle(ability, x, y);
        UpdateDirectionArrow(ability, casterPos, x, y);
    }

    private void UpdateCursorCircle(Ability ability, float x, float y)
    {
        if (!ability.ShowCursorCircle)
        {
            SetActive(_cursorCircleInstance, false);
            return;
        }

        if (_cursorCircleInstance == null)
        {
            if (_cursorCirclePrefab == null) return;
            _cursorCircleInstance = Instantiate(_cursorCirclePrefab, transform);
            _cursorCircleInstance.SetColor(_cursorCircleColor);
        }

        _cursorCircleInstance.transform.position = WorldPositionForXY(x, y);
        _cursorCircleInstance.SetScale(ability.CursorCircleRadius * 2f); // radius -> diameter
        _cursorCircleInstance.gameObject.SetActive(true);
    }

    private void UpdateDirectionArrow(Ability ability, PositionComponent casterPos, float x, float y)
    {
        if (!ability.ShowDirectionArrow)
        {
            SetActive(_directionArrowInstance, false);
            return;
        }

        if (_directionArrowInstance == null)
        {
            if (_directionArrowPrefab == null) return;
            _directionArrowInstance = Instantiate(_directionArrowPrefab, transform);
        }

        Vector3 basePos = WorldPositionForXY(casterPos.X, casterPos.Y);
        Vector3 tipPos = WorldPositionForXY(x, y);
        Vector3 delta = tipPos - basePos;
        float length = ability.DirectionArrowAlwaysMaxRange ? ability.Range : delta.magnitude;

        _directionArrowInstance.transform.position = basePos;
        if (delta.sqrMagnitude > 0.0001f)
            _directionArrowInstance.transform.rotation = Quaternion.FromToRotation(Vector3.right, delta.normalized);
        _directionArrowInstance.SetLength(length);
        _directionArrowInstance.gameObject.SetActive(true);
    }

    private void HideAll()
    {
        SetActive(_rangeCircleInstance, false);
        SetActive(_cursorCircleInstance, false);
        SetActive(_directionArrowInstance, false);
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
