using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Visual prefab for a troop rendered by BasicMeleeRenderer. Exposes the Animator so the
/// renderer can fire triggers off server-authoritative flag events (attack, death, ...).
/// </summary>
public class BasicTroopGameObject : MonoBehaviour
{
    [SerializeField] private Animator _animator;
    public Animator Animator => _animator;

    // Optional — assign every MeshRenderer/SkinnedMeshRenderer that should receive extra
    // overlay materials for status effects (see OverlayMaterialManager). A prefab built
    // from multiple meshes (e.g. body + separate weapon mesh) or a rigged/skinned mesh can
    // list more than one here; leave empty on a prefab that doesn't need this at all.
    [SerializeField] private List<Renderer> _renderers = new List<Renderer>();
    public IReadOnlyList<Renderer> Renderers => _renderers;
}
