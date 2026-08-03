using System.Collections.Generic;
using UnityEngine;

// Renders one small badge (marker icon + the bound key's letter, to its left — see
// TroopMarkerPrefab) for every currently-bound troop marker (see TroopMarkerManager),
// positioned on the marked troop. One instance per KEY, not per troop — a troop could in
// principle have more than one key bound to it, each then getting its own badge.
//
// Offset along the camera's own current horizontal LEFT direction (mirrors
// AbilityStatusIconManager's own camera-relative right-offset exactly, just negated), so this
// badge and the ability-status row on the opposite side never overlap regardless of camera
// angle, and reuses SelectionManager's own already-interpolated per-entity position
// (TryGetEntityPosition) rather than running a third independent TickPositionInterpolator
// chasing the same troop (ModifierIconManager and AbilityStatusIconManager each already have
// their own).
public class TroopMarkerIconManager : Singleton<TroopMarkerIconManager>
{
    [SerializeField] private TroopMarkerPrefab _markerPrefab;
    // See AbilityStatusIconManager._rightOffsetDistance's own doc comment — same reasoning,
    // mirrored to the opposite side.
    [SerializeField] private float _leftOffsetDistance = 2f;

    private readonly Dictionary<KeyCode, TroopMarkerPrefab> _instances = new Dictionary<KeyCode, TroopMarkerPrefab>();

    public void Initialize() { }

    void Update()
    {
        if (TickManager.instance == null || !TickManager.instance.IsGameStarted) { HideAll(); return; }
        if (TroopMarkerManager.instance == null || SelectionManager.instance == null) return;
        if (_markerPrefab == null) return;

        // See AbilityStatusIconManager.PositionContainers's own doc comment for why this is
        // recomputed every frame rather than cached, and why flattening/renormalizing is
        // mostly a defensive no-op for this game's own no-roll camera rig.
        Camera cam = Camera.main;
        Vector3 cameraLeft = cam != null ? -cam.transform.right : Vector3.left;
        cameraLeft.y = 0f;
        if (cameraLeft.sqrMagnitude < 0.0001f) cameraLeft = Vector3.left;
        Vector3 offset = cameraLeft.normalized * _leftOffsetDistance;

        foreach (KeyCode key in TroopMarkerManager.MarkerKeys)
        {
            Vector3 pos = new Vector3(0f, 0f, 0f);
            bool hasMarker = TroopMarkerManager.instance.TryGetMarker(key, out ulong troopId)
                && SelectionManager.instance.TryGetEntityPosition(troopId, out pos);

            if (!hasMarker)
            {
                if (_instances.TryGetValue(key, out TroopMarkerPrefab stale) && stale != null)
                    stale.gameObject.SetActive(false);
                continue;
            }

            if (!_instances.TryGetValue(key, out TroopMarkerPrefab instance) || instance == null)
            {
                instance = Instantiate(_markerPrefab, transform);
                instance.name = $"Marker_{key}";
                instance.SetKeyLabel(key.ToString());
                _instances[key] = instance;
            }

            instance.gameObject.SetActive(true);
            instance.transform.position = pos + offset;
        }
    }

    private void HideAll()
    {
        foreach (KeyValuePair<KeyCode, TroopMarkerPrefab> kvp in _instances)
            if (kvp.Value != null)
                kvp.Value.gameObject.SetActive(false);
    }
}
