using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Renders the local player's hand as a curved fan of screen-space CardGameObjects
/// sitting at the bottom of the screen (resting position keeps roughly the bottom half of
/// each card below the screen edge). Handles hover-raise, click/number-key selection
/// (stays selected until played or Escape, animating to _selectedAnchor), and drag-and-
/// drop play — dragging a card up past _dragLiftThreshold snaps its visual to
/// _selectedAnchor too, so the card itself doesn't obscure the drop spot while you aim.
/// Call Initialize() once (e.g. from RenderingSetup.SetupRendering, alongside
/// SelectionManager/HealthBarManager) before use.
/// </summary>
public class CardHandRenderer : Singleton<CardHandRenderer>
{
    [Header("Prefab")]
    [SerializeField] private CardGameObject _cardPrefab;

    [Header("Layout")]
    // Parent RectTransform for all card visuals — anchor/pivot should sit at the
    // bottom-center of the screen.
    [SerializeField] private RectTransform _handContainer;
    [SerializeField] private float _fanArcRadius = 1200f;
    // Upper bound on how far a card can rotate/spread from center, reached once the hand
    // has enough cards (see ComputeFanTransform) — never exceeded regardless of hand size.
    [SerializeField] private float _fanMaxAngleDegrees = 30f;
    // Desired angle between adjacent cards for a small hand. The actual half-spread used is
    // min(_fanMaxAngleDegrees, _fanAngleStepDegrees * (count - 1) / 2), so a couple of cards
    // sit close together instead of being yanked out to the full ± _fanMaxAngleDegrees spread.
    [SerializeField] private float _fanAngleStepDegrees = 12f;
    // How far below _handContainer's pivot the resting fan sits — tune so roughly the
    // bottom half of each card is below the screen edge at rest.
    [SerializeField] private float _restingYOffset = -220f;
    [SerializeField] private float _hoverRaiseAmount = 150f;
    // Extra vertical slack above the raised (hovered) card position still counted as
    // hovering it — keeps hover sticky right at the card's raised edge without this needing
    // to be huge. Previously this slack was the card's own full height stacked on top of
    // _hoverRaiseAmount (see UpdateHoveredCard), which made the hover zone — and, since
    // IsCardHovered also suppresses troop selection/movement clicks, the "no troop clicks
    // here" zone — extend uncomfortably far up into the area used for controlling troops.
    [SerializeField] private float _hoverDetectionMargin = 40f;
    [SerializeField] private float _cardMoveSpeed = 12f;
    [SerializeField] private float _cardRotateSpeed = 12f;
    // Hover hit-test half-width is multiplied by this for whichever card is already
    // hovered, so small mouse jitter right at a slot boundary doesn't flicker between
    // neighboring cards.
    [SerializeField] private float _hoverStickyMultiplier = 1.25f;

    [Header("Selection / Drag")]
    // Where a selected card (or a card dragged past the lift threshold) animates to.
    [SerializeField] private RectTransform _selectedAnchor;
    // Screen-space Y (pixels from the bottom) past which a dragged card snaps to
    // _selectedAnchor instead of following the cursor directly.
    [SerializeField] private float _dragLiftThreshold = 250f;

    private ECS _ecs;
    private readonly Dictionary<ulong, CardGameObject> _handCards = new();
    private readonly List<ulong> _handOrder = new();

    private ulong _hoveredCardId;
    private ulong _selectedCardId;
    private ulong _draggingCardId;

    private ulong _localPlayerResourceEntityId;

    // True while a card is selected or being dragged — SelectionManager checks this to
    // suppress normal troop selection/targeting input, the same way it already checks
    // Space (see SelectionManager.HandleSelectionInput).
    public bool IsCardSelectedOrDragging => _selectedCardId != 0 || _draggingCardId != 0;

    // True while a card is merely hovered (not yet selected/dragging) — SelectionManager
    // also checks this alongside IsCardSelectedOrDragging so a click that lands on a
    // hovered card's hand icon never falls through and deselects the current troop
    // selection.
    public bool IsCardHovered => _hoveredCardId != 0;

