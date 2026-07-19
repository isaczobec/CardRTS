using System;
using UnityEngine;
using UnityEngine.EventSystems;

// Minimal reusable pointer-hover forwarder — attach to any UI element that needs to raise
// hover events but doesn't already have its own dedicated component for it (e.g.
// AbilityBarUI's ability icons, which are plain Images). Mirrors the HoverEntered/
// HoverExited events CardGameObject/UpgradeGameObject implement directly on themselves.
public class HoverEventForwarder : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public event Action HoverEntered;
    public event Action HoverExited;

    public void OnPointerEnter(PointerEventData eventData) => HoverEntered?.Invoke();
    public void OnPointerExit(PointerEventData eventData) => HoverExited?.Invoke();
}
