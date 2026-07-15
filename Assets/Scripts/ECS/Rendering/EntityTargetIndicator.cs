using UnityEngine;

// Owns a single EntityTargetIndicatorPrefab instance, showing which selectable entity is
// currently being targeted (see EntityTargeting.FindClosestSelectable for resolving which
// one that is). Not a MonoBehaviour/singleton — instantiated as a plain field by whichever
// manager needs one (AbilityIndicatorManager today; a similarly cursor-targeted card kind
// later), so each owner gets its own independent indicator instance with no shared state.
public class EntityTargetIndicator
{
    private readonly EntityTargetIndicatorPrefab _prefab;
    private readonly Transform _parent;
    private EntityTargetIndicatorPrefab _instance;

    public EntityTargetIndicator(EntityTargetIndicatorPrefab prefab, Transform parent)
    {
        _prefab = prefab;
        _parent = parent;
    }

    // Shows the indicator at worldPosition (creating the instance lazily on first use).
    // Caller resolves worldPosition itself (world-height lookup differs by context) and is
    // expected to call Hide() instead when there's no target to show.
    public void Show(Vector3 worldPosition)
    {
        if (_instance == null)
        {
            if (_prefab == null) return;
            _instance = Object.Instantiate(_prefab, _parent);
        }

        _instance.transform.position = worldPosition;
        _instance.gameObject.SetActive(true);
    }

    public void Hide()
    {
        if (_instance != null)
            _instance.gameObject.SetActive(false);
    }
}