    // The card currently selected or being dragged (0 if neither) — the one about to be
    // played. Used by things like CardRangeIndicatorManager that need to know WHICH card's
    // rules currently apply, not just whether one is active.
    public ulong ActiveCardEntityId => _selectedCardId != 0 ? _selectedCardId : _draggingCardId;

    // Resolves the Card definition for ActiveCardEntityId, or null if nothing is active /
    // not resolvable. Shared convenience for other systems (CardRangeIndicatorManager,
    // CardPlacementIndicatorManager, ...) that need to know which card's rules currently
    // apply rather than duplicating the CardComponent -> CardRegistry lookup themselves.
    public Card ResolveActiveCard()
    {
        ulong cardEntityId = ActiveCardEntityId;
        if (cardEntityId == 0 || _ecs == null) return null;

        ComponentStore<CardComponent> cardStore = _ecs.GetComponentStore<CardComponent>();
        if (cardStore == null || !cardStore.HasComponent(cardEntityId)) return null;

        CardComponent card = cardStore.GetComponent(cardEntityId);
        return CardRegistry.TryGet(card.Type, out Card definition) ? definition : null;
    }

    public void Initialize()
    {
        TickManager.instance.ServerFlagEvents.Subscribe<CardDrawnEvent>(OnCardDrawn);
        TickManager.instance.ServerFlagEvents.Subscribe<CardPlayedEvent>(OnCardPlayed);
        TickManager.instance.ServerFlagEvents.Subscribe<ResourcesChangedEvent>(OnResourcesChanged);

        _ecs = TickManager.instance.ActiveECS;
    }

    private ushort LocalPlayerId()
        => NetworkManager.instance != null ? NetworkManager.instance.LocalPlayerId : (ushort)0;

    // ── Resources ────────────────────────────────────────────────────────────

    // Re-resolved lazily (mirroring ResourceCounterUI.FindLocalPlayerEntity) rather than
    // cached forever, in case the local player's entity doesn't exist yet the first time
    // this is queried.
    private ulong ResolveLocalPlayerResourceEntity()
    {
        if (_ecs == null) return 0;

        ComponentStore<PlayerResourcesComponent> resourceStore = _ecs.GetComponentStore<PlayerResourcesComponent>();
        if (resourceStore != null && _localPlayerResourceEntityId != 0 && resourceStore.HasComponent(_localPlayerResourceEntityId))
            return _localPlayerResourceEntityId;

        _localPlayerResourceEntityId = ResourceHelper.FindPlayerResourcesEntity(_ecs, LocalPlayerId());
        return _localPlayerResourceEntityId;
    }

    private bool TryGetLocalPlayerResources(out PlayerResourcesComponent resources)
    {
        resources = default;

        ulong resourceEntityId = ResolveLocalPlayerResourceEntity();
        if (resourceEntityId == 0) return false;

        ComponentStore<PlayerResourcesComponent> resourceStore = _ecs.GetComponentStore<PlayerResourcesComponent>();
        if (resourceStore == null || !resourceStore.HasComponent(resourceEntityId)) return false;

        resources = resourceStore.GetComponent(resourceEntityId);
        return true;
    }

    private bool CanAfford(ulong cardEntityId)
    {
        if (_ecs == null) return false;

        ComponentStore<CardComponent> cardStore = _ecs.GetComponentStore<CardComponent>();
        if (cardStore == null || !cardStore.HasComponent(cardEntityId)) return false;

        CardComponent card = cardStore.GetComponent(cardEntityId);
        if (!CardRegistry.TryGet(card.Type, out Card definition)) return false;

        if (!TryGetLocalPlayerResources(out PlayerResourcesComponent resources)) return false;

        return definition.Cost.CanAfford(resources);
    }

