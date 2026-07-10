using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Visual + input-forwarding component for a single card in hand. Purely "how do I look
/// and what happened to me" — CardHandRenderer owns all layout/selection/drag logic and
/// just subscribes to the events below; this never decides anything on its own.
/// Hover is deliberately NOT handled via IPointerEnterHandler/Exit here — CardHandRenderer
/// computes it against each card's static rest-slot position instead (see
/// CardHandRenderer.UpdateHoveredCard), since raycasting against the animated/raised
/// transform creates a feedback loop (raising a card moves its own hit-box out from under
/// the cursor, dropping hover, which lowers it again, regaining hover, ...).
/// </summary>
public class CardGameObject : MonoBehaviour,
    IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [SerializeField] private Image _image;
    [SerializeField] private TMP_Text _titleText;
    [SerializeField] private TMP_Text _text;

    // Set directly by CardHandRenderer right after instantiation — not part of BuildCard,
    // which is purely about how the card looks, not which ECS entity it represents.
    public ulong CardEntityId { get; set; }

    public event Action<CardGameObject> Clicked;
    public event Action<CardGameObject> DragStarted;
    public event Action<CardGameObject, PointerEventData> DragEnded;

    public void BuildCard(string title, string description, Sprite image, StatsComponent? stats = null)
    {
        if (_image != null)
            _image.sprite = image;

        if (_titleText != null)
            _titleText.text = title;

        if (_text != null)
            _text.text = stats.HasValue ? $"{description}\n\n{FormatStats(stats.Value)}" : description;
    }

    private static string FormatStats(StatsComponent stats)
        => $"HP {stats.MaxHealth}   DMG {stats.Damage}   RNG {stats.Range}   SPD {stats.Speed}";

    public void OnPointerClick(PointerEventData eventData) => Clicked?.Invoke(this);
    public void OnBeginDrag(PointerEventData eventData) => DragStarted?.Invoke(this);
    // Per-frame drag position is driven by CardHandRenderer reading Input.mousePosition
    // directly (needs the same lift-threshold logic every frame regardless of whether the
    // pointer actually moved), so OnDrag itself doesn't need to do anything.
    public void OnDrag(PointerEventData eventData) { }
    public void OnEndDrag(PointerEventData eventData) => DragEnded?.Invoke(this, eventData);
}
