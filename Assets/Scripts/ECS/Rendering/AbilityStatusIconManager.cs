using System.Collections.Generic;
using UnityEngine;

// Renders one small icon per equipped ability slot (see AbilityComponent — up to 4, Q/W/E/R)
// for each of the LOCAL PLAYER'S OWN troops that have any, in a row offset to the right of it
// — the world-space, always-on-every-troop counterpart to the old (screen-space,
// single-selected-troop) AbilityBarUI, restoring that per-troop cooldown/charge visibility
// now that AbilityBarUI itself shows an aggregated player-wide bar instead (see that class's
// own doc comment). Deliberately local-player-only: unlike ModifierIconManager's buff/debuff
// icons (relevant to see on any troop, friend or enemy), a troop's own ability cast
// cooldown/charges are information you'd only want visibility into for your own army — showing
// it for enemy troops would hand over free tactical information no other RTS convention grants.
//
// Structurally mirrors ModifierIconManager closely (per-target row container + polling every
// frame rather than tracking via FlagEvents, same TickPositionInterpolator position-follow),
// with one difference: a troop's 4 ability slots are fixed by AbilityComponent itself (not an
// open-ended set of independent modifier entities), so this keeps one icon instance per SLOT
// (a plain length-4 array, null where a slot is empty) per troop instead of
// ModifierIconManager's nested per-modifier-entity dictionary.
public class AbilityStatusIconManager : Singleton<AbilityStatusIconManager>
{
    [SerializeField] private AbilityStatusContainerPrefab _containerPrefab;
    [SerializeField] private AbilityStatusIconPrefab _iconPrefab;
    // Distance (world units) the row sits from the troop, along the CAMERA's own current
    // horizontal right direction (see PositionContainers) rather than a fixed world axis —
    // unlike ModifierIconManager's own straight-up (+Y, camera-independent) _worldOffset, a
    // "to the right" offset has to be recomputed from wherever the camera currently is or it
    // would only actually look right from one particular camera angle (this game's own
    // CameraController supports free yaw rotation — see its own WorldRight()/Yaw).
    [SerializeField] private float _rightOffsetDistance = 2f;

    // Fallback for StatsQuery.GetSpeed below, mirrors ModifierIconManager/SelectionManager/
    // HealthBarManager's own DefaultSpeed.
    private const int DefaultSpeed = 100;

    private ECS _ecs;
    private ComponentStore<AbilityComponent> _abilityStore;
    private ComponentStore<TroopComponent> _troopStore;
    private ComponentStore<PositionComponent> _positionStore;
    private ComponentStore<MovableComponent> _movableStore;
    private readonly TickPositionInterpolator _interpolator = new();

    // Per troop entity: its row container, and one icon instance per ability slot (index
    // 0-3, matching AbilityComponent's own Q/W/E/R slot order) — null where that slot is
    // currently empty.
    private readonly Dictionary<ulong, GameObject> _containers = new();
    private readonly Dictionary<ulong, AbilityStatusIconPrefab[]> _iconsByTroop = new();

    // Scratch, rebuilt every frame: which troops currently qualify for a row at all (owned by
    // the local player, has a position, has at least one equipped ability) — anything in
    // _containers that ISN'T in this set this frame gets torn down, same "no separate stale
    // pass" reasoning ModifierIconManager's own SyncTargetIcons doc comment gives.
    private readonly HashSet<ulong> _aliveTroopsScratch = new HashSet<ulong>();
    private readonly List<ulong> _staleTroopsScratch = new List<ulong>();

    public void Initialize()
    {
        _ecs = TickManager.instance.ActiveECS;
        _abilityStore = _ecs.GetComponentStore<AbilityComponent>();
        _troopStore = _ecs.GetComponentStore<TroopComponent>();
        _positionStore = _ecs.GetComponentStore<PositionComponent>();
        _movableStore = _ecs.GetComponentStore<MovableComponent>();
    }

    void Update()
    {
        if (TickManager.instance == null || !TickManager.instance.IsGameStarted) return;
        if (_abilityStore == null || _troopStore == null || _positionStore == null) return;
        if (_containerPrefab == null || _iconPrefab == null) return;

        ushort localPlayerId = LocalPlayerId();
        _aliveTroopsScratch.Clear();

        _abilityStore.ForEach((ulong troopId) =>
        {
            if (!_troopStore.HasComponent(troopId) || _troopStore.GetComponent(troopId).OwnerPlayerId != localPlayerId) return;
            if (!_positionStore.HasComponent(troopId)) return;

            AbilityComponent abilities = _abilityStore.GetComponent(troopId);
            if (!HasAnyAbility(abilities)) return;

            _aliveTroopsScratch.Add(troopId);
            SyncTroopIcons(troopId, abilities);
        });

        PruneStaleContainers();
        PositionContainers();
    }

    private static bool HasAnyAbility(AbilityComponent abilities)
        => abilities.GetAbilityId(0) != 0 || abilities.GetAbilityId(1) != 0
        || abilities.GetAbilityId(2) != 0 || abilities.GetAbilityId(3) != 0;