    private void OnResourcesChanged(ResourcesChangedEvent e)
    {
        if (e.EntityId != ResolveLocalPlayerResourceEntity()) return;

        if (_selectedCardId != 0 && !CanAfford(_selectedCardId))
            _selectedCardId = 0;

        if (!TryGetLocalPlayerResources(out PlayerResourcesComponent resources)) return;

        foreach (CardGameObject go in _handCards.Values)
            go.RefreshAffordability(resources);
    }

    // ── Card lifecycle ──────────────────────────────────────────────────────

    private void OnCardDrawn(CardDrawnEvent e)
    {
        if (_ecs == null || _cardPrefab == null || _handContainer == null) return;
        if (_handCards.ContainsKey(e.EntityId)) return;

        ComponentStore<CardComponent> cardStore = _ecs.GetComponentStore<CardComponent>();
        if (cardStore == null || !cardStore.HasComponent(e.EntityId)) return;

        CardComponent card = cardStore.GetComponent(e.EntityId);
        if (card.OwnerPlayerId != LocalPlayerId()) return; // only render our own hand

        if (!CardRegistry.TryGet(card.Type, out Card definition)) return;

        CardGameObject go = Instantiate(_cardPrefab, _handContainer);
        go.CardEntityId = e.EntityId;

        Sprite artwork = null;
        if (ImageRegistry.instance != null)
            ImageRegistry.instance.TryGet(definition.ImageName, out artwork);

        go.BuildCard(definition.Title, definition.Description, artwork, definition.DefaultStats, definition.Cost);
        if (TryGetLocalPlayerResources(out PlayerResourcesComponent resources))
            go.RefreshAffordability(resources);

        go.Clicked      += OnCardClicked;
        go.DragStarted  += OnCardDragStarted;
        go.DragEnded    += OnCardDragEnded;

        _handCards[e.EntityId] = go;
        _handOrder.Add(e.EntityId);
    }

    private void OnCardPlayed(CardPlayedEvent e)
    {
        if (!_handCards.TryGetValue(e.EntityId, out CardGameObject go)) return;

        _handCards.Remove(e.EntityId);
        _handOrder.Remove(e.EntityId);

        if (_selectedCardId == e.EntityId) _selectedCardId = 0;
        if (_hoveredCardId == e.EntityId) _hoveredCardId = 0;
        if (_draggingCardId == e.EntityId) _draggingCardId = 0;

        Destroy(go.gameObject);
    }

    // ── Input ────────────────────────────────────────────────────────────────

    public void Update()
    {
        if (TickManager.instance == null || !TickManager.instance.IsGameStarted) return;

        HandleKeyboardInput();
        HandlePlaySelectedCardClick();
        UpdateDraggingCardPosition();
        LayoutHand();
    }

    private void HandleKeyboardInput()
    {
        if (_selectedCardId != 0 && Input.GetKeyDown(KeyCode.Escape))
            _selectedCardId = 0;

        for (int i = 0; i < _handOrder.Count && i < 9; i++)
        {
            if (Input.GetKeyDown(KeyCode.Alpha1 + i))
            {
                ulong id = _handOrder[i];
                if (_selectedCardId == id)
                    _selectedCardId = 0; // pressing the selected card's own number deselects it
                else
                    SelectCard(id);
                break;
            }
        }
    }

    // While a card is selected (not dragging), a left-click anywhere in the world plays
    // it there. A click that lands on UI (e.g. a different card) is left for that
    // element's own handler instead.
    private void HandlePlaySelectedCardClick()
    {
        if (_selectedCardId == 0) return;
        if (!Input.GetMouseButtonDown(0)) return;
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

        if (TileSpaceMouse.TryGetPosition(out float x, out float y))
        {
            ulong cardId = _selectedCardId;
            _selectedCardId = 0;
            PlayCard(cardId, x, y);
        }
    }

    private void SelectCard(ulong cardEntityId)
    {
        if (_draggingCardId != 0) return; // don't fight an active drag
        if (!CanAfford(cardEntityId)) return; // can't select a card the player can't play
        _selectedCardId = cardEntityId;
    }

    private void OnCardClicked(CardGameObject go) => SelectCard(go.CardEntityId);

