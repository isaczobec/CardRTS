using UnityEngine;

// World-space arrow mesh for a skillshot-style ability's direction indicator — authored at
// 1 unit long along local X, base at the origin, tip pointing toward +X. AbilityIndicatorManager
// owns positioning it at the caster and rotating it to face the cast point; this just owns
// scaling it to the cast distance.
public class AbilityDirectionIndicatorPrefab : MonoBehaviour
{
    // length is in world units — the prefab's mesh should be authored at 1 unit long
    // (base at the origin, tip along +X) so this maps 1:1. Y/Z scale are left alone.
    public void SetLength(float length)
    {
        Vector3 scale = transform.localScale;
        scale.x = length;
        transform.localScale = scale;
    }
}