    private void SyncTroopIcons(ulong troopId, AbilityComponent abilities)
    {
        if (!_containers.TryGetValue(troopId, out GameObject container))
        {
            AbilityStatusContainerPrefab instance = Instantiate(_containerPrefab, transform);
            instance.name = $"AbilityStatus_{troopId}";
            container = instance.gameObject;
            _containers[troopId] = container;
            _iconsByTroop[troopId] = new AbilityStatusIconPrefab[4];
        }

        AbilityStatusIconPrefab[] icons = _iconsByTroop[troopId];

        for (int slot = 0; slot < icons.Length; slot++)
        {
            int abilityId = abilities.GetAbilityId(slot);
            if (abilityId == 0 || !AbilityManager.TryGet(abilityId, out Ability ability))
            {
                if (icons[slot] != null)
                {
                    Destroy(icons[slot].gameObject);
                    icons[slot] = null;
                }
                continue;
            }

            if (icons[slot] == null)
            {
                icons[slot] = Instantiate(_iconPrefab, container.transform);
                icons[slot].name = $"Slot{slot}";
                icons[slot].SetIcon(ResolveSprite(ability.ImageName));
            }

            ApplySlot(icons[slot], abilities, slot);
        }
    }

    // Same MaxCharges-aware "which timer actually gates the next cast" math AbilityBarUI
    // itself applies per-troop.
    private static void ApplySlot(AbilityStatusIconPrefab icon, AbilityComponent abilities, int slot)
    {
        int maxCharges = abilities.GetMaxCharges(slot);
        int ticksRemaining;
        int cooldownTicks;
        if (maxCharges > 1 && abilities.GetChargesRemaining(slot) <= 0)
        {
            ticksRemaining = abilities.GetChargeCooldownTicksRemaining(slot);
            cooldownTicks = abilities.GetChargeCooldownTicks(slot);
        }
        else
        {
            ticksRemaining = abilities.GetCooldownTicksRemaining(slot);
            cooldownTicks = abilities.GetCooldownTicks(slot);
        }

        bool onCooldown = ticksRemaining > 0;
        string cooldownText = onCooldown ? Mathf.CeilToInt(TickManager.TicksToSeconds(ticksRemaining)).ToString() : null;
        float fillAmount = onCooldown && cooldownTicks > 0 ? (float)ticksRemaining / cooldownTicks : 0f;
        icon.SetCooldown(cooldownText, fillAmount);

        bool hasCharges = maxCharges > 1;
        icon.SetCharges(hasCharges ? abilities.GetChargesRemaining(slot).ToString() : null);
    }

    private void PruneStaleContainers()
    {
        if (_containers.Count == 0) return;

        _staleTroopsScratch.Clear();
        foreach (ulong troopId in _containers.Keys)
            if (!_aliveTroopsScratch.Contains(troopId))
                _staleTroopsScratch.Add(troopId);

        foreach (ulong troopId in _staleTroopsScratch)
            DestroyContainer(troopId);
    }

    private void DestroyContainer(ulong troopId)
    {
        if (_containers.TryGetValue(troopId, out GameObject container))
        {
            if (container != null) Destroy(container);
            _containers.Remove(troopId);
        }
        _iconsByTroop.Remove(troopId);
        _interpolator.Remove(troopId);
    }

    // Identical chase-at-Speed-stat follow logic to ModifierIconManager.PositionContainers —
    // see that method's own doc comment — plus a camera-relative "right" offset instead of a
    // fixed world one (see _rightOffsetDistance).
    private void PositionContainers()
    {
        if (_containers.Count == 0) return;

        // Computed once per call (identical for every troop this frame), not cached across
        // frames — must track the camera's CURRENT horizontal facing so the row always reads
        // as "to the right of the troop" on screen, no matter how the camera's been rotated.
        // Flattened to the ground plane (Y zeroed) so camera pitch never lifts/drops the row,
        // only yaw affects which way it points — camera.transform.right is already ~horizontal
        // for this game's own no-roll camera rig (see CameraController's Quaternion.Euler(
        // pitch, yaw, 0)), so this is mostly a defensive renormalize against float drift.
        Camera cam = Camera.main;
        Vector3 cameraRight = cam != null ? cam.transform.right : Vector3.right;
        cameraRight.y = 0f;
        if (cameraRight.sqrMagnitude < 0.0001f) cameraRight = Vector3.right; // camera looking straight down — fall back rather than a zero offset
        Vector3 offset = cameraRight.normalized * _rightOffsetDistance;

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
            bool displaced = _movableStore != null && _movableStore.HasComponent(entityId)
                && _movableStore.GetComponent(entityId).IsDisplaced;

            Vector3 interpolated;
            if (isMoving && !displaced)
            {
                float speedWorldUnitsPerSecond = StatsQuery.GetSpeed(_ecs, entityId, DefaultSpeed) / StatsQuery.SpeedScale;
                interpolated = _interpolator.Update(entityId, worldPos, isMoving, teleported, speedWorldUnitsPerSecond);
            }
            else
            {
                interpolated = _interpolator.Update(entityId, worldPos, isMoving, teleported);
            }

            kvp.Value.transform.position = interpolated + offset;
        }
    }

    private static ushort LocalPlayerId()
        => NetworkManager.instance != null ? NetworkManager.instance.LocalPlayerId : (ushort)0;

    private static Sprite ResolveSprite(string imageName)
    {
        if (ImageRegistry.instance == null) return null;
        return ImageRegistry.instance.TryGet(imageName, out Sprite sprite) ? sprite : null;
    }
}
