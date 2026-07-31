using System.Collections.Generic;
using UnityEngine;

// Toggles WorldRenderer's per-chunk terrain GameObjects and GoTileManager's per-chunk
// decoration roots on/off based on camera frustum + a max view distance. This is on top of
// (not a replacement for) Unity's own automatic per-renderer frustum culling — that only
// skips a renderer for the MAIN camera's own color pass. A directional light's shadow pass
// has its own, much larger frustum, so a chunk sitting well outside what the player can see
// can still cost real GPU time rendering into the shadow map every frame unless the
// GameObject is actually disabled. Disabling also stops any Update()/physics/animation on
// GoTileManager's spawned prefabs, which is free CPU headroom on top of the GPU win.
//
// Purely a render-side optimization — this must never touch ECS/TickManager state. The
// simulation is server-authoritative + client-predicted lockstep, so every system has to
// tick identically on every machine regardless of what one particular client's camera can
// see; only the MonoBehaviour visuals here are safe to cull per-client.
public class WorldChunkVisibilityManager : Singleton<WorldChunkVisibilityManager>
{
    [SerializeField] private WorldRenderer _worldRenderer;
    [SerializeField] private GoTileManager _goTileManager;
    [SerializeField] private Camera _camera;

    // World units beyond which a chunk is hidden even if technically still inside the
    // frustum (e.g. a huge far clip plane) — tune to the camera's actual max useful view
    // range (see CameraController._maxDistance) plus some margin.
    [SerializeField] private float _viewDistance = 100f;

    // Extra padding added around each chunk's tight height-derived bounds, in world units,
    // to absorb error from only sampling the 4 corner heights (a chunk's interior could
    // peak higher/lower) — cheap insurance against a chunk visibly popping in/out at its
    // edges rather than a real accuracy requirement.
    [SerializeField] private float _boundsPadding = 5f;

    // How often (seconds) to re-evaluate visibility — chunk granularity (16 tiles) and
    // camera pan/zoom speed both make sub-frame precision pointless, so this trades a little
    // pop-in latency for a lot less GeometryUtility/Bounds work.
    [SerializeField] private float _checkInterval = 0.15f;

    private readonly Dictionary<(int cx, int cy), Bounds> _chunkBounds = new();
    private readonly HashSet<(int cx, int cy)> _visibleChunks = new();
    private float _timer;

    // Called by WorldManager.GenerateAndRender right after WorldRenderer/GoTileManager have
    // finished spawning this game's chunks — safe to call again on a fresh world (e.g.
    // rematch), since it fully rebuilds cached state from whatever's currently registered.
    public void Initialize()
    {
        if (_camera == null) _camera = Camera.main;
        if (_worldRenderer == null) _worldRenderer = FindFirstObjectByType<WorldRenderer>();
        if (_goTileManager == null) _goTileManager = FindFirstObjectByType<GoTileManager>();
        if (_camera == null || _worldRenderer == null) return;

        CacheChunkBounds();

        // WorldRenderer/GoTileManager spawn every chunk active — start the tracked set in
        // sync with that so the first RefreshVisibility() correctly detects and hides
        // whatever's actually out of view instead of treating everything as "already
        // hidden, nothing to do".
        _visibleChunks.Clear();
        foreach (var key in _chunkBounds.Keys)
            _visibleChunks.Add(key);

        _timer = 0f;
    }

    private void CacheChunkBounds()
    {
        _chunkBounds.Clear();

        WorldGenHandler handler = WorldManager.instance.Handler;
        if (handler == null) return;

        int size = WorldGenHandler.CHUNK_SIZE_TILES;
        ushort maxCoord = (ushort)(size * WorldGenHandler.WorldSizeChunks - 1);

        foreach ((int cx, int cy) in _worldRenderer.ChunkObjects.Keys)
        {
            ushort minX = (ushort)(cx * size);
            ushort minY = (ushort)(cy * size);
            ushort maxX = (ushort)Mathf.Min(minX + size, maxCoord);
            ushort maxY = (ushort)Mathf.Min(minY + size, maxCoord);

            float h00 = handler.GetHeight(minX, minY);
            float h10 = handler.GetHeight(maxX, minY);
            float h01 = handler.GetHeight(minX, maxY);
            float h11 = handler.GetHeight(maxX, maxY);
            float minH = Mathf.Min(Mathf.Min(h00, h10), Mathf.Min(h01, h11));
            float maxH = Mathf.Max(Mathf.Max(h00, h10), Mathf.Max(h01, h11));

            Vector3 worldMin = WorldManager.instance.TileToWorldPosition(minX, minY, useHeightmap: false);
            Vector3 center = new Vector3(worldMin.x + size * 0.5f, (minH + maxH) * 0.5f, worldMin.z + size * 0.5f);
            Vector3 fullSize = new Vector3(
                size + _boundsPadding * 2f,
                (maxH - minH) + _boundsPadding * 2f,
                size + _boundsPadding * 2f);

            _chunkBounds[(cx, cy)] = new Bounds(center, fullSize);
        }
    }

    private void Update()
    {
        if (_camera == null || _worldRenderer == null) return;

        _timer -= Time.deltaTime;
        if (_timer > 0f) return;
        _timer = _checkInterval;

        RefreshVisibility();
    }

    private void RefreshVisibility()
    {
        Plane[] frustumPlanes = GeometryUtility.CalculateFrustumPlanes(_camera);
        Vector3 camPos = _camera.transform.position;
        float viewDistanceSqr = _viewDistance * _viewDistance;

        foreach (var pair in _chunkBounds)
        {
            Bounds bounds = pair.Value;
            bool inRange = bounds.SqrDistance(camPos) <= viewDistanceSqr;
            bool visible = inRange && GeometryUtility.TestPlanesAABB(frustumPlanes, bounds);

            SetChunkVisible(pair.Key, visible);
        }
    }

    private void SetChunkVisible((int cx, int cy) key, bool visible)
    {
        if (_visibleChunks.Contains(key) == visible) return;

        if (visible) _visibleChunks.Add(key);
        else _visibleChunks.Remove(key);

        if (_worldRenderer.ChunkObjects.TryGetValue(key, out GameObject terrainChunk))
            terrainChunk.SetActive(visible);

        if (_goTileManager != null && _goTileManager.ChunkRoots.TryGetValue(key, out Transform goTileRoot))
            goTileRoot.gameObject.SetActive(visible);
    }
}