    private void OnCardDragStarted(CardGameObject go)
    {
        _draggingCardId = go.CardEntityId;
        _selectedCardId = 0; // dragging supersedes an explicit selection
    }

    private void OnCardDragEnded(CardGameObject go, PointerEventData eventData)
    {
        if (_draggingCardId != go.CardEntityId) return;
        _draggingCardId = 0;

        // If the drop point isn't a valid world position (e.g. released over UI or above
        // the horizon), the card just falls back into its normal hand layout below.
        if (TileSpaceMouse.TryGetPosition(out float x, out float y))
            PlayCard(go.CardEntityId, x, y);
    }

    // Single choke point for both the click-to-play (HandlePlaySelectedCardClick) and
    // drag-to-play (OnCardDragEnded) paths — including the SelectionManager notification,
    // so neither path can forget it (see SelectionManager.SuppressNextClickSelect for why
    // it's needed: this same click/release is what just cleared _selectedCardId/
    // _draggingCardId, so SelectionManager can no longer tell "a card was just played
    // here" from "nothing was selected" by the time it processes the corresponding
    // mouse-up).
    private void PlayCard(ulong cardEntityId, float x, float y)
    {
        SelectionManager.instance?.SuppressNextClickSelect();
        InputBuffer.EnqueueInput(new SpawnAtPointInput { CardEntityId = cardEntityId, X = x, Y = y });
    }

    // While below the lift threshold the dragged card sticks exactly to the cursor
    // (direct-manipulation feel); past it, it eases toward _selectedAnchor instead of
    // continuing to follow the cursor, so the card's own visual doesn't sit on top of the
    // spot you're about to drop it — TileSpaceMouse still reads the real cursor position
    // for the actual drop, independent of where the card is currently drawn.
    private void UpdateDraggingCardPosition()
    {
        if (_draggingCardId == 0) return;
        if (!_handCards.TryGetValue(_draggingCardId, out CardGameObject go)) return;

        bool pastLiftThreshold = Input.mousePosition.y > _dragLiftThreshold;

        if (pastLiftThreshold && _selectedAnchor != null)
            go.transform.position = Vector3.Lerp(go.transform.position, _selectedAnchor.position, Time.deltaTime * _cardMoveSpeed);
        else
            go.transform.position = Input.mousePosition;
    }

    // ── Layout ───────────────────────────────────────────────────────────────

    private void LayoutHand()
    {
        UpdateHoveredCard();

        int count = _handOrder.Count;
        for (int i = 0; i < count; i++)
        {
            ulong id = _handOrder[i];
            if (!_handCards.TryGetValue(id, out CardGameObject go)) continue;

            go.SetPanelsVisible(id == _hoveredCardId || id == _selectedCardId);

            if (id == _draggingCardId) continue; // handled by UpdateDraggingCardPosition

            if (id == _selectedCardId && _selectedAnchor != null)
            {
                AnimateCardTo(go, _selectedAnchor.position, _selectedAnchor.rotation);
                continue;
            }

            (Vector3 restPos, Quaternion restRot) = ComputeFanTransform(i, count);

            if (id == _hoveredCardId && _handContainer != null)
            {
                restPos += _handContainer.up * _hoverRaiseAmount;
                restRot = _handContainer.rotation; // straighten for readability
                go.transform.SetAsLastSibling(); // draw on top of overlapping neighbors
            }

            AnimateCardTo(go, restPos, restRot);
        }
    }

