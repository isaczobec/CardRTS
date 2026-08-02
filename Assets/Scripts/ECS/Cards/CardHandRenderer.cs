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

    [Header("Discard")]
    // Right-click never selects a card (see CardGameObject.RightClicked vs. Clicked) — two
    // right-clicks on the SAME UNAFFORDABLE card within this window discard it instead (see
    // OnCardRightClicked). A right-click on a different card, a second right-click after this
    // window has elapsed, or any right-click on a card the player can currently afford, just
    // does nothing (or starts a fresh pair) instead of discarding anything.
    [SerializeField] private float _discardDoubleClickWindow = 0.4f;

    private ulong _lastRightClickCardId;
    private float _lastRightClickTime;

    private ECS _ecs;
    private readonly Dictionary<ulong, CardGameObject> _handCards = new();
    private readonly List<ulong> _handOrder = new();

    private ulong _hoveredCardId;
    private ulong _selectedCardId;
    private ulong _draggingCardId;

    // In-progress point sequence for a MultiPointCard (see TryPlayCard) — _multiPointCardId
    // is which card the points below belong to (0 = no sequence in progress). Reset whenever
    // a different card becomes selected, on Escape/deselect, and once the sequence completes
    // and its input is sent.
    private ulong _multiPointCardId;
    private readonly List<Vector2> _multiPointPoints = new List<Vector2>();

    // Read by CardPlacementIndicatorManager to show a frozen indicator per point already
    // placed, alongside its own live one for the point not yet clicked.
    public IReadOnlyList<Vector2> ArmedMultiPointPoints => _multiPointPoints;

    // Non-positional (spatialBlend 0) UI audio source shared by every hover/select/draw/play
    // feedback sound — these are hand-UI cues, not tied to any world position. Sound keys are
    // resolved through SoundRegistry, so each of "CardHover"/"CardSelect"/"CardDraw"/
    // "CardPlay" needs a clip assigned there.
    private CardRTSAudioSource _audioSource;

    private ulong _localPlayerResourceEntityId;

    // Scratch buffer for EntityTargeting.FindClosestSelectable (TargetEntityCard play) —
    // reused across calls rather than allocated per play.
    private readonly List<ulong> _targetQueryBuffer = new List<ulong>();

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
    // CardPlacementIndicatorManager, CardTargetIndicatorManager, ...) that need to know
    // which card's rules currently apply rather than duplicating the CardComponent ->
    // CardRegistry lookup themselves.
    public Card ResolveActiveCard() => ResolveCard(ActiveCardEntityId);

    // Same lookup as ResolveActiveCard, but for an explicit card entity rather than
    // whichever is currently selected/dragging — needed by TryPlayCard, which is called
    // from OnCardDragEnded after _draggingCardId has already been cleared (so
    // ActiveCardEntityId would no longer resolve to the card actually being played).
    private Card ResolveCard(ulong cardEntityId)
    {
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
        TickManager.instance.ServerFlagEvents.Subscribe<CardDiscardedEvent>(OnCardDiscarded);
        TickManager.instance.ServerFlagEvents.Subscribe<ResourcesChangedEvent>(OnResourcesChanged);
        TickManager.instance.ServerFlagEvents.Subscribe<ComponentAddedEvent<UpgradeComponent>>(OnUpgradeAdded);

        _ecs = TickManager.instance.ActiveECS;

        if (AudioManager.instance != null)
            _audioSource = AudioManager.instance.CreateAudioSource(Vector3.zero, spatialBlend: 0f);
    }

    private void PlayCardSound(string soundName) => _audioSource?.PlaySound(soundName);

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
        if (cardStore.GetComponent(e.EntityId).OwnerPlayerId != LocalPlayerId()) return; // only render our own hand

        CardGameObject go = Instantiate(_cardPrefab, _handContainer);
        go.CardEntityId = e.EntityId;

        BuildCardVisual(e.EntityId, go);

        go.Clicked      += OnCardClicked;
        go.RightClicked += OnCardRightClicked;
        go.DragStarted  += OnCardDragStarted;
        go.DragEnded    += OnCardDragEnded;

        _handCards[e.EntityId] = go;
        _handOrder.Add(e.EntityId);

        PlayCardSound("CardDraw");
    }

    // Populates go's face from cardEntityId's current CardComponent/upgrades — shared by
    // OnCardDrawn (first build) and OnUpgradeAdded (rebuild once a purchased upgrade attaches
    // to a card already sitting in hand).
    private void BuildCardVisual(ulong cardEntityId, CardGameObject go)
    {
        ComponentStore<CardComponent> cardStore = _ecs.GetComponentStore<CardComponent>();
        if (cardStore == null || !cardStore.HasComponent(cardEntityId)) return;

        CardComponent card = cardStore.GetComponent(cardEntityId);
        if (!CardRegistry.TryGet(card.Type, out Card definition)) return;

        Sprite artwork = null;
        if (ImageRegistry.instance != null)
            ImageRegistry.instance.TryGet(definition.ImageName, out artwork);

        go.BuildCard(definition.Title, definition.Description, artwork, definition.DefaultStats, definition.Cost,
            definition.Category, definition.BackgroundColor, definition.TextBackgroundColor, definition.EdgeColor);
        go.SetUpgradeIcons(UpgradeIconResolver.Resolve(_ecs, cardEntityId));

        if (TryGetLocalPlayerResources(out PlayerResourcesComponent resources))
            go.RefreshAffordability(resources);
    }

    // The new entity here is the UPGRADE entity BuyUpgradeSystem just created, not the card
    // itself — its UpgradeComponent.TargetCardEntityId says which card to rebuild, if that
    // card happens to be currently rendered in hand at all.
    private void OnUpgradeAdded(ComponentAddedEvent<UpgradeComponent> e)
    {
        if (_ecs == null) return;

        ComponentStore<UpgradeComponent> upgradeStore = _ecs.GetComponentStore<UpgradeComponent>();
        if (upgradeStore == null || !upgradeStore.HasComponent(e.EntityId)) return;

        ulong targetCardEntityId = upgradeStore.GetComponent(e.EntityId).TargetCardEntityId;
        if (!_handCards.TryGetValue(targetCardEntityId, out CardGameObject go)) return;

        BuildCardVisual(targetCardEntityId, go);
    }

    private void OnCardPlayed(CardPlayedEvent e) => RemoveHandCardVisual(e.EntityId, "CardPlay");

    // A discard isn't predicted (see DiscardCardSystem's own doc comment) — the hand-card
    // visual only disappears once the server confirms it via this ServerFlagEvents-only
    // event, exactly like a normal play.
    private void OnCardDiscarded(CardDiscardedEvent e) => RemoveHandCardVisual(e.EntityId, "CardDiscard");

    private void RemoveHandCardVisual(ulong entityId, string soundName)
    {
        if (!_handCards.TryGetValue(entityId, out CardGameObject go)) return;

        _handCards.Remove(entityId);
        _handOrder.Remove(entityId);

        if (_selectedCardId == entityId) _selectedCardId = 0;
        if (_hoveredCardId == entityId) _hoveredCardId = 0;
        if (_draggingCardId == entityId) _draggingCardId = 0;

        Destroy(go.gameObject);

        PlayCardSound(soundName);
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
        {
            _selectedCardId = 0;
            ClearMultiPointSequence();
        }

        for (int i = 0; i < _handOrder.Count && i < 9; i++)
        {
            if (Input.GetKeyDown(KeyCode.Alpha1 + i))
            {
                ulong id = _handOrder[i];
                if (_selectedCardId == id)
                {
                    _selectedCardId = 0; // pressing the selected card's own number deselects it
                    ClearMultiPointSequence();
                }
                else
                {
                    SelectCard(id);
                }
                break;
            }
        }
    }

    private void ClearMultiPointSequence()
    {
        _multiPointCardId = 0;
        _multiPointPoints.Clear();
    }

    // While a card is selected (not dragging), a left-click anywhere in the world plays
    // it there. A click that lands on UI (e.g. a different card) is left for that
    // element's own handler instead.
    private void HandlePlaySelectedCardClick()
    {
        if (_selectedCardId == 0) return;
        if (!Input.GetMouseButtonDown(0)) return;
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

        ulong cardId = _selectedCardId;
        if (TryPlayCard(cardId))
            _selectedCardId = 0;
    }

    private void SelectCard(ulong cardEntityId)
    {
        if (_draggingCardId != 0) return; // don't fight an active drag
        if (!CanAfford(cardEntityId)) return; // can't select a card the player can't play

        // Selecting a genuinely different card than whatever MultiPointCard sequence was
        // mid-flight abandons that sequence — re-selecting the SAME card (e.g. after
        // switching away and back) is left alone below, since TryPlayCard already restarts
        // the sequence itself once a click actually lands.
        if (_multiPointCardId != 0 && _multiPointCardId != cardEntityId)
            ClearMultiPointSequence();

        _selectedCardId = cardEntityId;
        PlayCardSound("CardSelect");
    }

    private void OnCardClicked(CardGameObject go) => SelectCard(go.CardEntityId);

    // Right-click never selects/plays a card — two right-clicks on the SAME card within
    // _discardDoubleClickWindow enqueues a discard instead; anything else (a single
    // right-click, or one that lands on a different card than the last) just (re)arms the
    // pending pair and does nothing further. Only a card the player currently CAN'T afford
    // is eligible — explicit design ask, discarding is a way to get rid of a dead card in
    // hand, not a free way to cycle an affordable one (DiscardCardSystem enforces this
    // server-side too; this is just so an affordable card doesn't visibly arm/discard at all).
    private void OnCardRightClicked(CardGameObject go)
    {
        if (_draggingCardId != 0) return; // don't fight an active drag
        if (CanAfford(go.CardEntityId)) return;

        ulong cardId = go.CardEntityId;
        float now = Time.unscaledTime;

        bool isDoubleClick = cardId == _lastRightClickCardId && (now - _lastRightClickTime) <= _discardDoubleClickWindow;

        if (!isDoubleClick)
        {
            _lastRightClickCardId = cardId;
            _lastRightClickTime = now;
            return;
        }

        // Consumed — a third rapid right-click starts a fresh pair rather than instantly
        // discarding whatever card happens to be clicked next.
        _lastRightClickCardId = 0;

        if (_selectedCardId == cardId) _selectedCardId = 0;
        if (_multiPointCardId == cardId) ClearMultiPointSequence();

        InputBuffer.EnqueueInput(new DiscardCardInput { CardEntityId = cardId });
    }

    private void OnCardDragStarted(CardGameObject go)
    {
        _draggingCardId = go.CardEntityId;
        _selectedCardId = 0; // dragging supersedes an explicit selection
    }

    private void OnCardDragEnded(CardGameObject go, PointerEventData eventData)
    {
        if (_draggingCardId != go.CardEntityId) return;
        _draggingCardId = 0;

        // If the drop point/target isn't valid (e.g. released over UI, above the horizon,
        // or no matching entity under the cursor), the card just falls back into its
        // normal hand layout below.
        TryPlayCard(go.CardEntityId);
    }

    // Resolves cardEntityId's kind and, if a valid play point/target is currently under
    // the cursor, sends the matching input and returns true (so the caller can clear
    // _selectedCardId/_draggingCardId) — false leaves the card as-is (still selected, or
    // falling back into the hand) for the caller to decide what to do. SpawnAtPointCard
    // plays at a ground point; TargetEntityCard plays on the closest matching selectable
    // entity to the cursor (see EntityTargeting.FindClosestSelectable); MultiPointCard
    // accumulates one ground point per call until it has PointCount of them (see
    // TryAddMultiPointClick) — a future card kind needing a different targeting shape gets
    // its own branch here too (and its own InputBase subtype/play system, see Card.cs).
    //
    // Takes cardEntityId explicitly rather than reading ActiveCardEntityId, since
    // OnCardDragEnded already clears _draggingCardId before calling this — by then
    // ActiveCardEntityId would no longer resolve to the card actually being played.
    private bool TryPlayCard(ulong cardEntityId)
    {
        Card definition = ResolveCard(cardEntityId);

        if (definition is SpawnAtPointCard)
        {
            if (!TileSpaceMouse.TryGetPosition(out float x, out float y)) return false;
            SelectionManager.instance?.SuppressNextClickSelect();
            InputBuffer.EnqueueInput(new SpawnAtPointInput { CardEntityId = cardEntityId, X = x, Y = y });
            return true;
        }

        if (definition is TargetEntityCard targetEntityCard)
        {
            if (!TileSpaceMouse.TryGetPosition(out float x, out float y)) return false;

            ulong targetId = EntityTargeting.FindClosestSelectable(
                _ecs, x, y, targetEntityCard.TargetSelectionRadius, LocalPlayerId(),
                targetEntityCard.CanTargetFriendly, targetEntityCard.CanTargetEnemyOrNeutral, _targetQueryBuffer);
            if (targetId == 0) return false;

            SelectionManager.instance?.SuppressNextClickSelect();
            InputBuffer.EnqueueInput(new SpawnAtEntityInput { CardEntityId = cardEntityId, TargetEntityId = targetId });
            return true;
        }

        if (definition is MultiPointCard multiPointCard)
            return TryAddMultiPointClick(cardEntityId, multiPointCard);

        return false;
    }

    // Accumulates ground clicks for a MultiPointCard across however many calls to
    // TryPlayCard it takes to gather PointCount of them (each click/drag-release routes
    // through TryPlayCard exactly like a single-point card, so this works whether the
    // player clicks each point individually or drags for one of them) — only once the last
    // point is placed does this actually enqueue MultiPointInput and return true. A false
    // return here mid-sequence still re-arms _selectedCardId itself (below), since unlike
    // SpawnAtPointCard/TargetEntityCard's false-means-try-again, here it means "still
    // waiting on more clicks," not "that click didn't land on anything valid."
    //
    // Every point after the first is clamped to MultiPointCard.MaxRangeFromPreviousPoint
    // before it's added — CardPlacementIndicatorManager clamps its live preview identically,
    // so the point actually captured here always matches what was last shown on screen.
    private bool TryAddMultiPointClick(ulong cardEntityId, MultiPointCard multiPointCard)
    {
        if (!TileSpaceMouse.TryGetPosition(out float x, out float y))
        {
            // A miss (cursor ray didn't hit the ground plane) shouldn't lose an in-progress
            // sequence or drop the card back to hand — re-arm exactly like the "not enough
            // points yet" case below, so the player can just try the click again. Matters
            // most for a drag-release (OnCardDragEnded ignores TryPlayCard's return value and
            // unconditionally clears _draggingCardId), which would otherwise leave both
            // _draggingCardId and _selectedCardId at 0 with no way to resume.
            _selectedCardId = cardEntityId;
            return false;
        }

        if (_multiPointCardId != cardEntityId)
        {
            _multiPointCardId = cardEntityId;
            _multiPointPoints.Clear();
        }

        Vector2 point = new Vector2(x, y);
        if (_multiPointPoints.Count > 0)
            point = MultiPointCard.ClampToPreviousPoint(_multiPointPoints[_multiPointPoints.Count - 1], point, multiPointCard.MaxRangeFromPreviousPoint);

        _multiPointPoints.Add(point);

        if (_multiPointPoints.Count < multiPointCard.PointCount)
        {
            // Stay armed for the next click even if this one came from a drag-release,
            // which would otherwise leave both _selectedCardId and _draggingCardId at 0
            // with nothing left to keep the sequence alive.
            _selectedCardId = cardEntityId;
            return false;
        }

        SelectionManager.instance?.SuppressNextClickSelect();
        InputBuffer.EnqueueInput(new MultiPointInput { CardEntityId = cardEntityId, Points = new List<Vector2>(_multiPointPoints) });

        ClearMultiPointSequence();
        return true;
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

        if (bestId != 0 && bestId != _hoveredCardId)
            PlayCardSound("CardHover");

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
