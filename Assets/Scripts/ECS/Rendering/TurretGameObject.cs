using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Visual prefab for a turret-style building rendered by BuildingTurretRenderer. Exposes the
/// Animator so the renderer can fire triggers off server-authoritative flag events (attack,
/// death, ...) — mirrors BasicTroopGameObject — plus which child transforms should
/// continuously rotate to face this turret's current target (e.g. a rotating barrel/head).
/// Any part of the hierarchy NOT listed in _rotatingParts (the base, mount, ...) is simply
/// never touched by BuildingTurretRenderer, so it stays stationary without needing its own
/// separate list.
/// </summary>
public class TurretGameObject : MonoBehaviour
{
    [SerializeField] private Animator _animator;
    public Animator Animator => _animator;

    // Child transforms BuildingTurretRenderer rotates to face the turret's current target
    // every frame — e.g. the barrel. Left empty on a prefab with no aiming part at all.
    [SerializeField] private List<Transform> _rotatingParts = new List<Transform>();
    public IReadOnlyList<Transform> RotatingParts => _rotatingParts;

    // Optional — assign every MeshRenderer/SkinnedMeshRenderer that should receive extra
    // overlay materials for status effects (see OverlayMaterialManager) — mirrors
    // BasicTroopGameObject's own field.
    [SerializeField] private List<Renderer> _renderers = new List<Renderer>();
    public IReadOnlyList<Renderer> Renderers => _renderers;
}