    // Determines the hovered card purely from the mouse's screen position against each
    // card's static REST slot (never its current animated/raised position). Hit-testing
    // against the animated position would create a feedback loop: raising a card on hover
    // moves its own hit-box, which can push it out from under the cursor near the card's
    // edge or bottom, dropping hover and lowering it again, regaining hover, and so on.
    // The sticky multiplier on the currently-hovered card adds a little hysteresis on top,
    // so tiny mouse jitter right at a slot boundary doesn't flicker between neighbors.
    private void UpdateHoveredCard()
    {
        if (_draggingCardId != 0 || _handContainer == null)
        {
            _hoveredCardId = 0;
            return;
        }

        int count = _handOrder.Count;
        if (count == 0)
        {
            _hoveredCardId = 0;
            return;
        }

        Vector2 cardSize = EstimateCardScreenSize();
        Vector2 mouse = Input.mousePosition;

        ulong bestId = 0;
        float bestDist = float.MaxValue;

        for (int i = 0; i < count; i++)
        {
            ulong id = _handOrder[i];
            if (id == _selectedCardId) continue; // already flown out to the anchor

            (Vector3 restPos, _) = ComputeFanTransform(i, count);

            float halfWidth = cardSize.x * 0.5f * (id == _hoveredCardId ? _hoverStickyMultiplier : 1f);
            float dx = Mathf.Abs(mouse.x - restPos.x);
            if (dx > halfWidth) continue;

            // Vertical window: a little below the resting slot up through the raised
            // (hovered) position plus a small margin — see _hoverDetectionMargin.
            float dy = mouse.y - restPos.y;
            if (dy < -cardSize.y * 0.25f || dy > _hoverRaiseAmount + _hoverDetectionMargin) continue;

            if (dx < bestDist)
            {
                bestDist = dx;
                bestId = id;
            }
        }

        _hoveredCardId = bestId;
    }

    // Approximate on-screen size of a card, sampled from a currently-instantiated card (or
    // the prefab, before any are drawn) so it reflects the actual canvas scale.
    private Vector2 EstimateCardScreenSize()
    {
        RectTransform sample = null;
        foreach (KeyValuePair<ulong, CardGameObject> kvp in _handCards)
        {
            sample = kvp.Value.GetComponent<RectTransform>();
            break;
        }
        if (sample == null && _cardPrefab != null)
            sample = _cardPrefab.GetComponent<RectTransform>();
        if (sample == null)
            return new Vector2(200f, 280f);

        Vector3 scale = sample.lossyScale;
        return new Vector2(sample.rect.width * Mathf.Abs(scale.x), sample.rect.height * Mathf.Abs(scale.y));
    }

    private void AnimateCardTo(CardGameObject go, Vector3 targetPos, Quaternion targetRot)
    {
        go.transform.position = Vector3.Lerp(go.transform.position, targetPos, Time.deltaTime * _cardMoveSpeed);
        go.transform.rotation = Quaternion.Slerp(go.transform.rotation, targetRot, Time.deltaTime * _cardRotateSpeed);
    }

    // Resting position/rotation for hand slot `index` of `count` total cards, arranged
    // along a circular arc whose pivot sits _fanArcRadius below _handContainer — the
    // classic "hand of cards" fan, center highest, ends dipping down and rotating
    // outward. _restingYOffset then shifts the whole fan up/down.
    //
    // The half-spread scales with card count (up to _fanMaxAngleDegrees) instead of always
    // stretching to the full range, so a hand of one or two cards sits close together
    // rather than being yanked out to the same spread as a full hand.
    private (Vector3 position, Quaternion rotation) ComputeFanTransform(int index, int count)
    {
        float halfSpread = count <= 1 ? 0f : Mathf.Min(_fanMaxAngleDegrees, _fanAngleStepDegrees * (count - 1) * 0.5f);
        float t = count <= 1 ? 0.5f : index / (float)(count - 1);
        float angleDeg = Mathf.Lerp(-halfSpread, halfSpread, t);
        float angleRad = angleDeg * Mathf.Deg2Rad;

        float x = Mathf.Sin(angleRad) * _fanArcRadius;
        float y = -_fanArcRadius * (1f - Mathf.Cos(angleRad)) + _restingYOffset;

        Vector3 localPos = new Vector3(x, y, 0f);
        Vector3 worldPos = _handContainer.TransformPoint(localPos);
        Quaternion worldRot = _handContainer.rotation * Quaternion.Euler(0f, 0f, -angleDeg);

        return (worldPos, worldRot);
    }
}
