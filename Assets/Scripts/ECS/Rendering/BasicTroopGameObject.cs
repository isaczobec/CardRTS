using UnityEngine;

/// <summary>
/// Visual prefab for a troop rendered by BasicMeleeRenderer. Exposes the Animator so the
/// renderer can fire triggers off server-authoritative flag events (attack, death, ...).
/// </summary>
public class BasicTroopGameObject : MonoBehaviour
{
    [SerializeField] private Animator _animator;
    public Animator Animator => _animator;
}
