using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spawns a marker prefab wherever the LOCAL player issues an explicit move order (see
/// SelectionManager.MoveCommandIssued, the only source) — a simple "your click registered
/// here" ping, the same shape as e.g. StarCraft/WC3's own move-order marker. Unlike a
/// fire-and-forget VFX, this keeps the marker alive and polls every frame until the move
/// order it represents is actually finished: every entity it was issued to has either
/// arrived, been given a different order (a new move, an attack, ...), or stopped existing.
/// Owns nothing beyond placement/lifetime: the prefab itself is responsible for however it
/// looks/animates while it's alive.
/// </summary>
public class MoveMarkerManager : Singleton<MoveMarkerManager>
{
    [SerializeField] private GameObject _markerPrefab;

    // Safety fallback only — a marker normally disappears the moment its move order actually
    // resolves (see IsMoveStillInProgress), not on a timer. This just guarantees one can't
    // linger forever in a pathological case (e.g. every entity it was issued to standing on
    // an unreachable destination, which PathfindingSystem leaves playerDestinationSet true
    // for indefinitely rather than clearing it — see its own "path == null" branch).
    [SerializeField] private float _maxMarkerLifetimeSeconds = 30f;

    // How close (world/tile units) an entity's OWN currently-commanded destination needs to
    // be to this marker's point to still count as "still walking here" — not 0, since
    // MoveTroopInput can nudge an individual troop's destination slightly off the exact
    // click point for formation spacing (see SelectionManager.SendMoveCommand's own
    // SnapToWalkable), so an exact-equality check would immediately (and
    // incorrectly) read as "already done" for every troop but the very first.
    private const float DestinationMatchEpsilon = 2f;

    // MoveCommandIssued fires the instant the move is ENQUEUED (same rendered frame as the
    // click), but MovableComponent.playerDestinationSet only actually flips true once the
    // next simulation tick processes that input — up to a full TickInterval later, longer
    // still if a rendered frame lands before any tick has run at all. Without this grace
    // window, IsMoveStillInProgress would see every tracked entity still reading its OLD
    // (pre-order) MovableComponent state on those first frames and conclude the order was
    // already "done," destroying the marker instantly. A handful of ticks' worth of slack is
    // enough to guarantee at least one tick has actually applied the input before the real
    // in-progress check starts being trusted.
    private static readonly float GracePeriodSeconds = TickManager.TickInterval * 4f;

    // Deliberately above TerrainPatchRegistry's own +0.01 ground offset — mirrors
    // SelectionManager.WorldPositionFor's own reasoning (avoids a depth-test coin flip
    // against Transparent/ZWrite-off ground patches).
    private const float GroundOffset = 0.03f;

    private class ActiveMarker
    {
        public GameObject GameObject;
        public Vector2 Point;
        public List<ulong> EntityIds;
        public float SpawnTime;
    }

    private readonly List<ActiveMarker> _activeMarkers = new();

    public void Initialize()
    {
        if (SelectionManager.instance != null)
            SelectionManager.instance.MoveCommandIssued += OnMoveCommandIssued;
    }

    private void OnMoveCommandIssued(Vector2 point, List<ulong> entityIds)
    {
        if (_markerPrefab == null) return;

        GameObject marker = Instantiate(_markerPrefab, ToWorldPosition(point), Quaternion.identity, transform);
        _activeMarkers.Add(new ActiveMarker
        {
            GameObject = marker,
            Point      = point,
            EntityIds  = entityIds,
            SpawnTime  = Time.unscaledTime,
        });
    }

    private void Update()
    {
        if (_activeMarkers.Count == 0) return;
        if (TickManager.instance == null || !TickManager.instance.IsGameStarted) return;

        ECS ecs = TickManager.instance.ActiveECS;
        ComponentStore<MovableComponent> movStore = ecs?.GetComponentStore<MovableComponent>();
        if (ecs == null || movStore == null) return;

        for (int i = _activeMarkers.Count - 1; i >= 0; i--)
        {
            ActiveMarker marker = _activeMarkers[i];

            float age = Time.unscaledTime - marker.SpawnTime;
            bool expired = age >= _maxMarkerLifetimeSeconds;
            bool withinGracePeriod = age < GracePeriodSeconds;
            if (!expired && (withinGracePeriod || IsMoveStillInProgress(ecs, movStore, marker))) continue;

            if (marker.GameObject != null) Destroy(marker.GameObject);
            _activeMarkers.RemoveAt(i);
        }
    }

    // True while at least one of the entities this order was issued to is still actively
    // walking toward (roughly) this marker's own point — false once every one of them has
    // arrived (PathfindingSystem clears playerDestinationSet on arrival), been redirected
    // (a new move/attack order changes currentMovementMode and/or the commanded destination
    // away from this marker's point), or stopped existing (dead/deleted).
    private bool IsMoveStillInProgress(ECS ecs, ComponentStore<MovableComponent> movStore, ActiveMarker marker)
    {
        foreach (ulong entityId in marker.EntityIds)
        {
            if (!ecs.HasEntity(entityId)) continue;
            if (!movStore.HasComponent(entityId)) continue;

            MovableComponent mov = movStore.GetComponent(entityId);
            if (!mov.playerDestinationSet) continue;
            if (mov.currentMovementMode != MovementMode.MoveToPlayerSetDestination) continue;

            float dx = mov.playerSetDestinationX - marker.Point.x;
            float dy = mov.playerSetDestinationY - marker.Point.y;
            if (dx * dx + dy * dy <= DestinationMatchEpsilon * DestinationMatchEpsilon)
                return true;
        }

        return false;
    }

    // Mirrors BasicTroopRenderer.ToWorldPosition's own "clamp a raw world/tile-space float
    // into TileX/TileY range for the height lookup" pattern — point here is a raw click
    // point (SelectionManager's own TileSpaceMouse result), not a PositionComponent, so
    // there's no ready-made TileX/TileY to read directly.
    private static Vector3 ToWorldPosition(Vector2 point)
    {
        float height = 0f;
        if (WorldManager.instance?.Handler != null)
        {
            ushort maxTile = (ushort)(WorldGenHandler.CHUNK_SIZE_TILES * WorldGenHandler.WorldSizeChunks - 1);
            ushort tx = (ushort)Mathf.Clamp(point.x, 0, maxTile);
            ushort ty = (ushort)Mathf.Clamp(point.y, 0, maxTile);
            height = WorldManager.instance.Handler.GetHeight(tx, ty);
        }
        return new Vector3(point.x, height + GroundOffset, point.y);
    }
}
