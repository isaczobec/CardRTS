using UnityEngine;
using UnityEngine.Rendering.Universal;

public class SelectionPrefab : MonoBehaviour
{
    [SerializeField] private DecalProjector _decalProjector;

    void Awake()
    {
        if (_decalProjector == null)
            _decalProjector = GetComponentInChildren<DecalProjector>();

        _decalProjector.renderingLayerMask = WorldManager.instance.Renderer.TerrainRenderingLayerMask;
    }
}
